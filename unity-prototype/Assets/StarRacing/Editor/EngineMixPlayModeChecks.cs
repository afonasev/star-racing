using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class EngineMixPlayModeChecks {
  const string Key="StarRacing.EngineMixChecks";
  static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static EngineMixPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){SessionState.SetBool(Key,true);SessionState.SetInt(Key+"Result",1);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.update+=Check;
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetInt(Key+"Result",1));}
  }
  static void Require(bool value,string message){if(!value)throw new Exception("ENGINE_COORDINATOR: "+message);}
  static void Tick(RaceAudioCoordinator audio)=>typeof(RaceAudioCoordinator).GetMethod("LateUpdate",Private).Invoke(audio,null);
  static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private).GetValue(target);
  static void Feedback(MagneticVehicle car,float throttle){car.SetPresentationInput(throttle,true);typeof(MagneticVehicle).GetMethod("UpdateExhaust",Private).Invoke(car,new object[]{.1f});}
  static void Check(){
   EditorApplication.update-=Check;
   bool had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);string config=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);
   string[] keys={"Muted","Music","Engines","Effects"};bool[] existing=new bool[4];float[] saved=new float[4];for(int i=0;i<4;i++){existing[i]=PlayerPrefs.HasKey("StarRacing.Audio."+keys[i]);saved[i]=i==0?PlayerPrefs.GetInt("StarRacing.Audio."+keys[i]):PlayerPrefs.GetFloat("StarRacing.Audio."+keys[i]);}
   float volume=AudioListener.volume;var before=UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
   try{
    AudioListener.volume=0;EngineMixChecks.Run();VehicleAudioChecks.Run();
    new LocalRaceConfig{humans=4,entrants=8,theme="cloud-city",jumps=false}.Save();
    var root=new GameObject("Engine coordinator fixture");var director=root.AddComponent<RaceDirector>();director.enabled=false;
    var audio=root.GetComponent<RaceAudioCoordinator>();audio.enabled=false;
    foreach(var car in director.Cars)car.enabled=false;
    typeof(RaceDirector).GetProperty("Started").SetValue(director,true);director.Session.Begin();director.SetPaused(false);
    audio.SetMix(false,1,1,1);
    for(int frame=0;frame<12;frame++){for(int i=0;i<8;i++)Feedback(director.Cars[i],i==3?1:0);Tick(audio);}
    var targets=Field<float[]>(audio,"engineTargets");var voices=Field<EngineMixVoice[]>(audio,"mixVoices");
    Require(voices[3].Throttle==1,"gas reads direct control before flame ramp");
    foreach(var source in Field<AudioSource[]>(audio,"engines"))if(source!=null)Require(source.clip==null&&source.GetComponent<EngineAudioOutput>()!=null,"engine still prebuffers a streaming clip");
    Require(targets[3]>.03f&&Math.Abs(targets[3]-RaceAudioPolicy.EngineGain(voices[3].Speed,voices[3].Throttle))<.000001f,"one gas attenuated by seats/bots");
    foreach(var car in director.Cars)car.DriveFeedback.Reset();
    for(int frame=0;frame<20;frame++){foreach(var car in director.Cars)Feedback(car,1);Tick(audio);}
    targets=Field<float[]>(audio,"engineTargets");Require(targets[0]>0&&targets[1]==0&&targets[2]==0&&targets[3]==0,"equal local voices not coalesced");
    Require(audio.ActiveEngineCount>0&&audio.ActiveEngineCount<=8,"source budget");
    voices=Field<EngineMixVoice[]>(audio,"mixVoices");float throttle=voices[0].Throttle;director.SetPaused(true);foreach(var car in director.Cars)Feedback(car,0);Tick(audio);
    voices=Field<EngineMixVoice[]>(audio,"mixVoices");Require(voices[0].Throttle==throttle,"pause loses frozen controls");
    audio.SetMix(true,1,1,1);Tick(audio);foreach(var source in Field<AudioSource[]>(audio,"engines"))if(source!=null)Require(source.mute,"mute bypassed");
    audio.SetMix(false,1,0,1);Tick(audio);foreach(var dsp in Field<EngineAudioDsp[]>(audio,"dsp"))if(dsp!=null)Require(Field<float>(dsp,"targetGain")==0,"engine slider zero bypassed");
    audio.SetMix(false,1,1,1);Tick(audio);Require(Field<EngineAudioDsp[]>(audio,"dsp")[0]!=null,"restore engine volume loses voice");
    director.SetPaused(false);foreach(var car in director.Cars)Feedback(car,0);Tick(audio);
    Require(Field<EngineMixVoice[]>(audio,"mixVoices")[0].Throttle==0,"release waits for flame decay");
    director.SetPaused(true);Require(director.ExitToMenu(),"menu transition rejected");Tick(audio);Require(!audio.Running&&audio.ActiveEngineCount==0,"menu engine leak");
    typeof(RaceDirector).GetProperty("Started").SetValue(director,true);director.Session.Begin();director.SetPaused(false);Tick(audio);Require(audio.Running&&audio.ActiveEngineCount>0,"repeat has no engine");
    audio.StopRace();Require(!audio.Running&&audio.ActiveEngineCount==0,"stop engine leak");
    Debug.Log("ENGINE_COORDINATOR_PLAYMODE_OK oneGas coalescing sourceBudget pause mute engineSlider menu repeat stop");SessionState.SetInt(Key+"Result",0);
   }catch(Exception error){Debug.LogException(error);}
   finally{
    foreach(var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())if(Array.IndexOf(before,go)<0)UnityEngine.Object.DestroyImmediate(go);
    if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,config);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);
    for(int i=0;i<4;i++){string pref="StarRacing.Audio."+keys[i];if(!existing[i])PlayerPrefs.DeleteKey(pref);else if(i==0)PlayerPrefs.SetInt(pref,(int)saved[i]);else PlayerPrefs.SetFloat(pref,saved[i]);}PlayerPrefs.Save();AudioListener.volume=volume;Time.timeScale=1;EditorApplication.isPlaying=false;
   }
  }
 }
}
