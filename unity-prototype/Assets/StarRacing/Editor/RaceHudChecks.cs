using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class RaceHudChecks {
  const string Key="StarRacing.HudProof";
  static string output;static int state,humans,assertions;static double next,deadline;
  static RaceDirector director;static RaceMenu menu;static readonly Gamepad[] pads=new Gamepad[4];
  static bool hadPreference;static string preference;static bool restoring;
  static RaceHudChecks(){EditorApplication.playModeStateChanged+=PlayState;}
  static void Check(bool value,string message){assertions++;if(!value)throw new InvalidOperationException("HUD_CHECK "+message);}
  public static void Run(){
   assertions=0;
   foreach(int n in new[]{1,2,3,4})for(int seat=0;seat<n;seat++){
    var view=RaceHudLayout.View(n,seat);var meter=RaceHudLayout.Instrument(n,seat);var position=RaceHudLayout.Position(n,seat);
    Check(view.Contains(meter.min)&&view.Contains(meter.max-Vector2.one),"instrument inside view");
    Check(view.Contains(position.min)&&view.Contains(position.max-Vector2.one),"position inside view");
    Check(meter.width<=108&&meter.height<=54,"compact instrument");
    Check(!meter.Overlaps(position),"position separate from instrument");
    bool right=n>1&&seat%2==1,top=n>2&&seat<2;
    Check(right?meter.xMin>view.center.x:meter.xMax<view.center.x,"instrument outer side");
    Check(right?position.xMax<view.center.x:position.xMin>view.center.x,"position inner side (solo upper right)");
    Check((top?meter.yMax<view.center.y:meter.yMin>view.center.y)&&position.yMax<view.center.y,"instrument outer corner and position at top");
    Check(Mathf.Abs((right?position.x-view.x:view.xMax-position.xMax)-14)<.01f,"position close to inner edge");
    Check(RaceHudLayout.View(n,seat).yMax<=900,"no reserved camera band");
   }
   Check(RaceHudLayout.ProgressArea.width==960&&RaceHudLayout.ProgressArea.center.x==800,"60 percent centered overlay");
   Check(RaceHudLayout.ProgressLine.height<=2,"thin line");
   Check(RaceHudLayout.InstrumentOpacity>=.7f&&RaceHudLayout.InstrumentOpacity<=.9f,"readable dark edge with inward fade");
   float left=RaceHudLayout.ProgressLine.x,rightEdge=RaceHudLayout.ProgressLine.xMax;
   Check(RaceHudLayout.Marker(-1,100).x==left,"negative progress clamp");
   Check(RaceHudLayout.Marker(0,100).x==left,"start position");
   Check(RaceHudLayout.Marker(50,100).x==(left+rightEdge)/2,"true horizontal position");
   Check(RaceHudLayout.Marker(101,100).x==rightEdge,"finish clamp");
   Check(RaceHudLayout.Marker(float.NaN,100).x==left&&RaceHudLayout.Marker(1,0).x==left,"invalid progress finite");
   foreach(int count in new[]{8,16,32,64}){
    var race=new RaceSession(100,200,count);for(int i=0;i<count;i++){
     var marker=RaceHudLayout.Marker(race.Racers[i].Progress,race.RaceLength);
     Check(marker.x>=left&&marker.x<=rightEdge&&marker.y==RaceHudLayout.ProgressLine.center.y,"every entrant on same rail");
    }
   }
   var vehicleObject=new GameObject("HUD Editor missing-component regression");PhysicsMaterial first=null,last=null;
   try{
    var vehicle=vehicleObject.AddComponent<MagneticVehicle>();var ensure=typeof(MagneticVehicle).GetMethod("EnsurePhysics",BindingFlags.Instance|BindingFlags.NonPublic);
    ensure.Invoke(vehicle,null);Check(vehicle.Body!=null&&vehicleObject.GetComponent<BoxCollider>()!=null,"missing components created under Unity fake null");
    var body=vehicle.Body;var collider=vehicleObject.GetComponent<BoxCollider>();first=collider.sharedMaterial;
    ensure.Invoke(vehicle,null);last=collider.sharedMaterial;Check(vehicle.Body==body&&vehicleObject.GetComponent<BoxCollider>()==collider,"existing components reused");
    Check(vehicleObject.GetComponents<Rigidbody>().Length==1&&vehicleObject.GetComponents<BoxCollider>().Length==1,"no duplicate physics components");
   }finally{UnityEngine.Object.DestroyImmediate(vehicleObject);if(first!=null)UnityEngine.Object.DestroyImmediate(first);if(last!=null&&last!=first)UnityEngine.Object.DestroyImmediate(last);}
   Debug.Log("RACE_HUD_CHECKS_OK assertions="+assertions+" token="+(Environment.GetEnvironmentVariable("STAR_RACING_QA_TOKEN")??"manual"));
  }
  // Opt-in Editor PlayMode rendering probe. Does not call any Player build route.
  public static void Visual(){
   output=Environment.GetEnvironmentVariable("STAR_RACING_HUD_PROOF_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Missing HUD proof directory");
   Directory.CreateDirectory(output);Run();
   SessionState.SetBool(Key+".Background",File.ReadAllText(Path.Combine(Application.dataPath,"../ProjectSettings/ProjectSettings.asset")).Contains("  runInBackground: 1"));Application.runInBackground=true;EditorApplication.isPaused=false;SessionState.SetString(Key,output);
   EditorSceneManager.OpenScene("Assets/StarRacing/Generated/Prototype.unity");
   var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);
   view.Show();view.Focus();view.maximized=true;EditorApplication.isPlaying=true;
  }
  static void PlayState(PlayModeStateChange change){
   if(change==PlayModeStateChange.EnteredPlayMode){output=SessionState.GetString(Key,"");if(output.Length==0)return;
    try{
     Application.runInBackground=true;
     hadPreference=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);preference=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);restoring=true;
     for(int i=0;i<4;i++)pads[i]=InputSystem.AddDevice<Gamepad>();
     director=UnityEngine.Object.FindAnyObjectByType<RaceDirector>();menu=director.GetComponent<RaceMenu>();Check(menu!=null,"race initialization provides menu");
     var audio=director.GetComponent<RaceAudioCoordinator>();if(audio!=null)audio.enabled=false;AudioListener.volume=0;
     state=Environment.GetEnvironmentVariable("STAR_RACING_HUD_RESULTS_ONLY")=="1"?6:0;humans=1;next=EditorApplication.timeSinceStartup+.8;deadline=EditorApplication.timeSinceStartup+150;
     EditorApplication.update+=Tick;
    }catch(Exception e){Fail(e);}
   }else if(change==PlayModeStateChange.ExitingPlayMode)Restore();
   else if(change==PlayModeStateChange.EnteredEditMode&&SessionState.GetString(Key,"").Length>0){output=SessionState.GetString(Key,"");SessionState.EraseString(Key);
    if(!File.Exists(Path.Combine(output,"failure.txt")))try{
     PlayerSettings.runInBackground=SessionState.GetBool(Key+".Background",false);
     Run();File.WriteAllText(Path.Combine(output,"ui-suite-success.json"),"{\"success\":true,\"scope\":\"local-ui\",\"complete_suite\":false,\"player_build\":false}");
    }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());}
    EditorApplication.Exit(File.Exists(Path.Combine(output,"failure.txt"))?1:0);}
  }
  static void Set(object obj,string property,object value){obj.GetType().GetProperty(property).SetValue(obj,value);}
  static void Shot(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));}
  static void Tick(){
   if(EditorApplication.timeSinceStartup>deadline){Fail(new TimeoutException("HUD proof exceeded deadline"));return;}
   if(EditorApplication.timeSinceStartup<next)return;
   try{
    next=EditorApplication.timeSinceStartup+.6;
    switch(state){
     case 0:
      menu.Open(RaceMenuScreen.LocalSetup);var chosen=menu.Selected;chosen.humans=humans;chosen.entrants=64;chosen.devices=new int[4];for(int i=0;i<4;i++)chosen.devices[i]=SessionControllers.Slot(pads[i]);
      chosen.seed="77";chosen.theme=humans==4?"space-station":"cloud-city";chosen.jumps=false;chosen.rails="normal";
      typeof(RaceMenu).GetField("seedText",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,"77");menu.StartSelected();
      Check(director.Started&&director.HumanCount==humans&&director.Cars.Length==64,"playmode roster "+humans);state=1;break;
     case 1: Shot("countdown-"+humans);state=2;break;
     case 2:
      // Countdown advances on physics ticks, not Editor wall time (cold shader imports may stall frames).
      if(director.Session.Phase==RacePhase.Countdown){
       // Autonomous rendering must not depend on which desktop app currently has focus.
       if(director.Paused&&!director.Input.MissingDevice)director.SetPaused(false);
       break;
      }
      Check(director.Session.Phase==RacePhase.Racing,"countdown to race "+humans);
      // Freeze only simulation update for readable fixture values; rendering remains actual HUD.
      director.enabled=false;Time.timeScale=0;
      for(int i=0;i<64;i++){Set(director.Session.Racers[i],"Progress",director.Session.RaceLength*(.02f+.76f*i/63));Set(director.Session.Racers[i],"Place",64-i);}
      for(int i=0;i<humans;i++){Set(director.Session.Racers[i],"Progress",director.Session.RaceLength*(.79f-i*.14f));Set(director.Session.Racers[i],"Place",humans==4&&i==3?64:1+i*12);director.Cars[i].Telemetry.speedKmh=218-i*7;director.Cars[i].Telemetry.nitro01=.76f-i*.14f;}
      state=3;break;
     case 3: Shot("race-"+humans+"-64-fixture-progress");state=4;break;
     case 4:
      director.enabled=true;Time.timeScale=1;director.Restart();Check(director.Session.Phase==RacePhase.Countdown,"repeat "+humans);
      director.SetPaused(true);Check(director.Paused,"pause "+humans);state=5;break;
     case 5: Shot("pause-"+humans);state=15;break;
     case 15: director.StartRace();Check(!director.Paused,"resume "+humans);director.SetPaused(true);Check(director.ExitToMenu()&&!director.Started,"menu return "+humans);
      if(++humans<=4)state=0;else state=6;break;
     case 6:
      // Small roster and Results fixture verify HUD hide/show independently of natural finish acceptance.
      var config=menu.Selected;config.humans=1;config.entrants=8;menu.StartSelected();state=7;break;
     case 7: Shot("solo-8-countdown");state=17;break;
     case 17: for(int i=0;i<director.Session.Racers.Length;i++)Set(director.Session.Racers[i],"Place",i+1);Set(director.Session,"Phase",RacePhase.Results);state=8;break;
     case 8: Shot("results-fixture");state=18;break;
     case 18: Check(director.ExitToMenu(),"Results menu return");state=9;break;
     case 9:
      Shot("menu-return");state=10;break;
     case 10:
      File.WriteAllText(Path.Combine(output,"success.json"),"{\"success\":true,\"editor_playmode\":true,\"player_build\":false,\"seed\":77,\"fixtures\":true,\"natural_finish\":false,\"physical_devices\":false,\"human_acceptance\":false,\"assertions\":"+assertions+"}");
      Debug.Log("RACE_HUD_EDITOR_VISUAL_OK");Restore();EditorApplication.isPlaying=false;break;
    }
   }catch(Exception e){Fail(e);}
  }
  static void Fail(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Restore();EditorApplication.isPlaying=false;}
  static void Restore(){EditorApplication.update-=Tick;if(!restoring)return;restoring=false;
   if(hadPreference)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,preference);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();
   foreach(var pad in pads)if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);Time.timeScale=1;Application.runInBackground=SessionState.GetBool(Key+".Background",false);PlayerSettings.runInBackground=SessionState.GetBool(Key+".Background",false);
  }
 }
}
