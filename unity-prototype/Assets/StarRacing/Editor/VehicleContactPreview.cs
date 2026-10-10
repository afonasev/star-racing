using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype
{
    // Offscreen Editor Play Mode evidence; no Player build, focus or system input.
    [InitializeOnLoad]
    public static class VehicleContactPreview
    {
        const string Key="StarRacing.ContactPreview";
        static int ticks;
        static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_CONTACT_PREVIEW_DIR");
        static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
        static VehicleContactPreview(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Run(){Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);ticks=0;EditorApplication.update+=Enter;}
        static void Enter(){if(++ticks<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
        static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Draw;}if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);if(!SessionState.GetBool(Key+"Failed",false)){try {PrototypeChecks.Run();RosterFixtureEquivalenceChecks.Run();}catch(Exception e){SessionState.SetBool(Key+"Failed",true);Debug.LogException(e);}}EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
        static void Draw()
        {
            if(++ticks<15)return;EditorApplication.update-=Draw;
            var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime,volume=AudioListener.volume;
            try {Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;AudioListener.volume=0;Pair();Crest();Debug.Log("CONTACT_PREVIEW_OK actualPlayMode=true offscreen=true");}
            catch(Exception e){SessionState.SetBool(Key+"Failed",true);Debug.LogException(e);}
            finally {Physics.simulationMode=mode;Time.fixedDeltaTime=dt;AudioListener.volume=volume;EditorApplication.isPlaying=false;}
        }
        static MagneticVehicle Car(GameObject root,TrackBuilder track,int seat,float distance,float lane,float speed)
        {
            var obj=new GameObject("contact preview car");obj.transform.SetParent(root.transform);
            var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,seat,seat==0?Color.cyan:Color.yellow,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text));car.enabled=false;car.ResetAt(distance,lane);car.Body.linearVelocity=car.Frame.tangent*speed;car.SetInput(new DrivingInput{throttle=1});return car;
        }
        static void Step(MagneticVehicle[] cars,int index){foreach(var car in cars){car.SetRaceContext(true,1,cars.Length,index*.01);car.PrepareProjection();Tick.Invoke(car,null);}RacePhysicsStepper.Simulate(.01f);}
        static void Pair()
        {
            var root=new GameObject("contact preview pair");
            try {
                var road=new GameObject("preview road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(RoadVolumeChecks.FlatRoad()));
                var cars=new[]{Car(root,track,0,80,-1.4f,44),Car(root,track,1,80,1.4f,42)};cars[0].Body.linearVelocity+=Vector3.right*2;cars[1].Body.linearVelocity-=Vector3.right*2;Physics.SyncTransforms();
                for(int i=0;i<40;i++)Step(cars,i);
                var at=(cars[0].Body.position+cars[1].Body.position)*.5f;
                Capture(root,at+Vector3.right*10+Vector3.up*6-Vector3.forward*12,at+Vector3.up*.6f,"side-contact.png");
            }finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        static void Crest()
        {
            var root=new GameObject("contact preview crest");
            try {
                var definition=RoadVolumeChecks.FlatRoad();
                for(int i=0;i<definition.samples.Length;i++){double z=i*5,x=(z-130)/14.0,y=6*Math.Exp(-x*x),slope=-12*x/14*Math.Exp(-x*x),length=Math.Sqrt(1+slope*slope);definition.samples[i].position=new Procedural.DVec(1000,120+y,z);definition.samples[i].tangent=new Procedural.DVec(0,slope/length,1/length);definition.samples[i].normal=new Procedural.DVec(0,1/length,-slope/length);}
                var road=new GameObject("preview crest road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(definition));
                var car=Car(root,track,0,80,0,83.33f);car.SetInput(new DrivingInput{throttle=1,nitro=true});Physics.SyncTransforms();
                bool captured=false;
                for(int i=0;i<250;i++){Step(new[]{car},i);float height=Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.normal);if(height>.48f){Capture(root,car.Body.position+car.Frame.right*10+car.Frame.normal*5-car.Frame.tangent*12,car.Body.position+car.Frame.tangent*2,"boost-crest.png");captured=true;break;}}
                if(!captured)throw new Exception("Crest preview did not reach measured crest excursion");
            }finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        static void Capture(GameObject root,Vector3 from,Vector3 to,string filename)
        {
            var lightObject=new GameObject("preview light");lightObject.transform.SetParent(root.transform);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;lightObject.transform.rotation=Quaternion.Euler(45,-30,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.65f);
            var cameraObject=new GameObject("preview camera");cameraObject.transform.SetParent(root.transform);var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.08f,.11f);camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.transform.position=from;camera.transform.LookAt(to);
            var rt=new RenderTexture(1400,900,24,RenderTextureFormat.ARGB32);rt.Create();var previous=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            try {RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,filename),image.EncodeToPNG());}
            finally {RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
