using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class NitroEdgeBlurChecks {
  const string Key="StarRacing.NitroEdgeBlurChecks";
  static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static readonly MethodInfo Publish=typeof(MagneticVehicle).GetMethod("UpdateExhaust",Private);
  static readonly MethodInfo UpdateEffect=typeof(NitroEdgeBlur).GetMethod("UpdateEffect",Private);
  static int assertions,ticks;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_NITRO_QA_DIR");
  static NitroEdgeBlurChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_NITRO_QA_DIR required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);ticks=0;EditorApplication.update+=Enter;
  }
  static void Enter(){if(++ticks<10)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Execute;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Check(bool ok,string message){assertions++;if(!ok)throw new Exception("Nitro blur: "+message);}
  static void Pulse(MagneticVehicle car,bool accepted){
   car.SetRaceContext(true,1,8);car.SetPresentationInput(1,true);
   typeof(MagneticVehicle).GetField("acceptedBoost",Private).SetValue(car,accepted);
   Publish.Invoke(car,new object[]{1f/60});
  }
  static void Advance(NitroEdgeBlur blur,float dt)=>UpdateEffect.Invoke(blur,new object[]{dt});
  static void Execute(){
   if(++ticks<10)return;EditorApplication.update-=Execute;assertions=0;
   float volume=AudioListener.volume,scale=Time.timeScale;GameObject root=null;
   bool hadSetting=PlayerPrefs.HasKey(NitroBlurSettings.PreferenceKey);int previousSetting=PlayerPrefs.GetInt(NitroBlurSettings.PreferenceKey);
   try{
    AudioListener.volume=0;Time.timeScale=1;NitroBlurSettings.Enabled=true;
    EnvelopeChecks();
    root=new GameObject("nitro blur fixture");
    var trackGo=new GameObject("track");trackGo.transform.SetParent(root.transform);
    var track=trackGo.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal","cloud-city",false,true)));
    var cars=new MagneticVehicle[4];var cameras=new Camera[4];var effects=new NitroEdgeBlur[4];
    var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
    for(int i=0;i<4;i++){
     var carGo=new GameObject("car "+i);carGo.transform.SetParent(root.transform);cars[i]=carGo.AddComponent<MagneticVehicle>();cars[i].Initialize(track,i,Color.cyan,balance);cars[i].Hold(true);
     var cameraGo=new GameObject("view "+i);cameraGo.transform.SetParent(root.transform);cameras[i]=cameraGo.AddComponent<Camera>();cameras[i].enabled=false;
     cameraGo.AddComponent<ChaseCamera>().target=cars[i];effects[i]=cameraGo.AddComponent<NitroEdgeBlur>();
    }
    Lifecycle(cars,effects);
    RenderChecks(root,cars,cameras,effects);
    ScenePreview(root,track,cars,cameras,effects);
    if(Environment.GetEnvironmentVariable("STAR_RACING_NITRO_RENDER_ONLY")!="1"){CloudlineVehicleChecks.Run();VehicleAudioChecks.Run();RaceSessionChecks.Run();}
    Debug.Log($"NITRO_EDGE_BLUR_OK assertions={assertions} actualPlayMode=true layouts=1,2,3,4 sizes=960x540,800x600 acceptedFeedback renderOnly={Environment.GetEnvironmentVariable("STAR_RACING_NITRO_RENDER_ONLY")=="1"}");
    File.WriteAllText(Path.Combine(Output,"result.json"),$"{{\"success\":true,\"assertions\":{assertions},\"playMode\":true,\"layouts\":[1,2,3,4],\"sizes\":[\"960x540\",\"800x600\"]}}");
    SessionState.SetBool(Key+"Failed",false);
   }catch(Exception e){Debug.LogException(e);}
   finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);if(hadSetting)PlayerPrefs.SetInt(NitroBlurSettings.PreferenceKey,previousSetting);else PlayerPrefs.DeleteKey(NitroBlurSettings.PreferenceKey);PlayerPrefs.Save();NitroBlurSettings.Reload();Time.timeScale=scale;AudioListener.volume=volume;EditorApplication.isPlaying=false;}
  }
  static void EnvelopeChecks(){
   Check(NitroBlurEnvelope.Target(299,true)==0,"below threshold");Check(NitroBlurEnvelope.Target(300,true)==0,"threshold");
   Check(Mathf.Abs(NitroBlurEnvelope.Target(375,true)-.5f)<.00001f,"midpoint");Check(NitroBlurEnvelope.Target(450,true)==1,"max");
   Check(NitroBlurEnvelope.Target(500,true)==1,"plateau");Check(NitroBlurEnvelope.Target(500,false)==0,"no boost");Check(NitroBlurEnvelope.Target(-500,true)==0,"reverse speed");
   var e=new NitroBlurEnvelope();e.Step(450,true,.016f);Check(e.Strength>0&&e.Strength<.2f,"smooth attack");
   for(int i=0;i<60;i++)e.Step(450,true,1f/60);Check(e.Strength>.999f,"sustained maximum");
   float before=e.Strength;e.Step(450,false,.016f);Check(e.Strength>0&&e.Strength<before,"smooth release");
   for(int i=0;i<120;i++)e.Step(450,false,1f/60);Check(e.Strength==0,"release ends");e.Step(450,true,1);e.Reset();Check(e.Strength==0,"reset");
   var a=new NitroBlurEnvelope();var b=new NitroBlurEnvelope();for(int i=0;i<60;i++)a.Step(375,true,1f/60);for(int i=0;i<120;i++)b.Step(375,true,1f/120);
   Check(Mathf.Abs(a.Strength-b.Strength)<.00001f,"frame independent envelope");
  }
  static void Lifecycle(MagneticVehicle[] cars,NitroEdgeBlur[] effects){
   var car=cars[0];var effect=effects[0];car.Telemetry.speedKmh=450;Pulse(car,true);Advance(effect,1);
   Check(car.NitroFeedbackActive&&effect.Strength>.99f,"accepted pulse published after cleared boost");
   Check(!(bool)typeof(MagneticVehicle).GetField("acceptedBoost",Private).GetValue(car),"snapshot does not consume twice");
   Pulse(car,false);car.SetInput(new DrivingInput{nitro=true});Advance(effect,1);Check(effect.Strength<.005f,"raw button cannot start blur");
   Pulse(car,true);Advance(effect,1);Time.timeScale=0;Check(effect.Strength==0&&!car.NitroFeedbackActive,"pause immediate");Advance(effect,.1f);
   Time.timeScale=1;Pulse(car,false);Advance(effect,.016f);Check(effect.Strength==0,"resume no stale impulse");
   Pulse(car,true);Advance(effect,1);car.ResetAt(80);Check(effect.Strength==0&&!car.NitroFeedbackActive,"restart immediate");
   car.Telemetry.speedKmh=450;Pulse(car,true);Advance(effect,1);effect.GetComponent<ChaseCamera>().LockAtFinish();Check(effect.Strength==0,"finish lock immediate");effect.GetComponent<ChaseCamera>().Snap();
   car.BeginFinishCoast();Check(effect.Strength==0&&!car.NitroFeedbackActive,"vehicle finish immediate");car.ResetAt(80);
   car.Telemetry.speedKmh=450;Pulse(car,true);Advance(effect,1);effect.GetComponent<ChaseCamera>().target=cars[1];Check(effect.Strength==0,"target switch immediate");Advance(effect,.016f);Check(effect.Strength==0,"target switch no old envelope");
   effect.GetComponent<ChaseCamera>().target=car;Advance(effect,1);effect.enabled=false;Check(effect.Strength==0,"disable");effect.enabled=true;Pulse(car,false);Advance(effect,.016f);Check(effect.Strength==0,"reenable no stale envelope");
   car.Telemetry.speedKmh=450;Pulse(car,true);Advance(effect,1);
   var recovery=(RecoveryPolicy)typeof(MagneticVehicle).GetField("recovery",Private).GetValue(car);
   Check(recovery.Observe(.01f,true,10,false,false,true,0,1,0),"production fall transition");
   Check(effect.Strength==0,"fall immediate");car.ResetAt(80);
   foreach(var x in effects)x.ResetEffect();
  }
  static Texture2D Capture(Camera[] cameras,int humans,RenderTexture rt){
   var previous=RenderTexture.active;RenderTexture.active=rt;GL.Clear(true,true,Color.black);RenderTexture.active=previous;
   for(int i=0;i<humans;i++){
    cameras[i].rect=RaceViewports.For(humans,i);cameras[i].targetTexture=rt;
    RenderPipeline.SubmitRenderRequest(cameras[i],new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
   }
   var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=previous;return image;
  }
  static int Difference(Color32 a,Color32 b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)));
  static void ScenePreview(GameObject root,TrackBuilder track,MagneticVehicle[] cars,Camera[] cameras,NitroEdgeBlur[] effects){
   var decor=new TrackEnvironmentBuilder();
   try{
    decor.Build(track.Route,track.transform,77);decor.Sky.Activate();
    var sunGo=new GameObject("preview sun");sunGo.transform.SetParent(root.transform);var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;
    var fillGo=new GameObject("preview fill");fillGo.transform.SetParent(root.transform);var fill=fillGo.AddComponent<Light>();fill.type=LightType.Directional;
    var follows=new ChaseCamera[4];
    for(int i=0;i<4;i++){
     cars[i].ResetAt(160+i*25);cars[i].Hold(true);cars[i].Telemetry.speedKmh=450;Pulse(cars[i],true);
     cameras[i].orthographic=false;cameras[i].fieldOfView=65;cameras[i].nearClipPlane=.2f;cameras[i].farClipPlane=1800;cameras[i].cullingMask=~0;
     cameras[i].GetUniversalAdditionalCameraData().renderPostProcessing=true;follows[i]=cameras[i].GetComponent<ChaseCamera>();
    }
    RaceLightingController.Apply(decor.Plan.lighting,sun,fill,follows);
    foreach(int humans in new[]{1,2,4}){
     var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);rt.Create();
     try{
      for(int i=0;i<humans;i++){
       cameras[i].rect=RaceViewports.For(humans,i);cameras[i].aspect=(1600f/900)*cameras[i].rect.width/cameras[i].rect.height;follows[i].Snap();
       typeof(ChaseCamera).GetMethod("UpdateCamera",Private).Invoke(follows[i],new object[]{1f/60});
       effects[i].enabled=false;
      }
      var normal=Capture(cameras,humans,rt);try{File.WriteAllBytes(Path.Combine(Output,$"scene-{humans}-baseline.png"),normal.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(normal);}
      effects[0].enabled=true;Advance(effects[0],2);
      var nitro=Capture(cameras,humans,rt);try{File.WriteAllBytes(Path.Combine(Output,$"scene-{humans}-nitro.png"),nitro.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(nitro);}
     }finally{foreach(var c in cameras)c.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
    Debug.Log("NITRO_SCENE_PREVIEW_OK actualShader=true fixtureSpeed=450 layouts=1,2,4 HUD=notCaptured");
   }finally{decor.Clear();}
  }
  static void RenderChecks(GameObject root,MagneticVehicle[] cars,Camera[] cameras,NitroEdgeBlur[] effects){
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/StarRacing/Generated/PrototypeRenderer.asset");
   NitroEdgeBlurRendererFeature.EnsureConfigured(renderer);int count=renderer.rendererFeatures.Count;NitroEdgeBlurRendererFeature.EnsureConfigured(renderer);Check(renderer.rendererFeatures.Count==count,"feature idempotence");
   var shader=Resources.Load<Shader>("Effects/NitroEdgeBlur");
   if(shader!=null)foreach(var message in ShaderUtil.GetShaderMessages(shader))Debug.Log("NITRO_SHADER_DIAGNOSTIC "+message.message);
   Debug.Log("NITRO_GRAPHICS "+SystemInfo.graphicsDeviceType+" shader="+(shader!=null)+" supported="+(shader!=null&&shader.isSupported));
   Check(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"shader compiles/supports graphics device");
   var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
   for(int y=0;y<128;y++)for(int x=0;x<128;x++)texture.SetPixel(x,y,((x/3+y/3)%2==0)?Color.white:new Color(.03f,.03f,.03f));texture.Apply();
   var materials=new Material[4];
   try{
    for(int i=0;i<4;i++){
     var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.transform.SetParent(root.transform);quad.transform.position=new Vector3(i*20,10000,5);quad.transform.localScale=new Vector3(12,12,1);quad.layer=20+i;
     var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));materials[i]=material;material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",new[]{Color.red,Color.green,Color.blue,Color.yellow}[i]);quad.GetComponent<MeshRenderer>().sharedMaterial=material;
     cameras[i].transform.SetPositionAndRotation(new Vector3(i*20,10000,0),Quaternion.identity);cameras[i].orthographic=true;cameras[i].orthographicSize=3;cameras[i].clearFlags=CameraClearFlags.SolidColor;cameras[i].backgroundColor=Color.black;cameras[i].cullingMask=1<<(20+i);
     cameras[i].GetUniversalAdditionalCameraData().renderPostProcessing=false;
     cameras[i].GetUniversalAdditionalCameraData().requiresColorTexture=true;
     cars[i].Telemetry.speedKmh=450;Pulse(cars[i],true);Advance(effects[i],2);
    }
    foreach(var size in new[]{new Vector2Int(960,540),new Vector2Int(800,600)})foreach(int humans in new[]{1,2,3,4}){
     var rt=new RenderTexture(size.x,size.y,24,RenderTextureFormat.ARGB32);rt.Create();
     try{
      foreach(var effect in effects)effect.enabled=false;
      var baseline=Capture(cameras,humans,rt);
      try{
       for(int boosting=0;boosting<humans;boosting++){
        foreach(var effect in effects)effect.enabled=false;effects[boosting].enabled=true;Advance(effects[boosting],2);
        var blurred=Capture(cameras,humans,rt);
        try{
         var before=baseline.GetPixels32();var after=blurred.GetPixels32();var rect=RaceViewports.For(humans,boosting);int edgeChanged=0,centerChanged=0,outsideChanged=0,carChanged=0;
         for(int y=0;y<size.y;y++)for(int x=0;x<size.x;x++){
          int d=Difference(before[y*size.x+x],after[y*size.x+x]);if(d<=1)continue;
          float u=((x+.5f)/size.x-rect.x)/rect.width,v=((y+.5f)/size.y-rect.y)/rect.height;
          if(u<0||u>1||v<0||v>1)outsideChanged++;
          else if(u>=.2f&&u<=.8f&&v>=.2f&&v<=.8f)centerChanged++;
          else if(u>=.38f&&u<=.62f&&v<.2f)carChanged++;
          else edgeChanged++;
         }
         Check(outsideChanged==0,$"isolated {size}/{humans}/{boosting} outside={outsideChanged}");Check(centerChanged==0,$"clear center {size}/{humans}/{boosting} center={centerChanged}");Check(carChanged==0,$"clear chase car {size}/{humans}/{boosting} car={carChanged}");Check(edgeChanged>100,$"visible peripheral blur {size}/{humans}/{boosting} changed={edgeChanged}");
         Debug.Log($"NITRO_RENDER_PIXELS size={size} humans={humans} boosting={boosting} outside={outsideChanged} center={centerChanged} edge={edgeChanged}");
         if(size.x==960&&boosting==0){File.WriteAllBytes(Path.Combine(Output,$"render-{humans}-baseline.png"),baseline.EncodeToPNG());File.WriteAllBytes(Path.Combine(Output,$"render-{humans}-nitro.png"),blurred.EncodeToPNG());}
        }finally{UnityEngine.Object.DestroyImmediate(blurred);}
       }
      }finally{UnityEngine.Object.DestroyImmediate(baseline);}
     }finally{foreach(var c in cameras)c.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
   }finally{foreach(var m in materials)if(m!=null)UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(texture);}
  }
 }
}
