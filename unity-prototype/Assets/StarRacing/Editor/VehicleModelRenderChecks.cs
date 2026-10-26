using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class VehicleModelRenderChecks {
  const string Key="StarRacing.VehicleModelRender"; static int ticks;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_VEHICLE_RENDER_DIR");
  static VehicleModelRenderChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Verify(){Begin("verify");}
  public static void Audit(){StrideVisualChecks.Run();PhysicalContactChecks.CheckBakedProfile();UnityEngine.Debug.Log("VEHICLE_RENDER_BASELINE_AUDIT_OK");}
  public static void Baseline()=>Begin("baseline");
  public static void Measure()=>Begin("measure");
  public static void Visual()=>Begin("visual");
  static void Begin(string mode){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_VEHICLE_RENDER_DIR required");
   Directory.CreateDirectory(Output);SessionState.SetString(Key,mode);SessionState.SetBool(Key+"Failed",false);
   GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);ticks=0;EditorApplication.update+=Enter;
  }
  static void Enter(){if(++ticks<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(string.IsNullOrEmpty(SessionState.GetString(Key,"")))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Draw;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetString(Key,"");EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
  }
  static void Draw(){
   if(++ticks<12)return;EditorApplication.update-=Draw;
   try{if(SessionState.GetString(Key,"")=="verify"){Application.runInBackground=true;VehicleModelLodChecks.Run();VehicleModelLodChecks.Quality();}else if(SessionState.GetString(Key,"")=="visual")VehicleModelLodChecks.Quality();else Render(SessionState.GetString(Key,""));UnityEngine.Debug.Log("VEHICLE_MODEL_RENDER_OK mode="+SessionState.GetString(Key,""));}
   catch(Exception e){SessionState.SetBool(Key+"Failed",true);UnityEngine.Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}
   finally{EditorApplication.isPlaying=false;}
  }
  [Serializable] public class Window {public string arm;public int entrants,repeat;public bool distant;public double medianMs,p95Ms;public int canonicalVehicleTriangles;public string measurement="Editor URP submission + synchronous pixel fence, render-only, not ordinary FPS";}
  [Serializable] public class Report {public bool editorPlayMode=true,muted=true;public string gpuTiming="unavailable";public float lodBias;public Window[] windows;}
  static void Render(string mode){
   var root=new GameObject("Vehicle rendering diagnostic");var windows=new List<Window>();int oldVsync=QualitySettings.vSyncCount,oldTarget=Application.targetFrameRate;float oldLodBias=QualitySettings.lodBias;
   var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);Material ground=null;
   try{
    QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;rt.Create();
    var sunGo=new GameObject("Diagnostic sun");sunGo.transform.SetParent(root.transform);var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(35,-35,0);
    RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.35f,.4f);
    var road=GameObject.CreatePrimitive(PrimitiveType.Plane);road.transform.SetParent(root.transform);road.transform.position=new Vector3(0,-.02f,300);road.transform.localScale=new Vector3(3,1,70);Object.DestroyImmediate(road.GetComponent<Collider>());ground=new Material(Shader.Find("Universal Render Pipeline/Lit"));ground.SetColor("_BaseColor",new Color(.12f,.15f,.18f));road.GetComponent<Renderer>().sharedMaterial=ground;
    var cameraGo=new GameObject("Diagnostic camera");cameraGo.transform.SetParent(root.transform);var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=65;camera.aspect=16f/9;camera.nearClipPlane=.2f;camera.farClipPlane=1000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.05f);camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
    foreach(bool distant in new[]{false,true})foreach(int count in new[]{8,32,64}){
     var field=new GameObject("Field");field.transform.SetParent(root.transform);var body=new List<MeshRenderer>();var groups=new List<LODGroup>();
     for(int i=0;i<count;i++){
      var car=new GameObject("Car "+i);car.transform.SetParent(field.transform);car.transform.position=new Vector3((i%4-1.5f)*3.1f,0,i/4*(distant?16:7)+(distant&&i>=4?60:0));
      var visual=StaticVehicleVisual.Create(car.transform,i<4?CloudlineSkin.PlayerColors[i]:Color.HSVToRGB(i*.618034f%1,.75f,.95f),i<1);
      body.AddRange(visual.GetComponentsInChildren<MeshRenderer>().Where(r=>!r.name.StartsWith("Exhaust plume")));groups.AddRange(visual.GetComponentsInChildren<LODGroup>());
     }
     camera.transform.position=new Vector3(-4.65f,3.2f,-8);camera.transform.LookAt(new Vector3(-4.65f,.9f,18));
     foreach(int repeat in Enumerable.Range(0,3))foreach(string arm in mode=="baseline"?new[]{"canonical","body-hidden","canonical-return"}:new[]{"canonical","automatic","canonical-return"}){
      foreach(var renderer in body)renderer.enabled=arm!="body-hidden";
      foreach(var group in groups)group.ForceLOD(arm=="automatic"?-1:0);
      for(int warm=0;warm<8;warm++)Capture(camera,rt,pixels);
      var times=new double[24];for(int frame=0;frame<times.Length;frame++){var clock=Stopwatch.StartNew();Capture(camera,rt,pixels);times[frame]=clock.Elapsed.TotalMilliseconds;}
      Array.Sort(times);windows.Add(new Window{arm=arm,entrants=count,repeat=repeat,distant=distant,medianMs=times[times.Length/2],p95Ms=times[(int)Math.Ceiling(times.Length*.95)-1],canonicalVehicleTriangles=count*79318});
      if(repeat==0&&arm!="body-hidden")File.WriteAllBytes(Path.Combine(Output,$"field-{count}-{distant}-{arm}.png"),pixels.EncodeToPNG());
     }
     Object.DestroyImmediate(field);
     File.WriteAllText(Path.Combine(Output,"render-windows.json"),JsonUtility.ToJson(new Report{lodBias=QualitySettings.lodBias,windows=windows.ToArray()},true));
    }
   }finally{QualitySettings.vSyncCount=oldVsync;QualitySettings.lodBias=oldLodBias;Application.targetFrameRate=oldTarget;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(root);if(ground!=null)Object.DestroyImmediate(ground);}
  }
  static void Capture(Camera camera,RenderTexture rt,Texture2D pixels){
   var previous=RenderTexture.active;
   try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);pixels.Apply(false);}
   finally{RenderTexture.active=previous;}
  }
 }
}
