using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype
{
    // Actual Play Mode/PhysX fixtures: production controller, geometry and eight substeps.
    [InitializeOnLoad]
    public static class VehicleContactChecks
    {
        const string Key="StarRacing.VehicleContacts";
        static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
        static int ticks,cases;
        static bool failed;
        static readonly FieldInfo YawAssist=typeof(MagneticVehicle).GetField("preparedContactYawTorque",BindingFlags.NonPublic|BindingFlags.Instance);
        static bool MeasureOnly=>Environment.GetEnvironmentVariable("STAR_RACING_CONTACT_MEASURE_ONLY")=="1";
        static void Check(bool ok,string message)
        {
            if(!MeasureOnly && !ok)throw new Exception("Vehicle contacts: "+message);
        }
        static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_CONTACT_QA_DIR");
        static VehicleContactChecks()
        {
            EditorApplication.playModeStateChanged+=Changed;
        }
        public static void Run()
        {
            if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_CONTACT_QA_DIR required");
            Directory.CreateDirectory(Output);
            SessionState.SetBool(Key,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            ticks=0;
            EditorApplication.update+=Enter;
        }
        static void Enter()
        {
            if(++ticks<15)return;
            EditorApplication.update-=Enter;
            EditorApplication.isPlaying=true;
        }
        static void Changed(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                ticks=0;
                EditorApplication.update+=Execute;
            }
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Key,false);
                EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);
            }
        }
        static void Execute()
        {
            if(++ticks<15)return;
            EditorApplication.update-=Execute;
            failed=false;cases=0;
            float volume=AudioListener.volume;
            var mode=Physics.simulationMode;
            float dt=Time.fixedDeltaTime;
            try
            {
                AudioListener.volume=0;
                Physics.simulationMode=SimulationMode.Script;
                Time.fixedDeltaTime=.01f;
                foreach(bool human in new[]
                {
                    true,false
                }
                )
                {
                    if(Environment.GetEnvironmentVariable("STAR_RACING_CONTACT_DRIFT_CONTROL_ONLY")=="1"){Pinch(human,true);continue;}
                    if(Environment.GetEnvironmentVariable("STAR_RACING_CONTACT_CREST_ONLY")!="1") {
                    Pair(human,"rear",2);
                    Pair(human,"strong",25);
                    Pair(human,"side",2);
                    Pair(human,"group",2);
                    Wall(human,8);
                    Wall(human,80);
                    Wall(human,-8);
                    Wall(human,-80);
                    Pinch(human);
                    foreach(float direction in new[]{-1f,1f}) {
                        ControlAuthority(human,direction,false);
                        ControlAuthority(human,direction,true);
                    }
                    StrongAfterSide(human);
                    }
                    Crest(human,false);
                    Crest(human,true);
                    Crest(human,false,"space-station");
                    Crest(human,true,"space-station");
                }
                File.WriteAllText(Path.Combine(Output,"result.txt"),MeasureOnly?"CONTACT_BASELINE_MEASUREMENTS_COMPLETE\n":$"CONTACT_FIXTURES_OK cases={cases}\n");
            }
            catch(Exception e)
            {
                failed=true;
                Debug.LogException(e);
                File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());
            }
            finally
            {
                Physics.simulationMode=mode;
                Time.fixedDeltaTime=dt;
                AudioListener.volume=volume;
                SessionState.SetBool(Key+"Failed",failed);
                EditorApplication.isPlaying=false;
            }
        }
        static MagneticVehicle Car(GameObject root,TrackBuilder track,int seat,bool human,float distance,float lane,float speed,float yaw=0)
        {
            var obj=new GameObject("contact car "+seat);
            obj.transform.SetParent(root.transform);
            var car=obj.AddComponent<MagneticVehicle>();
            car.Initialize(track,seat,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text),human);
            car.enabled=false;
            car.ResetAt(distance,lane);
            car.Body.rotation=car.Frame.Rotation*Quaternion.Euler(0,yaw,0);
            car.Body.linearVelocity=car.Body.rotation*Vector3.forward*speed;
            car.SetInput(new DrivingInput
            {
                throttle=1
            }
            );
            obj.transform.SetPositionAndRotation(car.Body.position,car.Body.rotation);
            return car;
        }
        static TrackBuilder Track(GameObject root,bool rails=false)
        {
            var obj=new GameObject("contact road");
            obj.transform.SetParent(root.transform);
            var track=obj.AddComponent<TrackBuilder>();
            var definition=RoadVolumeChecks.FlatRoad();if(rails)definition.guardrailMode="full";
            track.Build(new TrackRoute(definition));
            return track;
        }
        static void Step(MagneticVehicle[] cars,int tick)
        {
            foreach(var car in cars)
            {
                car.SetRaceContext(true,1,cars.Length,tick*.01);
                car.Hold(false); // Shipping director repeats release before every controller tick.
                car.PrepareProjection();
                Tick.Invoke(car,null);
                var assist=YawAssist==null?Vector3.zero:(Vector3)YawAssist.GetValue(car);
                Check(Mathf.Abs(car.CurrentInput.steer)<.01f && !car.CurrentInput.drift || assist==Vector3.zero,"contact helper opposed steering/drift");
            }
            RacePhysicsStepper.Simulate(.01f,()=> {
                foreach(var car in cars)if(Mathf.Abs(car.CurrentInput.steer)>=.01f || car.CurrentInput.drift)
                    Check((Vector3)YawAssist.GetValue(car)==Vector3.zero,"native substep helper opposed control");
            });
        }
        static void Pair(bool human,string kind,float relative)
        {
            cases++;
            var root=new GameObject("pair fixture");
            try
            {
                var track=Track(root);
                bool side=kind=="side"||kind=="group";
                int count=kind=="group"?4:2;
                var cars=new MagneticVehicle[count];
                int hits=0;
                float impulse=0,peakNormal=0,minSpeed=999,peakAhead=0;
                float firstRear=0,firstFront=0;
                var revisions=new int[count];
                int contactTicks=0;
                for(int i=0;i<count;i++)
                {
                    cars[i]=Car(root,track,i,human,80+(side?i/2*4.25f:i*4.25f),side?(i%2==0?-1.4f:1.4f):0,42+(i==0?relative:0));
                    revisions[i]=cars[i].PositionRevision;
                    if(side)cars[i].Body.linearVelocity+=cars[i].Frame.right*(i%2==0?relative:-relative);
                    cars[i].ContactObserved+=c=>
                    {
                        if(c.rigidbody==null)return;
                        hits++;
                        impulse+=c.impulse.magnitude;
                        for(int j=0;j<c.contactCount;j++)peakNormal=Mathf.Max(peakNormal,Mathf.Abs(Vector3.Dot(c.GetContact(j).normal,Vector3.forward)));
                    }
                    ;
                }
                var drivers=new AiDriver[count];
                var world=new AiWorldSnapshot(count);
                for(int i=0;i<count;i++)drivers[i]=new AiDriver(i,DriverProfile.Racer,77,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text));
                Physics.SyncTransforms();
                for(int t=0;t<300;t++)
                {
                    int oldHits=hits;
                    if(Environment.GetEnvironmentVariable("STAR_RACING_CONTACT_SHIPPING_AI")=="1" && !human && side && t>=10)
                    {
                        foreach(var c in cars)c.PrepareProjection();
                        world.Capture(cars,track.Route,t*.01);
                        for(int i=0;i<count;i++)cars[i].SetInput(drivers[i].Step(world,track.Route,.01f,false));
                    }
                    Step(cars,t);
                    if(hits>oldHits)
                    {
                        contactTicks++;
                        if(firstRear==0)
                        {
                            firstRear=Vector3.Dot(cars[0].Body.linearVelocity,cars[0].Frame.tangent);
                            firstFront=Vector3.Dot(cars[1].Body.linearVelocity,cars[1].Frame.tangent);
                        }
                    }
                    if(t<80)foreach(var c in cars)minSpeed=Mathf.Min(minSpeed,Vector3.Dot(c.Body.linearVelocity,c.Frame.tangent));
                    peakAhead=Mathf.Max(peakAhead,Vector3.Dot(cars[1].Body.linearVelocity,cars[1].Frame.tangent));
                }
                string row=$"CONTACT_METRIC human={human} kind={kind} relative={relative} hits={hits} contactSeconds={contactTicks*.01f:F3} impulse={impulse:F2} minRearSpeed={minSpeed:F3} peakFrontSpeed={peakAhead:F3} finalRear={cars[0].Body.linearVelocity.magnitude:F3} finalFront={cars[1].Body.linearVelocity.magnitude:F3} laneGap={Mathf.Abs(cars[0].Body.position.x-cars[1].Body.position.x):F3} firstImpact={firstRear:F3},{firstFront:F3} normalAlong={peakNormal:F3} revision={cars[0].PositionRevision}";
                Debug.Log(row);
                File.AppendAllText(Path.Combine(Output,"metrics.txt"),row+"\n");
                Check(hits>0,"no physical pair contact "+kind);
                Check(minSpeed>=37.8f,"weak/group lost tempo "+kind);
                if(kind=="rear")Check(firstRear>=40&&firstFront>42,"rear impact momentum");
                if(kind=="strong")Check(firstRear<=59,"strong impact erased");
                foreach(var c in cars)
                {
                    Check(!c.IsFalling&&c.PositionRevision==revisions[c.Seat],"pair reset/fall "+kind);
                    Check(Vector3.Dot(c.Body.linearVelocity,c.Frame.tangent)>=37.8f,"pair still lost tempo after3s "+kind);
                    Check(Mathf.Abs(Vector3.Dot(c.Body.position-c.Frame.position,c.Frame.right))<=c.Frame.halfWidth,"pair off road "+kind);
                }
                if(kind=="side")Check(Mathf.Abs(cars[0].Body.position.x-cars[1].Body.position.x)>=2.86f || (Environment.GetEnvironmentVariable("STAR_RACING_CONTACT_SHIPPING_AI")=="1"&&Mathf.Abs(cars[0].Distance-cars[1].Distance)>=4.3f),"side failed to open physical clearance");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        static void Pinch(bool human,bool drift=false)
        {
            cases++;
            var root=new GameObject("sustained side fixture");
            try
            {
                var track=Track(root);
                var cars=new[]
                {
                    Car(root,track,0,human,80,-1.385f,42,6),Car(root,track,1,human,80,1.385f,42,-6)
                }
                ;
                int hits=0;
                float impulse=0,minSpeed=999;
                foreach(var c in cars)c.ContactObserved+=collision=>
                {
                    if(collision.rigidbody!=null)
                    {
                        hits++;
                        impulse+=collision.impulse.magnitude;
                    }
                }
                ;
                Physics.SyncTransforms();
                for(int t=0;t<200;t++)
                {
                    cars[0].SetInput(new DrivingInput
                    {
                        throttle=1,steer=drift?0:.15f,drift=drift
                    }
                    );
                    cars[1].SetInput(new DrivingInput
                    {
                        throttle=1,steer=drift?0:-.15f,drift=drift
                    }
                    );
                    Step(cars,t);
                    minSpeed=Mathf.Min(minSpeed,Mathf.Min(cars[0].Body.linearVelocity.z,cars[1].Body.linearVelocity.z));
                }
                for(int t=200;t<300;t++)
                {
                    foreach(var c in cars)c.SetInput(new DrivingInput
                    {
                        throttle=1
                    }
                    );
                    Step(cars,t);
                }
                string row=$"PINCH_METRIC human={human} drift={drift} hits={hits} impulse={impulse:F2} minSpeed={minSpeed:F3} finalSpeed={cars[0].Body.linearVelocity.magnitude:F3},{cars[1].Body.linearVelocity.magnitude:F3} gap={Mathf.Abs(cars[0].Body.position.x-cars[1].Body.position.x):F3}";
                Debug.Log(row);
                File.AppendAllText(Path.Combine(Output,"metrics.txt"),row+"\n");
                Check(hits>0&&minSpeed>=37.8f,"pinch lost tempo");
                Check(Mathf.Abs(cars[0].Body.position.x-cars[1].Body.position.x)>=2.86f,"pinch did not separate after steering released");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        static void Crest(bool human,bool boost,string theme="cloud-city")
        {
            cases++;
            var root=new GameObject("sharp continuous crest");
            try
            {
                var d=RoadVolumeChecks.FlatRoad();d.theme=theme;
                for(int i=0;i<d.samples.Length;i++)
                {
                    double z=i*5,x=(z-130)/14.0,y=6*Math.Exp(-x*x),slope=-12*x/14*Math.Exp(-x*x),length=Math.Sqrt(1+slope*slope);
                    d.samples[i].position=new Procedural.DVec(1000,120+y,z);
                    d.samples[i].tangent=new Procedural.DVec(0,slope/length,1/length);
                    d.samples[i].normal=new Procedural.DVec(0,1/length,-slope/length);
                }
                var trackObject=new GameObject("crest road");
                trackObject.transform.SetParent(root.transform);
                var track=trackObject.AddComponent<TrackBuilder>();
                track.Build(new TrackRoute(d));
                var car=Car(root,track,0,human,80,0,boost?83.33f:42);
                car.SetInput(new DrivingInput
                {
                    throttle=1,nitro=boost
                }
                );
                int hits=0,air=0,maxAir=0,run=0;
                float height=0,impulse=0,minSpeed=999,maxRoadImpulse=0;int hardContacts=0;
                int revision=car.PositionRevision;
                car.ContactObserved+=c=>
                {
                    if(c.collider.GetComponent<TrackSurface>()!=null)
                    {
                        hits++;
                        impulse+=c.impulse.magnitude;
                        maxRoadImpulse=Mathf.Max(maxRoadImpulse,c.impulse.magnitude);
                        if(c.impulse.magnitude>1000)hardContacts++;
                    }
                };
                Physics.SyncTransforms();
                for(int t=0;t<400&&car.Distance<230;t++)
                {
                    Step(new[]
                    {
                        car
                    }
                    ,t);
                    height=Mathf.Max(height,Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.normal));
                    minSpeed=Mathf.Min(minSpeed,Vector3.Dot(car.Body.linearVelocity,car.Frame.tangent));
                    if(car.HandlingSupportCount==0)
                    {
                        air++;
                        run++;
                        maxAir=Mathf.Max(maxAir,run);
                    }
                    else run=0;
                }
                string row=$"CREST_METRIC theme={theme} human={human} boost={boost} peakHeight={height:F3} airSeconds={air*.01f:F3} longestAir={maxAir*.01f:F3} contacts={hits} impulse={impulse:F2} maxImpulse={maxRoadImpulse:F2} hardContacts={hardContacts} minSpeed={minSpeed:F3} distance={car.Distance:F3} revision={car.PositionRevision-revision} falling={car.IsFalling}";
                Debug.Log(row);
                File.AppendAllText(Path.Combine(Output,"metrics.txt"),row+"\n");
                Check(height<=.65f&&maxAir<=15,"crest did not retain support");
                Check(hardContacts<=1,"crest repeated hard body impacts");
                Check(!car.IsFalling&&car.PositionRevision==revision&&car.Distance>=230,"crest fell/reset");
                Check(minSpeed>=(boost?83.33f:42)*.9f,"crest lost tempo");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        static void ControlAuthority(bool human,float direction,bool drift)
        {
            cases++;
            var root=new GameObject("contact control authority");
            try {
                var track=Track(root);
                var cars=new[]{Car(root,track,0,human,80,-1.4f,44),Car(root,track,1,human,80,1.4f,42)};
                cars[0].Body.linearVelocity+=cars[0].Frame.right*2;
                cars[1].Body.linearVelocity-=cars[1].Frame.right*2;
                int contacts=0;cars[0].ContactObserved+=c=>{if(c.rigidbody==cars[1].Body)contacts++;};
                Physics.SyncTransforms();bool active=false;int t=0;
                for(;t<80;t++) {
                    foreach(var car in cars){car.SetRaceContext(true,1,2,t*.01);car.PrepareProjection();Tick.Invoke(car,null);}
                    active=((Vector3)YawAssist.GetValue(cars[0])).sqrMagnitude>1e-6f;
                    RacePhysicsStepper.Simulate(.01f);if(active)break;
                }
                Check(contacts>0&&active,"control fixture never enabled weak contact yaw helper");
                int inputStart=t;
                bool actualDrift=false;
                for(;t<inputStart+20;t++) {
                    foreach(var car in cars)car.SetInput(new DrivingInput{throttle=1,steer=direction*.7f,drift=drift});
                    Step(cars,t);
                    if(human)actualDrift|=cars[0].HandlingState.driftActive;
                }
                float yaw=Vector3.Dot(cars[0].Body.angularVelocity,cars[0].Frame.normal);
                Check(direction*yaw>.02f,"contact helper prevented requested turn");
                Check(!human||!drift||actualDrift,"human drift button did not produce real drift");
                string row=$"CONTACT_CONTROL_AUTHORITY_OK human={human} direction={direction} drift={drift} actualHumanDrift={actualDrift} commandedYaw={yaw:F4} contacts={contacts} helperTorqueDuringInput=0 window=.20s";
                Debug.Log(row);File.AppendAllText(Path.Combine(Output,"metrics.txt"),row+"\n");
            }finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        static void StrongAfterSide(bool human)
        {
            cases++;
            var root=new GameObject("strong hit during weak contact");
            try {
                var track=Track(root);
                var receiver=Car(root,track,0,human,80,-1.4f,42);
                var neighbour=Car(root,track,1,human,80,1.4f,42);
                receiver.Body.linearVelocity+=receiver.Frame.right*2;
                neighbour.Body.linearVelocity-=neighbour.Frame.right*2;
                var donor=Car(root,track,2,human,72,-1.4f,67);
                int strong=0;float helperAfterStrong=0;
                receiver.ContactObserved+=collision=> {
                    if(collision.rigidbody!=donor.Body)return;
                    if(collision.relativeVelocity.magnitude>12)strong++;
                    if(strong>0)helperAfterStrong=Mathf.Max(helperAfterStrong,((Vector3)YawAssist.GetValue(receiver)).magnitude);
                };
                Physics.SyncTransforms();
                for(int t=0;t<150;t++)Step(new[]{receiver,neighbour,donor},t);
                Check(strong>0 && helperAfterStrong==0,"strong hit did not revoke its side torque including Stay");
                float impactReceiverSpeed=receiver.Body.linearVelocity.magnitude;
                receiver.Hold(true);receiver.Hold(false);
                receiver.ResetAt(80);
                receiver.SetDrivingGeneration(2);
                receiver.gameObject.SetActive(false);receiver.gameObject.SetActive(true);
                Check((Vector3)YawAssist.GetValue(receiver)==Vector3.zero,"lifecycle retained contact torque");
                string row=$"CONTACT_STRONG_TAIL_OK human={human} strongOnset={strong} maxHelperDuringStrong={helperAfterStrong:F6} receiverSpeedBeforeReset={impactReceiverSpeed:F3}";
                Debug.Log(row);File.AppendAllText(Path.Combine(Output,"metrics.txt"),row+"\n");
            }finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        static void Wall(bool human,float angle)
        {
            cases++;
            var root=new GameObject("wall fixture");
            try
            {
                var track=Track(root,true);
                var wall=new GameObject("contact wall");
                wall.transform.SetParent(root.transform);
                var box=wall.AddComponent<BoxCollider>();
                box.size=new Vector3(.3f,3,290);
                foreach(var rail in track.GetComponentsInChildren<MeshCollider>())
                    if(rail.sharedMesh.name=="Procedural guard rails")box.sharedMaterial=rail.sharedMaterial;
                Check(box.sharedMaterial!=null,"production rail material missing");
                wall.transform.position=new Vector3(1000+Mathf.Sign(angle)*5,121,145);
                var car=Car(root,track,0,human,80,Mathf.Sign(angle)*3.2f,42,angle);
                int hits=0;
                float impulse=0,minAlong=999,maxRebound=0,minMagnitude=999;
                car.ContactObserved+=c=>
                {
                    if(c.collider!=box)return;
                    hits++;
                    impulse+=c.impulse.magnitude;
                }
                ;
                Physics.SyncTransforms();
                for(int t=0;t<150;t++)
                {
                    Step(new[]
                    {
                        car
                    }
                    ,t);
                    minAlong=Mathf.Min(minAlong,car.Body.linearVelocity.z);
                    minMagnitude=Mathf.Min(minMagnitude,car.Body.linearVelocity.magnitude);
                    maxRebound=Mathf.Max(maxRebound,-Mathf.Sign(angle)*car.Body.linearVelocity.x);
                }
                string row=$"WALL_METRIC human={human} angle={angle} hits={hits} impulse={impulse:F2} minAlong={minAlong:F3} minMagnitude={minMagnitude:F3} rebound={maxRebound:F3} finalSpeed={car.Body.linearVelocity.magnitude:F3} heading={car.transform.eulerAngles.y:F2}";
                Debug.Log(row);
                File.AppendAllText(Path.Combine(Output,"metrics.txt"),row+"\n");
                Check(hits>0,"wall not contacted");
                if(Mathf.Abs(angle)<10)Check(minAlong>=42*Mathf.Cos(angle*Mathf.Deg2Rad)*.9f&&maxRebound<=2,"grazing wall slowed/rebounded");
                else Check(minMagnitude<=10.5f,"direct wall did not substantially brake within1.5s");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
