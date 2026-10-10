using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace StarRacingPrototype {
 public static class GamepadStartChecks {
  static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static void Field(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
  static void Check(bool ok,string message){if(!ok)throw new Exception("START_X: "+message);}
  static void Press(Gamepad pad,bool pressed){var state=Sn30ProState.Neutral;state.buttons=(ushort)(pressed?4:0);InputSystem.QueueStateEvent(pad,state);InputSystem.Update();}
  static void Tick(RaceMenu menu)=>typeof(RaceMenu).GetMethod("Update",Private).Invoke(menu,null);
  public static void Run(){
   var before=SceneManager.GetActiveScene().GetRootGameObjects();
   bool had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);string saved=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);
   Gamepad pad=null;float audio=AudioListener.volume;
   const string playerUpdates="RUN_PLAYER_UPDATES_IN_EDIT_MODE";
   var settings=InputSystem.settings;
   bool savedPlayerUpdates=(bool)typeof(InputSettings).GetMethod("IsFeatureEnabled",Private).Invoke(settings,new object[]{playerUpdates});
   bool editorBatch=!Application.isPlaying;
   try{
    AudioListener.volume=0;
    // EditMode normally updates Editor input state; wasPressedThisFrame needs
    // player update counters when invoking the menu directly in a batch fixture.
    if(editorBatch)settings.SetInternalFeatureFlag(playerUpdates,true);
    Sn30ProGamepad.Register();pad=InputSystem.AddDevice<Sn30ProGamepad>();
    new LocalRaceConfig{humans=1,entrants=8,devices=new[]{SessionControllers.Slot(pad),-1,-2,-1},theme="cloud-city",jumps=false}.Save();
    var root=new GameObject("start shortcut checks");var director=root.AddComponent<RaceDirector>();
    if(!Application.isPlaying)root.SendMessage("Awake");
    var menu=root.GetComponent<RaceMenu>();if(!Application.isPlaying)menu.SendMessage("Awake");
    menu.Open(RaceMenuScreen.LocalSetup,pad);Press(pad,false);
    Action blocked=()=>{Press(pad,true);Tick(menu);Check(!director.Started,"shortcut escaped modal/screen/validation guard");Press(pad,false);};
    menu.Open(RaceMenuScreen.Main);blocked();menu.Open(RaceMenuScreen.Settings);blocked();menu.Open(RaceMenuScreen.LocalSetup);
    Field(menu,"popupOptions",new[]{"Choice"});blocked();Field(menu,"popupOptions",null);
    Field(menu,"nameEditor",0);blocked();Field(menu,"nameEditor",-1);
    Field(menu,"<EditingSeed>k__BackingField",true);blocked();Field(menu,"<EditingSeed>k__BackingField",false);
    string seed=(string)typeof(RaceMenu).GetField("seedText",Private).GetValue(menu);
    Field(menu,"seedText","invalid");blocked();Field(menu,"seedText",seed);
    var balance=director.Balance;Field(director,"<Balance>k__BackingField",null);blocked();Field(director,"<Balance>k__BackingField",balance);
    int device=menu.Selected.devices[0];menu.Selected.devices[0]=int.MaxValue;blocked();menu.Selected.devices[0]=device;
    Check(menu.CanStartSelected,"valid setup cannot start");
    Field(menu,"focus","back");Press(pad,true);Tick(menu);
    Check(director.Started&&director.Session.Phase==RacePhase.Countdown,"X did not start countdown from unrelated focus");
    int generation=director.RaceGeneration;Tick(menu);Check(director.RaceGeneration==generation,"held X started twice");
    director.SetPaused(true);Press(pad,false);Press(pad,true);Tick(menu);Check(director.RaceGeneration==generation&&director.Paused,"X restarted paused race");
    Check(director.ExitToMenu(),"return to menu failed");menu.Open(RaceMenuScreen.LocalSetup);Press(pad,true);Tick(menu);generation=director.RaceGeneration;Check(!director.Started,"held X started after return to menu");
    Press(pad,false);Press(pad,true);Tick(menu);Check(director.Started&&director.RaceGeneration==generation+1,"fresh X did not start repeat race");
    Debug.Log("GAMEPAD_START_CHECKS_OK otherScreens popup name seed invalidSeed missingBalance focusIndependent countdown held pause repeat");
   }finally{
    if(editorBatch)settings.SetInternalFeatureFlag(playerUpdates,savedPlayerUpdates);
    foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())if(Array.IndexOf(before,go)<0)UnityEngine.Object.DestroyImmediate(go);
    if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);
    if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();AudioListener.volume=audio;Time.timeScale=1;
   }
  }
 }
}
