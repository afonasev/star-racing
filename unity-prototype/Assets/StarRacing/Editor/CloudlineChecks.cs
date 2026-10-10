using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace StarRacingPrototype {
 public static class CloudlineChecks {
  static int assertions;
  static void Check(bool condition,string message){assertions++;if(!condition)throw new Exception("CLOUDLINE: "+message);}
  public static void Run(){
   assertions=0;
   var bad=LocalRaceConfig.Normalize(new LocalRaceConfig{entrants=2,seed="bad",theme="missing",rails="missing",humans=7,devices=new[]{2,2}});
   Check(bad.entrants==8&&bad.Seed==77&&bad.theme=="random"&&bad.rails=="normal"&&bad.humans==4,"legacy / invalid configuration migration");
   var config=new LocalRaceConfig();config.seed="";Check(!config.Validate(out _),"empty seed accepted");config.seed="4294967296";Check(!config.Validate(out _),"overflow seed accepted");config.seed="4294967295";Check(config.Validate(out _),"uint max refused");
   config.devices[1]=config.devices[0];Check(!config.Validate(out _),"duplicate input accepted");
   foreach(int humans in new[]{1,2,3,4})foreach(int count in LocalRaceConfig.ParticipantCounts){
    var roster=new RaceRoster(count,humans,77);int total=0;foreach(var entrant in roster.Entrants)if(entrant.HumanSeat>=0)total++;
    Check(total==humans&&roster.Entrants.Length==count,"human/AI count mismatch");
    for(int i=0;i<humans;i++){
     var r=RaceViewports.For(humans,i);Check(r.width>0&&r.height>0&&r.x>=0&&r.y>=0&&r.xMax<=1&&r.yMax<=1,"viewport bounds");
     for(int j=0;j<i;j++)Check(!r.Overlaps(RaceViewports.For(humans,j)),"overlapping viewport");
    }
   }
   config=new LocalRaceConfig();uint oldSeed=config.Seed;config.RandomizeSeed();Check(config.Seed!=oldSeed,"dice reused previous seed");
   Check(LocalRaceConfig.CleanName("ШШШШШШШШШШШШ\n").Length==10,"wide-name limit");
   Check(LocalRaceConfig.CleanName("Имя\n\t")=="Имя","name contains controls");
   var empty=new LocalRaceConfig{humans=0};Check(empty.Validate(out _)&&!LocalInputRouter.CanBind(empty,out _),"empty draft must save but not start");
   bool existing=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);string previous=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);
   try{
    config=new LocalRaceConfig{entrants=32,humans=4,seed="4294967295",theme="cloud-city",rails="full",jumps=true,handicap=true,devices=new[]{0,1,2,3}};config.Save();
    var restored=LocalRaceConfig.Load();Check(JsonUtility.ToJson(config)==JsonUtility.ToJson(restored),"roundtrip loses configuration");
    var copy=config.Copy();copy.seed="12";Check(config.seed=="4294967295","active snapshot aliases selected settings");
   }finally{if(existing)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,previous);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();}
   var owned=new Gamepad[4];Keyboard keyboard=null;
   try{
    keyboard=InputSystem.AddDevice<Keyboard>();
    // Isolated batch process only. Existing hardware is not removed or rebound.
    for(int i=0;i<4;i++)owned[i]=InputSystem.AddDevice<Gamepad>();
    config=new LocalRaceConfig{humans=4,devices=new[]{-2,-1,0,1}};var router=new LocalInputRouter();Check(router.TryBind(config,keyboard,owned,out _),"mixed four-seat bind");
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));InputSystem.Update();router.PrepareStart();
    Check(router.Read(0).throttle==1&&router.Read(1).throttle==0&&router.Read(2).throttle==0&&router.Read(3).throttle==0,"WASD leaks into other seats");
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.UpArrow));InputSystem.Update();
    Check(router.Read(0).throttle==0&&router.Read(1).throttle==1&&router.Read(2).throttle==0&&router.Read(3).throttle==0,"arrows leak into other seats");
    InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(owned[0],new GamepadState{rightTrigger=1});InputSystem.Update();
    Check(router.Read(2).throttle==1&&router.Read(3).throttle==0,"gamepad 1 leaks into seat 4");
    router.Block();router.Refresh();Check(router.Read(2).throttle==0,"held input escapes resume gate");
    InputSystem.QueueStateEvent(owned[0],new GamepadState());InputSystem.Update();router.Refresh();
    InputSystem.QueueStateEvent(owned[0],new GamepadState{rightTrigger=1});InputSystem.Update();Check(router.Read(2).throttle==1,"release gate never reopens");
    config=new LocalRaceConfig{humans=4,devices=new[]{0,1,2,3}};Check(router.TryBind(config,keyboard,owned,out _),"four-pad bind");
    for(int i=0;i<4;i++)InputSystem.QueueStateEvent(owned[i],new GamepadState{rightTrigger=(i+1)*.2f});InputSystem.Update();router.PrepareStart();
    for(int i=0;i<4;i++)Check(Mathf.Abs(router.Read(i).throttle-(i+1)*.2f)<.05f,"four-pad independent throttle");
    int first=SessionControllers.Slot(owned[0]),second=SessionControllers.Slot(owned[1]);
    SessionControllers.Remember(first,"Штурман");InputSystem.RemoveDevice(owned[0]);
    Check(SessionControllers.Resolve(first)==null&&SessionControllers.Resolve(second)==owned[1],"disconnect moved another pad");
    InputSystem.AddDevice(owned[0]);Check(SessionControllers.Slot(owned[0])==first&&SessionControllers.Name(first,"")=="Штурман","reconnect lost identity/name");
    var saved=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);bool had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);GameObject menuObject=null;
    try{
     new LocalRaceConfig{humans=0}.Save();menuObject=new GameObject("Cloudline isolated menu checks");var menu=menuObject.AddComponent<RaceMenu>();menuObject.SendMessage("Awake");
     Check(menu.JoinDevice(first)&&!menu.JoinDevice(first),"duplicate Y join");Check(menu.Selected.names[0]=="Штурман","join did not restore name");
     Check(menu.JoinDevice(second)&&menu.JoinDevice(-2)&&menu.JoinDevice(-1)&&!menu.JoinDevice(SessionControllers.Slot(owned[2])),"four-seat cap");
     menu.RemoveSeat(0);Check(menu.Selected.humans==3&&menu.Selected.devices[0]==second&&menu.Selected.devices[1]==-2,"remove did not compact device/name pair");
     uint before=menu.Selected.Seed;menu.Open(RaceMenuScreen.LocalSetup);Check(menu.Selected.Seed!=before,"setup entry kept seed");
     before=menu.Selected.Seed;menu.Open(RaceMenuScreen.Settings);Check(menu.Selected.Seed==before,"settings changed seed");
    }finally{if(menuObject!=null)UnityEngine.Object.DestroyImmediate(menuObject);if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();}
   }finally{foreach(var pad in owned)if(pad!=null)InputSystem.RemoveDevice(pad);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);}
   RaceHudChecks.Run();
   Debug.Log("CLOUDLINE_CHECKS_OK assertions="+assertions);
  }
 }
}
