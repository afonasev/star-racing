using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class CursorMusicChecks {
  const string Key="StarRacing.CursorMusicChecks";
  static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
  static int assertions;
  static CursorMusicChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){
    try{CursorContracts();}catch(Exception e){Debug.LogException(e);SessionState.SetBool(Key+"Failed",true);}
    EditorApplication.isPlaying=false;
   }
   if(state==PlayModeStateChange.EnteredEditMode){
    if(!SessionState.GetBool(Key+"Failed",false))try{GamepadStartChecks.Run();VehicleAudioChecks.Run();}catch(Exception e){Debug.LogException(e);SessionState.SetBool(Key+"Failed",true);}
    SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);
   }
  }
  static void Check(bool value,string message){assertions++;if(!value)throw new Exception("CURSOR_MUSIC: "+message);}
  static void Field(object target,string name,object value)=>target.GetType().GetField("<"+name+">k__BackingField",Private).SetValue(target,value);
  static void Tick(RaceMenu menu)=>typeof(RaceMenu).GetMethod("LateUpdate",Private).Invoke(menu,null);
  static void CursorContracts(){
   var settings=InputSystem.settings;
   const string playerUpdates="RUN_PLAYER_UPDATES_IN_EDIT_MODE";
   bool savedPlayerUpdates=(bool)typeof(InputSettings).GetMethod("IsFeatureEnabled",Private).Invoke(settings,new object[]{playerUpdates});
   settings.SetInternalFeatureFlag(playerUpdates,true);
   bool visible=Cursor.visible;var root=new GameObject("Cursor policy fixture");root.SetActive(false);
   var pad=InputSystem.AddDevice<Gamepad>();var mouse=InputSystem.AddDevice<Mouse>();var keyboard=InputSystem.AddDevice<Keyboard>();
   try{
    // Inactive root avoids track generation and saved menu/display settings.
    var director=root.AddComponent<RaceDirector>();var menu=root.AddComponent<RaceMenu>();menu.director=director;Field(menu,"Display",new DisplaySettings());
    var session=new RaceSession(0,100,1);Field(director,"Session",session);Field(director,"Paused",false);
    Action<GamepadState,MouseState> input=(p,m)=>{InputSystem.QueueStateEvent(pad,p);InputSystem.QueueStateEvent(mouse,m);InputSystem.Update();Tick(menu);};
    input(default,default);Check(Cursor.visible,"connected idle pad hides mouse");
    input(new GamepadState().WithButton(GamepadButton.DpadDown),default);Check(!Cursor.visible,"menu gamepad navigation");
    input(default,default);Check(!Cursor.visible,"idle menu loses selected input mode");
    input(default,new MouseState{delta=new Vector2(5,0)});Check(Cursor.visible,"mouse movement fails to restore cursor");
    input(new GamepadState().WithButton(GamepadButton.Start),default);Check(!Cursor.visible,"Start button fails to select gamepad");
    input(default,new MouseState().WithButton(MouseButton.Left));Check(Cursor.visible,"stationary mouse click fails to restore cursor");
    menu.Open(RaceMenuScreen.Settings);
    input(new GamepadState{leftStick=new Vector2(0,.8f)},default);Check(!Cursor.visible,"settings stick navigation");
    InputSystem.QueueStateEvent(pad,default(GamepadState));InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.DownArrow));InputSystem.Update();Tick(menu);Check(Cursor.visible,"keyboard navigation fails to restore cursor");
    InputSystem.QueueStateEvent(keyboard,default(KeyboardState));InputSystem.Update();
    Field(director,"Started",true);session.Begin();
    input(default,new MouseState{delta=new Vector2(5,0)});Check(!Cursor.visible,"countdown cursor visible with mouse");
    session.Tick(3,new[]{new RaceObservation(0)});
    input(default,new MouseState().WithButton(MouseButton.Left));Check(!Cursor.visible,"racing cursor visible with mouse");
    Field(director,"Paused",true);Tick(menu);Check(Cursor.visible,"mouse pause cursor hidden");
    input(new GamepadState().WithButton(GamepadButton.South),default);Check(!Cursor.visible,"gamepad pause cursor visible");
    menu.OpenPauseSettings();Tick(menu);Check(!Cursor.visible,"gamepad pause settings cursor visible");
    input(default,new MouseState{scroll=new Vector2(0,1)});Check(Cursor.visible,"pause settings scroll fails to restore cursor");
    Field(director,"Paused",false);Tick(menu);Check(!Cursor.visible,"resuming fails to hide cursor");
    Field(session,"Phase",RacePhase.Results);Tick(menu);Check(Cursor.visible,"mouse results cursor hidden");
    input(new GamepadState().WithButton(GamepadButton.DpadUp),default);Check(!Cursor.visible,"gamepad results cursor visible");
    Field(director,"Started",false);Field(menu,"ScreenState",RaceMenuScreen.LocalSetup);Tick(menu);Check(!Cursor.visible,"return to menu loses gamepad mode");
    typeof(RaceMenu).GetMethod("OnApplicationFocus",Private).Invoke(menu,new object[]{false});Tick(menu);Check(Cursor.visible,"focus loss hides system cursor");
    typeof(RaceMenu).GetMethod("OnApplicationFocus",Private).Invoke(menu,new object[]{true});Tick(menu);Check(!Cursor.visible,"focus restoration loses gamepad mode");
    typeof(RaceMenu).GetMethod("OnDisable",Private).Invoke(menu,null);Check(Cursor.visible,"disabling menu leaves cursor hidden");
    Check(Math.Abs(RaceAudioPolicy.MusicGain-.26f*.5f)<.000001f,"music base gain is not halved");
    Debug.Log("CURSOR_MUSIC_PLAYMODE_OK assertions="+assertions+" menu mouse keyboard gamepad countdown racing pause settings results focus disable");
   }finally{
    settings.SetInternalFeatureFlag(playerUpdates,savedPlayerUpdates);
    UnityEngine.Object.DestroyImmediate(root);InputSystem.RemoveDevice(pad);InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);Cursor.visible=visible;
   }
  }
 }
}
