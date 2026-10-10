using System;

namespace StarRacingPrototype {
 // PCM callback owns all phase/filter history. The control thread publishes only
 // scalar targets; this class has no UnityEngine dependency or scene access.
 public sealed class EngineAudioDsp {
  readonly int sampleRate,index;
  readonly double[] phase=new double[3];
  readonly double[] detune=new double[3];
  volatile float targetFrequency=46,targetGain,targetCutoff=900,targetLfoFrequency=10,targetLfoDepth,targetLoad,targetBoost;
  double frequency=46,gain,cutoff=900,lfoFrequency=10,lfoDepth,lfoPhase,x1,x2,y1,y2;
  double load,boost,noiseLow,noiseBass;uint noiseState=0x6d2b79f5;int filterTick;
  double b0,b1,b2,a1,a2;
  public EngineAudioDsp(int sampleRate,int index){
   if(sampleRate<8000)throw new ArgumentOutOfRangeException(nameof(sampleRate));
   this.sampleRate=sampleRate;this.index=index;noiseState+=(uint)(index*7919);
   for(int i=0;i<3;i++)detune[i]=Math.Pow(2,(((index%3)-1)*4)/1200.0);
   lfoFrequency=10+index*.4;Coefficients();
  }
  public void Set(float speed,float throttle,bool nitro,float volume)=>SetPresentation(speed,throttle,nitro?1:0,volume);
  // Throttle is the direct control; boost is the accepted onset and short tail.
  public void SetPresentation(float speed,float throttle,float boost,float volume){
   boost=RaceAudioPolicy.Clamp(boost);
   throttle=RaceAudioPolicy.Clamp(throttle);speed=Math.Max(0,speed);
   float f=RaceAudioPolicy.EngineFrequency(speed,throttle);
   targetLoad=throttle;targetBoost=boost;
   targetFrequency=f;targetGain=RaceAudioPolicy.Clamp(volume,0,.34f);
   targetLfoFrequency=6+f*.095f;
   targetLfoDepth=.02f+throttle*.03f;
   targetCutoff=420+throttle*(650+Math.Min(1000,speed*12));
  }
  public void Render(float[] data)=>RenderInterleaved(data,1);
  public void RenderInterleaved(float[] data,int channels){
   if(channels<1||data.Length%channels!=0)throw new ArgumentException("Invalid audio channels");
   double sf=1-Math.Exp(-1.0/(sampleRate*.025)),sg=1-Math.Exp(-1.0/(sampleRate*.035));
   double sl=1-Math.Exp(-1.0/(sampleRate*.005)),sc=1-Math.Exp(-1.0/(sampleRate*.06));
   // Capture the current control block. No allocations or Unity API on DSP thread.
   float tf=targetFrequency,tg=targetGain,tl=targetLfoFrequency,td=targetLfoDepth,tc=targetCutoff,tload=targetLoad,tboost=targetBoost;
   double boostAttack=1-Math.Exp(-1.0/(sampleRate*.012)),boostRelease=1-Math.Exp(-1.0/(sampleRate*.055));
   double rushFast=1-Math.Exp(-2*Math.PI*900/sampleRate),rushSlow=1-Math.Exp(-2*Math.PI*160/sampleRate);
   for(int n=0;n<data.Length;n+=channels){
    frequency+=(tf-frequency)*sf;gain+=(tg-gain)*sg;
    cutoff+=(tc-cutoff)*sc;lfoFrequency+=(tl-lfoFrequency)*sl;lfoDepth+=(td-lfoDepth)*sc;
    load+=(tload-load)*sl;
    boost+=(tboost-boost)*(tboost>boost?boostAttack:boostRelease);
    if((filterTick++&31)==0)Coefficients();
    double signal=Triangle(phase[0])*(.6-load*.1)+Saw(phase[1],frequency*detune[1]/sampleRate)*(.1+load*.14)+Triangle(phase[2])*(.04+load*.16);
    double filtered=b0*signal+b1*x1+b2*x2-a1*y1-a2*y2;
    x2=x1;x1=signal;y2=y1;y1=filtered;
    // Fade AM together with the voice: a silent voice must remain silent.
    noiseState^=noiseState<<13;noiseState^=noiseState>>17;noiseState^=noiseState<<5;
    double noise=(noiseState/(double)uint.MaxValue)*2-1;
    noiseLow+=(noise-noiseLow)*rushFast;noiseBass+=(noise-noiseBass)*rushSlow;
    double rush=(noiseLow-noiseBass)*1.3;
    double output=gain*(filtered*(1+Math.Sin(lfoPhase*2*Math.PI)*lfoDepth)+boost*rush);
    float sample=(float)Math.Max(-1,Math.Min(1,output));
    for(int channel=0;channel<channels;channel++)data[n+channel]=sample;
    phase[0]=Wrap(phase[0]+frequency*.5*detune[0]/sampleRate);
    phase[1]=Wrap(phase[1]+frequency*detune[1]/sampleRate);
    phase[2]=Wrap(phase[2]+frequency*2*detune[2]/sampleRate);
    lfoPhase=Wrap(lfoPhase+lfoFrequency/sampleRate);
   }
  }
  void Coefficients(){
   double w=2*Math.PI*Math.Min(sampleRate*.45,cutoff)/sampleRate,c=Math.Cos(w),alpha=Math.Sin(w)/(2*.7),a0=1+alpha;
   b0=(1-c)/2/a0;b1=(1-c)/a0;b2=b0;a1=-2*c/a0;a2=(1-alpha)/a0;
  }
  static double Wrap(double p)=>p-Math.Floor(p);
  static double Triangle(double p)=>1-4*Math.Abs(p-.5);
  static double Saw(double p,double dt){
   double correction=0;
   if(p<dt){double t=p/dt;correction=t+t-t*t-1;}
   else if(p>1-dt){double t=(p-1)/dt;correction=t*t+t+t+1;}
   return 2*p-1-correction;
  }
 }
}
