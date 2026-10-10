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
    // Real Play Mode, production forces and CCD substeps on production rail geometry.
    [InitializeOnLoad]
    public static class RoadStabilityChecks
    {
        const string Key="StarRacing.RoadStability";
        static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
        static int ticks,cases,failures;
        static readonly FieldInfo Forces=typeof(MagneticVehicle).GetField("preparedForces",BindingFlags.NonPublic|BindingFlags.Instance);
        static bool IsProtected(MagneticVehicle car)=>(bool)(typeof(MagneticVehicle).GetProperty("ProtectRoadContacts",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(car)??false);
        static TrackBuilder fixtureTrack; static GameObject fixtureRoot;
        static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_STABILITY_QA_DIR");
        static bool Visual=>SessionState.GetBool(Key+"Visual",false);
        static bool Trace=>Environment.GetEnvironmentVariable("STAR_RACING_STABILITY_TRACE")=="1";
        static bool Measure=>Environment.GetEnvironmentVariable("STAR_RACING_STABILITY_MEASURE")=="1";
        static RoadStabilityChecks(){EditorApplication.playModeStateChanged+=Changed;}
        public static void RunAdjacent(){
            AirSteeringChecks.Run();RecoveryGhostChecks.Run();GravityChecks.Run();RoadSmoothnessChecks.Run();RoadVolumeChecks.Run();
            Debug.Log("ROAD_STABILITY_ADJACENT_OK air steering recovery gravity smoothness");
        }
        public static void RunVisual(){SessionState.SetBool(Key+"Visual",true);Run();}
        public static void Run(){
            if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_STABILITY_QA_DIR required");
            Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);ticks=0;EditorApplication.update+=Enter;
        }
        static void Enter(){if(++ticks<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
        static void Changed(PlayModeStateChange state){
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Execute;}
            if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
        }
        static void Check(bool ok,string reason){if(!ok){failures++;File.AppendAllText(Path.Combine(Output,"metrics.txt"),"FAIL "+reason+"\n");}}
        static void Execute(){
            if(++ticks<15)return;EditorApplication.update-=Execute;
            var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime,volume=AudioListener.volume;cases=failures=0;
            try{
                AudioListener.volume=0;Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
                fixtureRoot=new GameObject("shared stability geometry");fixtureTrack=Road(fixtureRoot,true);
                if(Visual){Case(true,83.3333f,1,"graze");Case(false,138.8889f,-1,"direct");}
                else foreach(bool human in (Trace?new[]{true}:new[]{true,false}))foreach(float speed in (Trace?new[]{42f}:new[]{42f,83.3333f,138.8889f}))foreach(float side in (Trace?new[]{-1f}:new[]{-1f,1f})) {
                    Case(human,speed,side,"graze");if(Trace)continue;Case(human,speed,side,"direct");Case(human,speed,side,"maneuver");Case(human,speed,side,"edge");Case(human,speed,side,"pinch");
                }
                if(!Visual && !Trace && Environment.GetEnvironmentVariable("STAR_RACING_STABILITY_EXTENDED")=="1") {
                    UnityEngine.Object.DestroyImmediate(fixtureRoot);fixtureRoot=new GameObject("banked stability geometry");fixtureTrack=Road(fixtureRoot,true,30);
                    foreach(bool human in new[]{true,false})foreach(float side in new[]{-1f,1f})foreach(string kind in new[]{"graze","maneuver","edge"})Case(human,83.3333f,side,kind);
                    Lifecycle();MissedJump();
                }
                File.WriteAllText(Path.Combine(Output,"result.txt"),$"ROAD_STABILITY_{(Measure?"MEASURED":failures==0?"OK":"FAILED")} cases={cases} failures={failures}\n");
            }catch(Exception e){failures++;Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}
            finally{if(fixtureRoot!=null)UnityEngine.Object.DestroyImmediate(fixtureRoot);fixtureTrack=null;Physics.simulationMode=mode;Time.fixedDeltaTime=dt;AudioListener.volume=volume;SessionState.SetBool(Key+"Failed",!Measure&&failures>0);EditorApplication.isPlaying=false;}
        }
        static TrackBuilder Road(GameObject root,bool rails,float bank=0){
            var d=RoadVolumeChecks.FlatRoad();var samples=new Procedural.Sample[241];
            for(int i=0;i<samples.Length;i++){var s=d.samples[0];samples[i]=new Procedural.Sample {index=i,distance=i*5,progress=i/240.0,halfWidth=8,position=new Procedural.DVec(1000,120,i*5),tangent=s.tangent,normal=s.normal,right=s.right,kind=s.kind,segmentIndex=0,railLeft=rails,railRight=rails};}
            if(bank!=0){var rotation=Quaternion.AngleAxis(bank,Vector3.forward);foreach(var sample in samples){var normal=rotation*Vector3.up;var right=rotation*Vector3.left;sample.normal=new Procedural.DVec(normal.x,normal.y,normal.z);sample.right=new Procedural.DVec(right.x,right.y,right.z);}}
            d.samples=samples;d.totalLength=1200;d.guardrailMode=rails?"full":"none";
            var road=new GameObject("stability road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(d));return track;
        }
        static MagneticVehicle Car(GameObject root,TrackBuilder track,int seat,bool human,float lane,float speed,float yaw){
            var obj=new GameObject("stability car "+seat);obj.transform.SetParent(root.transform);var car=obj.AddComponent<MagneticVehicle>();
            car.Initialize(track,seat,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text),human);car.enabled=false;car.ResetAt(100,lane);
            car.Body.rotation=car.Frame.Rotation*Quaternion.Euler(0,yaw,0);car.Body.linearVelocity=car.Body.rotation*Vector3.forward*speed;
            obj.transform.SetPositionAndRotation(car.Body.position,car.Body.rotation);return car;
        }
        static void Capture(GameObject root,MagneticVehicle car,string name){
            var lightObject=new GameObject("evidence light");lightObject.transform.SetParent(root.transform);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;lightObject.transform.rotation=Quaternion.Euler(45,-30,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.65f);
            var cameraObject=new GameObject("evidence camera");cameraObject.transform.SetParent(root.transform);var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.08f,.11f);camera.fieldOfView=48;
            camera.transform.position=car.Body.position+car.Frame.right*10+car.Frame.normal*6-car.Frame.tangent*14;camera.transform.LookAt(car.Body.position+car.Frame.normal*.6f);
            var rt=new RenderTexture(1400,900,24);rt.Create();var previous=RenderTexture.active;var pixels=new Texture2D(1400,900,TextureFormat.RGB24,false);
            try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1400,900),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Output,name),pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);}
        }
        static void Set(MagneticVehicle car,string name,object value)=>typeof(MagneticVehicle).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(car,value);
        static void Lifecycle(){
            cases++;var root=new GameObject("protection lifecycle");
            try{
                var car=Car(root,fixtureTrack,0,true,0,83,0);car.SetRaceContext(true,1,1,1);
                foreach(var input in new[]{new DrivingInput{steer=1,throttle=1},new DrivingInput{steer=-1,drift=true,throttle=1}}){
                    Set(car,"neutralContactRemaining",.6f);car.SetInput(input);car.PrepareProjection();Tick.Invoke(car,null);
                    Check(!(bool)typeof(MagneticVehicle).GetProperty("NeutralContactRecovery",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(car),"neutral recovery opposed steering/drift");RacePhysicsStepper.Simulate(.01f);
                }
                car.ResetAt(100,0);var f=car.Frame;car.Body.position=f.position+f.normal*4;car.transform.position=car.Body.position;car.SetInput(default);
                Set(car,"unlandedJump",true);Physics.SyncTransforms();car.PrepareProjection();Tick.Invoke(car,null);
                var packet=(VehicleForceFrame)Forces.GetValue(car);
                Check(!IsProtected(car)&&Vector3.Distance(packet.acceleration,car.GravityAcceleration)<.0001f&&packet.force.sqrMagnitude<.0001f,"failed confirmed flight was magnetically rescued");
                typeof(MagneticVehicle).GetMethod("EndJump",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(car,new object[]{"corridor-or-timeout"});
                Check((bool)typeof(MagneticVehicle).GetField("unlandedJump",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(car),"corridor exit lost flight provenance");
                car.ResetAt(100);Check(!(bool)typeof(MagneticVehicle).GetField("unlandedJump",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(car),"reset left flight provenance");
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static void MissedJump(){
            cases++;var root=new GameObject("actual missed authored jump");
            try{
                var road=new GameObject("jump road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();
                track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal","cloud-city",true,true)));
                var jump=Array.Find(track.Route.Definition.jumps,x=>x.kind=="mandatory");var car=Car(root,track,0,true,0,83.3333f,0);
                car.ResetAt(30+jump.rampStartIndex*5-10,-(float)jump.lateralCenter);car.Body.linearVelocity=car.Frame.tangent*83.3333f;Physics.SyncTransforms();
                bool launched=false,fallen=false,recovered=false;int revision=car.PositionRevision;
                for(int tick=0;tick<600;tick++){
                    car.SetInput(new DrivingInput{throttle=1});car.SetRaceContext(true,1,1,tick*.01);car.PrepareProjection();Tick.Invoke(car,null);
                    if(!launched&&car.ActiveJumpId!=""){
                        launched=true;
                        // A reproducible adverse takeoff impulse tests a genuine miss,
                        // without assigning a pose or synthesizing activeJump state.
                        car.Body.AddForce(car.Frame.right*70,ForceMode.VelocityChange);
                    }
                    RacePhysicsStepper.Simulate(.01f);fallen|=car.IsFalling;
                    if(fallen&&car.PositionRevision!=revision){recovered=true;break;}
                }
                Check(launched&&fallen&&recovered,"actual adverse jump did not retain fall/recovery");
                File.AppendAllText(Path.Combine(Output,"metrics.txt"),$"MISSED_JUMP launched={launched} fallen={fallen} recovered={recovered}\n");
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static float[] Case(bool human,float speed,float side,string kind,bool reference=false){
            float[] commandReference=!reference&&(kind=="maneuver"||kind=="edge")?Case(human,speed,side,kind,true):null;
            var yawTrace=new float[250*RacePhysicsStepper.Substeps];int sample=0;float peakExcessYaw=0;
            if(!reference)cases++;var root=new GameObject("stability "+kind);
            try{
                var track=fixtureTrack;foreach(var mesh in track.GetComponentsInChildren<MeshCollider>())if(mesh.sharedMesh.name=="Procedural guard rails")mesh.enabled=kind!="edge";float angle=kind=="direct"?65:kind=="graze"?8:0;
                float extent=Mathf.Abs(Mathf.Sin(angle*Mathf.Deg2Rad))*VehicleGeometry.HalfLength+Mathf.Abs(Mathf.Cos(angle*Mathf.Deg2Rad))*VehicleGeometry.HalfWidth;
                float lane=kind=="maneuver"?0:kind=="pinch"?6.1f:7.85f-extent-.04f;
                Check(lane+extent<7.85f,"invalid overlapping rail setup");
                var car=Car(root,track,0,human,side*lane,speed,side*angle);
                var cars=kind=="pinch"?new[]{car,Car(root,track,1,human,side*2.1f,speed,side*20)}:new[]{car};
                int hits=0;float impulse=0,peakYaw=0,peakStep=0,peakHeight=0,peakLane=0,minSpeed=speed,peakTilt=0;bool fallen=false;string peakDetail="";int protectedTicks=0;
                car.ContactObserved+=c=>{if(c.collider.name.Contains("guard")||c.rigidbody!=null){hits++;impulse=Mathf.Max(impulse,c.impulse.magnitude);}};
                if(reference)foreach(var own in car.GetComponentsInChildren<Collider>())foreach(var mesh in track.GetComponentsInChildren<MeshCollider>())if(mesh.sharedMesh.name=="Procedural guard rails")Physics.IgnoreCollision(own,mesh,true);
                Physics.SyncTransforms();var previous=car.Body.rotation;var previousVelocity=car.Body.linearVelocity;int traceRows=0;
                var inventory=new System.Collections.Generic.Dictionary<int,Collider>();
                if(Trace){foreach(var c in fixtureRoot.GetComponentsInChildren<Collider>())inventory[c.GetInstanceID()]=c;foreach(var c in root.GetComponentsInChildren<Collider>())inventory[c.GetInstanceID()]=c;RoadNativeContactObserver.Begin(root);}
                for(int tick=0;tick<250;tick++){
                    for(int i=0;i<cars.Length;i++){
                        float steer=kind=="maneuver"?(tick<50?side:tick<100?-side:0):kind=="edge"&&tick<80?side:0;
                        if(kind=="pinch"&&i==1&&tick<80)steer=side*.4f;
                        cars[i].SetInput(new DrivingInput {throttle=1,steer=steer,drift=kind=="maneuver"&&tick<100,nitro=speed>100});
                        cars[i].SetRaceContext(true,1,cars.Length,tick*.01);cars[i].Hold(false);cars[i].PrepareProjection();Tick.Invoke(cars[i],null);
                    }
                    RacePhysicsStepper.Simulate(.01f,()=>{
                        if(Trace){RoadNativeContactObserver.AfterStep();var packet=(VehicleForceFrame)Forces.GetValue(car);var predictedDelta=(packet.acceleration+packet.force/car.Body.mass)*(.01f/RacePhysicsStepper.Substeps);
                            float loss=(previousVelocity.z+predictedDelta.z)-car.Body.linearVelocity.z;
                            if(loss>1&&traceRows++<8){string trace=$"LOSS tick={tick} zBefore={previousVelocity.z} zAfter={car.Body.linearVelocity.z} expectedDelta={predictedDelta} pose={car.Body.position} angle={car.Body.rotation.eulerAngles}";
                                foreach(var c in car.GetComponentsInChildren<Collider>())trace+=RoadNativeContactObserver.For(c,inventory);
                                File.AppendAllText(Path.Combine(Output,"trace.txt"),trace+"\n");}previousVelocity=car.Body.linearVelocity;}
                        var f=track.Route.Project(car.Body.position,car.Distance,30);
                        peakHeight=Mathf.Max(peakHeight,Vector3.Dot(car.Body.position-f.position,f.normal));
                        peakLane=Mathf.Max(peakLane,Mathf.Abs(Vector3.Dot(car.Body.position-f.position,f.right)));
                        minSpeed=Mathf.Min(minSpeed,car.Body.linearVelocity.magnitude);
                        float signedYaw=Vector3.Dot(car.Body.angularVelocity,f.normal);yawTrace[sample]=signedYaw;
                        if(commandReference!=null)peakExcessYaw=Mathf.Max(peakExcessYaw,Mathf.Abs(signedYaw-commandReference[sample]));sample++;
                        float yaw=Mathf.Abs(signedYaw);
                        if(yaw>peakYaw){var packet=(VehicleForceFrame)Forces.GetValue(car);peakDetail=$" protected={IsProtected(car)} supports={car.HandlingSupportCount} angularCommand={packet.angularAcceleration} torque={packet.torque} normalSpeed={Vector3.Dot(car.Body.linearVelocity,f.normal):F3} lateralSpeed={Vector3.Dot(car.Body.linearVelocity,f.right):F3}";peakYaw=yaw;}
                        if(IsProtected(car))protectedTicks++;
                        peakStep=Mathf.Max(peakStep,Quaternion.Angle(previous,car.Body.rotation));previous=car.Body.rotation;
                        peakTilt=Mathf.Max(peakTilt,Vector3.Angle(car.Body.rotation*Vector3.up,f.normal));
                        fallen|=car.IsFalling;
                    });
                    minSpeed=Mathf.Min(minSpeed,car.Body.linearVelocity.magnitude);
                    if(Visual&&!reference&&tick==30)Capture(root,car,kind+"-"+human+".png");
                }
                string id=$"reference={reference} kind={kind} human={human} speed={speed:F3} side={side}";
                File.AppendAllText(Path.Combine(Output,"metrics.txt"),$"{id} hits={hits} impulse={impulse:F1} yaw={peakYaw:F3} stepAngle={peakStep:F3} height={peakHeight:F3} lane={peakLane:F3} tilt={peakTilt:F2} minSpeed={minSpeed:F3} fallen={fallen} protectedSubsteps={protectedTicks} peakDetail={peakDetail} excessYaw={peakExcessYaw:F3}\n");
                if(reference)return yawTrace;
                Check(!fallen&&peakLane<=8.1f,id+" left road");Check(peakHeight<1.8f&&peakTilt<35,id+" lifted/rolled");Check(peakStep<1,id+" violent spin");
                if(kind!="maneuver" && kind!="edge")Check(peakYaw<2.5f,id+" uncommanded spin");
                else {Check(peakExcessYaw<2.5f,id+" excess contact spin over same-input reference");Check(Mathf.Abs(Vector3.Dot(car.Body.angularVelocity,car.Frame.normal))<.3f,id+" commanded spin did not settle after release");}
                if(kind=="graze"||kind=="direct"||kind=="pinch")Check(hits>0,id+" no target contact");
                if(kind=="direct")Check(minSpeed<speed*.5f,id+" impact did not brake");
                if(kind=="graze")Check(minSpeed>=speed*Mathf.Cos(8*Mathf.Deg2Rad)*.9f,id+" grazing contact lost tempo");
                return yawTrace;
            }finally{if(Trace)RoadNativeContactObserver.End();UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
