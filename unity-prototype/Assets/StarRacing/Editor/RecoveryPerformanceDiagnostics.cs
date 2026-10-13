using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
    // Opt-in production-physics measurement. Does not alter solver, collision
    // geometry, recovery rules, or run an aggregate test suite.
    [InitializeOnLoad]
    public static class RecoveryPerformanceDiagnostics {
        const string Key="StarRacing.RecoveryPerformance";
        static int startup;
        static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_RECOVERY_PROFILE_DIR");
        static string Theme=>Environment.GetEnvironmentVariable("STAR_RACING_RECOVERY_PROFILE_THEME")??"cloud-city";
        static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
        static RecoveryPerformanceDiagnostics(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Run(){
            if(string.IsNullOrEmpty(Output))throw new ArgumentException("STAR_RACING_RECOVERY_PROFILE_DIR required");
            Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            startup=0;EditorApplication.update+=Enter;
        }
        static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
        static void Changed(PlayModeStateChange state){
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){startup=0;EditorApplication.update+=Measure;}
            if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
        }
        [Serializable] public sealed class Sample {
            public string scenario;public int repeat,tick,fallingBefore,fallingAfter,falls,respawns,contacts,supportContacts;
            public double projectionMs,vehicleMs,physicsMs,renderSubmitMs=-1,readbackMs=-1;public long allocatedBytes;
            // First interval includes stepper inventory setup; final presentation
            // publication is outside these eight intervals but inside physicsMs.
            public double[] substepMs=new double[RacePhysicsStepper.Substeps];
        }
        [Serializable] public sealed class Report {
            public string source,unity,device,notes,theme,broadphaseName;
            public string[] broadphaseNames;
            public int broadphase;
            public double setupMs;public List<double> warmupMs=new List<double>();
            public int entrants=32,substeps=RacePhysicsStepper.Substeps,colliders,supportColliders,triangles;
            public int expectedTicks=2700;public List<Sample> samples=new List<Sample>(2700);
            public List<string> hostSnapshots=new List<string>();
        }
        static double Ms(long start)=>(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
        static void Measure(){
            if(++startup<15)return;EditorApplication.update-=Measure;
            GameObject root=null;RenderTexture target=null;Texture2D pixels=null;
            var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime,volume=AudioListener.volume;
            var ambient=RenderSettings.ambientLight;var ambientMode=RenderSettings.ambientMode;
            try {
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;AudioListener.volume=0;
                if(Theme!="cloud-city"&&Theme!="space-station")throw new ArgumentException("Unsupported profile theme "+Theme);
                var physicsSettings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
                var broadphaseProperty=physicsSettings.FindProperty("m_BroadphaseType");
                int broadphase=broadphaseProperty.intValue;
                var broadphaseNames=broadphaseProperty.enumDisplayNames;
                string broadphaseName=broadphaseNames[broadphaseProperty.enumValueIndex];
                var expected=Environment.GetEnvironmentVariable("STAR_RACING_PROFILE_BROADPHASE");
                if(expected!=null&&int.Parse(expected)!=broadphase)throw new Exception("Broadphase configuration mismatch");
                if(broadphase==2&&broadphaseName.Replace(" ","")!="AutomaticBoxPruning")throw new Exception("ABP enum name mismatch: "+broadphaseName);
                var report=new Report{source=Environment.GetEnvironmentVariable("STAR_RACING_PROFILE_SOURCE"),unity=Application.unityVersion,theme=Theme,broadphase=broadphase,
                    broadphaseName=broadphaseName,broadphaseNames=broadphaseNames,
                    device=SystemInfo.graphicsDeviceName,notes="Exclusive Editor Play Mode; 32 production vehicles; seed 2273265884, "+Theme+", rails and jumps enabled. Three repeats of drive / forced edge departure / forced remote fall, 300 x 10ms ticks each. Native physics has unchanged eight substeps. Render submission and synchronous GPU readback are sampled separately every ten ticks at 1280x720. These CPU tick/offscreen measurements are NOT real Player FPS. AI, HUD, audio and environment decor are excluded. Scenario setup, warmup, import and compilation are excluded from samples."};
                long setupAt=Stopwatch.GetTimestamp();root=new GameObject("Recovery performance fixture");
                var road=new GameObject("road");road.transform.SetParent(root.transform);
                var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(2273265884,"normal",Theme,true,true)));
                foreach(var collider in track.GetComponentsInChildren<Collider>()){report.colliders++;if(collider.name==RoadVolumeMesh.SupportName)report.supportColliders++;}
                foreach(var mesh in track.GetComponentsInChildren<MeshFilter>())if(mesh.sharedMesh!=null)report.triangles+=mesh.sharedMesh.triangles.Length/3;
                var light=new GameObject("profile sun");light.transform.SetParent(root.transform);light.AddComponent<Light>().type=LightType.Directional;
                light.transform.rotation=Quaternion.Euler(45,-30,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.65f);
                var cameraObject=new GameObject("profile camera");cameraObject.transform.SetParent(root.transform);var camera=cameraObject.AddComponent<Camera>();
                camera.enabled=false;camera.nearClipPlane=.1f;camera.farClipPlane=500;camera.fieldOfView=65;
                target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};
                var cars=new MagneticVehicle[32];var ticks=new Action[32];var events=new List<RecoveryEvent>(64);
                Sample activeSample=null;int part=0;long partAt=0;
                Action afterSubstep=()=>{long now=Stopwatch.GetTimestamp();if(activeSample!=null)activeSample.substepMs[part]=(now-partAt)*1000.0/Stopwatch.Frequency;part++;partAt=now;};
                int contacts=0,supportContacts=0;
                var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                for(int i=0;i<cars.Length;i++){
                    var obj=new GameObject("profile entrant "+i);obj.transform.SetParent(root.transform);
                    var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,i,Color.HSVToRGB(i/32f,.6f,.9f),balance,true);cars[i]=car;
                    ticks[i]=(Action)Delegate.CreateDelegate(typeof(Action),car,Tick);
                    car.ContactObserved+=collision=>{contacts++;if(collision.collider.name==RoadVolumeMesh.SupportName)supportContacts++;};
                }
                void Step(int number,Sample sample){
                    contacts=supportContacts=0;events.Clear();
                    long allocation=GC.GetAllocatedBytesForCurrentThread(),at=Stopwatch.GetTimestamp();
                    foreach(var car in cars){if(sample!=null&&car.IsFalling)sample.fallingBefore++;car.PrepareProjection();car.SetRaceContext(true,1,32,number*.01);}
                    if(sample!=null)sample.projectionMs=Ms(at);at=Stopwatch.GetTimestamp();
                    foreach(var tick in ticks)tick();
                    if(sample!=null)sample.vehicleMs=Ms(at);at=Stopwatch.GetTimestamp();
                    activeSample=sample;part=0;partAt=at;
                    RacePhysicsStepper.SimulatePrepared(cars,.01f,RacePhysicsStepper.Substeps,afterSubstep);
                    if(sample!=null){sample.physicsMs=Ms(at);sample.allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;sample.contacts=contacts;sample.supportContacts=supportContacts;}
                    foreach(var car in cars){car.DrainRecoveryEvents(events);if(sample!=null&&car.IsFalling)sample.fallingAfter++;}
                    if(sample!=null)foreach(var e in events){if(e.Kind==RecoveryEventKind.Fall)sample.falls++;else sample.respawns++;}
                }
                void Render(Sample sample){
                    // Fixed world camera keeps culling/framing comparable across scenarios.
                    var frame=track.Route.Evaluate(110);camera.transform.position=frame.position+frame.normal*18-frame.tangent*45;camera.transform.LookAt(frame.position+frame.tangent*35,frame.normal);
                    long at=Stopwatch.GetTimestamp();RenderPipeline.SubmitRenderRequest(camera,request);
                    if(sample!=null)sample.renderSubmitMs=Ms(at);at=Stopwatch.GetTimestamp();
                    var previous=RenderTexture.active;
                    try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();}finally{RenderTexture.active=previous;}
                    if(sample!=null)sample.readbackMs=Ms(at);
                }
                for(int repeat=0;repeat<3;repeat++)foreach(string scenario in new[]{"drive","edge","remote"}){
                    if(repeat==0&&scenario=="drive")report.setupMs=Ms(setupAt);
                    long warmupAt=Stopwatch.GetTimestamp();
                    var ids=new List<int>();
                    foreach(var process in Process.GetProcessesByName("Unity")){using(process)ids.Add(process.Id);}
                    report.hostSnapshots.Add(scenario+"/"+repeat+": "+string.Join(",",ids));
                    if(ids.Count!=1||ids[0]!=Process.GetCurrentProcess().Id)throw new Exception("Host isolation unproven: "+string.Join(",",ids));
                    for(int i=0;i<cars.Length;i++){
                        cars[i].ResetAt(80+(i/2)*6,i%2==0?-2.5f:2.5f);cars[i].SetInput(default);
                        if(!cars[i].HasProvenCheckpoint)throw new Exception("Unproven profile checkpoint entrant "+i);
                    }
                    Physics.SyncTransforms();for(int i=0;i<40;i++)Step(i,null);Render(null);report.warmupMs.Add(Ms(warmupAt));
                    foreach(var car in cars){
                        var frame=car.Frame;float side=car.Seat%2==0?-1:1;
                        if(scenario=="drive")car.Body.linearVelocity=frame.tangent*car.Drive.BaseSpeed;
                        else {
                            var position=frame.position+frame.right*side*(frame.halfWidth+(scenario=="edge"?.8f:30f))+frame.normal*.15f;
                            car.Body.position=position;car.transform.position=position;car.ResetPresentation();
                            car.Body.linearVelocity=scenario=="edge"?frame.right*side*10:Vector3.zero;
                        }
                        car.SetInput(new DrivingInput{throttle=scenario=="drive"?1:0});
                    }
                    Physics.SyncTransforms();int falls=0,respawns=0;
                    for(int i=0;i<300;i++){
                        var sample=new Sample{scenario=scenario,repeat=repeat,tick=i};Step(i,sample);
                        if(i%10==0)Render(sample);
                        report.samples.Add(sample);falls+=sample.falls;respawns+=sample.respawns;
                        if(i==10&&repeat==0){File.WriteAllBytes(Path.Combine(Output,scenario+".png"),pixels.EncodeToPNG());}
                    }
                    UnityEngine.Debug.Log("RECOVERY_PROFILE_CASE scenario="+scenario+" repeat="+repeat+" falls="+falls+" respawns="+respawns);
                    if(scenario!="drive"&&(falls<16||respawns<16))throw new Exception("Mass fall/recovery was not reproduced: "+scenario);
                }
                if(report.samples.Count!=report.expectedTicks)throw new Exception("Incomplete profile samples");
                File.WriteAllText(Path.Combine(Output,"samples.json"),JsonUtility.ToJson(report,true));
                UnityEngine.Debug.Log("RECOVERY_PROFILE_OK ticks="+report.samples.Count+" colliders="+report.colliders+" supports="+report.supportColliders);
            }catch(Exception e){SessionState.SetBool(Key+"Failed",true);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());UnityEngine.Debug.LogException(e);}
            finally {
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
                Physics.simulationMode=mode;Time.fixedDeltaTime=dt;AudioListener.volume=volume;RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=ambientMode;
                EditorApplication.isPlaying=false;
            }
        }
    }
}
