using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class MenuMusicChecks {
  const string Key="StarRacing.MenuMusicChecks";
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static readonly string[] AudioKeys={"Muted","Music","Engines","Effects"};
  static readonly Dictionary<string,(bool exists,float value)> saved=new Dictionary<string,(bool,float)>();
  static bool hadConfig;static string config;static float listenerVolume,timeScale;
  static int ticks,assertions,menuPosition;static double started,nextTick;
  static GameObject root;static RaceDirector director;static RaceAudioCoordinator audio;static AudioSource menu,race;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_MENU_MUSIC_QA_DIR");
  static MenuMusicChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_MENU_MUSIC_QA_DIR required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=assertions=0;started=nextTick=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
   if(state==PlayModeStateChange.EnteredEditMode){
    if(!SessionState.GetBool(Key+"Failed",false))try{VehicleAudioChecks.Run();}catch(Exception e){Debug.LogException(e);SessionState.SetBool(Key+"Failed",true);}
    SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);
   }
  }
  static void Check(bool value,string message){assertions++;if(!value)throw new Exception("MENU_MUSIC: "+message);}
  static object Field(string name)=>typeof(RaceAudioCoordinator).GetField(name,Private).GetValue(audio);
  static void Call(string name)=>typeof(RaceAudioCoordinator).GetMethod(name,Private).Invoke(audio,null);
  static void Tick(){
   try{
    if(EditorApplication.timeSinceStartup-started>120)throw new Exception("Menu music fixture timed out");
    // Editor callbacks can outrun audio buffers in headless mode. Keep the
    // original playback assertions, but allow real DSP time between steps.
    if(EditorApplication.timeSinceStartup<nextTick)return;
    nextTick=EditorApplication.timeSinceStartup+.05;
    ticks++;
    if(ticks==1){
     hadConfig=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);config=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);
     listenerVolume=AudioListener.volume;timeScale=Time.timeScale;AudioListener.volume=0;saved.Clear();
     foreach(string suffix in AudioKeys){string key="StarRacing.Audio."+suffix;saved[key]=(PlayerPrefs.HasKey(key),suffix=="Muted"?PlayerPrefs.GetInt(key):PlayerPrefs.GetFloat(key));}
     PlayerPrefs.SetInt("StarRacing.Audio.Muted",0);PlayerPrefs.SetFloat("StarRacing.Audio.Music",1);
     new LocalRaceConfig{humans=1,entrants=8,devices=new[]{-2,-1,-2,-1},theme="cloud-city",jumps=false}.Save();
     root=new GameObject("Menu music fixture");director=root.AddComponent<RaceDirector>();audio=root.GetComponent<RaceAudioCoordinator>();
     Check(audio!=null,"real director did not create audio coordinator");
    }
    if(ticks==30){
     menu=(AudioSource)Field("menuMusic");
     Check(menu!=null&&menu.isPlaying,"menu theme did not start");
     Check(!audio.Running&&!director.Started&&Time.timeScale==0,"preparation menu must remain paused");
     Check(menu.loop&&menu.spatialBlend==0&&menu.pitch==1,"menu source playback settings");
     Check(menu.volume>0&&menu.volume<=.65f,"unscaled fade/headroom");
     Check(menu.clip.channels==2&&menu.clip.frequency==44100&&Math.Abs(menu.clip.length-46)<.01f,"imported loop format/duration");
     var pcm=new float[menu.clip.samples*menu.clip.channels];Check(menu.clip.GetData(pcm,0),"loop PCM import unreadable");
     float peak=0;foreach(float x in pcm){CheckFinite(x);peak=Math.Max(peak,Math.Abs(x));}
     Check(peak<1,"loop PCM clips");
     float seam=Math.Max(Math.Abs(pcm[0]-pcm[pcm.Length-2]),Math.Abs(pcm[1]-pcm[pcm.Length-1]));
     Check(seam<.01f,"loop sample boundary discontinuity");
     menuPosition=menu.timeSamples;director.GetComponent<RaceMenu>().Open(RaceMenuScreen.Settings);
    }
    if(ticks==40){
     Check(ReferenceEquals(menu,Field("menuMusic"))&&menu.isPlaying&&menu.timeSamples>menuPosition,"submenu restarted theme: same="+ReferenceEquals(menu,Field("menuMusic"))+" playing="+menu.isPlaying+" samples="+menuPosition+" -> "+menu.timeSamples);
     float full=menu.volume;
     audio.SetMix(false,.5f,0,0);Check(Math.Abs(menu.volume-full*.5f)<.00001f,"music slider or unrelated buses");
     audio.SetMix(true,1,1,1);Check(menu.mute&&menu.volume==0,"mute leaks menu music");
     audio.SetMix(false,0,1,1);Check(!menu.mute&&menu.volume==0,"zero music volume leaks");
     audio.SetMix(false,1,1,1);Check(menu.volume>0&&menu.isPlaying,"unmute/re-enable music");
     typeof(RaceAudioCoordinator).GetField("forceMuted",Private).SetValue(audio,true);Call("SyncMix");Check(menu.mute&&menu.volume==0,"--muted policy leaks");
     typeof(RaceAudioCoordinator).GetField("forceMuted",Private).SetValue(audio,false);Call("SyncMix");
    }
    if(ticks==45){
     director.Restart(true);Call("LateUpdate");race=(AudioSource)Field("music");
     Check(audio.Running&&race!=null,"race music lifecycle did not begin");
     Check(!menu.isPlaying&&menu.volume==0,"menu overlaps countdown music");
     Check(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length==1,"extra listener");
    }
    if(ticks==55){
     director.SetPaused(true);director.GetComponent<RaceMenu>().OpenPauseSettings();Call("LateUpdate");
     Check(!menu.isPlaying&&ReferenceEquals(race,Field("music")),"pause settings replaced racing theme with menu");
     director.Restart(true);Call("LateUpdate");
     Check(audio.Running&&!menu.isPlaying&&!ReferenceEquals(race,Field("music")),"repeat race audio lifecycle");
    }
    if(ticks==65){
     director.SetPaused(true);Check(director.ExitToMenu(),"real exit to menu failed");Call("LateUpdate");
     Check(!audio.Running&&Field("music")==null&&menu.isPlaying,"race/menu transition");
     Check(ReferenceEquals(menu,Field("menuMusic")),"return created duplicate menu voice");
    }
    if(ticks==80){
     Check(menu.volume>0,"return fade did not advance while paused");
     audio.enabled=false;Check(!menu.isPlaying&&menu.volume==0,"disabled coordinator leaks theme");
     audio.enabled=true;Call("LateUpdate");Check(menu.isPlaying,"re-enable does not resume menu");
     foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())UnityEngine.Object.DestroyImmediate(go);
    }
    if(ticks==85){
     Check(UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length==0,"destroy leaked audio voices");
     Check(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length==0,"destroy leaked owned listener");
     string result="MENU_MUSIC_PLAYMODE_OK assertions="+assertions+" startup submenu slider mute forced-muted countdown pause repeat return disable reenable destroy\n";
     File.WriteAllText(Path.Combine(Output,"playmode.txt"),result);Debug.Log(result);Finish(false);
    }
   }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(true);}
  }
  static void CheckFinite(float value){if(float.IsNaN(value)||float.IsInfinity(value))throw new Exception("Nonfinite imported loop PCM");}
  static void Finish(bool failed){
   EditorApplication.update-=Tick;SessionState.SetBool(Key+"Failed",failed);
   if(hadConfig)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,config);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);
   foreach(var row in saved){if(!row.Value.exists)PlayerPrefs.DeleteKey(row.Key);else if(row.Key.EndsWith("Muted"))PlayerPrefs.SetInt(row.Key,(int)row.Value.value);else PlayerPrefs.SetFloat(row.Key,row.Value.value);}
   PlayerPrefs.Save();AudioListener.volume=listenerVolume;Time.timeScale=timeScale;EditorApplication.isPlaying=false;
  }
 }
}
