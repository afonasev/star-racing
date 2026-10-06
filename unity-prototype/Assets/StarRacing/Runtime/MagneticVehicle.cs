using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype
{
    /// <summary>
    /// A deliberately force-driven car. The route is used as a magnetic reference, but never
    /// as a kinematic rail: position and velocity remain owned by the Rigidbody.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MagneticVehicle : MonoBehaviour
    {
        public Rigidbody Body;
        public TrackFrame Frame;
        public VehicleTelemetry Telemetry;
        public float Distance;
        public int Seat;
        public int PositionRevision { get; private set; }
        public bool IsFalling => recovery.Falling;
        public RecoveryPolicy Recovery => recovery;
        public bool FinishedCoasting { get; private set; }
        public bool StoppedAtWall { get; private set; }

        const float Mass = 1000f;
        const float WheelRadius = .34f;
        const float SuspensionTravel = .78f;
        const float Wheelbase = 2.35f;
        const float TrackWidth = 1.42f;
        readonly List<Transform> wheelVisuals = new List<Transform>(4);
        readonly List<Collider> ignoredCars = new List<Collider>();
        readonly RecoveryPolicy recovery = new RecoveryPolicy();
        readonly List<RecoveryEvent> recoveryEvents = new List<RecoveryEvent>(2);
        long recoveryTick, recoverySequence;

        TrackBuilder track;
        DrivingInput input;
        public DrivingInput CurrentInput => input;
        public float OffRouteSeconds => offRouteTime;
        public bool IsGhosting => FinishedCoasting || recovery.GhostSeconds > 0;
        BoxCollider chassis;
        Vector3 lastSafePosition;
        Quaternion lastSafeRotation;
        float lastSafeDistance;
        CheckpointAdmission.Result lastSafeAdmission;
        bool lastSafeValid;
        public bool HasProvenCheckpoint => lastSafeValid && track!=null && lastSafeAdmission.revision==track.BuildRevision;
        readonly CheckpointAdmission.Support[] suspensionSupports=new CheckpointAdmission.Support[4];
        float offRouteTime;
        public DrivingBalance Drive {get;private set;}
        bool raceActive; int place=1, entrantCount=2; float driveAcceleration; double raceElapsed;
        public void SetRaceContext(bool active,int position,int count,double elapsed=0){raceActive=active;place=position;entrantCount=count;raceElapsed=elapsed;}
        public void DrainRecoveryEvents(List<RecoveryEvent> destination) {destination.AddRange(recoveryEvents);recoveryEvents.Clear();}
        bool wasGrounded;
        Procedural.Jump lastRampSupport, activeJump;
        Vector3 launchNormal;
        float rampContactTime, flightStarted;
        public string ActiveJumpId => activeJump?.id ?? "";
        void EndJump(string reason) {activeJump=null;}
        void ApplyAirborneGravity()
        {
            float magnitude = activeJump != null ? 18f : Physics.gravity.magnitude;
            var query = track.NearestRoad;
            RoadSurfaceQuery.Hit hit = default;
            Vector3 gravity = query != null && query.Nearest(Body.position, out hit)
                ? -hit.normal * magnitude
                : activeJump != null ? -launchNormal * 18f : Physics.gravity;
            Body.AddForce(gravity * Mass, ForceMode.Force);
        }

        bool humanDriver;
        readonly HumanHandlingState handling = new HumanHandlingState();
        readonly HumanHandlingReconciliation reconciliation = new HumanHandlingReconciliation();
        long physicsInterval, lastHandlingContactInterval=-1;
        long handlingTick;
        int handlingGeneration;
        DrivingIntentSnapshot drivingIntent;
        public HandlingObservation HandlingObserved { get; private set; }
        public HumanHandlingState HandlingState => handling;
        public int HandlingSupportCount { get; private set; }
        public bool HandlingContinuous { get; private set; }
        public bool HandlingContacted { get; private set; }
        public DrivingIntentSnapshot DrivingIntent => new DrivingIntentSnapshot(Seat,handlingGeneration,PositionRevision,handlingTick,handling,
            drivingIntent.Suppression | (recovery.Falling?DrivingSuppression.Falling:DrivingSuppression.None)
            | (IsGhosting?DrivingSuppression.Ghost:DrivingSuppression.None)
            | (Time.timeScale==0?DrivingSuppression.Paused:DrivingSuppression.None));
        public void SetDrivingGeneration(int generation) { if(handlingGeneration==generation)return;handlingGeneration=generation;handling.Reset();reconciliation.Invalidate("generation");handlingTick=0;recovery.Reset();recoveryEvents.Clear();recoverySequence=0; }
        bool initialized;

        public void Initialize(TrackBuilder sourceTrack, int seat, Color color, ReleaseBalance balance = null, bool human = true)
        {
            track = sourceTrack;
            Seat = seat; humanDriver=human;
            if(balance!=null)Drive=new DrivingBalance(balance);
            EnsurePhysics();
            CreateVisuals(color, human);
            ResetAt(seat * 3f, seat == 0 ? -1.5f : 1.5f);
            initialized = true;
        }

        public void SetInput(DrivingInput value) => input = FinishedCoasting ? default : value;

        public void Hold(bool held) {
            if(held) reconciliation.Invalidate("hold");
            if (held && !Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = held;
        }

        public void BeginFinishCoast() {
            if (FinishedCoasting) return;
            FinishedCoasting = true; input = default;Drive?.StopBoost();handling.Reset();reconciliation.Invalidate("finish");
            recovery.Reset();recoveryEvents.Clear();
            chassis.enabled=true;
            var f = track.Route.Evaluate(track.Route.FinishDistance);
            float speed = Mathf.Max(0, Vector3.Dot(Body.linearVelocity, f.tangent));
            Body.rotation = f.Rotation; Body.angularVelocity = Vector3.zero;
            Body.linearVelocity = f.tangent * speed;
            // The finish straight is parallel to world Z. PhysX retains forward inertia and suspension.
            Body.constraints = track.Route.Definition == null ? RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezeRotation : RigidbodyConstraints.FreezeRotation;
            BeginGhosting();
        }

        public void ResetAt(float distance, float lateral = 0f)
        {
            if (track == null || track.Route == null) return;
            PositionRevision++;
            EndJump("reset");lastRampSupport=null;
            FinishedCoasting = false; StoppedAtWall = false;
            Body.isKinematic = false; Body.constraints = RigidbodyConstraints.None;
            Distance = track.Route.Wrap(distance);
            Frame = track.Route.Evaluate(Distance);
            Vector3 point = Frame.position + Frame.right * lateral + Frame.normal * 1.1f;
            Body.position = point;
            Body.rotation = Frame.Rotation;
            // Reset is a teleport: also synchronize the rendered pose before countdown makes the body kinematic.
            transform.SetPositionAndRotation(point, Frame.Rotation);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            lastSafePosition = point;
            lastSafeRotation = Frame.Rotation;
            lastSafeDistance = Distance;
            lastSafeValid=false;
            TryStoreSafePoint(point,Frame.Rotation,Distance,true);
            offRouteTime = 0f;
            recovery.Reset(); recoveryEvents.Clear(); recoverySequence=0;
            chassis.enabled=true;
            input = default;
            Drive?.Reset(); handling.Reset();reconciliation.Invalidate("reset");handlingTick=0;
            wasGrounded = false;
            RestoreCarCollisions();
            Telemetry.recovering = false;
            Telemetry.speedKmh = 0; Telemetry.nitro01 = 1;
        }

        public void ResetToCheckpoint()
        {
            if (track == null || track.Route == null) return;
            if (FinishedCoasting) return;
            if (!lastSafeValid || lastSafeAdmission.revision!=track.BuildRevision) {
                Debug.LogWarning($"RECOVERY_CHECKPOINT_UNPROVEN entrant={Seat} revision={track.BuildRevision}");
                return;
            }
            PositionRevision++;
            handling.Reset();reconciliation.Invalidate("recovery");
            EndJump("recovery");lastRampSupport=null;
            Body.position = lastSafePosition;
            Body.rotation = lastSafeRotation;
            chassis.enabled=true;
            Frame = track.Route.Evaluate(lastSafeDistance);
            Body.linearVelocity = Frame.tangent.normalized * (Drive == null ? 0f : Drive.BaseSpeed * .5f);
            Body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(lastSafePosition,lastSafeRotation);
            Distance = lastSafeDistance;

            offRouteTime = 0f;
            if(recovery.Falling) {
                recovery.Respawn();
                recoveryEvents.Add(new RecoveryEvent(RecoveryEventKind.Respawn,Seat,handlingGeneration,recovery.Episode,
                    PositionRevision-1,PositionRevision,recoveryTick,++recoverySequence));
            }
            BeginGhosting();
            Telemetry.recovering = false;
        }

        void EnsurePhysics()
        {
            Body = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
            Body.mass = Mass;
            Body.useGravity = false;
            Body.linearDamping = .04f;
            Body.angularDamping = .8f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.centerOfMass = new Vector3(0f, -.18f, 0f);
            chassis = GetComponent<BoxCollider>() ?? gameObject.AddComponent<BoxCollider>();
            chassis.size = VehicleGeometry.ChassisSize;
            chassis.center = VehicleGeometry.ChassisCenter;
            Body.inertiaTensor = VehicleGeometry.BaselineInertia;
            Body.inertiaTensorRotation = VehicleGeometry.BaselineInertiaRotation;
            PhysicsMaterial roadSafeMaterial = new PhysicsMaterial("MagneticCarChassis") {
                dynamicFriction = .12f,
                staticFriction = .12f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            chassis.material = roadSafeMaterial;
        }

        void CreateVisuals(Color color, bool human)
        {
            if (GetComponentInChildren<StaticVehicleVisual>() != null) return;
            StaticVehicleVisual.Create(transform, color, human);
        }

        public void PrepareProjection(){if(initialized && !recovery.Falling && track!=null){Frame=track.Route.Project(Body.position,Distance,30f);Distance=Frame.distance;}}
        public event System.Action<Collision> ContactObserved;
        void FixedUpdate()
        {
            if (!initialized || track == null || track.Route == null || Body.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            physicsInterval++;
            recoveryTick++;
            recovery.StepGhost(dt);
            if(recovery.Falling) {
                ApplyAirborneGravity();
                if(recovery.AdvanceFall(dt))ResetToCheckpoint();
                Telemetry.recovering=recovery.Falling;
                Telemetry.speedKmh=Body.linearVelocity.magnitude*3.6f;
                if(!IsGhosting)RestoreCarCollisions();
                return;
            }

            bool routeNear = Vector3.Distance(Body.position, Frame.position) < Frame.halfWidth + 6f;
            int contacts = ApplySuspension(dt);
            bool grounded = contacts > 0;
            HandlingSupportCount=contacts;
            HandlingContinuous=grounded && wasGrounded && routeNear && !Frame.gap && Frame.normal.sqrMagnitude>0 && Frame.tangent.sqrMagnitude>0;
            HandlingContacted=lastHandlingContactInterval==physicsInterval-1;
            HandlingObserved=HumanHandlingForces.Observe(Body.linearVelocity,Body.rotation*Vector3.forward,Body.angularVelocity,Frame);
            if(humanDriver) reconciliation.Observe(handling,HandlingObserved,Body.angularVelocity,Frame,physicsInterval,handlingGeneration,PositionRevision,
                HandlingContinuous,HandlingContacted);
            driveAcceleration=Drive==null?0:humanDriver
                ?Drive.StepHuman(input,(float)HandlingObserved.Speed,dt,raceActive && !FinishedCoasting,grounded && !IsGhosting,place,entrantCount,handling)
                :Drive.Step(input,Vector3.Dot(Body.linearVelocity,transform.forward),dt,raceActive && !FinishedCoasting,grounded && !IsGhosting,place,entrantCount);
            if(grounded) EndJump("landing");
            else if(wasGrounded && lastRampSupport!=null && Time.time-rampContactTime<.1f &&
                    track.Route.CanLaunch(lastRampSupport,Body.position,Distance,Body.linearVelocity)) {
                handling.Reset();reconciliation.Invalidate("launch"); // Source confirmed launch resets handling intent without writing the physical pose.
                activeJump=lastRampSupport;launchNormal=track.Route.Evaluate(30+activeJump.launchIndex*5).normal;
                flightStarted=Time.time;
            }
            if(activeJump!=null && (Time.time-flightStarted>=4 || !track.Route.InFlightCorridor(activeJump,Body.position,Distance)))EndJump("corridor-or-timeout");

            if (grounded)
            {
                ApplyMagnetAndDrive(dt);
                if (contacts >= (track.Route.Definition == null ? 3 : 4) && !Frame.gap && Vector3.Dot(transform.up, Frame.normal) > .7f)
                    StoreSafePoint();
                offRouteTime = routeNear ? 0f : offRouteTime + dt;
            }
            else
            {
                reconciliation.Invalidate("flight");
                ApplyAirborneGravity();
                offRouteTime += dt;
            }
            if(humanDriver) {
                var suppression=(!raceActive?DrivingSuppression.Inactive:DrivingSuppression.None)
                    | (FinishedCoasting?DrivingSuppression.Finished:DrivingSuppression.None)
                    | (IsGhosting?DrivingSuppression.Ghost:DrivingSuppression.None)
                    | (activeJump!=null?DrivingSuppression.Jumping:DrivingSuppression.None)
                    | (!grounded && activeJump==null?DrivingSuppression.Falling:DrivingSuppression.None);
                HumanHandling.ObserveSkid(handling,HandlingObserved,suppression);
                drivingIntent=new DrivingIntentSnapshot(Seat,handlingGeneration,PositionRevision,++handlingTick,handling,suppression);
            }
            AlignToRoad(grounded);
            UpdateWheels();

            wasGrounded = grounded;
            Telemetry.recovering = offRouteTime > .05f;
            float lateralFromSupportedSpan=Mathf.Abs(Vector3.Dot(Body.position-Frame.position,Frame.right));
            float heightFromRoad=Vector3.Dot(Body.position-Frame.position,Frame.normal);
            float headingCos=Mathf.Abs(Vector3.Dot(transform.forward,Frame.tangent));
            float headingSin=Mathf.Abs(Vector3.Dot(transform.forward,Frame.right));
            float footprintExtent=headingCos*1.38f+headingSin*2.1f;
            bool unsupported = !grounded && activeJump==null &&
                (Frame.gap || !routeNear || lateralFromSupportedSpan-footprintExtent>Frame.halfWidth || heightFromRoad< -2f);
            float along = Vector3.Dot(Body.linearVelocity,Frame.tangent);
            if(recovery.Observe(dt,raceActive,raceElapsed,FinishedCoasting,activeJump!=null,unsupported,
                along,Mathf.Clamp01(input.throttle),Mathf.Clamp01(input.brake))) {
                Drive?.StopBoost();handling.Reset();reconciliation.Invalidate("fall");
                chassis.enabled=false;
                recoveryEvents.Add(new RecoveryEvent(RecoveryEventKind.Fall,Seat,handlingGeneration,recovery.Episode,
                    PositionRevision,PositionRevision,recoveryTick,++recoverySequence));
            }
            if(recovery.Falling)Telemetry.recovering=true;
            if(!IsGhosting)RestoreCarCollisions();

            Telemetry.speedKmh = Body.linearVelocity.magnitude * 3.6f;
            Telemetry.nitro01 = Drive==null?0:Drive.Charge/Drive.Capacity;
            Telemetry.progress01 = track.Route.Length > .01f ? Distance / track.Route.Length : 0f;
            Telemetry.grounded = grounded;
            Telemetry.compression = contacts == 0 ? 0f : Telemetry.compression / contacts;
        }

        void StoreSafePoint()
        {
            if(track.Route.Definition != null) {
                // A recovery must leave enough runway to accelerate again; a stationary
                // reset on the lip would turn one missed jump into an endless failure loop.
                foreach(var jump in track.Route.Definition.jumps) {
                    if(Distance>=30+jump.rampStartIndex*5-80 && Distance<=30+jump.landingEndIndex*5)return;
                }
                TryStoreSafePoint(Frame.position+Frame.normal*1.1f,Frame.Rotation,Distance,false,true);
                return;
            }
            float lateral = Mathf.Clamp(Vector3.Dot(Body.position - Frame.position, Frame.right), -Frame.halfWidth * .72f, Frame.halfWidth * .72f);
            TryStoreSafePoint(Frame.position + Frame.right * lateral + Frame.normal * 1.1f,Frame.Rotation,Distance,false);
        }

        void TryStoreSafePoint(Vector3 proposed,Quaternion rotation,float distance,bool initial,bool useHitPoint=false) {
            Vector3 centre=proposed-rotation*Vector3.up*1.1f;
            Vector3 up=rotation*Vector3.up;
            if(!track.RaycastRoad(centre+up*2f,-up,4f,distance,out var hit) ||
                Vector3.Distance(hit.point,centre)>.5f || Vector3.Dot(hit.normal,up)<.7f)return;
            if(useHitPoint)proposed=hit.point+up*1.1f;
            var centreSupport=new CheckpointAdmission.Support(centre+up*2f,-up,4f,hit);
            var supports=suspensionSupports;
            if(initial) {
                supports=new CheckpointAdmission.Support[4];
                for(int i=0;i<4;i++) {
                    float x=(i%2==0?-1f:1f)*TrackWidth*.5f;
                    float z=(i<2?1f:-1f)*Wheelbase*.5f;
                    Vector3 origin=proposed+rotation*(new Vector3(x,.2f,z));
                    float length=SuspensionTravel+WheelRadius+.25f;
                    if(!track.RaycastRoad(origin,-up,length,distance,out var wheelHit))return;
                    supports[i]=new CheckpointAdmission.Support(origin,-up,length,wheelHit);
                }
            }
            var result=CheckpointAdmission.Check(track,proposed,rotation,distance,centreSupport,supports);
            if(!result.accepted)return;
            lastSafePosition=proposed;
            lastSafeRotation=rotation;
            lastSafeDistance=distance;
            lastSafeAdmission=result;
            lastSafeValid=true;
        }

        bool IsExpectedJump()
        {
            if (track.Route.Definition != null) return track.Route.IsJumpRegion(Distance);
            if (track.Route.JumpEnd <= track.Route.JumpStart + .01f) return false;
            float start = Mathf.Repeat(track.Route.JumpStart, track.Route.Length);
            float end = Mathf.Repeat(track.Route.JumpEnd, track.Route.Length);
            return start <= end ? Distance >= start && Distance <= end : Distance >= start || Distance <= end;
        }

        int ApplySuspension(float dt)
        {
            int contacts = 0;
            float totalCompression = 0f;
            for(int i=0;i<suspensionSupports.Length;i++)suspensionSupports[i]=default;
            Vector3[] offsets = {
                new Vector3(-TrackWidth * .5f, 0f, Wheelbase * .5f), new Vector3(TrackWidth * .5f, 0f, Wheelbase * .5f),
                new Vector3(-TrackWidth * .5f, 0f, -Wheelbase * .5f), new Vector3(TrackWidth * .5f, 0f, -Wheelbase * .5f)
            };
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 origin = transform.TransformPoint(offsets[i] + Vector3.up * .2f);
                if (!track.RaycastRoad(origin, -transform.up, SuspensionTravel + WheelRadius + .25f, Distance, out RaycastHit hit)) continue;
                suspensionSupports[i]=new CheckpointAdmission.Support(origin,-transform.up,SuspensionTravel+WheelRadius+.25f,hit);
                var supportedRamp=track.Route.RampAtContact(hit.point,Distance);
                if(supportedRamp!=null){lastRampSupport=supportedRamp;rampContactTime=Time.time;}
                float length = Mathf.Max(0f, hit.distance - WheelRadius);
                float compression = Mathf.Clamp01((SuspensionTravel - length) / SuspensionTravel);
                float pointVelocity = Vector3.Dot(Body.GetPointVelocity(origin), transform.up);
                float force = compression * 62000f - pointVelocity * 7000f;
                Body.AddForceAtPosition(transform.up * Mathf.Max(0f, force), origin, ForceMode.Force);
                contacts++;
                totalCompression += compression;
                if (i < wheelVisuals.Count) wheelVisuals[i].localPosition = offsets[i] + Vector3.up * .2f + Vector3.down * length;
            }
            Telemetry.compression = totalCompression;
            return contacts;
        }

        void ApplyMagnetAndDrive(float dt)
        {
            Vector3 velocity = Body.linearVelocity;
            float speedAlong = Vector3.Dot(velocity, transform.forward);
            if(humanDriver && Drive!=null && raceActive && !FinishedCoasting) {
                var observed=HandlingObserved;
                var accelerated=new HandlingObservation(observed.Speed+driveAcceleration*dt,observed.Heading,observed.MotionYaw,observed.YawRate,observed.LateralVelocity);
                var longStraight=HasLongStraightAhead();
                var sourceInput=HumanHandlingForces.NormalizeInput(input);
                var targets=HumanHandling.Step(handling,sourceInput,accelerated,Drive.Snapshot,dt,Drive.Active,longStraight);
                HumanHandlingForces.Increments(handling,observed,velocity,Frame,dt,targets,out var acceleration,out var angularAcceleration);
                reconciliation.Record(handling,Body.angularVelocity,angularAcceleration,Frame,dt,physicsInterval,handlingGeneration,PositionRevision);
                Body.AddForce(acceleration,ForceMode.Acceleration);Body.AddTorque(angularAcceleration,ForceMode.Acceleration);
            }
            else if(!humanDriver) Body.AddForce(transform.forward * driveAcceleration, ForceMode.Acceleration);

            if(!humanDriver) {

            float steerAngle = input.steer * Mathf.Lerp(25f, 9f, Mathf.InverseLerp(12f, 48f, Mathf.Abs(speedAlong)));
            float lateralGrip = input.drift ? 3500f : 10500f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 front = transform.TransformPoint(new Vector3(side * TrackWidth * .5f, -.15f, Wheelbase * .5f));
                Vector3 wheelRight = Quaternion.AngleAxis(steerAngle, transform.up) * transform.right;
                float lateralSpeed = Vector3.Dot(Body.GetPointVelocity(front), wheelRight);
                Body.AddForceAtPosition(-wheelRight * Mathf.Clamp(lateralSpeed * lateralGrip, -12000f, 12000f), front, ForceMode.Force);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 rear = transform.TransformPoint(new Vector3(side * TrackWidth * .5f, -.15f, -Wheelbase * .5f));
                float lateralSpeed = Vector3.Dot(Body.GetPointVelocity(rear), transform.right);
                Body.AddForceAtPosition(-transform.right * Mathf.Clamp(lateralSpeed * lateralGrip * 1.15f, -14000f, 14000f), rear, ForceMode.Force);
            }
            }
            float curve = EstimateCurvature();
            Body.AddForce(-Frame.normal * Mass * (14f + velocity.sqrMagnitude * curve), ForceMode.Force);
        }

        bool HasLongStraightAhead()
        {
            var definition=track.Route.Definition;
            if(definition==null)return false;
            float sourceDistance=Distance-30;
            if(sourceDistance<0 || sourceDistance+45>definition.totalLength)return false;
            var current=track.Route.SourceAt(Distance);var ahead=track.Route.SourceAt(Distance+45);
            return current.kind=="straight" && ahead.kind=="straight" && current.segmentIndex==ahead.segmentIndex;
        }

        float EstimateCurvature()
        {
            TrackFrame ahead = track.Route.Evaluate(Distance + 3f);
            return Vector3.Angle(Frame.tangent, ahead.tangent) * Mathf.Deg2Rad / 3f;
        }

        void AlignToRoad(bool grounded)
        {
            // Arcade attitude assistance controls pitch/roll, never yaw or position.
            // Damping both swing axes prevents nose-first rebound after a ramp landing.
            Vector3 desiredUp = Frame.normal;
            Vector3 axis = Vector3.Cross(transform.up, desiredUp);
            Vector3 swing = Vector3.ProjectOnPlane(Body.angularVelocity, desiredUp);
            Vector3 acceleration = axis * (grounded ? 65f : 24f) - swing * (grounded ? 13f : 7f);
            Body.AddTorque(Vector3.ClampMagnitude(acceleration, 70f), ForceMode.Acceleration);
        }

        void UpdateWheels()
        {
            float spin = Vector3.Dot(Body.linearVelocity, transform.forward) / WheelRadius * Time.fixedDeltaTime * Mathf.Rad2Deg;
            foreach (Transform wheel in wheelVisuals) wheel.Rotate(Vector3.up, spin, Space.Self);
        }

        void OnCollisionStay(Collision collision) { lastHandlingContactInterval=physicsInterval; }

        void OnCollisionEnter(Collision collision)
        {
            lastHandlingContactInterval=physicsInterval;
            ContactObserved?.Invoke(collision);
            if (FinishedCoasting && collision.gameObject.name == "Finish wall") {
                Hold(true); StoppedAtWall = true; Telemetry.speedKmh = 0;
            }

            MagneticVehicle other = collision.rigidbody == null ? null : collision.rigidbody.GetComponent<MagneticVehicle>();
            if (other != null && IsGhosting)
            {
                Collider otherCollider = collision.collider;
                Physics.IgnoreCollision(chassis, otherCollider, true);
                ignoredCars.Add(otherCollider);
            }
        }

        void RestoreCarCollisions()
        {
            foreach (Collider other in ignoredCars) if (other != null) {
                var vehicle=other.GetComponent<MagneticVehicle>();
                if (vehicle==null || !vehicle.IsGhosting) Physics.IgnoreCollision(chassis, other, false);
            }
            ignoredCars.Clear();
        }

        void BeginGhosting()
        {
            RestoreCarCollisions();
            foreach (MagneticVehicle vehicle in FindObjectsByType<MagneticVehicle>(FindObjectsSortMode.None))
            {
                if (vehicle == this || vehicle.chassis == null) continue;
                Physics.IgnoreCollision(chassis, vehicle.chassis, true);
                ignoredCars.Add(vehicle.chassis);
            }
        }

        void OnDestroy() => RestoreCarCollisions();
    }
}
