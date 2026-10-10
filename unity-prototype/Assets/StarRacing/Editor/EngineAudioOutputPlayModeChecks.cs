using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class EngineAudioOutputPlayModeChecks {
  const string Key="StarRacing.EngineOutputChecks";
  static GameObject root;static EngineAudioOutput output;static double deadline;static float volume;
  static EngineAudioOutputPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){SessionState.SetBool(Key,true);SessionState.SetInt(Key+"Result",1);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){
    volume=AudioListener.volume;AudioListener.volume=0;
    root=new GameObject("Actual DSP callback fixture");
    var listener=new GameObject("Fixture listener");listener.transform.SetParent(root.transform);listener.AddComponent<AudioListener>();
    var source=root.AddComponent<AudioSource>();source.spatialBlend=0;source.volume=1;source.loop=true;source.playOnAwake=false;
    var dsp=new EngineAudioDsp(AudioSettings.outputSampleRate,0);dsp.Set(40,.6f,true,.2f);
    output=root.AddComponent<EngineAudioOutput>();output.Bind(dsp);source.Play();
    deadline=EditorApplication.timeSinceStartup+5;EditorApplication.update+=Poll;
   }
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetInt(Key+"Result",1));}
  }
  static void Poll(){
   if(output.RenderedBlocks<3&&EditorApplication.timeSinceStartup<deadline)return;
   EditorApplication.update-=Poll;
   try{
    if(output.RenderedBlocks<3)throw new Exception("Actual OnAudioFilterRead callback did not run without streaming clip");
    Debug.Log("ENGINE_OUTPUT_PLAYMODE_OK blocks="+output.RenderedBlocks+" clipless actualDSP muted");SessionState.SetInt(Key+"Result",0);
   }catch(Exception error){Debug.LogException(error);}
   finally{UnityEngine.Object.DestroyImmediate(root);AudioListener.volume=volume;EditorApplication.isPlaying=false;}
  }
 }
}
