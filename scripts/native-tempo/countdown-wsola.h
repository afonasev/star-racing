// Copyright 2012-2013 The Chromium Authors
// BSD-3-Clause; see third_party/LICENSE-chromium.txt.
// Finite, resident-only adaptation of AudioRendererAlgorithm/WSOLA internals at
// 75d3bf3a5f29a71cc27e870cbc9d86f2fe7149ec. No media queue or rate automation.
// Windows, search, transition and EOF rules come from upstream, not captures.
#pragma once
#include <vector>
#include <cmath>
#include <algorithm>
#include <limits>
#include <cstring>

struct CountdownWsola {
 const std::vector<float>& pcm;int channels,frames,window,hop,candidates,searchSize,centerOffset;
 int base=0,target=0,search=0,complete=0,position=0;double outputTime=0;long long taken=0;bool exhausted=false;float speed=0;
 std::vector<float> ola,transition,pending,optimal,targetBlock,searchBlock,energies;
 CountdownWsola(const std::vector<float>& input,int n,int ch,int sr):pcm(input),channels(ch),frames(n),window(sr*20/1000),candidates(sr*30/1000){
  window+=window&1;hop=window/2;searchSize=candidates+window-1;centerOffset=candidates/2+hop-1;
  ola.resize(window);transition.resize(window*2);pending.resize((window+hop)*ch);optimal.resize(window*ch);targetBlock.resize(window*ch);searchBlock.resize(searchSize*ch);energies.resize(candidates*ch);
  auto hann=[](std::vector<float>& w){float scale=2.f*3.14159265358979323846f/w.size();for(size_t i=0;i<w.size();i++)w[i]=.5f*(1.f-std::cos(i*scale));};hann(ola);hann(transition);
 }
 void peek(int at,std::vector<float>& out,int count){for(int c=0;c<channels;c++)for(int i=0;i<count;i++){int n=base+at+i;out[c*count+i]=at+i<0?0:pcm[n*channels+c];}}
 float dot(const float* a,const float* b,int n){
  // Same four-lane reduction shape as the browser's NEON/SSE dot product.
  float v[4]{};int i=0;for(;i+3<n;i+=4)for(int k=0;k<4;k++)v[k]+=a[i+k]*b[i+k];float sum=(v[0]+v[2])+(v[1]+v[3]);for(;i<n;i++)sum+=a[i]*b[i];return sum;
 }
 int bestIndex(int excludeLow,int excludeHigh){
  float targetEnergy[2]{};
  for(int c=0;c<channels;c++){
   const float* s=searchBlock.data()+c*searchSize;float energy=0;for(int i=0;i<window;i++)energy+=s[i]*s[i];energies[c]=energy;
   for(int n=1;n<candidates;n++)energies[n*channels+c]=energies[(n-1)*channels+c]-s[n-1]*s[n-1]+s[n+window-1]*s[n+window-1];
   auto t=targetBlock.data()+c*window;targetEnergy[c]=dot(t,t,window);
  }
  auto similarity=[&](int n){float value=0;for(int c=0;c<channels;c++)value+=dot(targetBlock.data()+c*window,searchBlock.data()+c*searchSize+n,window)/std::sqrt(targetEnergy[c]*energies[n*channels+c]+1e-12f);return value;};
  auto excluded=[&](int n){return n>=excludeLow&&n<=excludeHigh;};
  constexpr int decimation=5;float values[3]{similarity(0),similarity(decimation),0};float best=values[0];int index=0;
  for(int n=decimation*2;n<candidates;n+=decimation){
   values[2]=similarity(n);
   if((values[1]>values[0]&&values[1]>=values[2])||(values[1]>=values[0]&&values[1]>values[2])){
    float a=.5f*(values[2]+values[0])-values[1],b=.5f*(values[2]-values[0]);float extremum=a==0?0:-b/(2.f*a);float v=a*extremum*extremum+b*extremum+values[1];int candidate=n-decimation+(int)(extremum*decimation+.5f);
    if(v>best&&!excluded(candidate)){index=candidate;best=v;}
   }else if(n+decimation>=candidates&&values[2]>best&&!excluded(n)){index=n;best=values[2];}
   values[0]=values[1];values[1]=values[2];
  }
  int low=std::max(0,index-decimation),high=std::min(candidates-1,index+decimation);best=std::numeric_limits<float>::min();index=0;
  for(int n=low;n<=high;n++)if(!excluded(n)){float v=similarity(n);if(v>best){best=v;index=n;}}
  return index;
 }
 void updateTime(double change){outputTime+=change;search=(int)(outputTime*speed+.5)-centerOffset;}
 bool iteration(){
  // As in browser CanPerformWsola: no zero input after finite EOF.
  if(target+window>frames-base||search+searchSize>frames-base){exhausted=true;return false;}
  int index=target;
  if(target>=search&&target+window<=search+searchSize)peek(index,optimal,window);
  else{
   peek(target,targetBlock,window);peek(search,searchBlock,searchSize);int last=target-hop-search;index=search+bestIndex(last-80,last+80);peek(index,optimal,window);
   for(int c=0;c<channels;c++)for(int i=0;i<window;i++)optimal[c*window+i]=optimal[c*window+i]*transition[i]+targetBlock[c*window+i]*transition[window+i];
  }
  target=index+hop;
  for(int c=0;c<channels;c++){
   float* dest=pending.data()+c*(window+hop)+complete;const float* src=optimal.data()+c*window;
   for(int i=0;i<hop;i++)dest[i]=dest[i]*ola[hop+i]+src[i]*ola[i];std::copy(src+hop,src+window,dest+hop);
  }
  complete+=hop;updateTime(hop);int earliest=std::min(target,search);
  if(earliest>0){base+=earliest;target-=earliest;updateTime(-earliest/(double)speed);}
  return true;
 }
 void render(float* data,int count,float ratio){
  if(speed==0)speed=ratio;if(speed!=ratio)throw std::runtime_error("countdown rate changed mid-shot");
  std::fill(data,data+count*channels,0.f);
  if(ratio==1){int n=std::min(count,frames-position);for(int i=0;i<n;i++)for(int c=0;c<channels;c++)data[i*channels+c]=pcm[(position+i)*channels+c];position+=n;taken+=n;exhausted=position==frames;return;}
  int offset=0;
  while(offset<count){
   if(!complete&&(!iteration()))break;int n=std::min(complete,count-offset);
   for(int c=0;c<channels;c++){float* src=pending.data()+c*(window+hop);for(int i=0;i<n;i++)data[(offset+i)*channels+c]=src[i];std::memmove(src,src+n,(window+hop-n)*sizeof(float));}
   complete-=n;offset+=n;taken+=n;
  }
 }
 bool ended() const{return exhausted&&complete==0;}
};
