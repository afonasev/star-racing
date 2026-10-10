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
  static object sizeGroup;static EditorWindow captureView;static int originalSize=-1,addedSize=-1;
  static bool hadPreference;static string preference;static bool restoring;
  static RaceHudChecks(){EditorApplication.playModeStateChanged+=PlayState;}
  static void Check(bool value,string message){assertions++;if(!value)throw new InvalidOperationException("HUD_CHECK "+message);}
  public static void Run(){
   assertions=0;
   foreach(int n in new[]{1,2,3,4})for(int seat=0;seat<n;seat++){
    var view=RaceHudLayout.View(n,seat);var meter=RaceHudLayout.Instrument(n,seat);var position=RaceHudLayout.Position(n,seat);
    Check(view.Contains(meter.min)&&view.Contains(meter.max-Vector2.one),"instrument inside view");
    Check(view.Contains(position.min)&&view.Contains(position.max-Vector2.one),"position inside view");
    Check(meter.width==176&&meter.height==80,"readable instrument size");
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
  public static void RunTextScaleChecks(){
   int previous=assertions;assertions=0;
   foreach(float scale in new[]{1f,1.2f,2.4f}){
    var matrix=Matrix4x4.TRS(new Vector3(17,23,0),Quaternion.identity,Vector3.one*scale);
    float raster=CloudlineSkin.TextScale(matrix);var rect=new Rect(10,20,100,40);var physical=CloudlineSkin.ScaledRect(rect,raster);
    var compensated=matrix*Matrix4x4.Scale(new Vector3(1/raster,1/raster,1));
    Check(Mathf.Abs(raster-scale)<.001f,"font raster follows 900p/1080p/4K scale");
    Check((compensated.MultiplyPoint3x4(physical.min)-matrix.MultiplyPoint3x4(rect.min)).sqrMagnitude<.001f,"sharp text preserves origin");
    Check((compensated.MultiplyPoint3x4(physical.max)-matrix.MultiplyPoint3x4(rect.max)).sqrMagnitude<.001f,"sharp text preserves bounds");
   }
   Debug.Log("HUD_TEXT_SCALE_CHECKS_OK assertions="+assertions);assertions=previous;
  }
  // Opt-in Editor PlayMode rendering probe. Does not call any Player build route.
  public static void Visual(){
   output=Environment.GetEnvironmentVariable("STAR_RACING_HUD_PROOF_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Missing HUD proof directory");
   Directory.CreateDirectory(output);Run();RunTextScaleChecks();
   SessionState.SetBool(Key+".Background",File.ReadAllText(Path.Combine(Application.dataPath,"../ProjectSettings/ProjectSettings.asset")).Contains("  runInBackground: 1"));Application.runInBackground=true;EditorApplication.isPaused=false;SessionState.SetString(Key,output);
   EditorSceneManager.OpenScene("Assets/StarRacing/Generated/Prototype.unity");
   var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);
   view.Show();view.maximized=true;SetCaptureSize(3840,2160);EditorApplication.isPlaying=true;
  }
  static void SetCaptureSize(int width,int height){
   if(Environment.GetEnvironmentVariable("STAR_RACING_HUD_4K_PROOF")!="1")return;
   var assembly=typeof(Editor).Assembly;var sizesType=assembly.GetType("UnityEditor.GameViewSizes");
   var sizes=sizesType.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
   var getGroup=sizesType.GetMethod("GetGroup");sizeGroup=getGroup.Invoke(sizes,new[]{Enum.ToObject(getGroup.GetParameters()[0].ParameterType,0)});
   captureView=EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));var selected=captureView.GetType().GetProperty("selectedSizeIndex",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
   originalSize=SessionState.GetInt(Key+".OriginalSize",(int)selected.GetValue(captureView));
   SessionState.SetInt(Key+".OriginalSize",originalSize);addedSize=SessionState.GetInt(Key+".AddedSize",-1);
   if(addedSize>=0)sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(sizeGroup,new object[]{(int)sizeGroup.GetType().GetMethod("GetBuiltinCount").Invoke(sizeGroup,null)+addedSize});
   addedSize=(int)sizeGroup.GetType().GetMethod("GetCustomCount").Invoke(sizeGroup,null);SessionState.SetInt(Key+".AddedSize",addedSize);
   var size=Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"),new[]{Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"),1),(object)width,height,"HUD owned proof"});
   sizeGroup.GetType().GetMethod("AddCustomSize").Invoke(sizeGroup,new[]{size});
   selected.SetValue(captureView,(int)sizeGroup.GetType().GetMethod("GetBuiltinCount").Invoke(sizeGroup,null)+addedSize);captureView.Repaint();SessionState.SetBool(Key+".Capture",true);
  }
  static void RestoreCaptureSize(){
   if(!SessionState.GetBool(Key+".Capture",false))return;
   SetCaptureSize(3840,2160);
   captureView.GetType().GetProperty("selectedSizeIndex",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(captureView,originalSize);
   if(addedSize>=0)sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(sizeGroup,new object[]{(int)sizeGroup.GetType().GetMethod("GetBuiltinCount").Invoke(sizeGroup,null)+addedSize});int builtin=(int)sizeGroup.GetType().GetMethod("GetBuiltinCount").Invoke(sizeGroup,null);
   for(int i=builtin+(int)sizeGroup.GetType().GetMethod("GetCustomCount").Invoke(sizeGroup,null)-1;i>=builtin;i--){
    var size=sizeGroup.GetType().GetMethod("GetGameViewSize").Invoke(sizeGroup,new object[]{i});
    if((string)size.GetType().GetProperty("baseText").GetValue(size)=="HUD owned proof")sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(sizeGroup,new object[]{i});
   }
   originalSize=addedSize=-1;SessionState.EraseInt(Key+".OriginalSize");SessionState.EraseInt(Key+".AddedSize");SessionState.EraseBool(Key+".Capture");
  }
  static void PlayState(PlayModeStateChange change){
   if(change==PlayModeStateChange.EnteredPlayMode){output=SessionState.GetString(Key,"");if(output.Length==0)return;
    try{
     Application.runInBackground=true;
     hadPreference=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);preference=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);restoring=true;
     for(int i=0;i<4;i++)pads[i]=InputSystem.AddDevice<Gamepad>();
     director=null;menu=null;AudioListener.volume=0;
     state=Environment.GetEnvironmentVariable("STAR_RACING_HUD_RESULTS_ONLY")=="1"?6:Environment.GetEnvironmentVariable("STAR_RACING_HUD_4K_PROOF")=="1"?-10:0;humans=1;next=EditorApplication.timeSinceStartup+.8;deadline=EditorApplication.timeSinceStartup+150;
     EditorApplication.update+=Tick;
    }catch(Exception e){Fail(e);}
   }else if(change==PlayModeStateChange.ExitingPlayMode)Restore();
   else if(change==PlayModeStateChange.EnteredEditMode&&SessionState.GetString(Key,"").Length>0){output=SessionState.GetString(Key,"");SessionState.EraseString(Key);
    RestoreCaptureSize();
    if(!File.Exists(Path.Combine(output,"failure.txt")))try{
     PlayerSettings.runInBackground=SessionState.GetBool(Key+".Background",false);
     Run();RunTextScaleChecks();DisplaySettingsChecks.Run();File.WriteAllText(Path.Combine(output,"ui-suite-success.json"),"{\"success\":true,\"scope\":\"local-ui\",\"complete_suite\":false,\"player_build\":false}");
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
    if(director==null){director=UnityEngine.Object.FindAnyObjectByType<RaceDirector>();if(director==null)return;menu=director.GetComponent<RaceMenu>();Check(menu!=null,"race initialization provides menu");var audio=director.GetComponent<RaceAudioCoordinator>();if(audio!=null)audio.enabled=false;}
    switch(state){
     case -10: Shot("main-4k");state=-11;break;
     case -11: menu.Open(RaceMenuScreen.LocalSetup);state=-12;break;
     case -12: Shot("setup-4k");state=-13;break;
     case -13: menu.Open(RaceMenuScreen.Settings);state=-14;break;
     case -14: Shot("settings-4k");state=-15;break;
     case -15: if(Environment.GetEnvironmentVariable("STAR_RACING_HUD_TEXT_ONLY")=="1"){
      var controls=(System.Collections.IEnumerable)typeof(RaceMenu).GetField("controls",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(menu);
      bool clicked=false;foreach(var control in controls)if((string)control.GetType().GetField("id").GetValue(control)=="resolution"){((Action)control.GetType().GetField("action").GetValue(control))();clicked=true;break;}
      Check(clicked&&menu.PopupOpen,"resolution popup rendered for text clip proof");state=-17;
     }else{menu.Open(RaceMenuScreen.Main);state=0;}break;
     case -17: Shot("resolution-popup-4k");state=-18;break;
     case -18: SetCaptureSize(1920,1080);menu.Open(RaceMenuScreen.LocalSetup);state=-16;break;
     case -16: Shot("setup-1080p");state=10;break;
     case 0:
      menu.Open(RaceMenuScreen.LocalSetup);var chosen=menu.Selected;chosen.humans=humans;chosen.entrants=64;chosen.devices=new int[4];for(int i=0;i<4;i++)chosen.devices[i]=SessionControllers.Slot(pads[i]);
      chosen.seed="77";chosen.theme=humans==4?"space-station":"cloud-city";chosen.jumps=false;chosen.rails="normal";
      typeof(RaceMenu).GetField("seedText",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,"77");menu.StartSelected();
      state=-1;break;
     case -1: if(menu.Loading)break;Check(director.Started&&director.HumanCount==humans&&director.Cars.Length==64,"playmode roster "+humans);state=1;break;
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
     case 3: Shot("race-"+humans+"-64-fixture-progress");if(Environment.GetEnvironmentVariable("STAR_RACING_HUD_4K_PROOF")=="1"){state=23;}else state=4;break;
     case 23: SetCaptureSize(1920,1080);state=24;break;
     case 24: Shot("race-"+humans+"-1080p");state=25;break;
     case 25: SetCaptureSize(3840,2160);state=4;break;
     case 4:
      director.enabled=true;Time.timeScale=1;director.Restart();Check(director.Session.Phase==RacePhase.Countdown,"repeat "+humans);
      director.SetPaused(true);Check(director.Paused,"pause "+humans);state=5;break;
     case 5: Shot("pause-"+humans);state=15;break;
     case 15: director.StartRace();Check(!director.Paused,"resume "+humans);director.SetPaused(true);Check(director.ExitToMenu()&&!director.Started,"menu return "+humans);
      if(++humans<=4)state=0;else state=6;break;
     case 6:
      // Small roster and Results fixture verify HUD hide/show independently of natural finish acceptance.
      var config=menu.Selected;config.humans=1;config.entrants=8;menu.StartSelected();state=7;break;
     case 7: if(menu.Loading)break;Shot("solo-8-countdown");state=17;break;
     case 17: for(int i=0;i<director.Session.Racers.Length;i++)Set(director.Session.Racers[i],"Place",i+1);Set(director.Session,"Phase",RacePhase.Results);state=8;break;
     case 8: Shot("results-fixture");state=18;break;
     case 18: Check(director.ExitToMenu(),"Results menu return");if(Environment.GetEnvironmentVariable("STAR_RACING_HUD_TEXT_ONLY")=="1"){menu.Open(RaceMenuScreen.Main);state=-10;}else state=9;break;
     case 9:
      Shot("menu-return");state=10;break;
     case 10:
      File.WriteAllText(Path.Combine(output,"success.json"),"{\"success\":true,\"editor_playmode\":true,\"player_build\":false,\"race_lifecycle\":"+(Environment.GetEnvironmentVariable("STAR_RACING_HUD_TEXT_ONLY")=="1"?"false":"true")+",\"fixtures\":true,\"natural_finish\":false,\"physical_devices\":false,\"human_acceptance\":false,\"assertions\":"+assertions+"}");
      Debug.Log(Environment.GetEnvironmentVariable("STAR_RACING_HUD_TEXT_ONLY")=="1"?"HUD_TEXT_EDITOR_VISUAL_OK":"RACE_HUD_EDITOR_VISUAL_OK");Restore();EditorApplication.isPlaying=false;break;
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
