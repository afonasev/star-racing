#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#define SR_TEMPO_EXPORT __declspec(dllexport)
#define SR_TEMPO_CALL __cdecl
#else
#define SR_TEMPO_EXPORT __attribute__((visibility("default")))
#define SR_TEMPO_CALL
#endif
#include "third_party/signalsmith-stretch.h"
#include <atomic>
#include <thread>
#include <cstdio>
#include <cstring>
#include <chrono>
#include <cmath>
#include <memory>
#include <array>
#include <cstddef>
#include "countdown-wsola.h"

namespace {
constexpr int Capacity=65536,Chunk=1024,InputMax=2048,Taps=64,Phases=2048,ResampleCapacity=4096;
struct Stats { long long outputFrames,consumedFrames,wraps,starvations,nonFinite,callbacks;double cpuTotalUs,cpuMaxUs,cpuP50Us,cpuP95Us,cpuP99Us;int channels,rate,inputLatency,outputLatency; };
static_assert(sizeof(int)==4 && sizeof(long long)==8 && sizeof(float)==4, "tempo scalar ABI");
static_assert(sizeof(Stats)==104 && offsetof(Stats,cpuTotalUs)==48 && offsetof(Stats,channels)==88, "tempo Stats ABI");
#if defined(_WIN32)
static_assert(sizeof(void*)==8, "Windows tempo supports x64 only");
#endif
// Unity supplies UTF-8 paths on every platform. Windows CRT fopen uses ANSI,
// so decode the path once during creation; no path work runs in the callback.
FILE* openSource(const char* path){
 if(!path)return nullptr;
#if defined(_WIN32)
 int count=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path,-1,nullptr,0);
 if(count<=0)return nullptr;
 std::vector<wchar_t> wide(count);
 if(!MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path,-1,wide.data(),count))return nullptr;
 return _wfopen(wide.data(),L"rb");
#else
 return fopen(path,"rb");
