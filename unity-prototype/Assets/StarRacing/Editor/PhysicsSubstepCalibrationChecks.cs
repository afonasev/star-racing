using System;
using System.Reflection;
using UnityEngine;

namespace StarRacingPrototype
{
    public static class PhysicsSubstepCalibrationChecks
    {
        static void Check(bool valid,string message){if(!valid)throw new Exception("Force replay: "+message);}
        static (Vector3 linear,Vector3 angular) Measure(int kind,float[] steps,bool direct,Quaternion rotation)
        {
            var obj=new GameObject("isolated force calibration");
            try {
                var box=obj.AddComponent<BoxCollider>();box.size=Vector3.one;
                var body=obj.AddComponent<Rigidbody>();body.mass=2;body.useGravity=false;body.linearDamping=0;body.angularDamping=0;
                body.centerOfMass=new Vector3(.1f,-.18f,.07f);body.inertiaTensor=new Vector3(2,3,4);body.inertiaTensorRotation=Quaternion.identity;
                body.rotation=rotation;obj.transform.rotation=rotation;Physics.SyncTransforms();
                var packet=new VehicleForceFrame();
                Vector3 f=kind==0?new Vector3(10,0,0):Vector3.zero;
                Vector3 a=kind==1?new Vector3(0,3,0):Vector3.zero;
                Vector3 torque=kind==2?new Vector3(0,0,8):Vector3.zero;
                Vector3 angular=kind==3?new Vector3(0,0,7):Vector3.zero;
                Vector3 point=body.worldCenterOfMass+Vector3.right;
                if(direct) {
                    body.AddForce(f,ForceMode.Force);body.AddForce(a,ForceMode.Acceleration);
                    body.AddTorque(torque,ForceMode.Force);body.AddTorque(angular,ForceMode.Acceleration);
                    if(kind==4)body.AddForceAtPosition(new Vector3(0,10,0),point,ForceMode.Force);
                } else {
                    packet.AddForce(f,ForceMode.Force);packet.AddForce(a,ForceMode.Acceleration);
                    packet.AddTorque(torque,ForceMode.Force);packet.AddTorque(angular,ForceMode.Acceleration);
                    if(kind==4)packet.AddForceAtPosition(new Vector3(0,10,0),point,body);
                }
                foreach(float step in steps){if(!direct)packet.Apply(body,step);Physics.Simulate(step);}
                return (body.linearVelocity,body.angularVelocity);
            }finally {UnityEngine.Object.DestroyImmediate(obj);}
        }
        static void VehicleLifecycle()
        {
            var mode=Physics.simulationMode;var root=new GameObject("force packet lifecycle");int before=RacePhysicsStepper.Vehicles.Count;
            try {
                Physics.simulationMode=SimulationMode.Script;
                var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(RoadVolumeChecks.FlatRoad()));
                var obj=new GameObject("packet lifecycle car");obj.transform.SetParent(root.transform);
                var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text),false);
                car.enabled=false;car.AdditionalMagneticAdhesion=false;var frame=track.Route.Evaluate(80);
                var tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
                var interval=typeof(MagneticVehicle).GetField("physicsInterval",BindingFlags.NonPublic|BindingFlags.Instance);
                Action air=()=> {car.Body.position=frame.position+frame.normal*5;car.Body.rotation=frame.Rotation;car.Body.linearVelocity=Vector3.zero;car.Body.angularVelocity=Vector3.zero;obj.transform.SetPositionAndRotation(car.Body.position,car.Body.rotation);Physics.SyncTransforms();};
                Action prepare=()=> {car.PrepareProjection();tick.Invoke(car,null);};
                air();long initial=(long)interval.GetValue(car);prepare();long native=RacePhysicsStepper.NativeSteps;double time=RacePhysicsStepper.SimulatedSeconds;
                RacePhysicsStepper.Simulate(.01f);
                Check((long)interval.GetValue(car)==initial+1,"native substeps repeated controller tick");
                Check(RacePhysicsStepper.NativeSteps-native==8 && Math.Abs(RacePhysicsStepper.SimulatedSeconds-time-.01)<1e-7,"substeps changed simulated time");
                Check(car.Body.linearVelocity.y<-.17f,"prepared gravity was not replayed");
                air();prepare();car.Hold(false);RacePhysicsStepper.Simulate(.01f);Check(car.Body.linearVelocity.y<-.17f,"repeated release discarded prepared gravity");
                air();RacePhysicsStepper.Simulate(.01f);Check(car.Body.linearVelocity.sqrMagnitude<1e-10f,"previous force packet leaked into next tick");
                prepare();car.ResetAt(80,0);air();RacePhysicsStepper.Simulate(.01f);Check(car.Body.linearVelocity.sqrMagnitude<1e-10f,"reset retained stale forces");
                prepare();car.Hold(true);car.Hold(false);air();RacePhysicsStepper.Simulate(.01f);Check(car.Body.linearVelocity.sqrMagnitude<1e-10f,"hold retained stale forces");
                car.enabled=true;prepare();obj.SetActive(false);obj.SetActive(true);air();RacePhysicsStepper.Simulate(.01f);Check(car.Body.linearVelocity.sqrMagnitude<1e-10f,"disable retained stale forces");
                var ownerObject=new GameObject("physics owner lifecycle");ownerObject.transform.SetParent(root.transform);
                var owner=ownerObject.AddComponent<RacePhysicsStepper>();Check(Physics.simulationMode==SimulationMode.Script,"owner did not own manual simulation");
                owner.enabled=false;Check(Physics.simulationMode==SimulationMode.Script,"owner did not restore previous mode");
                Debug.Log("PHYSICS_PACKET_LIFECYCLE_OK macroTicks=1 nativeSteps=8 duration=0.01 reset=true hold=true disable=true");
            }finally {UnityEngine.Object.DestroyImmediate(root);Physics.simulationMode=mode;}
            Check(RacePhysicsStepper.Vehicles.Count==before,"vehicle registration leaked after teardown");
        }
        public static void Run()
        {
            Check(Application.isPlaying,"actual Play Mode required");
            var oldMode=Physics.simulationMode;float oldStep=Time.fixedDeltaTime;
            try {
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
                var eight=new float[8];for(int i=0;i<8;i++)eight[i]=.01f/8;
                var uneven=new[]{.001f,.002f,.003f,.004f};
                Vector3[] linear={new Vector3(.05f,0,0),new Vector3(0,.03f,0),Vector3.zero,Vector3.zero,new Vector3(0,.05f,0)};
                Vector3[] angular={Vector3.zero,Vector3.zero,new Vector3(0,0,.02f),new Vector3(0,0,.07f),new Vector3(0,0,.025f)};
                float maxLinear=0,maxAngular=0;int cases=0;
                for(int kind=0;kind<5;kind++) {
                    var baseline=Measure(kind,new[]{.01f},true,Quaternion.identity);
                    Check(Vector3.Distance(baseline.linear,linear[kind])<1e-5f && Vector3.Distance(baseline.angular,angular[kind])<1e-5f,"native analytic case="+kind);
                    foreach(var steps in new[]{new[]{.01f},eight,uneven}) {
                        var actual=Measure(kind,steps,false,Quaternion.identity);float lv=Vector3.Distance(actual.linear,linear[kind]),av=Vector3.Distance(actual.angular,angular[kind]);
                        maxLinear=Mathf.Max(maxLinear,lv);maxAngular=Mathf.Max(maxAngular,av);
                        Check(lv<1e-5f && av<1e-5f,"analytic packet case="+kind+" parts="+steps.Length+" linearError="+lv+" angularError="+av);cases++;
                    }
                    var rotated=Quaternion.Euler(31,42,17);var reference=Measure(kind,new[]{.01f},true,rotated);var replay=Measure(kind,new[]{.01f},false,rotated);
                    Check(Vector3.Distance(reference.linear,replay.linear)<1e-5f && Vector3.Distance(reference.angular,replay.angular)<1e-5f,"rotated native N1 equivalence case="+kind);cases++;
                }
                VehicleLifecycle();
                Debug.Log("PHYSICS_FORCE_REPLAY_OK cases="+cases+" maxLinearError="+maxLinear+" maxAngularError="+maxAngular+" outerStep=0.01 unequalSteps=true");
            }finally {Physics.simulationMode=oldMode;Time.fixedDeltaTime=oldStep;}
        }
    }
}
