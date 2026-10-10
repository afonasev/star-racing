using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class SplitScreenCameraPreview {
  const string Key="StarRacing.SplitCameraPreview";static int ticks;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_CAMERA_PREVIEW_DIR");
  static SplitScreenCameraPreview(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);ticks=0;EditorApplication.update+=Enter;}
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
    track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal","cloud-city",true,true)));decor.Build(track.Route,track.transform,77);decor.Sky.Activate();
    var sunGo=new GameObject("sun");sunGo.transform.SetParent(root.transform);var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;
    var fillGo=new GameObject("fill");fillGo.transform.SetParent(root.transform);var fill=fillGo.AddComponent<Light>();fill.type=LightType.Directional;
    var cars=new MagneticVehicle[4];var follows=new ChaseCamera[4];
    for(int i=0;i<4;i++){
     var go=new GameObject("Player "+(i+1));go.transform.SetParent(root.transform);cars[i]=go.AddComponent<MagneticVehicle>();cars[i].Initialize(track,i,CloudlineSkin.PlayerColors[i],ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text));cars[i].ResetAt(160+i*25);cars[i].Hold(true);
     var viewGo=new GameObject("View "+(i+1));viewGo.transform.SetParent(root.transform);var view=viewGo.AddComponent<Camera>();view.enabled=false;view.fieldOfView=65;view.nearClipPlane=.2f;view.farClipPlane=1800;
     view.GetUniversalAdditionalCameraData().renderPostProcessing=true;follows[i]=viewGo.AddComponent<ChaseCamera>();follows[i].target=cars[i];
    }
    RaceLightingController.Apply(decor.Plan.lighting,sun,fill,follows);
    var tick=typeof(ChaseCamera).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    foreach(int humans in new[]{2,3,4}){
     var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var black=new Color[1920*1080];image.SetPixels(black);
     try{
      for(int seat=0;seat<humans;seat++){
       var camera=follows[seat].GetComponent<Camera>();var rect=RaceViewports.For(humans,seat);camera.rect=rect;int width=(int)(1920*rect.width),height=(int)(1080*rect.height);camera.aspect=(float)width/height;follows[seat].Snap();tick.Invoke(follows[seat],null);
       var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);rt.Create();var previous=RenderTexture.active;var part=new Texture2D(width,height,TextureFormat.RGB24,false);
       // Render the already computed split pose into its entire per-seat texture.
       camera.rect=new Rect(0,0,1,1);
       try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;part.ReadPixels(new Rect(0,0,width,height),0,0);part.Apply();image.SetPixels((int)(1920*rect.x),(int)(1080*rect.y),width,height,part.GetPixels());}
       finally{camera.rect=rect;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(part);}
      }
      image.Apply();File.WriteAllBytes(Path.Combine(Output,$"camera-{humans}-players.png"),image.EncodeToPNG());
     }finally{UnityEngine.Object.DestroyImmediate(image);}
    }
    Debug.Log("SPLIT_CAMERA_PREVIEW_OK actualPlayMode=true offscreen=true layouts=2,3,4");
   }catch(Exception e){SessionState.SetBool(Key+"Failed",true);Debug.LogException(e);}
   finally{decor.Clear();UnityEngine.Object.DestroyImmediate(root);EditorApplication.isPlaying=false;}
  }
 }
}
