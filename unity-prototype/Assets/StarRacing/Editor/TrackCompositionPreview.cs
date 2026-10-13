using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class TrackCompositionPreview {
  const string Key="StarRacing.TrackCompositionPreview";static int ticks;
  static TrackCompositionPreview(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){SessionState.SetBool(Key+"RenderOnly",false);Begin();}
  public static void RunRenderOnly(){SessionState.SetBool(Key+"RenderOnly",true);Begin();}
  static void Begin(){SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);ticks=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++ticks<10)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Draw;}if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  static void Draw(){if(++ticks<15)return;EditorApplication.update-=Draw;try{
   Directory.CreateDirectory(TrackCompositionChecks.Output);
   if(!SessionState.GetBool(Key+"RenderOnly",false)){TrackCompositionChecks.Run();TrackCompositionPlayModeChecks.RunNarrowingPack();}
   foreach(uint seed in TrackCompositionChecks.CorpusSeeds()) {
    var root=new GameObject("composition preview");try{
     var d=Generator.Generate(seed,"normal","cloud-city",seed%2==0,true);var road=new GameObject("road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(d));Physics.SyncTransforms();
     VerifyPhysicalWidth(track,d);
     var lightGo=new GameObject("sun");lightGo.transform.SetParent(root.transform);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-20,0);RenderSettings.ambientLight=Color.white*.6f;
     var cameraGo=new GameObject("camera");cameraGo.transform.SetParent(root.transform);var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.aspect=1.6f;camera.nearClipPlane=1;camera.farClipPlane=12000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.22f,.27f);
     Capture(camera,d.samples,$"{d.family}-{seed}-overview.png");
     var main=d.composition.Single(x=>x.role=="main");Capture(camera,d.samples.Skip(main.startIndex).Take(main.endIndex-main.startIndex+1).ToArray(),$"{d.family}-{seed}-main.png");
    }finally{UnityEngine.Object.DestroyImmediate(root);}
   }
   File.WriteAllText(Path.Combine(TrackCompositionChecks.Output,"preview-result.txt"),$"COMPOSITION_PREVIEW_OK actualMesh=true editorPlayMode=true images=18 assertions={TrackCompositionChecks.Assertions}\n");Debug.Log("COMPOSITION_PREVIEW_OK images=18");
  }catch(Exception e){SessionState.SetBool(Key+"Failed",true);Debug.LogException(e);}finally{EditorApplication.isPlaying=false;}}
  static void VerifyPhysicalWidth(TrackBuilder track,Definition d) {
   foreach(var section in d.composition.Where(x=>x.role!="recovery")) {
    var phrase=d.samples.Skip(section.startIndex).Take(section.endIndex-section.startIndex+1).ToArray();
    foreach(var sample in new[]{phrase.First(),phrase.OrderByDescending(x=>x.halfWidth).First(),phrase.Last()}) {
     var f=track.Route.Evaluate(30+(float)sample.distance);
     foreach(float side in new[]{-1f,1f}) {
      var inside=f.position+f.right*side*(f.halfWidth-1);
      TrackCompositionChecks.Check(track.RaycastRoad(inside+f.normal*2,-f.normal,4,f.distance,out var hit),"physical widened road missing");
      TrackCompositionChecks.Check(Vector3.Distance(hit.point,inside)<.15f,"mesh/route widened edge mismatch");
      var outside=f.position+f.right*side*(f.halfWidth+1);
      TrackCompositionChecks.Check(!track.RaycastRoad(outside+f.normal*2,-f.normal,4,f.distance,out _),"physical road extends beyond bounds");
     }
    }
   }
  }
  static void Capture(Camera camera,Sample[] samples,string file){
   var bounds=new Bounds(samples[0].position.Float,Vector3.zero);foreach(var s in samples)bounds.Encapsulate(s.position.Float);
   var direction=new Vector3(.22f,1,-.28f).normalized;camera.transform.position=bounds.center+direction*6000;camera.transform.rotation=Quaternion.LookRotation(-direction,Vector3.forward);
   float height=1,width=1;foreach(var s in samples){var v=camera.transform.InverseTransformDirection(s.position.Float-bounds.center);height=Mathf.Max(height,Mathf.Abs(v.y)+15);width=Mathf.Max(width,Mathf.Abs(v.x)+15);}camera.orthographicSize=Mathf.Max(height,width/camera.aspect)*1.12f;
   var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);rt.Create();var previous=RenderTexture.active;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
   try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(TrackCompositionChecks.Output,file),image.EncodeToPNG());}finally{RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
  }
 }
}
