using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class DriftTireMarkPlayModeChecks {
  const string Pending="StarRacing.DriftProof.Pending",Output="StarRacing.DriftProof.Output",Result="StarRacing.DriftProof.Result";
  static IEnumerator routine;static double started;static int assertions;
  static SimulationMode simulation;static float volume,scale,fixedStep,maxStep;
  static DriftTireMarkPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_DRIFT_PROOF");
   if(string.IsNullOrEmpty(output))throw new Exception("STAR_RACING_DRIFT_PROOF required");Directory.CreateDirectory(output);
   DriftTireMarkChecks.Run();
   SessionState.SetString(Output,output);SessionState.SetInt(Result,1);SessionState.SetBool(Pending,true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Pending,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){
    simulation=Physics.simulationMode;volume=AudioListener.volume;scale=Time.timeScale;fixedStep=Time.fixedDeltaTime;maxStep=Time.maximumDeltaTime;
    Physics.simulationMode=SimulationMode.Script;AudioListener.volume=0;Time.timeScale=1;
    Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f; // production RaceDirector clock
    started=EditorApplication.timeSinceStartup;assertions=0;routine=Proof();EditorApplication.update+=Tick;
   }
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Pending,false);EditorApplication.Exit(SessionState.GetInt(Result,1));}
  }
  static void Tick(){try{
   if(EditorApplication.timeSinceStartup-started>150)throw new Exception("Drift proof timed out");
   if(routine.MoveNext())return;
   File.WriteAllText(Path.Combine(SessionState.GetString(Output,""),"success.json"),"{\"success\":true,\"assertions\":"+assertions+",\"prescribed_motion_fixture\":true,\"human_acceptance\":false,\"player_build\":false}");
   Debug.Log("DRIFT_TIRE_MARK_PLAYMODE_OK assertions="+assertions+" humanAI=true bothThemes=true roster64=true");Finish(true);
  }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(SessionState.GetString(Output,""),"failure.txt"),e.ToString());Finish(false);}}
  static void Finish(bool success){EditorApplication.update-=Tick;Physics.simulationMode=simulation;AudioListener.volume=volume;Time.timeScale=scale;Time.fixedDeltaTime=fixedStep;Time.maximumDeltaTime=maxStep;SessionState.SetInt(Result,success?0:1);EditorApplication.isPlaying=false;}
  static void Check(bool valid,string label){assertions++;if(!valid)throw new Exception("DRIFT_PLAYMODE "+label);}
  static IEnumerator Proof(){
   var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
   Check(pipeline!=null,"existing URP pipeline");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
   RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
   var sun=new GameObject("Drift proof sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.8f;sun.transform.rotation=Quaternion.Euler(40,-25,0);
   var camera=new GameObject("Drift proof camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.2f,.25f,.3f);camera.fieldOfView=50;
   var tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
   var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
   foreach(string theme in new[]{"cloud-city","space-station"}){
    var root=new GameObject("Drift proof "+theme);var track=root.AddComponent<TrackBuilder>();
    track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,false,true)));var marks=track.TireMarks;
    var cars=new MagneticVehicle[theme=="cloud-city"?64:2];
    for(int i=0;i<cars.Length;i++){
     var go=new GameObject(i==0?"Human drift":"AI drift "+i);go.transform.SetParent(root.transform);
     cars[i]=go.AddComponent<MagneticVehicle>();cars[i].Initialize(track,i,i==0?Color.cyan:Color.red,balance,i==0);cars[i].enabled=false;
    }
    // Controlled motion fixture: production FixedUpdate and real wheel hits, no fabricated visual contacts.
    float until=Time.time+1.2f;int step=0;float lastTick=Time.time-.02f;
    while(Time.time<until){
     while(Time.time-lastTick<.02f-.000001f)yield return null;lastTick=Time.time;
     for(int i=0;i<cars.Length;i++){
      var car=cars[i];float distance=80+step*.45f+(i<2?0:(i-1)*7);
      var frame=track.Route.Evaluate(distance);float lateral=(i%2==0?-2:2)+Mathf.Sin(step*.07f)*.6f;
      var point=frame.position+frame.right*lateral+frame.normal*VehicleGeometry.AdheredBodyHeight;
      car.Distance=distance;car.Frame=frame;car.Body.position=point;car.Body.rotation=frame.Rotation;
      car.transform.SetPositionAndRotation(point,frame.Rotation);car.Body.linearVelocity=frame.tangent*30+frame.right*10;
      car.SetRaceContext(true,i+1,cars.Length);car.SetInput(new DrivingInput{throttle=1,drift=true});
     }
     Physics.SyncTransforms();foreach(var car in cars)tick.Invoke(car,null);step++;yield return null;
    }
    marks.Flush(Time.time);Check(marks.LiveSegments>20,"production wheel contacts emit "+theme);
    for(int i=0;i<cars.Length;i++)Check(marks.TotalFor(i)>0,"production entrant emits "+theme+" "+i);
    Check(marks.RejectedSegments==0,"roster budget "+cars.Length);
    var focus=track.Route.Evaluate(cars[0].Distance-4);
    camera.transform.position=focus.position+focus.normal*11-focus.tangent*13+focus.right*6;
    camera.transform.LookAt(focus.position+focus.tangent*2,focus.normal);
    var renderer=marks.GetComponent<Renderer>();renderer.enabled=false;var blank=Capture(camera,theme+"-baseline");renderer.enabled=true;
    var visible=Capture(camera,theme+"-drift");int darker=0;
    for(int i=0;i<blank.Length;i++)if(blank[i].r+blank[i].g+blank[i].b-visible[i].r-visible[i].g-visible[i].b>8)darker++;
    Check(darker>100,"rubber visible in rendered frame "+theme+" pixels="+darker);
    var widePosition=camera.transform.position;var wideRotation=camera.transform.rotation;
    camera.transform.position=focus.position+focus.normal*3.5f-focus.tangent*3+focus.right*1;
    camera.transform.LookAt(focus.position-focus.right*1.4f,focus.normal);Capture(camera,theme+"-rubber-detail");
    camera.transform.SetPositionAndRotation(widePosition,wideRotation);
    long total=marks.TotalSegments;var first=cars[0];var supportedPosition=first.Body.position;
    first.Body.linearVelocity=Vector3.zero;tick.Invoke(first,null);Check(marks.TotalSegments==total,"stationary drift-button does not emit "+theme);
    first.Body.linearVelocity=first.Frame.tangent*30;tick.Invoke(first,null);Check(marks.TotalSegments==total,"straight drift-button does not emit "+theme);
    first.Body.linearVelocity=first.Frame.tangent*30+first.Frame.right*10;first.Body.position+=first.Frame.normal*4;first.transform.position=first.Body.position;Physics.SyncTransforms();
    tick.Invoke(first,null);Check(marks.TotalSegments==total,"airborne production contacts do not emit "+theme);
    first.Body.position=supportedPosition;first.transform.position=supportedPosition;Physics.SyncTransforms();tick.Invoke(first,null);Check(marks.TotalSegments==total,"landing seeds instead of bridging "+theme);
    cars[0].Hold(true);cars[0].Hold(false);
    tick.Invoke(cars[0],null);Check(marks.TotalSegments==total,"hold resumes without bridge "+theme);
    cars[0].ResetAt(80);cars[0].Body.linearVelocity=cars[0].Frame.tangent*30+cars[0].Frame.right*10;tick.Invoke(cars[0],null);
    Check(marks.TotalSegments==total,"respawn/reset breaks production emitter "+theme);
    marks.BreakAll();float born=Time.time;marks.Flush(born+4);Capture(camera,theme+"-fading");
    marks.Flush(born+8.1f);Check(marks.LiveSegments==0,"shader and buffer age retirement "+theme);Capture(camera,theme+"-expired");
    marks.Clear();Check(track.Route.LoopEnd>track.Route.LoopStart,"authored loop available "+theme);
    // Loop ranges include authored entry/exit runs. A quarter of their metric range
    // is not necessarily the vertical wall; select it from actual road-frame normals.
    float verticalStart=track.Route.LoopStart,leastUp=1;
    foreach(var frame in track.Route.Samples){
     if(frame.distance<track.Route.LoopStart||frame.distance>track.Route.LoopEnd-10)continue;
     float up=Mathf.Abs(Vector3.Dot(frame.normal,Vector3.up));
     if(up<leastUp){leastUp=up;verticalStart=frame.distance;}
    }
    Check(leastUp<.3f,"vertical wall exists in authored loop "+theme);
    step=0;lastTick=Time.time-.02f;
    // Editor update callbacks can run several times per game frame. Advance the fixture
    // only with the game clock, just like production FixedUpdate, never by editor callback count.
    for(int sample=0;sample<12;sample++){
     while(Time.time-lastTick<.02f-.000001f)yield return null;lastTick=Time.time;
     for(int i=0;i<2;i++){
      var car=cars[i];float distance=verticalStart+step*.4f;var frame=track.Route.Evaluate(distance);
      var point=frame.position+frame.right*(i==0?-2:2)+frame.normal*VehicleGeometry.AdheredBodyHeight;
      car.Distance=distance;car.Frame=frame;car.Body.position=point;car.Body.rotation=frame.Rotation;car.transform.SetPositionAndRotation(point,frame.Rotation);
      car.Body.linearVelocity=frame.tangent*30+frame.right*10;car.SetRaceContext(true,i+1,cars.Length);
     }
     Physics.SyncTransforms();for(int i=0;i<2;i++)tick.Invoke(cars[i],null);
     Debug.Log("DRIFT_LOOP_SAMPLE theme="+theme+" step="+step+" time="+Time.time+" supports="+cars[0].HandlingSupportCount+","+cars[1].HandlingSupportCount+" ghost="+cars[0].IsGhosting+","+cars[1].IsGhosting+" gap="+cars[0].Frame.gap+" emitted="+marks.TotalFor(0)+","+marks.TotalFor(1));
     step++;yield return null;
    }
    marks.Flush(Time.time);Check(marks.TotalFor(0)>0&&marks.TotalFor(1)>0,"human and AI supported loop marks "+theme);
    focus=track.Route.Evaluate(cars[0].Distance-3);Check(Mathf.Abs(Vector3.Dot(focus.normal,Vector3.up))<.85f,"loop fixture is inclined/vertical "+theme);
    camera.transform.position=focus.position+focus.normal*10-focus.tangent*12+focus.right*5;camera.transform.LookAt(focus.position+focus.tangent*2,focus.normal);Capture(camera,theme+"-loop-drift");
    UnityEngine.Object.DestroyImmediate(root);yield return null;
   }
   // Actual race lifecycle: pause, repeat and atomic replacement preserve the renderer contract.
   var director=new GameObject("Drift lifecycle").AddComponent<RaceDirector>();var pool=director.Track.TireMarks;
   float now=Time.time;pool.Sample(0,Vector3.zero,Vector3.up,.8f,now);pool.Sample(0,Vector3.forward*.5f,Vector3.up,.8f,now+.02f);pool.Flush(now+.02f);
   Check(pool.LiveSegments==1,"lifecycle seed");director.SetPaused(true);pool.Sample(0,Vector3.forward,Vector3.up,.8f,now+.04f);Check(pool.TotalSegments==1,"pause breaks continuity");
   float pausedTime=Time.time;yield return null;yield return null;
   Check(Time.time==pausedTime&&pool.GetComponent<Renderer>().sharedMaterial.GetFloat("_MarkTime")==pausedTime,"pause freezes fade clock");
   director.Restart(false);Check(pool.LiveSegments==0,"repeat and menu clear race marks");
   var config=LocalRaceConfig.Load();config.humans=1;config.entrants=64;config.devices=new[]{-2,-1,-2,-1};config.theme="space-station";config.seed="77";config.jumps=false;
   Check(director.TryStartSelected(config,out string error),"replace race "+error);
   Check(director.Track.TireMarks!=pool&&!pool.gameObject.activeInHierarchy&&director.Track.TireMarks.LiveSegments==0,"atomic new race clears marks");
   Check(director.Cars.Length==64,"native 64-car lifecycle");
  }
  static Color32[] Capture(Camera camera,string name){
   var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
   try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(SessionState.GetString(Output,""),name+".png"),pixels.EncodeToPNG());return pixels.GetPixels32();}
   finally{camera.targetTexture=null;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
  }
 }
}
