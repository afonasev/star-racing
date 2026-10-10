using System;
using System.Reflection;
using UnityEngine;

namespace StarRacingPrototype
{
    public static class GravityChecks
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly MethodInfo Tick = typeof(MagneticVehicle).GetMethod("FixedUpdate", Private);
        static void Check(bool ok, string message) { if (!ok) throw new Exception("Gravity: " + message); }
        static void Set(MagneticVehicle car, string name, object value) => typeof(MagneticVehicle).GetField(name, Private).SetValue(car, value);
        static void Step(MagneticVehicle car) { car.PrepareProjection(); Tick.Invoke(car, null); RacePhysicsStepper.Simulate(.01f); }
        static bool Close(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .05f;

        public static void RunAndBuildMac()
        {
            PrototypeChecks.RunWithFixtureEquivalence();
            PrototypeBuilder.BuildMac();
        }

        static void CheckNaturalRamp(MagneticVehicle car,TrackBuilder track,Procedural.Jump ramp,string theme,int count=RacePhysicsStepper.Substeps,bool trace=false)
        {
            float start = 30 + ramp.launchIndex * 5 - 45;
            car.ResetAt(start, -(float)ramp.lateralCenter);
            car.Body.linearVelocity = car.Frame.tangent * 42;
            car.SetInput(new DrivingInput { throttle = 1 });
            car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms(); bool launched = false, landed = false; int flightTicks = 0;
            float peak = 0; int revision = car.PositionRevision;
            bool observer=Environment.GetEnvironmentVariable("STAR_RACING_ROAD_OBSERVER")=="1" && ramp.id=="jump-2";
            var inventory=new System.Collections.Generic.Dictionary<int,Collider>();
            var raw=new System.Collections.Generic.List<string>();
            if(observer){foreach(var c in track.GetComponentsInChildren<Collider>())inventory[c.GetInstanceID()]=c;RoadNativeContactObserver.Begin(car.gameObject);}
            for (int i = 0; i < 400; i++)
            {
                car.SetRaceContext(true, 1, 2, i * .01);
                car.PrepareProjection();Tick.Invoke(car,null);int part=0;var before=car.Body.linearVelocity;
                RacePhysicsStepper.SimulatePrepared(RacePhysicsStepper.Vehicles,.01f,count,()=>{
                    if(observer) {
                        RoadNativeContactObserver.AfterStep();
                        if(i>=81 && i<=85) {
                            raw.Add($"RAMP_NATIVE tick={i} part={part} before={before.ToString("G9")} after={car.Body.linearVelocity.ToString("G9")} pose={car.Body.position.ToString("G9")} rotation={car.Body.rotation.ToString("G9")}");
                            foreach(var c in car.GetComponentsInChildren<BoxCollider>())raw.Add($"RAMP_RAW tick={i} part={part} self={c.name}"+RoadNativeContactObserver.For(c,inventory));
                        }
                    }
                    before=car.Body.linearVelocity;part++;
                });
                if(trace)Debug.Log($"RAMP_STATE tick={i} distance={car.Distance:G9} position={car.Body.position.ToString("G9")} speed={car.Body.linearVelocity.magnitude:G9} lateral={Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.right):G9} height={Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.normal):G9} support={car.HandlingSupportCount} active={car.ActiveJumpId} falling={car.IsFalling}");
                if (car.ActiveJumpId != "") { launched = true; flightTicks++; }
                if (launched) peak = Mathf.Max(peak, Vector3.Dot(car.Body.position - car.Frame.position, car.Frame.normal));
                if (launched && car.ActiveJumpId == "" && car.HandlingSupportCount > 0 && car.Distance > 30 + ramp.gapEndIndex * 5) { landed = true; break; }
                if (car.IsFalling || car.PositionRevision != revision) break;
            }
            if(observer){RoadNativeContactObserver.End();foreach(var row in raw)Debug.Log(row);}
            Check(launched && landed, "natural ramp did not land: " + theme + "/" + ramp.id + " launched=" + launched + " falling=" + car.IsFalling + " distance=" + car.Distance);
            Debug.Log($"GRAVITY_JUMP_OK theme={theme} jump={ramp.id} flightSeconds={flightTicks * .01f:F2} peak={peak:F2}");
        }
        public static void DiagnoseNaturalRamp()
        {
            var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime;var root=new GameObject("natural ramp diagnostic");
            try {
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
                var theme=Environment.GetEnvironmentVariable("STAR_RACING_RAMP_THEME")??"cloud-city";
                var control=Environment.GetEnvironmentVariable("STAR_RACING_RAMP_CONTROL")??"production";
                var trackObject=new GameObject("track");trackObject.transform.SetParent(root.transform);
                var track=trackObject.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,true,true)));
                var carObject=new GameObject("car");carObject.transform.SetParent(root.transform);
                var car=carObject.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text));
                if(control.Contains("original"))foreach(var collider in car.GetComponentsInChildren<BoxCollider>())if(collider.gameObject!=car.gameObject)collider.enabled=false;
                if(control.Contains("surface"))foreach(var collider in track.GetComponentsInChildren<MeshCollider>())if(collider.sharedMesh!=null && (collider.sharedMesh.name==RoadVolumeMesh.BottomName || collider.sharedMesh.name==RoadVolumeMesh.ExteriorName || collider.sharedMesh.name==RoadVolumeMesh.SupportName))collider.enabled=false;
                var ramp=Array.Find(track.Route.Definition.jumps,j=>j.id==(Environment.GetEnvironmentVariable("STAR_RACING_RAMP_ID")??"jump-2"))??track.Route.Definition.jumps[0];
                Debug.Log($"RAMP_AUTHORED launch={ramp.launchIndex} gap={ramp.gapStartIndex}..{ramp.gapEndIndex} centre={ramp.lateralCenter} halfWidth={ramp.lateralHalfWidth}");
                if(Environment.GetEnvironmentVariable("STAR_RACING_RAMP_ID")=="all")foreach(var item in track.Route.Definition.jumps)CheckNaturalRamp(car,track,item,theme,control.EndsWith("1")?1:RacePhysicsStepper.Substeps,true);
                else CheckNaturalRamp(car,track,ramp,theme,control.EndsWith("1")?1:RacePhysicsStepper.Substeps,true);
            }finally{UnityEngine.Object.DestroyImmediate(root);Physics.simulationMode=mode;Time.fixedDeltaTime=dt;}
        }
        public static void Run()
        {
            var mode = Physics.simulationMode; float dt = Time.fixedDeltaTime;
            try
            {
                Physics.simulationMode = SimulationMode.Script; Time.fixedDeltaTime = .01f;
                var balance = ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                foreach (string theme in new[] { "cloud-city", "space-station" })
                {
                    var root = new GameObject("gravity physics checks");
                    try
                    {
                        // Cars are siblings of TrackBuilder in the real race. Parenting a
                        // chassis under the track misclassifies it as a track obstacle.
                        var trackObject = new GameObject("gravity track"); trackObject.transform.SetParent(root.transform);
                        var track = trackObject.AddComponent<TrackBuilder>();
                        track.Build(new TrackRoute(Procedural.Generator.Generate(77, "normal", theme, true, true)));
                        if(Environment.GetEnvironmentVariable("STAR_RACING_GRAVITY_SURFACE_CONTROL")=="1")foreach(var collider in track.GetComponentsInChildren<MeshCollider>())if(collider.sharedMesh!=null && (collider.sharedMesh.name==RoadVolumeMesh.BottomName || collider.sharedMesh.name==RoadVolumeMesh.ExteriorName || collider.sharedMesh.name==RoadVolumeMesh.SupportName))collider.enabled=false;
                        Physics.SyncTransforms();
                        var obj = new GameObject("gravity car"); obj.transform.SetParent(root.transform);
                        obj.AddComponent<Rigidbody>(); obj.AddComponent<BoxCollider>();
                        var car = obj.AddComponent<MagneticVehicle>(); car.Initialize(track, 0, Color.cyan, balance);
                        car.ResetAt(80); var frame = car.Frame;
                        CameraFlightChecks.Run(car, Array.Find(track.Route.Definition.jumps, j => j.kind == "mandatory"));
                        car.ResetAt(80); frame = car.Frame;
                        var modelBounds = car.GetComponentInChildren<StaticVehicleVisual>().LocalBounds;
                        float spawnClearance = Vector3.Dot(car.Body.position - frame.position, frame.normal) + modelBounds.min.y;
                        Check(spawnClearance >= .02f && spawnClearance < .06f, "spawn visibly hovers above road: " + spawnClearance);
                        for (int i = 0; i < 300; i++) Step(car);
                        float settledClearance = Vector3.Dot(car.Body.position - car.Frame.position, car.Frame.normal) + modelBounds.min.y;
                        Check(car.HandlingSupportCount == 4 && settledClearance >= .02f && settledClearance < .06f,
                            "settled model hovers or intersects road: " + settledClearance);
                        Check(Mathf.Abs(Vector3.Dot(car.Body.linearVelocity, car.Frame.normal)) < .1f, "ride height did not settle");
                        Check(car.HasProvenCheckpoint, "low ride height did not admit a safe checkpoint");
                        car.ResetToCheckpoint();
                        float recoveryClearance = Vector3.Dot(car.Body.position - car.Frame.position, car.Frame.normal) + modelBounds.min.y;
                        Check(recoveryClearance >= .02f && recoveryClearance < .06f, "checkpoint restored hovering height");
                        Debug.Log($"GRAVITY_RIDE_HEIGHT_OK theme={theme} spawn={spawnClearance:F3} settled={settledClearance:F3} checkpoint={recoveryClearance:F3}");
                        car.ResetAt(80); frame = car.Frame;
                        Check(Close(car.GravityAcceleration, -frame.normal * MagneticVehicle.GravityMagnitude), "reset direction");
                        Check(car.AdditionalMagneticAdhesion, "approved ground adhesion disabled");
                        car.AdditionalMagneticAdhesion = false;
                        Check(car.MagneticAdhesionAcceleration == Vector3.zero, "cannot disable adhesion");
                        car.AdditionalMagneticAdhesion = true;
                        Check(car.MagneticAdhesionAcceleration.magnitude >= 13.99f, "cannot restore adhesion");
                        car.AdditionalMagneticAdhesion = false;
                        Check(Mathf.Abs(MagneticVehicle.GravityMagnitude - 18f) < .001f, "approved stronger gravity");
                        // Airborne acceleration is measured from real PhysX velocity, not just a formula.
                        car.Body.position = frame.position + frame.normal * 6; car.Body.linearVelocity = Vector3.zero;
                        car.Body.rotation = frame.Rotation * Quaternion.Euler(180, 0, 0); car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms();
                        Step(car);
                        Check(Close(car.Body.linearVelocity / .01f, -frame.normal * MagneticVehicle.GravityMagnitude), "airborne force or car orientation changed gravity: acceleration=" + car.Body.linearVelocity / .01f + " expected=" + -frame.normal * MagneticVehicle.GravityMagnitude + " position=" + car.Body.position + " supports=" + car.HandlingSupportCount);
                        // Inverted loop: road-relative gravity points upward in world space.
                        float inverted = -1;
                        foreach (var sample in track.Route.Samples) if (Vector3.Dot(sample.normal, Vector3.up) < -.9f) { inverted = sample.distance; break; }
                        Check(inverted >= 0, "fixture has no inverted road");
                        // Suspension-independent attraction on a moving vertical/inverted bend.
                        foreach (var bend in new[] { track.Route.LoopStart + 12, inverted })
                        {
                            car.ResetAt(bend); frame = car.Frame;
                            car.AdditionalMagneticAdhesion = true;
                            car.Body.position = frame.position + frame.normal * 4;
                            car.Body.linearVelocity = frame.tangent * 42 + frame.normal * 2;
                            car.Body.rotation = frame.Rotation * Quaternion.Euler(25, 0, 30);
                            car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms();
                            var before = car.Body.linearVelocity;
                            Step(car);
                            Check(car.HandlingSupportCount == 0, "bend fixture accidentally grounded");
                            float inward = Vector3.Dot((car.Body.linearVelocity - before) / .01f, car.GravityAcceleration.normalized);
                            Check(inward > 24, "airborne bend lost attraction: " + theme + " distance=" + bend + " inward=" + inward);
                            Check(Vector3.Dot(car.GravityAcceleration, frame.normal) < -9, "bend attraction points away from road");
                        }
                        // An upward rebound above inverted road must fall back onto it without reset.
                        foreach (float height in new[] { 4f, 16f })
                        {
                            car.ResetAt(inverted); frame = car.Frame;
                            car.SetRaceContext(true, 1, 2, 0);
                            car.Body.position = frame.position + frame.normal * height;
                            car.Body.linearVelocity = frame.normal * 4;
                            car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms();
                            int reboundRevision = car.PositionRevision; bool returned = false;
                            for (int i = 0; i < 250; i++)
                            {
                                Step(car);
                                if (car.HandlingSupportCount > 0) { returned = true; break; }
                            }
                            Check(returned && !car.IsFalling && car.PositionRevision == reboundRevision, "inverted rebound did not physically return to road: height=" + height);
                        }
                        // The Space Station loop is a helix and requires steering. Use the
                        // shipping driver, while the human car's force/rebound tests above
                        // remain independent of AI. No pose/velocity writes after entry.
                        obj.SetActive(false);
                        var loopObject = new GameObject("gravity loop driver"); loopObject.transform.SetParent(root.transform);
                        try
                        {
                            var loopCar = loopObject.AddComponent<MagneticVehicle>(); loopCar.Initialize(track, 0, Color.cyan, balance, false);
                            loopCar.ResetAt(track.Route.LoopStart - 8);
                            loopCar.Body.linearVelocity = loopCar.Frame.tangent * 42;
                            loopCar.transform.SetPositionAndRotation(loopCar.Body.position, loopCar.Body.rotation); Physics.SyncTransforms();
                            var driver = new AiDriver(0, DriverProfile.Racer, 77, balance);
                            var world = new AiWorldSnapshot(1); var vehicles = new[] { loopCar };
                            int loopRevision = loopCar.PositionRevision, loopTicks = 0; float peakHeight = 0;
                            for (int i = 0; i < 6000 && loopCar.Distance < track.Route.LoopEnd; i++)
                            {
                                loopTicks++;
                                loopCar.PrepareProjection(); world.Capture(vehicles, track.Route, i * .01);
                                loopCar.SetRaceContext(true, 1, 2, i * .01);
                                loopCar.SetInput(driver.Step(world, track.Route, .01f, false)); Step(loopCar);
                                peakHeight = Mathf.Max(peakHeight, Vector3.Dot(loopCar.Body.position - loopCar.Frame.position, loopCar.Frame.normal));
                                if (loopCar.IsFalling || loopCar.PositionRevision != loopRevision) break;
                            }
                            Check(loopCar.Distance >= track.Route.LoopEnd && !loopCar.IsFalling && loopCar.PositionRevision == loopRevision && peakHeight < 4,
                                "natural loop detached: " + theme + " distance=" + loopCar.Distance + " end=" + track.Route.LoopEnd + " peak=" + peakHeight
                                + " seconds=" + loopTicks * .01f + " falling=" + loopCar.IsFalling + " revision=" + loopCar.PositionRevision + "/" + loopRevision
                                + " speed=" + loopCar.Body.linearVelocity.magnitude + " lateral=" + Vector3.Dot(loopCar.Body.position - loopCar.Frame.position, loopCar.Frame.right));
                            Debug.Log($"GRAVITY_LOOP_OK theme={theme} seconds={loopTicks * .01f:F2} peak={peakHeight:F2}");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(loopObject); obj.SetActive(true); }
                        car.SetRaceContext(false, 1, 2);
                        Debug.Log("GRAVITY_BEND_RETURN_OK theme=" + theme);
                        car.AdditionalMagneticAdhesion = false;
                        car.ResetAt(inverted); frame = car.Frame;
                        car.Body.position = frame.position + frame.normal * 4; car.Body.linearVelocity = Vector3.zero; car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms();
                        Step(car); var remembered = car.GravityAcceleration;
                        Check(Vector3.Dot(remembered, Vector3.up) > 8, "inverted road gravity");
                        // Remote/sideways road cannot replace the last direction after leaving the road.
                        car.Body.position = frame.position + frame.right * (frame.halfWidth + 30) + frame.normal * 4;
                        car.Body.linearVelocity = Vector3.zero; car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms(); Step(car);
                        Check(Close(car.GravityAcceleration, remembered), "off-road direction was replaced");
                        Check(car.Recovery.Observe(.01f, true, 30, false, false, true, 0, 0, 0), "fall fixture");
                        Step(car); Check(Close(car.GravityAcceleration, remembered), "recovery changed fall direction");
                        car.ResetAt(80); Check(Close(car.GravityAcceleration, -car.Frame.normal * MagneticVehicle.GravityMagnitude), "reset inherited fall direction");
                        car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms();
                        car.SetRaceContext(true, 1, 2, 0);
                        for (int i = 0; i < 200; i++) Step(car);
                        Check(car.HasProvenCheckpoint, "settled fixture has no proven checkpoint");
                        Set(car, "gravityDirection", Vector3.right); car.ResetToCheckpoint();
                        Check(Close(car.GravityAcceleration, -car.Frame.normal * MagneticVehicle.GravityMagnitude), "checkpoint inherited fall direction");

                        var jump = Array.Find(track.Route.Definition.jumps, j => j.kind == "mandatory");
                        Check(jump != null, "mandatory gap missing");
                        float middle = 30 + (jump.gapStartIndex + jump.gapEndIndex) * 2.5f;
                        car.ResetAt(middle); frame = car.Frame;
                        car.Body.position = frame.position + frame.normal * 6; car.Body.linearVelocity = Vector3.zero;
                        var takeoffNormal = (frame.normal + frame.tangent * .25f).normalized;
                        Set(car, "activeJump", jump); Set(car, "launchNormal", takeoffNormal); Set(car, "flightStarted", Time.time);
                        car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms(); Step(car);
                        var expected = -Vector3.Slerp(takeoffNormal, frame.normal, .5f).normalized * MagneticVehicle.GravityMagnitude;
                        Check(Close(car.GravityAcceleration, expected), "gap does not continue virtual plane");
                        Check(Mathf.Abs(car.GravityAcceleration.magnitude - MagneticVehicle.GravityMagnitude) < .001f, "gap magnitude");

                        // Approach every authored ramp naturally. No injected jump state or launch impulse.
                        car.AdditionalMagneticAdhesion = true;
                        foreach (var ramp in track.Route.Definition.jumps)
                        {
                            CheckNaturalRamp(car,track,ramp,theme,trace:Environment.GetEnvironmentVariable("STAR_RACING_RAMP_TRACE")=="1");
                        }
                        Debug.Log("GRAVITY_PHYSICS_OK theme=" + theme + " acceleration invertedRoad gap frozenFall reset checkpoint reversibleAdhesion naturalJumps");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
            }
            finally { Physics.simulationMode = mode; Time.fixedDeltaTime = dt; }
        }
    }
}
