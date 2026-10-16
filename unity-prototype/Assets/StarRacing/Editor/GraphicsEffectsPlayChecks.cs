using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class GraphicsEffectsPlayChecks {
  const string Key="StarRacing.GraphicsPlay";static int frames;static double deadline;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPHICS_QA_DIR");
  public static void Trace(string message){if(!string.IsNullOrEmpty(Output))File.AppendAllText(Path.Combine(Output,"progress.txt"),DateTime.UtcNow.ToString("O")+" "+message+"\n");}
  static GraphicsEffectsPlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("Graphics QA output required");Directory.CreateDirectory(Output);
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/StarRacing/Generated/PrototypeRenderer.asset");
   PrototypeBuilder.ConfigureGraphicsBindings(VehicleContactShadowsRendererFeature.EnsureConfigured(renderer));
   DisplaySettingsChecks.Run();VehicleContactShadowsChecks.Run();
   File.WriteAllBytes(Path.Combine(Output,"renderer-before.asset"),File.ReadAllBytes("Assets/StarRacing/Generated/PrototypeRenderer.asset"));
   SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"),false,"Game",false);view.position=new Rect(0,0,1600,940);view.Show();frames=0;EditorApplication.update+=Enter;
  }
  static void Enter(){if(++frames<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;Trace("state "+state);if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Watch;new GameObject("Graphics contract fixture").AddComponent<GraphicsEffectsPlayDriver>();}if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Watch;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}}
  static void Watch(){if(EditorApplication.timeSinceStartup>deadline){File.WriteAllText(Path.Combine(Output,"failure.txt"),"Graphics fixture timeout");Finish(false);}}
  public static void Finish(bool success){Trace("Finish success="+success);SessionState.SetBool(Key+"Failed",!success);EditorApplication.isPlaying=false;}
 }
 public sealed class GraphicsEffectsPlayDriver:MonoBehaviour {
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  readonly Dictionary<string,string> saved=new Dictionary<string,string>();readonly Gamepad[] pads=new Gamepad[4];
  string previousSettingsPath;UserSettings previousSettings;
  RaceDirector director;RaceMenu menu;int assertions;bool restored,nitroHad,nitroCaptured;int nitroPrevious;
  string Output=>GraphicsEffectsPlayChecks.Output;
  void Check(bool ok,string message){assertions++;if(!ok)throw new Exception("Graphics: "+message);}
  IEnumerator Start(){
   previousSettingsPath=UserSettingsStore.OverridePath;previousSettings=UserSettingsStore.Current;UserSettingsStore.OverridePath=Path.Combine(Output,"preferences.json");UserSettingsStore.Replace(new UserSettings());
   foreach(var key in new[]{RaceGraphicsSettings.PreferenceKey,LocalRaceConfig.PreferenceKey})saved[key]=PlayerPrefs.HasKey(key)?PlayerPrefs.GetString(key):null;
   // Nitro is an integer preference, retained separately rather than changing its storage.
   nitroHad=PlayerPrefs.HasKey(NitroBlurSettings.PreferenceKey);nitroPrevious=PlayerPrefs.GetInt(NitroBlurSettings.PreferenceKey);nitroCaptured=true;
   File.WriteAllText(Path.Combine(Output,"preferences-before.json"),JsonUtility.ToJson(new Backup{preferences=saved.Select(p=>new Saved{key=p.Key,existed=p.Value!=null,value=p.Value}).ToArray(),nitroExisted=nitroHad,nitroValue=nitroPrevious},true));
   var routines=new Stack<IEnumerator>();routines.Push(Run());bool success=true;
   while(routines.Count>0){object next=null;bool more=false;Exception error=null;try{var routine=routines.Peek();more=routine.MoveNext();if(more)next=routine.Current;else{routines.Pop();(routine as IDisposable)?.Dispose();}}catch(Exception e){error=e;}
    if(error!=null){Debug.LogException(error);File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());success=false;break;}if(!more)continue;if(next is IEnumerator nested){routines.Push(nested);continue;}yield return next;
   }
   Restore();
   if(success){File.WriteAllText(Path.Combine(Output,"success.json"),"{\"success\":true,\"assertions\":"+assertions+",\"playMode\":true,\"physicalAcceptance\":false}");Debug.Log("GRAPHICS_EFFECTS_PLAY_OK assertions="+assertions);}
   GraphicsEffectsPlayChecks.Finish(success);
  }
  IEnumerator Frames(int count=8){for(int i=0;i<count;i++)yield return null;}
  IEnumerator Shot(string name){ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));yield return Frames(12);}
  void Click(string id){foreach(var control in (IEnumerable)typeof(RaceMenu).GetField("controls",Private).GetValue(menu)){var type=control.GetType();if((string)type.GetField("id").GetValue(control)==id){((Action)type.GetField("action").GetValue(control))();return;}}throw new Exception("Missing actual UI control "+id);}
  TrackEnvironmentBuilder Environment=>(TrackEnvironmentBuilder)typeof(RaceDirector).GetField("environment",Private).GetValue(director);
  void State(bool ao,bool shadows,bool bloom,bool mist){Check(director.ContactShadowsActive==ao,"live SSAO");Check(director.ShadowsActive==shadows,"live light shadows");Check(director.BloomActive==bloom,"live Bloom");Check(director.MistActive==mist,"mist setting applied");foreach(var renderer in Environment.Root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="Tower cloud mist"))Check(renderer.enabled==mist,"actual mist renderer flag");}
  LocalRaceConfig Config(string theme,int revision,int humans)=>new LocalRaceConfig{theme=theme,generatorRevision=revision,seed=revision==12?"20":"77",rails="normal",jumps=revision==12,humans=humans,entrants=8,devices=pads.Select(SessionControllers.Slot).ToArray()};
  IEnumerator Run(){
   foreach(string json in new[]{"","{}","invalid","{\"version\":99}"}){var s=RaceGraphicsSettings.Parse(json);Check(s.contactShadows&&s.shadows&&s.bloom&&s.mist,"safe defaults "+json);}
   var partial=RaceGraphicsSettings.Parse("{\"bloom\":false}");Check(!partial.bloom&&partial.contactShadows&&partial.shadows&&partial.mist,"missing fields keep defaults");
   for(int i=0;i<4;i++)pads[i]=InputSystem.AddDevice<Gamepad>();new RaceGraphicsSettings().Save();Config("cloud-city",11,1).Save();NitroBlurSettings.Enabled=true;
   director=new GameObject("Real graphics race").AddComponent<RaceDirector>();menu=director.GetComponent<RaceMenu>();menu.Open(RaceMenuScreen.Settings);typeof(RaceMenu).GetField("settingsPage",Private).SetValue(menu,1);yield return Frames(15);State(true,true,true,true);yield return Shot("settings-effects-on");
   foreach(int choice in new[]{1,2,3,0}){
    Click("antialiasing");yield return Frames();Click("option-"+choice);yield return Frames();
    for(int seat=0;seat<4;seat++){
     var renderCamera=director.CameraFor(seat).GetComponent<Camera>();var data=renderCamera.GetUniversalAdditionalCameraData();
     Check(renderCamera.allowMSAA==(choice==0),"MSAA camera gate "+choice);
     Check(data.antialiasing==(choice==2?AntialiasingMode.FastApproximateAntialiasing:choice==3?AntialiasingMode.SubpixelMorphologicalAntiAliasing:AntialiasingMode.None),"post AA mode "+choice);
    }
   }
   var ids=new[]{"contact-shadows","shadows","bloom","mist"};bool[] flags={true,true,true,true};
   for(int i=0;i<ids.Length;i++){Click(ids[i]);flags[i]=false;State(flags[0],flags[1],flags[2],flags[3]);}
   var loaded=UserSettings.Parse(File.ReadAllText(UserSettingsStore.PathName)).graphics;Check(!loaded.contactShadows&&!loaded.shadows&&!loaded.bloom&&!loaded.mist,"all choices persisted");Click("nitro-blur");Check(!NitroBlurSettings.Enabled&&!UserSettings.Parse(File.ReadAllText(UserSettingsStore.PathName)).nitroBlur,"existing Nitro preference retained");yield return Shot("settings-effects-off");
   Click("settings-section-2");yield return Frames();Click("settings-section-1");yield return Frames();Check(!director.Graphics.bloom,"tab navigation keeps values");
   foreach(var scenario in new[]{Config("cloud-city",11,1),Config("cloud-city",12,4),Config("space-station",12,4)}){
    GraphicsEffectsPlayChecks.Trace("scenario "+scenario.theme+" revision="+scenario.generatorRevision+" humans="+scenario.humans);
    Check(director.TryStartSelected(scenario,out var error),"real transactional start "+error);yield return Frames(12);director.SetPaused(true);menu.OpenPauseSettings();yield return Frames();State(false,false,false,false);
    for(int i=0;i<4;i++){var camera=director.CameraFor(i).GetComponent<Camera>();Check(camera.enabled==(i<scenario.humans),"active camera count");if(scenario.theme=="space-station")Check(camera.farClipPlane==1800,"space camera distance unchanged");}
    var geometry=director.Course.GeometryIdentity;int colliders=director.Track.GetComponentsInChildren<Collider>().Length;var meshes=Environment.Root.GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh.GetInstanceID()).ToArray();
    for(int i=0;i<ids.Length;i++){Click(ids[i]);flags[i]=true;State(flags[0],flags[1],flags[2],flags[3]);}
    Check(director.Paused&&Time.timeScale==0,"immediate apply while paused");Check(director.Course.GeometryIdentity==geometry&&director.Track.GetComponentsInChildren<Collider>().Length==colliders,"game geometry unchanged");Check(meshes.SequenceEqual(Environment.Root.GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh.GetInstanceID())),"toggles reuse meshes");
    if(scenario.theme=="cloud-city"&&scenario.generatorRevision==12){yield return Shot("pause-graphics-four-player");Visibility();}
    for(int i=0;i<ids.Length;i++){Click(ids[i]);flags[i]=false;}State(false,false,false,false);director.Restart();yield return Frames();State(false,false,false,false);director.SetPaused(true);Check(director.ExitToMenu(),"return to menu");yield return Frames();
   }
   GraphicsEffectsPlayChecks.Trace("scenarios complete; teardown");
   var bindings=Resources.Load<RaceGraphicsBindings>("GraphicsRuntimeBindings");using(var other=bindings.Acquire()){other.Apply(true);}Check(!bindings.contactShadows.isActive,"nested owner restores live remaining owner");
   UnityEngine.Object.Destroy(director.gameObject);director=null;yield return Frames();Check(bindings.contactShadows.isActive,"final teardown restores original SSAO");
   Check(File.ReadAllBytes("Assets/StarRacing/Generated/PrototypeRenderer.asset").SequenceEqual(File.ReadAllBytes(Path.Combine(Output,"renderer-before.asset"))),"renderer asset bytes unchanged");Check(!EditorUtility.IsDirty(bindings.contactShadows),"runtime feature not dirty");
  }
  void Visibility(){
   var env=Environment;Check(env.Plan.towers.Count>0,"actual city towers exist");var cameraObject=new GameObject("Distance proof camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=45;camera.nearClipPlane=.2f;camera.clearFlags=CameraClearFlags.Skybox;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
   var tower=env.Root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="City tower");var at=tower.bounds.center;camera.transform.position=at+new Vector3(0,140,-2300);camera.transform.LookAt(at);camera.farClipPlane=1800;
   Check(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),tower.bounds),"old 1800m plane rejects existing distant tower");Capture(camera,"distant-before");var chase=cameraObject.AddComponent<ChaseCamera>();chase.SetSkyline(env.Plan);
   Check(camera.farClipPlane>2300&&GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),tower.bounds),"adaptive plane includes distant tower");Capture(camera,"distant-after");
   foreach(var position in new[]{Vector3.zero,new Vector3(20000,1000,-5000),at}){camera.transform.position=position;chase.SetSkyline(env.Plan);foreach(var t in env.Plan.towers)for(int corner=0;corner<8;corner++){var point=t.position+Vector3.Scale(new Vector3(t.width*.6f,t.height*.5f+6,t.width*.6f),new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));Check(Vector3.Distance(position,point)<camera.farClipPlane,"every real city bound corner covered");}}
   chase.SetSkyline(null);Check(camera.farClipPlane==1800,"non-city minimum unchanged");UnityEngine.Object.Destroy(cameraObject);
  }
  void Capture(Camera camera,string name){var rt=new RenderTexture(960,600,24);var image=new Texture2D(960,600,TextureFormat.RGB24,false);var old=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());}finally{camera.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);}}
  void Restore(){if(restored)return;restored=true;if(director!=null){UnityEngine.Object.DestroyImmediate(director.gameObject);director=null;}foreach(var pair in saved){if(pair.Value==null)PlayerPrefs.DeleteKey(pair.Key);else PlayerPrefs.SetString(pair.Key,pair.Value);}if(nitroCaptured){if(nitroHad)PlayerPrefs.SetInt(NitroBlurSettings.PreferenceKey,nitroPrevious);else PlayerPrefs.DeleteKey(NitroBlurSettings.PreferenceKey);}PlayerPrefs.Save();NitroBlurSettings.Reload();foreach(var pad in pads)if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);UserSettingsStore.OverridePath=previousSettingsPath;if(previousSettings!=null)UserSettingsStore.Replace(previousSettings);}
  [Serializable] sealed class Saved{public string key,value;public bool existed;}
  [Serializable] sealed class Backup{public Saved[] preferences;public bool nitroExisted;public int nitroValue;}
  void OnDestroy(){GraphicsEffectsPlayChecks.Trace("driver destroy restored="+restored+" stack="+System.Environment.StackTrace);Restore();}
 }
}
