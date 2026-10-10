using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StarRacingPrototype {
 public static class VehicleAudioChecks {
  static int assertions;
  static void Require(bool value,string message){assertions++;if(!value)throw new Exception("VEHICLE_AUDIO "+message);}
  static float[] Render(int rate,float speed,float throttle,bool boost,float gain,int index=0){
   var dsp=new EngineAudioDsp(rate,index);dsp.Set(speed,throttle,boost,gain);
   var warm=new float[rate];dsp.Render(warm);var pcm=new float[rate];dsp.Render(pcm);return pcm;
  }
  static double Energy(float[] pcm,bool difference=false){double e=0;for(int i=1;i<pcm.Length;i++){double v=difference?pcm[i]-pcm[i-1]:pcm[i];e+=v*v;}return Math.Sqrt(e/pcm.Length);}
  public static void Run(){
   assertions=0;
   Require(RaceAudioPolicy.EngineFrequency(0,1)>RaceAudioPolicy.EngineFrequency(0,0)+10,"throttle at start");
   Require(RaceAudioPolicy.EngineFrequency(70,1)>RaceAudioPolicy.EngineFrequency(55,1)+10,"high-speed pitch plateau");
   Require(RaceAudioPolicy.EngineFrequency(70,1)>RaceAudioPolicy.EngineFrequency(70,.4f)+30,"high-speed throttle response");
   Require(RaceAudioPolicy.SkidGain(0)==0&&RaceAudioPolicy.SkidGain(.1f)>.08f&&RaceAudioPolicy.SkidGain(1)<=.35f,"skid audibility/limit");
   Require(RaceAudioPolicy.ImpactGain(.2f)<RaceAudioPolicy.ImpactGain(.8f)&&RaceAudioPolicy.ImpactGain(1)<.8f,"impact severity/headroom");
   Require(!RaceAudioPolicy.ImpactReady(.1,0,1)&&RaceAudioPolicy.ImpactReady(.19,0,1)&&!RaceAudioPolicy.ImpactReady(.19,0,.1f),"strong after weak and shared minimum gap");
   Require(RaceAudioPolicy.Policy(RaceSound.Finish).Priority>RaceAudioPolicy.Policy(RaceSound.BarrierImpact).Priority,"impact cannot evict finish");
   foreach(int humans in new[]{1,4})Require(RaceAudioPolicy.EngineGain(0,0)*RaceAudioPolicy.EngineMixScale(humans,8-humans)>RaceAudioPolicy.EngineVoiceThreshold,"idle human starts with full roster "+humans);
   float full=RaceAudioPolicy.Smooth(0,1,.1f,.05f,.12f),half=RaceAudioPolicy.Smooth(0,1,.05f,.05f,.12f);
   Require(Math.Abs(full-RaceAudioPolicy.Smooth(half,1,.05f,.05f,.12f))<.00001,"frame-independent envelope");
   foreach(int rate in new[]{44100,48000}){
    // Publish a one-frame boost followed by release before the next PCM block.
    var envelope=new ExhaustEnvelope();var tapped=new EngineAudioDsp(rate,0);var idle=new EngineAudioDsp(rate,0);
    tapped.SetPresentation(30,1,0,.2f);idle.SetPresentation(30,1,0,.2f);
    var warmTap=new float[rate];var warmIdle=new float[rate];tapped.Render(warmTap);idle.Render(warmIdle);
    envelope.Step(1,true,true,1f/240);envelope.Step(0,false,true,1f/60);
    tapped.SetPresentation(30,envelope.GasSignal,envelope.NitroSignal,.2f);idle.SetPresentation(30,envelope.GasSignal,0,.2f);
    var shortTap=new float[rate/50];var noTap=new float[rate/50];tapped.Render(shortTap);idle.Render(noTap);
    Require(Energy(shortTap,true)>Energy(noTap,true)*1.2,"short tap reaches next PCM block "+rate);
    for(int frame=0;frame<120;frame++)envelope.Step(0,false,true,1f/60);
    Require(envelope.NitroSignal==0,"shared boost tail clears "+rate);
    var low=Render(rate,70,.3f,false,.2f);var high=Render(rate,70,1,false,.2f);var nitro=Render(rate,70,1,true,.2f);
    Require(Math.Abs(Energy(low,true)-Energy(high,true))>.0005,"load spectrum changes "+rate);
    Require(Energy(nitro,true)>Energy(high,true)*1.5,"nitro spectrum distinct "+rate);
    foreach(var pcm in new[]{low,high,nitro}){float peak=0;foreach(float x in pcm){if(float.IsNaN(x)||float.IsInfinity(x))throw new Exception("Nonfinite PCM");peak=Math.Max(peak,Math.Abs(x));}Require(peak<.34f,"individual PCM headroom "+rate);}
    var a=new EngineAudioDsp(rate,2);var b=new EngineAudioDsp(rate,2);a.Set(60,1,true,.2f);b.Set(60,1,true,.2f);
    var first=new float[1024];var second=new float[1024];a.Render(first);b.Render(second);
    bool same=true;for(int i=0;i<first.Length;i++)same&=first[i]==second[i];Require(same,"deterministic DSP");
    long before=GC.GetAllocatedBytesForCurrentThread();a.Render(first);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
    Require(allocated==0,"DSP callback allocation");
    a.Set(60,1,true,0);var tail=new float[rate];a.Render(tail);a.Render(tail);Require(Energy(tail)<1e-8,"zero engine volume silences nitro "+rate);
    double worst=0;for(int i=0;i<high.Length;i++)worst=Math.Max(worst,Math.Abs(high[i]*RaceAudioPolicy.EngineMixScale(4,4)*(4+4*.42f)));
    Require(worst<.34,"maximum engine voice sum headroom");
   }
   var contacts=new DrivingContactPolicy();
   var car=new DrivingContactObservation{Id=0,Generation=1,Revision=1,Right=Vector3.right,Normal=Vector3.up,Tangent=Vector3.forward,RightRail=true,RightClearance=0,Velocity=Vector3.right};
   contacts.BeginStep(new[]{car});Require(contacts.Barrier(0,1,out var light),"light barrier onset");
   Require(!contacts.Barrier(0,1,out _),"sustained barrier silence");
   car.Revision++;car.Velocity=Vector3.right*12;contacts.BeginStep(new[]{car});Require(contacts.Barrier(0,1,out var hard)&&hard.Strength>light.Strength,"barrier inward-speed severity");
   car.Suppressed=true;contacts.BeginStep(new[]{car});Require(!contacts.Barrier(0,1,out _),"suppressed contact silence");
   var tyre=Asset("sfx/skid-race",out _);
   Require(Energy(tyre)*RaceAudioPolicy.SkidGain(.65f)>.02,"skid PCM is audible above original quiet recording");
   foreach(string key in new[]{"skid-race","vehicle-impact","barrier-impact"}){var pcm=Asset("sfx/"+key,out _);float peak=0;foreach(float x in pcm)peak=Math.Max(peak,Math.Abs(x));Require(peak>.45f&&peak<.7f,"effect source headroom "+key);}
   string output=Environment.GetEnvironmentVariable("STAR_RACING_AUDIO_PREVIEW");if(!string.IsNullOrEmpty(output))Preview(output);
   Debug.Log("VEHICLE_AUDIO_CHECKS_OK assertions="+assertions+" rates=44100,48000 allocationFree silence nitro load contact");
  }
  static float[] Asset(string key,out int rate){
   // Music is imported as Streaming, so GetData cannot read its PCM. Use the
   // shipping source WAV for both music and effects in this Editor-only preview.
   using(var reader=new BinaryReader(File.OpenRead(Path.Combine(Application.dataPath,"StarRacing/Resources/Audio/pcm",key+".wav")))){
    string riff=new string(reader.ReadChars(4));reader.ReadInt32();string wave=new string(reader.ReadChars(4));
    if(riff!="RIFF"||wave!="WAVE")throw new Exception("Invalid preview WAV "+key);
    int channels=0,bits=0,format=0;rate=0;byte[] data=null;
    while(reader.BaseStream.Position+8<=reader.BaseStream.Length){
     string id=new string(reader.ReadChars(4));int size=reader.ReadInt32();long next=reader.BaseStream.Position+size+(size&1);
     if(id=="fmt "){format=reader.ReadInt16();channels=reader.ReadInt16();rate=reader.ReadInt32();reader.ReadInt32();reader.ReadInt16();bits=reader.ReadInt16();}
     else if(id=="data")data=reader.ReadBytes(size);
     reader.BaseStream.Position=next;
    }
    if(format!=1||bits!=16||channels<1||rate<8000||data==null)throw new Exception("Expected PCM16 WAV "+key);
    var mono=new float[data.Length/(2*channels)];for(int n=0;n<mono.Length;n++)for(int c=0;c<channels;c++)mono[n]+=BitConverter.ToInt16(data,(n*channels+c)*2)/(32768f*channels);return mono;
   }
  }
  static void Add(float[] mix,float[] source,int sourceRate,int rate,double start,float gain,float pitch=1,double duration=0){
   int begin=(int)(start*rate),length=duration>0?(int)(duration*rate):Math.Min(mix.Length-begin,(int)(source.Length*(double)rate/sourceRate/pitch));
   for(int n=0;n<length&&begin+n<mix.Length;n++){
    double position=n*(double)sourceRate/rate*pitch;int at=(int)position%source.Length,next=(at+1)%source.Length;
    float value=source[at]+(source[next]-source[at])*(float)(position-Math.Floor(position));
    float fade=duration>0?Math.Min(1,Math.Min(n/(rate*.04f),(length-n)/(rate*.12f))):1;
    mix[begin+n]+=value*gain*fade;
   }
  }
  static void Wav(string path,float[] pcm,int rate){using(var writer=new BinaryWriter(File.Create(path))){writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length*2);foreach(float v in pcm){if(Math.Abs(v)>=1||float.IsNaN(v))throw new Exception("Preview mix clipping");writer.Write((short)Math.Round(v*32767));}}}
  [Serializable] sealed class CatalogRow {public string key;public float assetGain;}
  [Serializable] sealed class Catalog {public CatalogRow[] files;}
  static float AssetGain(string key){foreach(var row in JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("Audio/decoded-manifest").text).files)if(row.key==key)return row.assetGain;throw new Exception("Missing preview gain "+key);}
  static float[] Demo(int index,float scale,bool local){
   const int rate=48000;var engine=new float[rate*24];var dsp=new EngineAudioDsp(rate,index);var block=new float[480];float gain=0;
   for(int n=0;n<engine.Length;n+=block.Length){
    float t=n/(float)rate,speed=t<3?0:t<6?30:t<9?65:70,throttle=t<3?.18f:t<6?.4f:t<21?1:0;
    bool boost=local&&t>=9&&t<12;float slip=t>=12&&t<15?.65f:0;
    float target=Math.Min(.34f,RaceAudioPolicy.EngineGain(speed,throttle,boost)*(local?1:.42f*RaceAudioPolicy.RivalGain(index*40)))*scale*(local?RaceAudioPolicy.SkidEngineDuck(slip):1);
    gain=RaceAudioPolicy.Smooth(gain,target,.01f,.055f,.12f);dsp.Set(speed,throttle,boost,gain);dsp.Render(block);Array.Copy(block,0,engine,n,block.Length);
   }return engine;
  }
  static void Effects(float[] mix,int rate){
   var skid=Asset("sfx/skid-race",out int skidRate);Add(mix,skid,skidRate,rate,12,RaceAudioPolicy.SkidGain(.65f),1,3);
   var car=Asset("sfx/vehicle-impact",out int carRate);var barrier=Asset("sfx/barrier-impact",out int barrierRate);
   Add(mix,car,carRate,rate,16,RaceAudioPolicy.ImpactGain(.2f),1.052f);Add(mix,barrier,barrierRate,rate,18,RaceAudioPolicy.ImpactGain(.85f),.961f);
  }
  static void Preview(string output){
   Directory.CreateDirectory(output);const int rate=48000,seconds=24;
   var engine=Demo(0,1,true);Wav(Path.Combine(output,"engine-nitro.wav"),engine,rate);
   var mix=(float[])engine.Clone();Effects(mix,rate);Wav(Path.Combine(output,"vehicle-demo.wav"),mix,rate);
   mix=Demo(0,RaceAudioPolicy.EngineMixScale(1,7),true);
   for(int index=1;index<8;index++){var rival=Demo(index,RaceAudioPolicy.EngineMixScale(1,7),false);for(int n=0;n<mix.Length;n++)mix[n]+=rival[n];}
   Effects(mix,rate);
   var wind=Asset("sfx/wind-loop",out int windRate);Add(mix,wind,windRate,rate,6,.1f,1,18);
   var music=Asset("music/city-loop",out int musicRate);Add(mix,music,musicRate,rate,0,RaceAudioPolicy.MusicGain/AssetGain("music/city-loop"),1,seconds);Wav(Path.Combine(output,"race-mix-demo.wav"),mix,rate);
   File.WriteAllText(Path.Combine(output,"preview.txt"),"PCM from actual EngineAudioDsp + shipping clips; not Player acceptance. Race-mix: 1 human + 7 rivals, manifest gain, engine normalization, full sliders. Scripted demo uses steady effect segments with edge fades, not driving QA.\n0–3 idle; 3–6 partial throttle; 6–9 full/high speed; 9–12 nitro; 12–15 skid; 16 light vehicle impact; 18 strong barrier impact; 21–24 throttle release.\n");
  }
 }
}
