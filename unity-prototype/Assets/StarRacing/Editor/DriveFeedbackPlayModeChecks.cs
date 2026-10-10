using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class DriveFeedbackPlayModeChecks {
  const string Key="StarRacing.DriveFeedbackChecks";
  static DriveFeedbackPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   SessionState.SetBool(Key,true);SessionState.SetInt(Key+"Result",1);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.update+=Check;
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetInt(Key+"Result",1));}
  }
  static void Check(){
   EditorApplication.update-=Check;
   float volume=UnityEngine.AudioListener.volume;
   var background=InputSystem.settings.backgroundBehavior;var editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
   try{
    UnityEngine.AudioListener.volume=0;
    InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
    InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
    CloudlineVehicleChecks.Run();VehicleAudioChecks.Run();
    UnityEngine.Debug.Log("DRIVE_FEEDBACK_PLAYMODE_OK inputPulse acceptedSubstep sharedEnvelope PCM pause reset countdown");
    SessionState.SetInt(Key+"Result",0);
   }catch(Exception error){UnityEngine.Debug.LogException(error);}
   finally{InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;UnityEngine.AudioListener.volume=volume;EditorApplication.isPlaying=false;}
  }
 }
}