#endif
}
struct Tempo {
 signalsmith::stretch::SignalsmithStretch<float> dsp{77};
 int channels=0,rate=0;long long sourceFrames=0,dataOffset=0,fileFrame=0;
 std::unique_ptr<CountdownWsola> countdown;
 FILE* file=nullptr;std::vector<float> resident,ring,input,output;std::vector<int16_t> raw;
 std::atomic<unsigned long long> read{0},write{0};std::atomic<bool> stop{false};std::thread worker;
 std::array<float,Chunk*2> deviceScratch{};std::array<float,ResampleCapacity*2> resampleRing{};std::vector<float> kernels;double devicePosition=0;long long generated=0;int deviceRate=0;
 float *in[2]{},*out[2]{};double fraction=0;long long sourcePosition=0;bool started=false;
 std::atomic<long long> outputFrames{0},consumedFrames{0},wraps{0},starvations{0},nonFinite{0},callbacks{0};std::atomic<double> cpuTotalUs{0},cpuMaxUs{0};std::array<std::atomic<unsigned>,8192> timings{};
 Tempo(int ch,int sr):channels(ch),rate(sr),input(InputMax*ch),output(Chunk*ch){
  if((ch!=1&&ch!=2)||sr<=0)throw std::runtime_error("invalid audio shape");
  dsp.configure(ch,1024,256,false);dsp.setTransposeFactor(1);
  for(int c=0;c<ch;c++){in[c]=input.data()+c*InputMax;out[c]=output.data()+c*Chunk;}
  kernels.resize(Taps*Phases);for(int phase=0;phase<Phases;phase++){double sum=0,f=phase/(double)Phases;for(int t=0;t<Taps;t++){double x=t-(Taps/2-1)-f;double sinc=std::abs(x)<1e-12?1:std::sin(M_PI*x)/(M_PI*x);double window=.5+.5*std::cos(M_PI*x/(Taps/2));double weight=sinc*window;kernels[phase*Taps+t]=(float)weight;sum+=weight;}for(int t=0;t<Taps;t++)kernels[phase*Taps+t]/=(float)sum;}
 }
 ~Tempo(){stop=true;if(worker.joinable())worker.join();if(file)fclose(file);}
 bool take(int count){
  if(file){auto r=read.load(std::memory_order_relaxed),w=write.load(std::memory_order_acquire);if(w-r<(unsigned)count){starvations++;return false;}
   for(int n=0;n<count;n++)for(int c=0;c<channels;c++)in[c][n]=ring[((r+n)%Capacity)*channels+c];read.store(r+count,std::memory_order_release);
  }else{
   for(int n=0;n<count;n++)for(int c=0;c<channels;c++)in[c][n]=sourcePosition+n<sourceFrames?resident[(sourcePosition+n)*channels+c]:0;
  }
  long long before=sourcePosition;sourcePosition+=count;consumedFrames+=count;if(file)wraps+=sourcePosition/sourceFrames-before/sourceFrames;return true;
 }
 void fill(){
  auto w=write.load(std::memory_order_relaxed),r=read.load(std::memory_order_acquire);int free=Capacity-int(w-r);if(!free)return;
  int count=std::min({free,4096,int(sourceFrames-fileFrame)});int values=count*channels;
  if(fread(raw.data(),sizeof(int16_t),values,file)!=(size_t)values){stop=true;starvations++;return;}
  for(int n=0;n<count;n++)for(int c=0;c<channels;c++)ring[((w+n)%Capacity)*channels+c]=raw[n*channels+c]*(1.0f/32768);
  write.store(w+count,std::memory_order_release);fileFrame+=count;
  if(fileFrame==sourceFrames){fileFrame=0;if(fseek(file,dataOffset,SEEK_SET)!=0){stop=true;starvations++;}}
 }
 void device(float*data,int frames,int busChannels,int outputRate,int prefix,int allowed,float ratio,float gain){
  auto begin=std::chrono::steady_clock::now();
  if(!data||frames<0||busChannels<1||busChannels>8||prefix<0||allowed<0||prefix+allowed>frames||rate!=44100||(outputRate!=44100&&outputRate!=48000)){nonFinite++;throw std::runtime_error("unsupported tempo output shape");}
  std::memset(data,0,frames*busChannels*sizeof(float));
  if(deviceRate&&deviceRate!=outputRate){nonFinite++;throw std::runtime_error("device rate changed mid-generation");}deviceRate=outputRate;
  for(int offset=0;offset<allowed;offset+=Chunk){int count=std::min(Chunk,allowed-offset);
   if(outputRate==rate)render(deviceScratch.data(),count,ratio,1);
   else{
    double step=rate/(double)outputRate;long long required=(long long)std::floor(devicePosition+(count-1)*step)+Taps/2+1;
    while(generated<required){int n=(int)std::min((long long)Chunk,required-generated);render(deviceScratch.data(),n,ratio,1);for(int i=0;i<n;i++)for(int c=0;c<channels;c++)resampleRing[((generated+i)%ResampleCapacity)*channels+c]=deviceScratch[i*channels+c];generated+=n;}
    for(int i=0;i<count;i++){long long center=(long long)std::floor(devicePosition);int phase=std::min(Phases-1,(int)((devicePosition-center)*Phases));for(int c=0;c<channels;c++){double x=0;for(int t=0;t<Taps;t++){long long n=center+t-(Taps/2-1);if(n>=0)x+=resampleRing[(n%ResampleCapacity)*channels+c]*kernels[phase*Taps+t];}deviceScratch[i*channels+c]=(float)x;}devicePosition+=step;}
   }
   for(int i=0;i<count;i++)for(int c=0;c<busChannels;c++){float x=channels==1?deviceScratch[i]:c<2?deviceScratch[i*2+c]:0;x*=gain;if(!std::isfinite(x)){nonFinite++;x=0;}data[(prefix+offset+i)*busChannels+c]=x;}
  }
  callbacks++;double us=std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-begin).count();cpuTotalUs.store(cpuTotalUs.load()+us);cpuMaxUs.store(std::max(cpuMaxUs.load(),us));timings[std::min(8191,(int)us)]++;
 }
 double percentile(double p){auto target=(long long)std::ceil(callbacks.load()*p);long long n=0;for(int i=0;i<8192;i++){n+=timings[i].load();if(n>=target)return i+1;}return cpuMaxUs.load();}
 void render(float* data,int frames,float ratio,float gain){
  if(!data||frames<0||!std::isfinite(ratio)||ratio<.89f||ratio>1.121f||!std::isfinite(gain)){if(data&&frames>0)std::memset(data,0,frames*channels*sizeof(float));nonFinite++;return;}
  if(countdown){countdown->render(data,frames,ratio);outputFrames+=frames;consumedFrames=ratio==1?countdown->position:countdown->base;return;}
  if(!started){if(!take(dsp.inputLatency())){std::memset(data,0,frames*channels*sizeof(float));return;}dsp.seek(in,dsp.inputLatency(),ratio);started=true;}
  for(int offset=0;offset<frames;offset+=Chunk){int count=std::min(Chunk,frames-offset);double exact=fraction+count*(double)ratio;int needed=(int)std::floor(exact);fraction=exact-needed;
   if(!take(needed)){std::memset(data+offset*channels,0,(frames-offset)*channels*sizeof(float));return;}
   dsp.process(in,needed,out,count);
   for(int n=0;n<count;n++)for(int c=0;c<channels;c++){float x=out[c][n]*gain;if(!std::isfinite(x)){nonFinite++;x=0;}data[(offset+n)*channels+c]=x;}
  }
  outputFrames+=frames;
 }
};
uint16_t u16(const unsigned char*p){return p[0]|p[1]<<8;}uint32_t u32(const unsigned char*p){return p[0]|p[1]<<8|p[2]<<16|uint32_t(p[3])<<24;}
}
extern "C" {
SR_TEMPO_EXPORT void* SR_TEMPO_CALL sr_tempo_memory(const float*pcm,int frames,int channels,int rate){try{auto p=std::make_unique<Tempo>(channels,rate);if(!pcm||frames<1)return nullptr;p->resident.assign(pcm,pcm+frames*channels);p->sourceFrames=frames;return p.release();}catch(...){return nullptr;}}
SR_TEMPO_EXPORT void* SR_TEMPO_CALL sr_tempo_countdown(const float*pcm,int frames,int channels,int rate){try{if(!pcm||frames<1||channels!=1||rate!=44100)return nullptr;auto p=std::make_unique<Tempo>(channels,rate);p->resident.assign(pcm,pcm+frames*channels);p->sourceFrames=frames;p->countdown=std::make_unique<CountdownWsola>(p->resident,frames,channels,rate);return p.release();}catch(...){return nullptr;}}
SR_TEMPO_EXPORT void* SR_TEMPO_CALL sr_tempo_file(const char*path,int frames,int channels,int rate,int startFrame){try{
 auto p=std::make_unique<Tempo>(channels,rate);p->file=openSource(path);if(!p->file)return nullptr;unsigned char header[12];if(fread(header,1,12,p->file)!=12||memcmp(header,"RIFF",4)||memcmp(header+8,"WAVE",4))return nullptr;
 bool format=false;uint32_t bytes=0;
 while(true){unsigned char chunk[8];if(fread(chunk,1,8,p->file)!=8)return nullptr;uint32_t size=u32(chunk+4);
  if(!memcmp(chunk,"fmt ",4)){unsigned char fmt[16];if(size<16||fread(fmt,1,16,p->file)!=16||u16(fmt)!=1||u16(fmt+2)!=channels||u32(fmt+4)!=(unsigned)rate||u16(fmt+14)!=16)return nullptr;format=true;if(fseek(p->file,size-16+(size&1),SEEK_CUR))return nullptr;}
  else if(!memcmp(chunk,"data",4)){bytes=size;p->dataOffset=ftell(p->file);break;}else if(fseek(p->file,size+(size&1),SEEK_CUR))return nullptr;
 }
 if(!format||bytes!=(unsigned)(frames*channels*2))return nullptr;if(startFrame<0||startFrame>=frames)return nullptr;p->sourceFrames=frames;p->fileFrame=startFrame;p->sourcePosition=startFrame;if(fseek(p->file,p->dataOffset+(long long)startFrame*channels*2,SEEK_SET))return nullptr;p->ring.resize(Capacity*channels);p->raw.resize(4096*channels);
 while(p->write<Capacity&&!p->stop)p->fill();if(p->stop)return nullptr;
 auto raw=p.get();p->worker=std::thread([raw]{while(!raw->stop){raw->fill();std::this_thread::sleep_for(std::chrono::milliseconds(1));}});return p.release();
 }catch(...){return nullptr;}}
SR_TEMPO_EXPORT int SR_TEMPO_CALL sr_tempo_render(void*handle,float*data,int frames,int channels,int outputRate,int prefix,int allowed,float ratio,float gain){try{if(!handle)return -1;((Tempo*)handle)->device(data,frames,channels,outputRate,prefix,allowed,ratio,gain);auto&p=*(Tempo*)handle;if(p.countdown&&p.countdown->ended()&&(outputRate==p.rate||p.devicePosition>=p.countdown->taken+Taps/2))return 1;return 0;}catch(...){return -2;}}
SR_TEMPO_EXPORT int SR_TEMPO_CALL sr_tempo_stats(void*handle,Stats*out){if(!handle||!out)return -1;auto&p=*(Tempo*)handle;*out={p.outputFrames.load(),p.consumedFrames.load(),p.wraps.load(),p.starvations.load(),p.nonFinite.load(),p.callbacks.load(),p.cpuTotalUs.load(),p.cpuMaxUs.load(),p.percentile(.5),p.percentile(.95),p.percentile(.99),p.channels,p.rate,p.countdown?0:p.dsp.inputLatency(),p.countdown?0:p.dsp.outputLatency()};return 0;}
SR_TEMPO_EXPORT void SR_TEMPO_CALL sr_tempo_destroy(void*handle){delete (Tempo*)handle;}
}
