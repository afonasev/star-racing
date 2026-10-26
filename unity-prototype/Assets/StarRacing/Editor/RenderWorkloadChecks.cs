using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace StarRacingPrototype {
 // One frozen control, shared by the geometry and shadow-stream hypotheses.
 [InitializeOnLoad] public static class RenderWorkloadChecks {
  const string Key="StarRacing.RenderWorkloads";
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_RENDER_WORKLOAD_DIR");
  static bool Timing=>Environment.GetEnvironmentVariable("STAR_RACING_RENDER_WORKLOAD_TIMING")=="1";
  static int frames;static double deadline;
  static RenderWorkloadChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void PrepareVector(){
   if(string.IsNullOrEmpty(Output)||!Path.IsPathRooted(Output)||Directory.Exists(Output))throw new Exception("Fresh absolute render workload output required");
   Directory.CreateDirectory(Output);VectorRuntimeChecks.PrepareAndCheck();
   SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  public static void CheckVector(){
   if(string.IsNullOrEmpty(Output)||!Path.IsPathRooted(Output)||Directory.Exists(Output))throw new Exception("Fresh absolute render workload output required");
   Directory.CreateDirectory(Output);VectorRuntimeChecks.Run();
   SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){frames=0;deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Enter;UnityEngine.Debug.Log("RENDER_WORKLOAD_ENTERED_PLAYMODE");}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Enter;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Enter(){
   if(EditorApplication.timeSinceStartup>deadline){EditorApplication.update-=Enter;File.WriteAllText(Path.Combine(Output,"failure.txt"),"Editor render entry timeout");EditorApplication.isPlaying=false;return;}
   EditorApplication.QueuePlayerLoopUpdate();if(++frames<4)return;EditorApplication.update-=Enter;Run();
  }
  [Serializable] sealed class Window {public int views,arm,samples;public bool candidate;public double meanMs,p95Ms;}
  [Serializable] sealed class ImageDifference {public int views,maxByteError;public double meanByteError,largeDifferenceFraction;}
  [Serializable] sealed class Report {
   public bool success,actualPlayMode,timing;public int entrants=64,triangles=79318,vertices=96413,width=1600,height=900;
   public int referenceStreams,candidateStreams,referenceStride,candidateGeometryStride,candidateSurfaceStride;
   public string limitation="Frozen Editor URP submission plus identical synchronous pixel-readback fence; not ordinary gameplay FPS or pure GPU timing. No physical devices/human acceptance. 5% utility thresholds are diagnostic.";
   public Window[] windows;public ImageDifference[] images;
  }
  static void Run(){
   UnityEngine.Debug.Log("RENDER_WORKLOAD_VECTOR_BEGIN");
   var root=new GameObject("Frozen lossless Vector control");Mesh reference=null;Material floorMaterial=null;
   var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
   var mode=Physics.simulationMode;float scale=Time.timeScale;var oldActive=RenderTexture.active;
   var windows=new List<Window>();var comparisons=new List<ImageDifference>();var result=new Report{actualPlayMode=Application.isPlaying,timing=Timing};
   try{
    Time.timeScale=0;Physics.simulationMode=SimulationMode.Script;rt.Create();reference=VectorAssetImport.CreateSourceMesh();
    var filters=new List<MeshFilter>();Mesh candidate=null;
    for(int i=0;i<64;i++){
     var car=new GameObject("Frozen Vector "+i);car.transform.SetParent(root.transform,false);car.transform.localPosition=new Vector3((i%4-1.5f)*3.1f,0,(i/4)*8);
     var visual=StaticVehicleVisual.Create(car.transform,Color.HSVToRGB(i*.618034f%1,.7f,.95f),i<4);var filter=visual.CanonicalRoot.GetComponentInChildren<MeshFilter>();candidate=filter.sharedMesh;filters.Add(filter);
    }
    result.referenceStreams=reference.vertexBufferCount;result.referenceStride=reference.GetVertexBufferStride(0);result.candidateStreams=candidate.vertexBufferCount;result.candidateGeometryStride=candidate.GetVertexBufferStride(0);result.candidateSurfaceStride=candidate.GetVertexBufferStride(1);
    var sun=new GameObject("Control sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.intensity=1.5f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(35,-35,0);
    RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.35f,.4f);RenderSettings.fog=false;
    var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.SetParent(root.transform);floor.transform.position=Vector3.down*.23f;floor.transform.localScale=Vector3.one*100;Object.DestroyImmediate(floor.GetComponent<Collider>());
    floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.12f,.15f,.18f));floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
    var cameras=new Camera[4];for(int i=0;i<4;i++){
     var cam=new GameObject("Frozen camera "+i).AddComponent<Camera>();cam.transform.SetParent(root.transform);cam.enabled=false;cam.fieldOfView=65;cam.nearClipPlane=.1f;cam.farClipPlane=200;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.025f,.035f,.05f);cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;cameras[i]=cam;
    }
    void Capture(int views){
     for(int i=0;i<views;i++)RenderPipeline.SubmitRenderRequest(cameras[i],new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
     RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1600,900),0,0,false);pixels.Apply(false);
    }
    foreach(int views in new[]{1,4}){
     for(int i=0;i<views;i++){
      var cam=cameras[i];cam.rect=RaceViewports.For(views,i);cam.aspect=1600f*cam.rect.width/(900f*cam.rect.height);
      var at=new Vector3((i-1.5f)*3.1f,0,0);cam.transform.position=at+new Vector3(0,views==1?4.8f:3.6f,views==1?-10:-7);cam.transform.LookAt(at+new Vector3(0,1.1f,views==1?9:6));
     }
     Color32[] before=null;
     foreach(bool useCandidate in new[]{false,true}){
      foreach(var filter in filters)filter.sharedMesh=useCandidate?candidate:reference;
      for(int warm=0;warm<6;warm++)Capture(views);
      var image=pixels.GetPixels32();File.WriteAllBytes(Path.Combine(Output,$"vector-{views}-"+(useCandidate?"candidate":"baseline")+".png"),pixels.EncodeToPNG());
      if(!useCandidate){before=image;continue;}
      long sum=0;int max=0,large=0;for(int i=0;i<before.Length;i++){
       int r=Math.Abs(before[i].r-image[i].r),g=Math.Abs(before[i].g-image[i].g),b=Math.Abs(before[i].b-image[i].b);sum+=r+g+b;int d=Math.Max(r,Math.Max(g,b));max=Math.Max(max,d);if(d>8)large++;
      }
      var difference=new ImageDifference{views=views,meanByteError=sum/(double)(before.Length*3),largeDifferenceFraction=large/(double)before.Length,maxByteError=max};comparisons.Add(difference);
      if(difference.meanByteError>.2||difference.largeDifferenceFraction>.001)throw new Exception("Lossless Vector render mismatch views="+views+" mean="+difference.meanByteError+" fraction="+difference.largeDifferenceFraction);
     }
     if(Timing)for(int arm=0;arm<4;arm++){
      bool useCandidate=arm==1||arm==2;foreach(var filter in filters)filter.sharedMesh=useCandidate?candidate:reference;
      for(int warm=0;warm<6;warm++)Capture(views);
      var samples=new List<double>(1024);long steps=RacePhysicsStepper.NativeSteps;var watch=Stopwatch.StartNew();
      while(watch.Elapsed.TotalSeconds<5){long began=Stopwatch.GetTimestamp();Capture(views);samples.Add((Stopwatch.GetTimestamp()-began)*1000d/Stopwatch.Frequency);}
      if(RacePhysicsStepper.NativeSteps!=steps)throw new Exception("Frozen control advanced native physics");
      var sorted=samples.OrderBy(x=>x).ToArray();windows.Add(new Window{views=views,arm=arm,candidate=useCandidate,samples=samples.Count,meanMs=samples.Average(),p95Ms=sorted[(int)Math.Ceiling(sorted.Length*.95)-1]});
     }
    }
    result.success=true;SessionState.SetBool(Key+"Failed",false);UnityEngine.Debug.Log("RENDER_WORKLOAD_VECTOR_OK timing="+Timing);
   }catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());UnityEngine.Debug.LogException(e);}
   finally{
    result.windows=windows.ToArray();result.images=comparisons.ToArray();File.WriteAllText(Path.Combine(Output,"vector-control.json"),JsonUtility.ToJson(result,true));
    Physics.simulationMode=mode;Time.timeScale=scale;RenderTexture.active=oldActive;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(root);if(reference!=null)Object.DestroyImmediate(reference);if(floorMaterial!=null)Object.DestroyImmediate(floorMaterial);EditorApplication.isPlaying=false;
   }
  }
 }
}
