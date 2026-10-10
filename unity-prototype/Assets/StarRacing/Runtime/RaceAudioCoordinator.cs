using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype {
 [DefaultExecutionOrder(200)]
 public sealed class RaceAudioCoordinator : MonoBehaviour {
  public RaceDirector Director {get;private set;}
  public bool Muted {get;private set;}
  public float MusicVolume {get;private set;}=1;
  public float EnginesVolume {get;private set;}=1;
  public float EffectsVolume {get;private set;}=1;
  public int Generation {get;private set;}=-1;
  public long Sequence {get;private set;}
  public int ActiveEngineCount {get;private set;}
  public int ActiveOneShotCount {get {int n=0;for(int i=0;i<shots.Length;i++)if(shots[i]!=null&&shots[i].isPlaying&&(shotTempo[i]==null||!shotTempo[i].Ended))n++;return n;}}
  public bool Running {get;private set;}
  [Serializable] class ClipDescriptor {public string key,resource;public float assetGain;}
  [Serializable] class ClipCatalog {public ClipDescriptor[] files;}
  ClipCatalog catalog;
  float musicAssetGain=1;
  NativeTempoPlayback musicTempo;
  readonly NativeTempoPlayback[] shotTempo=new NativeTempoPlayback[RaceAudioPolicy.MaxOneShots];
  float[] countdownPcm;int countdownFrames,countdownChannels,countdownFrequency;
  AudioSource music,wind,skid,menuMusic;
  float menuGain;
  const float MenuMusicGain=.65f, MenuFadeSeconds=.6f;
  readonly AudioSource[] engines=new AudioSource[RaceAudioPolicy.MaxEngines],shots=new AudioSource[RaceAudioPolicy.MaxOneShots];
  readonly EngineAudioDsp[] dsp=new EngineAudioDsp[RaceAudioPolicy.MaxEngines];
  readonly float[] engineGains=new float[RaceAudioPolicy.MaxEngines],shotGains=new float[RaceAudioPolicy.MaxOneShots];
  readonly float[] engineSpeeds=new float[RaceAudioPolicy.MaxEngines],engineThrottle=new float[RaceAudioPolicy.MaxEngines];
  readonly float[] engineBoost=new float[RaceAudioPolicy.MaxEngines];
  readonly int[] shotPriorities=new int[RaceAudioPolicy.MaxOneShots],audible=new int[RaceAudioPolicy.MaxEngines];
  readonly double[] shotTimes=new double[RaceAudioPolicy.MaxOneShots],lastPlayed=new double[8];
  readonly AudioClip[] eventClips=new AudioClip[8];
  readonly List<RecoveryEvent> recoveryBatch=new List<RecoveryEvent>(4);
  long[] lastRecoverySequence=Array.Empty<long>();
  int[] lastRecoveryEpisode=Array.Empty<int>();
  readonly EngineMixVoice[] mixVoices=new EngineMixVoice[RaceAudioPolicy.MaxEngines];
  readonly float[] engineTargets=new float[RaceAudioPolicy.MaxEngines];
  float windGain,skidGain;
  int previousBeat=-1;RacePhase previousPhase=RacePhase.Ready;
  double duckUntil;
  bool forceMuted,musicStartIssued;
  AudioListener ownedListener;
  const string Preference="StarRacing.Audio.";
  void Awake(){
   Director=GetComponent<RaceDirector>();
   Director.DrivingContact+=OnDrivingContact;
   Muted=PlayerPrefs.GetInt(Preference+"Muted",0)!=0;
   MusicVolume=Mathf.Clamp01(PlayerPrefs.GetFloat(Preference+"Music",1));
   EnginesVolume=Mathf.Clamp01(PlayerPrefs.GetFloat(Preference+"Engines",1));
   EffectsVolume=Mathf.Clamp01(PlayerPrefs.GetFloat(Preference+"Effects",1));
   var args=Environment.GetCommandLineArgs();
   forceMuted=Array.IndexOf(args,"--muted")>=0;
   if(FindAnyObjectByType<AudioListener>()==null)ownedListener=gameObject.AddComponent<AudioListener>();
   var manifest=Resources.Load<TextAsset>("Audio/decoded-manifest");
   if(manifest==null)throw new InvalidOperationException("AUDIO_MISSING decoded manifest");
   catalog=JsonUtility.FromJson<ClipCatalog>(manifest.text);
   for(int i=0;i<eventClips.Length;i++)eventClips[i]=Load("sfx/"+RaceAudioPolicy.Policy((RaceSound)i).Clip);
   var countdown=eventClips[(int)RaceSound.Countdown];countdownFrames=countdown.samples;countdownChannels=countdown.channels;countdownFrequency=countdown.frequency;
   countdownPcm=new float[countdownFrames*countdownChannels];if(!countdown.GetData(countdownPcm,0))throw new InvalidOperationException("AUDIO_TEMPO countdown PCM unavailable");

  }
  ClipDescriptor Descriptor(string key){foreach(var row in catalog.files)if(row.key==key){if(row.assetGain!=1&&row.assetGain!=.5f)throw new InvalidOperationException("AUDIO_INVALID_GAIN "+key);return row;}throw new InvalidOperationException("AUDIO_MISSING_DESCRIPTOR "+key);}
  AudioClip Load(string path){var clip=Resources.Load<AudioClip>(Descriptor(path).resource);if(clip==null)throw new InvalidOperationException("AUDIO_MISSING "+path);return clip;}
  AudioSource Voice(string label,AudioClip clip,bool loop){
   var child=new GameObject(label);child.transform.SetParent(transform,false);
   var source=child.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.dopplerLevel=0;source.loop=loop;source.clip=clip;source.volume=0;source.ignoreListenerPause=true;return source;
  }
  void Begin(){
   StopMenuMusic();
   StopRace();Generation=Director.RaceGeneration;Sequence=0;Running=true;
   lastRecoverySequence=new long[Director.Cars.Length];lastRecoveryEpisode=new int[Director.Cars.Length];
   Array.Clear(lastRecoveryEpisode,0,lastRecoveryEpisode.Length);
   for(int i=0;i<lastPlayed.Length;i++)lastPlayed[i]=double.NegativeInfinity;
   string musicKey="music/"+RaceAudioPolicy.Music[UnityEngine.Random.Range(0,RaceAudioPolicy.Music.Length)];
   musicAssetGain=Descriptor(musicKey).assetGain;
   var sourceMusic=Load(musicKey);
   string wav=System.IO.Path.GetFileName(Descriptor(musicKey).resource)+".wav";
   musicTempo=new NativeTempoPlayback(System.IO.Path.Combine(Application.streamingAssetsPath,"StarRacingAudio",wav),sourceMusic.samples,sourceMusic.channels,sourceMusic.frequency,0);
   musicTempo.Rate=RaceAudioPolicy.MusicRate(0);
   music=Voice("Race music",musicTempo.Clip,true);musicTempo.Bind(music);
   wind=Voice("Race wind",Load("sfx/wind-loop"),true);skid=Voice("Race skid",Load("sfx/skid-race"),true);
   previousPhase=RacePhase.Ready;previousBeat=-1;
   for(int i=0;i<Director.Cars.Length;i++){
    var observer=Director.Cars[i].GetComponent<NativeAudioVehicleObserver>()??Director.Cars[i].gameObject.AddComponent<NativeAudioVehicleObserver>();observer.Bind(this,Director.Cars[i]);
   }
   SyncMix();

  }
  void OnDrivingContact(DrivingContactEvent contact){
   if(Director==null||Director.Cars==null||Generation!=contact.Generation||Director.Paused)return;
   int id=contact.EntrantId;if(id<0||id>=Director.Cars.Length||Director.Cars[id].PositionRevision!=contact.PositionRevision)return;
   bool local=IsHuman(id);float distance=local?0:NearestHuman(id);
   if(contact.Kind==DrivingContactKind.Vehicle){
    int other=contact.OtherId;if(other<0||other>=Director.Cars.Length||Director.Cars[other].PositionRevision!=contact.OtherRevision)return;
    local|=IsHuman(other);distance=local?0:Mathf.Min(distance,NearestHuman(other));
   }
   if(distance>RaceAudioPolicy.AudibleRivalDistance)return;
   float presence=local?1:.32f*RaceAudioPolicy.RivalGain(distance);
   Emit(contact.Kind==DrivingContactKind.Vehicle?RaceSound.VehicleImpact:RaceSound.BarrierImpact,0,contact.Strength,presence);
  }
  bool Silent=>Muted||forceMuted;
  void TickMenuMusic(){
   if(menuMusic==null){
    var clip=Resources.Load<AudioClip>("Audio/menu-cloudline");
    if(clip==null)throw new InvalidOperationException("AUDIO_MISSING menu-cloudline");
    menuMusic=Voice("Menu music",clip,true);
   }
   if(!Silent&&MusicVolume>0&&!menuMusic.isPlaying)menuMusic.Play();
   menuGain=Mathf.MoveTowards(menuGain,MenuMusicGain,Time.unscaledDeltaTime*MenuMusicGain/MenuFadeSeconds);
   SyncMix();
  }
  void StopMenuMusic(){
   menuGain=0;
   if(menuMusic!=null){menuMusic.Stop();menuMusic.volume=0;}
  }
  void LateUpdate(){
   if(Director==null)return;
   if(!Director.Started){if(Running)StopRace();TickMenuMusic();return;}
   StopMenuMusic();
   if(Director.Cars==null||Director.Session==null)return;
   if(!Running||Generation!=Director.RaceGeneration)Begin();
   recoveryBatch.Clear();Director.ConsumeRecoveryEvents(recoveryBatch);
   foreach(var e in recoveryBatch) {
    if(e.Generation!=Generation||e.Entrant<0||e.Entrant>=Director.Cars.Length||e.Entrant>=lastRecoverySequence.Length||
       e.Sequence<=lastRecoverySequence[e.Entrant]||e.Episode<lastRecoveryEpisode[e.Entrant])continue;
    bool valid=e.Kind==RecoveryEventKind.Fall?e.RevisionBefore==e.RevisionAfter:
        e.Kind==RecoveryEventKind.Respawn && e.RevisionAfter==e.RevisionBefore+1;
    if(!valid)continue;
    lastRecoverySequence[e.Entrant]=e.Sequence;lastRecoveryEpisode[e.Entrant]=e.Episode;

    Emit(e.Kind==RecoveryEventKind.Fall?RaceSound.Fall:RaceSound.Respawn);
   }
   for(int i=0;i<shots.Length;i++)if(shotTempo[i]!=null&&shotTempo[i].Ended&&shots[i]!=null)shots[i].Stop();
   var session=Director.Session;
   if(session.Phase==RacePhase.Countdown){
    int beat=Mathf.CeilToInt((float)session.Countdown);if(beat!=previousBeat&&beat>0){Emit(RaceSound.Countdown,beat);previousBeat=beat;}
   }
   if(previousPhase==RacePhase.Countdown&&session.Phase!=RacePhase.Countdown)Emit(RaceSound.Go);
   if(previousPhase!=RacePhase.Results&&session.Phase==RacePhase.Results)Emit(RaceSound.Results);
   previousPhase=session.Phase;
   float progress=0,average=0,strongest=0;int humans=0;
   for(int i=0;i<Director.Cars.Length;i++)if(IsHuman(i)){
    humans++;var car=Director.Cars[i];average+=Mathf.Abs(car.Telemetry.speedKmh)/3.6f;
    progress=Mathf.Max(progress,session.Racers[i].Progress/session.RaceLength);
    var intent=car.DrivingIntent;if(intent.Generation==Generation && intent.PositionRevision==car.PositionRevision)strongest=Mathf.Max(strongest,intent.SlipIntensity);
   }
   if(Director.Paused)strongest=0; // Pause/recovery cannot sustain a tyre loop.
   int count=SelectAudible();ActiveEngineCount=0;
   for(int i=0;i<engines.Length;i++){
    int entrant=i<count?audible[i]:-1;var car=entrant<0?null:Director.Cars[entrant];
    float speed=car==null?0:Mathf.Abs(car.Telemetry.speedKmh)/3.6f,throttle=car==null?0:car.AudioThrottle;
    bool falling=car!=null&&car.IsFalling;
    float boost=car==null||falling?0:car.DriveFeedback?.NitroAudioSignal??0;
    // Native input is blocked on pause, while browser vehicle state freezes.
    // Keep the last audible controls so pause retains the source audio state.
    if(Director.Paused){speed=engineSpeeds[i];throttle=engineThrottle[i];boost=engineBoost[i];}
    else{engineSpeeds[i]=speed;engineThrottle[i]=throttle;engineBoost[i]=boost;}
    float priority=entrant<0?0:IsHuman(entrant)?1:.42f*RaceAudioPolicy.RivalGain(NearestHuman(entrant));
    mixVoices[i]=new EngineMixVoice(speed,throttle,boost,i<humans,priority*(IsHuman(entrant)?RaceAudioPolicy.SkidEngineDuck(strongest):1),car!=null&&!falling);
   }
   EngineMixPolicy.Targets(mixVoices,engineTargets);
   for(int i=0;i<engines.Length;i++)engineGains[i]=RaceAudioPolicy.Smooth(engineGains[i],engineTargets[i],Time.unscaledDeltaTime,.055f,.12f);
   EngineMixPolicy.Limit(mixVoices,engineGains);
   for(int i=0;i<engines.Length;i++){
    var voice=mixVoices[i];float target=engineTargets[i];
    if(engines[i]==null&&target>RaceAudioPolicy.EngineVoiceThreshold){
     int sampleRate=AudioSettings.outputSampleRate;dsp[i]=new EngineAudioDsp(sampleRate,i);
     engines[i]=Voice("Kart engine "+i,null,true);
     engines[i].gameObject.AddComponent<EngineAudioOutput>().Bind(dsp[i]);engines[i].volume=1;engines[i].Play();
    }
    if(engines[i]!=null){
     engines[i].mute=Silent;dsp[i].SetPresentation(voice.Speed,voice.Throttle,voice.Boost,engineGains[i]*EnginesVolume);
     if(target==0&&engineGains[i]<RaceAudioPolicy.EngineVoiceThreshold)DisposeEngine(i);
     else ActiveEngineCount++;
    }
   }
   windGain+=(Mathf.Min(.1f,average/Mathf.Max(1,humans)*.0016f)-windGain)*.2f;
   skidGain=RaceAudioPolicy.Smooth(skidGain,RaceAudioPolicy.SkidGain(strongest),Time.unscaledDeltaTime,.045f,.12f);
   if(musicTempo!=null)musicTempo.Rate=RaceAudioPolicy.MusicRate(progress);
   SyncMix();
  }
  bool IsHuman(int i)=>Director.AudioHumanSeat(i)>=0;
  float NearestHuman(int index){float best=float.PositiveInfinity;for(int i=0;i<Director.Cars.Length;i++)if(IsHuman(i))best=Mathf.Min(best,Mathf.Abs(Director.Cars[index].Distance-Director.Cars[i].Distance));return best;}
  int SelectAudible(){
   int count=0;for(int seat=0;seat<4;seat++)for(int i=0;i<Director.Cars.Length;i++)if(Director.AudioHumanSeat(i)==seat&&count<audible.Length)audible[count++]=i;
   int humanCount=count;
   while(count<audible.Length){int best=-1;float distance=RaceAudioPolicy.AudibleRivalDistance;
    for(int i=0;i<Director.Cars.Length;i++){if(IsHuman(i))continue;bool used=false;for(int j=humanCount;j<count;j++)if(audible[j]==i)used=true;
     float d=NearestHuman(i);if(!used&&(d<distance||(d==distance&&best<0))){best=i;distance=d;}}
    if(best<0)break;audible[count++]=best;
   }return count;
  }
  public bool Emit(RaceSound sound,int beat=0,float strength=1,float presence=1){
   if(!Running)return false;Sequence++;
   double now=Time.realtimeSinceStartupAsDouble;
   if(sound==RaceSound.Countdown)duckUntil=now+.65;
   if(Silent||EffectsVolume==0)return false;
   var policy=RaceAudioPolicy.Policy(sound);int key=sound==RaceSound.BarrierImpact?(int)RaceSound.VehicleImpact:(int)sound;
   bool impact=sound==RaceSound.VehicleImpact||sound==RaceSound.BarrierImpact;
   if(impact?!RaceAudioPolicy.ImpactReady(now,lastPlayed[key],strength):now-lastPlayed[key]<policy.Cooldown)return false;
   int slot=-1;
   for(int i=0;i<shots.Length;i++)if(shots[i]==null||!shots[i].isPlaying||(shotTempo[i]!=null&&shotTempo[i].Ended)){slot=i;break;}
   if(slot<0){slot=0;for(int i=1;i<shots.Length;i++)if(shotPriorities[i]<shotPriorities[slot]||(shotPriorities[i]==shotPriorities[slot]&&shotTimes[i]<shotTimes[slot]))slot=i;if(shotPriorities[slot]>policy.Priority)return false;}
   if(eventClips[(int)sound]==null)return false;
   if(shots[slot]==null)shots[slot]=Voice("Race event "+slot,null,false);
   shots[slot].Stop();if(shotTempo[slot]!=null){shotTempo[slot].Dispose();shotTempo[slot]=null;}
   if(sound==RaceSound.Countdown){shotTempo[slot]=new NativeTempoPlayback(countdownPcm,countdownFrames,countdownChannels,countdownFrequency,RaceAudioPolicy.CountdownRate(beat),true);shotTempo[slot].Bind(shots[slot]);}
   else{var filter=shots[slot].GetComponent<NativeTempoFilter>();if(filter!=null)filter.Bind(null);shots[slot].loop=false;shots[slot].clip=eventClips[(int)sound];}
   shots[slot].pitch=impact?Mathf.Lerp(1.08f,.94f,Mathf.Clamp01(strength)):1;
   float eventGain=impact?RaceAudioPolicy.ImpactGain(strength)*Mathf.Clamp01(presence):policy.Gain;
   shotGains[slot]=eventGain;shotPriorities[slot]=policy.Priority;shotTimes[slot]=now;lastPlayed[key]=now;
   if(shotTempo[slot]!=null){shotTempo[slot].Gain=eventGain*EffectsVolume;shots[slot].volume=1;}
   else shots[slot].volume=Mathf.Min(1,eventGain*EffectsVolume);if(shotTempo[slot]!=null)shotTempo[slot].Arm();shots[slot].Play();
   return true;
  }
  void SyncMix(){
   if(menuMusic!=null){menuMusic.mute=Silent;menuMusic.volume=Silent?0:menuGain*MusicVolume;}
   if(music!=null){music.mute=false;music.volume=1;musicTempo.Gain=Silent?0:RaceAudioPolicy.MusicGain*MusicVolume/musicAssetGain*(Time.realtimeSinceStartupAsDouble<duckUntil?RaceAudioPolicy.CountdownDuck:1);if(!Silent&&!musicStartIssued&&music.clip!=null){musicStartIssued=true;musicTempo.Arm();music.Play();}}
   MixLoop(wind,windGain);MixLoop(skid,skidGain);
   for(int i=0;i<shots.Length;i++)if(shots[i]!=null){if(shotTempo[i]!=null){shots[i].mute=false;shots[i].volume=1;shotTempo[i].Gain=Silent?0:shotGains[i]*EffectsVolume;}else{shots[i].mute=Silent;shots[i].volume=Mathf.Min(1,shotGains[i]*EffectsVolume);}}
  }
  void MixLoop(AudioSource source,float gain){if(source==null)return;source.mute=Silent;source.volume=Mathf.Min(1,gain*EffectsVolume);if(!Silent&&gain>.004f&&!source.isPlaying)source.Play();if(gain<.004f&&source.isPlaying){source.Stop();}}
  public void SetMix(bool mute,float musicLevel,float engineLevel,float effectsLevel){
   Muted=mute;MusicVolume=Mathf.Clamp01(musicLevel);EnginesVolume=Mathf.Clamp01(engineLevel);EffectsVolume=Mathf.Clamp01(effectsLevel);
   PlayerPrefs.SetInt(Preference+"Muted",mute?1:0);PlayerPrefs.SetFloat(Preference+"Music",MusicVolume);PlayerPrefs.SetFloat(Preference+"Engines",EnginesVolume);PlayerPrefs.SetFloat(Preference+"Effects",EffectsVolume);PlayerPrefs.Save();SyncMix();
  }
  void DisposeVoice(AudioSource source){if(source==null)return;source.Stop();Destroy(source.gameObject);}
  void DisposeEngine(int i){DisposeVoice(engines[i]);engines[i]=null;dsp[i]=null;engineGains[i]=0;engineSpeeds[i]=engineThrottle[i]=0;engineBoost[i]=0;}
  public void StopRace(){
   Running=false;musicStartIssued=false;DisposeVoice(music);DisposeVoice(wind);DisposeVoice(skid);music=wind=skid=null;
   if(musicTempo!=null){musicTempo.Dispose();musicTempo=null;}
   for(int i=0;i<engines.Length;i++)DisposeEngine(i);for(int i=0;i<shots.Length;i++){DisposeVoice(shots[i]);shots[i]=null;if(shotTempo[i]!=null){shotTempo[i].Dispose();shotTempo[i]=null;}}
   windGain=skidGain=0;duckUntil=0;ActiveEngineCount=0;
  }
  void OnDisable(){StopMenuMusic();StopRace();}
  void OnDestroy(){if(Director!=null)Director.DrivingContact-=OnDrivingContact;StopRace();DisposeVoice(menuMusic);menuMusic=null;if(ownedListener!=null)Destroy(ownedListener);}
 }
}
