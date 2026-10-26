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
 // Targeted shader workload, not gameplay FPS. Both arms include the same readback fence.
 [InitializeOnLoad] public static class EffectsWorkloadControl {
  const string Key="StarRacing.EffectsWorkloadControl";static int ticks;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_EFFECT_CONTROL_DIR");
  static bool Timing=>Environment.GetEnvironmentVariable("STAR_RACING_EFFECT_CONTROL_TIMING")=="1";
  static EffectsWorkloadControl(){EditorApplication.playModeStateChanged+=Changed;}
  public static void CheckAssets(){
   foreach(var name in new[]{"BaselineMist","CandidateMist","BaselineNitro","CandidateNitro"}){
    var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/StarRacing/Editor/RenderingControls/"+name+".shader");
    if(shader==null||!shader.isSupported||ShaderUtil.ShaderHasError(shader))throw new Exception("Control shader failed "+name);
   }
   UnityEngine.Debug.Log("EFFECT_CONTROL_ASSETS_OK original runtime shaders retained; negative candidates Editor-only");
  }
  public static void Run(){if(string.IsNullOrEmpty(Output)||!Path.IsPathRooted(Output)||Directory.Exists(Output))throw new Exception("Fresh absolute output required");Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Enter;}if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}}
  static void Enter(){EditorApplication.QueuePlayerLoopUpdate();if(++ticks<4)return;EditorApplication.update-=Enter;Execute();}
  [Serializable] sealed class Window {public string effect;public int views,arm,samples;public bool candidate;public double meanMs,p95Ms;}
  [Serializable] sealed class Image {public string effect;public int views,maxByteError,changedPixels;public double meanByteError,largeDifferenceFraction;}
  [Serializable] sealed class Report {public bool success,timing;public string limitation="Targeted72mist planes and mipless active12tap nitro workload; one bounded ABBA per1/4viewport, synchronous readback; not gameplay FPS or pure GPU time.";public List<Window> windows=new List<Window>();public List<Image> images=new List<Image>();}
  static void Execute(){var report=new Report{timing=Timing};var root=new GameObject("Effect shader control");var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);var source=new RenderTexture(1600,900,0,RenderTextureFormat.ARGB32){useMipMap=false};var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);var texture=new Texture2D(1600,900,TextureFormat.RGBA32,false);var mats=new List<Material>();var beforeRT=RenderTexture.active;RTHandle handle=null;var cmd=new CommandBuffer();
   try{rt.Create();source.Create();handle=RTHandles.Alloc(source);for(int y=0;y<900;y++)for(int x=0;x<1600;x++)texture.SetPixel(x,y,((x/7+y/9)%2)==0?new Color(.8f,.6f,.2f):new Color(.1f,.2f,.7f));texture.Apply();Graphics.Blit(texture,source);if(source.useMipMap||source.mipmapCount!=1)throw new Exception("Nitro source must be mipless");
    var camera=new GameObject("Mist camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.enabled=false;camera.orthographic=true;camera.orthographicSize=4.5f;camera.nearClipPlane=.1f;camera.farClipPlane=200;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.22f,.35f);camera.cullingMask=1<<29;
    var mist=new Material(Resources.Load<Shader>("Environment/Sky/CloudMist"));mist.SetTexture("_CloudTex",Resources.Load<Texture2D>("Environment/Sky/CloudSea"));mats.Add(mist);var mistRenderers=new List<Renderer>();
    for(int i=0;i<72;i++){var plane=GameObject.CreatePrimitive(PrimitiveType.Quad);Object.DestroyImmediate(plane.GetComponent<Collider>());plane.transform.SetParent(root.transform);plane.layer=29;plane.transform.position=new Vector3((i%9-4)*1.7f,(i/9-3.5f)*1.3f,5+i*.05f);plane.transform.localScale=new Vector3(3.8f,3.8f,1);plane.GetComponent<Renderer>().sharedMaterial=mist;mistRenderers.Add(plane.GetComponent<Renderer>());}
    var nitro=new Material(Resources.Load<Shader>("Effects/NitroEdgeBlur"));mats.Add(nitro);nitro.SetFloat("_Strength",1);nitro.SetVector("_BlitTexture_TexelSize",new Vector4(1f/1600,1f/900,1600,900));
    foreach(string effect in (Environment.GetEnvironmentVariable("STAR_RACING_EFFECT_CONTROL_ONLY")=="nitro"?new[]{"nitro"}:new[]{"mist","nitro"})){
     var material=effect=="mist"?mist:nitro;var candidateShader=effect=="mist"?AssetDatabase.LoadAssetAtPath<Shader>("Assets/StarRacing/Editor/RenderingControls/CandidateMist.shader"):AssetDatabase.LoadAssetAtPath<Shader>("Assets/StarRacing/Editor/RenderingControls/CandidateNitro.shader");var baselineShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/StarRacing/Editor/RenderingControls/"+(effect=="mist"?"BaselineMist":"BaselineNitro")+".shader");
     foreach(var shader in new[]{candidateShader,baselineShader})if(shader==null||!shader.isSupported||ShaderUtil.ShaderHasError(shader))throw new Exception("Shader compile "+shader);
     foreach(int views in new[]{1,4}){
      void Capture(){if(effect=="mist"){for(int i=0;i<views;i++){camera.rect=RaceViewports.For(views,i);camera.aspect=1600f*camera.rect.width/(900f*camera.rect.height);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});}}else{cmd.Clear();cmd.SetRenderTarget(rt);for(int i=0;i<views;i++){var r=RaceViewports.For(views,i);cmd.SetViewport(new Rect(r.x*1600,r.y*900,r.width*1600,r.height*900));Blitter.BlitTexture(cmd,handle,new Vector4(1,1,0,0),material,0);}Graphics.ExecuteCommandBuffer(cmd);}RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1600,900),0,0,false);pixels.Apply(false);}
      Color32[] baseline=null;foreach(bool candidate in new[]{false,true}){material.shader=candidate?candidateShader:baselineShader;if(effect=="nitro"){material.SetFloat("_Strength",1);material.SetVector("_BlitTexture_TexelSize",new Vector4(1f/1600,1f/900,1600,900));}for(int i=0;i<6;i++)Capture();var image=pixels.GetPixels32();if(effect=="nitro"&&image.Select(p=>((int)p.r<<16)|((int)p.g<<8)|p.b).Distinct().Count()<100)throw new Exception("Nitro control inactive: insufficient output colors");File.WriteAllBytes(Path.Combine(Output,$"{effect}-{views}-"+(candidate?"candidate":"baseline")+".png"),pixels.EncodeToPNG());if(!candidate){baseline=image;continue;}long sum=0;int max=0,large=0,changed=0;for(int i=0;i<image.Length;i++){int r=Math.Abs(image[i].r-baseline[i].r),g=Math.Abs(image[i].g-baseline[i].g),b=Math.Abs(image[i].b-baseline[i].b),d=Math.Max(r,Math.Max(g,b));sum+=r+g+b;max=Math.Max(max,d);if(d>8)large++;if(d>0)changed++;}var diff=new Image{effect=effect,views=views,maxByteError=max,changedPixels=changed,meanByteError=sum/(double)(image.Length*3),largeDifferenceFraction=large/(double)image.Length};report.images.Add(diff);if(diff.meanByteError>.2||diff.largeDifferenceFraction>.001)throw new Exception("Effect image mismatch "+effect+"/"+views+" max="+max+" mean="+diff.meanByteError);}
      if(Timing)for(int arm=0;arm<4;arm++){bool candidate=arm==1||arm==2;material.shader=candidate?candidateShader:baselineShader;if(effect=="nitro"){material.SetFloat("_Strength",1);material.SetVector("_BlitTexture_TexelSize",new Vector4(1f/1600,1f/900,1600,900));}for(int i=0;i<6;i++)Capture();var samples=new List<double>();var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<5){long start=Stopwatch.GetTimestamp();Capture();samples.Add((Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency);}var sorted=samples.OrderBy(v=>v).ToArray();report.windows.Add(new Window{effect=effect,views=views,arm=arm,candidate=candidate,samples=samples.Count,meanMs=samples.Average(),p95Ms=sorted[(int)Math.Ceiling(samples.Count*.95)-1]});}
     }material.shader=candidateShader;
    }report.success=true;SessionState.SetBool(Key+"Failed",false);UnityEngine.Debug.Log("EFFECT_WORKLOAD_OK");
   }catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());UnityEngine.Debug.LogException(e);}finally{File.WriteAllText(Path.Combine(Output,"effects-control.json"),JsonUtility.ToJson(report,true));RenderTexture.active=beforeRT;cmd.Release();handle?.Release();Object.DestroyImmediate(root);foreach(var m in mats)Object.DestroyImmediate(m);rt.Release();source.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(source);Object.DestroyImmediate(pixels);Object.DestroyImmediate(texture);EditorApplication.isPlaying=false;}
  }
 }
}
