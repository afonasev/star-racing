using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class StationPlacementPreview {
  const string Key="StarRacing.StationPlacementPreview";static int ticks;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_STATION_PREVIEW_DIR");
  static StationPlacementPreview(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){SkyboxChecks.Run();Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);ticks=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++ticks<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Draw;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
  }
  static void Draw(){
   if(++ticks<15)return;EditorApplication.update-=Draw;var root=new GameObject("split camera preview");var decor=new TrackEnvironmentBuilder();
   try{
    AudioListener.volume=0;
    var trackGo=new GameObject("track");trackGo.transform.SetParent(root.transform);var track=trackGo.AddComponent<TrackBuilder>();
    track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal","space-station",false,true)));decor.Build(track.Route,track.transform,77);decor.Sky.Activate();
    var sunGo=new GameObject("sun");sunGo.transform.SetParent(root.transform);var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;
    var fillGo=new GameObject("fill");fillGo.transform.SetParent(root.transform);var fill=fillGo.AddComponent<Light>();fill.type=LightType.Directional;
    var carGo=new GameObject("Player 1");carGo.transform.SetParent(root.transform);var car=carGo.AddComponent<MagneticVehicle>();car.Initialize(track,0,CloudlineSkin.PlayerColors[0],ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text));car.Hold(true);
    var viewGo=new GameObject("View");viewGo.transform.SetParent(root.transform);var camera=viewGo.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=65;camera.aspect=16f/9;camera.nearClipPlane=.2f;camera.farClipPlane=1800;
    camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;var follow=viewGo.AddComponent<ChaseCamera>();follow.target=car;
    RaceLightingController.Apply(decor.Plan.lighting,sun,fill,new[]{follow});
    var tick=typeof(ChaseCamera).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    foreach(float distance in new[]{0f,160f}){
     car.ResetAt(distance);follow.Snap();tick.Invoke(follow,null);Capture(camera,"station-chase-"+distance);
    }
    // Context view frames the actual nearby cluster and adjacent track; no Player build.
    var frame=track.Route.Samples[0];camera.transform.position=frame.position-frame.tangent*70+Vector3.up*35;
    camera.transform.LookAt(Vector3.Lerp(frame.position+frame.tangent*200,decor.Plan.station,.6f));Capture(camera,"station-context");
    File.WriteAllText(Path.Combine(Output,"success.json"),"{\"success\":true,\"actualPlayMode\":true,\"offscreen\":true,\"seed\":77,\"playerBuild\":false}");
    Debug.Log("STATION_PLACEMENT_PREVIEW_OK actualPlayMode=true offscreen=true");
   }catch(Exception e){SessionState.SetBool(Key+"Failed",true);Debug.LogException(e);}
   finally{decor.Clear();UnityEngine.Object.DestroyImmediate(root);EditorApplication.isPlaying=false;}
  }
  static void Capture(Camera camera,string name){
   var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);rt.Create();var previous=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
   try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());}
   finally{RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
  }
 }
}
