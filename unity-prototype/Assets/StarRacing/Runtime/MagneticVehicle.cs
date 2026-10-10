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

        public const float GravityMagnitude = 18f;
        // Adhesion remains active over continuous road even when suspension loses contact.
        public bool AdditionalMagneticAdhesion = true;
        Vector3 gravityDirection = Vector3.down;
        public Vector3 GravityAcceleration => gravityDirection * GravityMagnitude;
        float RestBodyHeight => AdditionalMagneticAdhesion ? VehicleGeometry.AdheredBodyHeight : VehicleGeometry.NeutralBodyHeight;
        public Vector3 MagneticAdhesionAcceleration => AdditionalMagneticAdhesion
            ? gravityDirection * (14f + Vector3.ProjectOnPlane(Body.linearVelocity, Frame.normal).sqrMagnitude * EstimateCurvature())
            : Vector3.zero;

        VehicleForceFrame preparedForces;
        Vector3 preparedHandlingCorrection,preparedHandlingAngular;
        float neutralContactRemaining,contactTravelSign=1;
        bool NeutralContactRecovery=>neutralContactRemaining>0 && roadProtection && !onRampSupport &&
            Mathf.Abs(input.steer)<.01f && !input.drift && handling.driftIntentRemaining<=0 && !FinishedCoasting;
        float ContactRecoveryYaw() {
            var from=Vector3.ProjectOnPlane(Body.rotation*Vector3.forward,Frame.normal).normalized;
            var to=Frame.tangent*contactTravelSign;
            float angle=Mathf.Atan2(Vector3.Dot(Vector3.Cross(from,to),Frame.normal),Vector3.Dot(from,to));
            return Mathf.Clamp(angle*4f,-1.8f,1.8f);
        }
        void ObserveRoadContact(Collision collision) {
            var other=collision.rigidbody==null?null:collision.rigidbody.GetComponent<MagneticVehicle>();
            if(!roadProtection || (other==null&&!RacePhysicsStepper.IsBarrier(collision.collider)))return;
            if(neutralContactRemaining<=0)contactTravelSign=Vector3.Dot(Body.rotation*Vector3.forward,Frame.tangent)<0?-1:1;
            neutralContactRemaining=.6f;
            bool strong=false;
            for(int i=0;i<collision.contactCount;i++)if(Mathf.Abs(Vector3.Dot(collision.relativeVelocity,collision.GetContact(i).normal))>WeakContactRelativeSpeed){strong=true;break;}
            if(!strong)return;
            // A target calculated before the hit must not restore lost kinetic energy
            // during remaining CCD steps. Preserve ordinary engine force and input yaw.
            preparedForces.acceleration-=preparedHandlingCorrection;preparedHandlingCorrection=Vector3.zero;
            if(Mathf.Abs(input.steer)<.01f&&!input.drift&&handling.driftIntentRemaining<=0){preparedForces.angularAcceleration-=preparedHandlingAngular;preparedHandlingAngular=Vector3.zero;}
            reconciliation.Invalidate("strong-road-contact");
        }

        Vector3 airSteeringNormal;float preparedAirSteer;bool airSteeringBlocked;
        public long AirSteeringSteps {get;private set;}
        public bool AirSteeringActive=>Mathf.Abs(preparedAirSteer)>=AirSteeringForces.Deadzone;
        internal void ApplyPreparedForces(float step){preparedForces.Apply(Body,step);ApplyRoadEdgeForces(step);if(AirSteeringActive){AirSteeringForces.Apply(Body,airSteeringNormal,preparedAirSteer,step);AirSteeringSteps++;}}
        void CancelAirSteering(){preparedAirSteer=0;if(activeJump!=null)airSteeringBlocked=true;}
        internal void ClearPreparedForces(){preparedForces=default;preparedHandlingCorrection=preparedHandlingAngular=Vector3.zero;preparedAirSteer=0;preparedContactYawTorque=preparedContactSeparation=Vector3.zero;}
        void AddForce(Vector3 value,ForceMode mode)=>preparedForces.AddForce(value,mode);
        void AddTorque(Vector3 value,ForceMode mode)=>preparedForces.AddTorque(value,mode);
        void AddForceAtPosition(Vector3 value,Vector3 position,ForceMode mode)
        {
            if(mode!=ForceMode.Force)throw new System.ArgumentException("Vehicle point forces use Force mode");
            preparedForces.AddForceAtPosition(value,position,Body);
        }

        const float Mass = 1000f;
        const float WheelRadius = .34f;
        const float SuspensionTravel = .78f;
        const float Wheelbase = 2.35f;
        const float TrackWidth = 1.42f;
        static readonly Vector3[] SuspensionOffsets = {
            new Vector3(-TrackWidth * .5f, 0f, Wheelbase * .5f), new Vector3(TrackWidth * .5f, 0f, Wheelbase * .5f),
            new Vector3(-TrackWidth * .5f, 0f, -Wheelbase * .5f), new Vector3(TrackWidth * .5f, 0f, -Wheelbase * .5f)
        };
        readonly List<Transform> wheelVisuals = new List<Transform>(4);
        readonly List<Collider> ignoredCars = new List<Collider>();
        readonly RecoveryPolicy recovery = new RecoveryPolicy();
        readonly List<RecoveryEvent> recoveryEvents = new List<RecoveryEvent>(2);
        long recoveryTick, recoverySequence;

        TrackBuilder track;
        internal TrackBuilder SourceTrack=>track;
        DrivingInput input;
        StaticVehicleVisual visual;
        Transform presentationRoot; readonly VehiclePresentation presentation=new VehiclePresentation();
        bool presentationCaptured;
        public Pose RenderPose=>presentationCaptured?presentation.Sample(Time.timeAsDouble):Body!=null?new Pose(Body.position,Body.rotation):new Pose(transform.position,transform.rotation);
        public int PresentationEpoch=>presentation.Epoch;
        public TrackFrame RenderFrame=>ProjectPresentationFrame(RenderPose.position);
        public TrackFrame ProjectPresentationFrame(Vector3 position)=>track.Route.Project(position,Distance,30f);
        internal void CapturePresentation(double time,float step){presentation.Capture(new Pose(Body.position,Body.rotation),time,step);presentationCaptured=true;}
        public void ResetPresentation(){if(Body==null)return;presentation.Reset(new Pose(Body.position,Body.rotation));presentationCaptured=false;if(presentationRoot!=null)presentationRoot.SetPositionAndRotation(Body.position,Body.rotation);}
        void UpdatePresentation(){if(presentationRoot==null)return;var pose=RenderPose;presentationRoot.SetPositionAndRotation(pose.position,pose.rotation);}
        float pendingThrottle;bool pendingNitro,acceptedBoost;
        public ExhaustEnvelope DriveFeedback => visual?.Exhaust.Envelope;
        bool nitroFeedbackActive;
        public bool NitroFeedbackActive => nitroFeedbackActive && presentationEnabled && raceActive && Time.timeScale>0 && !FinishedCoasting && !IsFalling;
        public float AudioThrottle => presentationEnabled && !FinishedCoasting && !IsFalling ? Mathf.Max(Mathf.Clamp01(presentationThrottle), DriveFeedback?.GasAudioSignal ?? 0) : 0;
        float presentationThrottle;bool presentationEnabled;int exhaustRevision=-1;
        public void SetPresentationInput(float throttle, bool enabled) { presentationThrottle=throttle;presentationEnabled=enabled;if(!enabled){pendingThrottle=0;pendingNitro=acceptedBoost=nitroFeedbackActive=false;} }
        void LateUpdate(){UpdatePresentation();UpdateExhaust(Time.deltaTime);}
        void UpdateExhaust(float dt) {
            nitroFeedbackActive=presentationEnabled && raceActive && Time.timeScale>0 && !FinishedCoasting && !IsFalling && (acceptedBoost || (Drive!=null && Drive.Active));
            if(visual==null)return;
            if(exhaustRevision!=PositionRevision){visual.Exhaust.ResetEffect();exhaustRevision=PositionRevision;}
            bool show=presentationEnabled && Time.timeScale>0 && !FinishedCoasting && !IsFalling;
            visual.Exhaust.Step(presentationThrottle,show && raceActive && (acceptedBoost || (Drive!=null && Drive.Active)),show,dt);
            acceptedBoost=false;
        }
        public DrivingInput CurrentInput => input;
        public float OffRouteSeconds => offRouteTime;
        public bool IsGhosting => FinishedCoasting || recovery.GhostSeconds > 0;
        BoxCollider chassis;
        float sideContactNormal; int sideContactSamples, sideContactRevision; long sideContactInterval=-2;
        internal const float WeakContactRelativeSpeed=12f;
        internal bool RearContactSteeringRequested => humanDriver && HandlingContinuous && activeJump==null
            && Mathf.Abs(input.steer)>=.01f && strongContacts.Count==0;
        float separationDirection,separationRemaining,contactYawRemaining;
        Vector3 preparedContactYawTorque,preparedContactSeparation;
        readonly HashSet<MagneticVehicle> strongContacts=new HashSet<MagneticVehicle>();
        void ClearSideSamples(){sideContactNormal=0;sideContactSamples=0;sideContactInterval=-2;}
        void CancelSideAssist(){
            ClearSideSamples();separationDirection=separationRemaining=contactYawRemaining=0;
            // Collision callbacks run between native substeps. Revoke only this
            // helper's prepared contribution, preserving gravity, springs and input.
            preparedForces.angularAcceleration-=preparedContactYawTorque;
            preparedForces.acceleration-=preparedContactSeparation;
            preparedContactYawTorque=preparedContactSeparation=Vector3.zero;
        }
        void ClearSideContacts(){neutralContactRemaining=0;CancelSideAssist();strongContacts.Clear();}
        readonly BoxCollider[] roadCcdFaces=new BoxCollider[4];
        public const int VehicleCollisionLayer=8;
        Vector3 lastSafePosition;
        Quaternion lastSafeRotation;
        float lastSafeDistance;
        CheckpointAdmission.Result lastSafeAdmission;
        bool lastSafeValid;
        public bool HasProvenCheckpoint => lastSafeValid && track!=null && lastSafeAdmission.revision==track.BuildRevision;
        readonly CheckpointAdmission.Support[] suspensionSupports=new CheckpointAdmission.Support[4];
        readonly float[] previousSuspensionLengths = new float[4];
        readonly bool[] previousSuspensionContacts = new bool[4];
        Vector3 suspensionNormal; bool onRampSupport;
        float offRouteTime;
        public DrivingBalance Drive {get;private set;}
        bool raceActive; int place=1, entrantCount=2; float driveAcceleration; double raceElapsed;
        public void SetRaceContext(bool active,int position,int count,double elapsed=0){raceActive=active;place=position;entrantCount=count;raceElapsed=elapsed;}
        public void DrainRecoveryEvents(List<RecoveryEvent> destination) {destination.AddRange(recoveryEvents);recoveryEvents.Clear();}
        bool wasGrounded;
        Procedural.Jump lastRampSupport, activeJump;
        Vector3 launchNormal;
        bool roadBelow, unlandedJump, roadProtection;
        float protectedLeft,protectedRight;
        Vector3 protectedOrigin,protectedNormal,protectedRightAxis;
        internal bool ProtectRoadContacts=>roadProtection;
        internal Vector3 ProtectedRoadNormal=>protectedNormal;
        // Braking acts before loss of the supported top surface, not as a rescue
        // after an off-road fall. Re-evaluate native velocity each CCD substep.
        void ApplyRoadEdgeForces(float step) {
            if(!roadProtection || onRampSupport || Body.isKinematic)return;
            var offset=Body.position-protectedOrigin;
            float lane=Vector3.Dot(offset,protectedRightAxis);
            float height=Vector3.Dot(offset,protectedNormal);
            if(lane<protectedLeft || lane>protectedRight || height<-.05f || height>RestBodyHeight+1.5f)return;
            var forward=Body.rotation*Vector3.forward;
            float extent=Mathf.Abs(Vector3.Dot(forward,protectedRightAxis))*VehicleGeometry.HalfLength+
                Mathf.Abs(Vector3.Dot(forward,Frame.tangent))*VehicleGeometry.HalfWidth;
            // AddForce queues delta-v until Simulate. Include every already queued
            // controller force when predicting this native step's outward speed.
            var predicted=Body.linearVelocity+(preparedForces.force/Body.mass+preparedForces.acceleration)*step;
            float lateral=Vector3.Dot(predicted,protectedRightAxis);
            float left=lane-protectedLeft-extent-.12f,right=protectedRight-lane-extent-.12f;
            float allowedRight=EdgeSpeed(right,step),allowedLeft=EdgeSpeed(left,step);
            float target=Mathf.Clamp(lateral,-allowedLeft,allowedRight);
            float correction=Mathf.Clamp(target-lateral,-1000f*step,1000f*step);
            if(correction!=0)Body.AddForce(protectedRightAxis*correction,ForceMode.VelocityChange);
        }
        static float EdgeSpeed(float clearance,float step) {
            // Include a geometric one-step bound, so a held outward command cannot
            // creep across the edge after its braking distance reaches zero.
            if(clearance<=0)return -Mathf.Min(20f,-clearance*30f);
            return Mathf.Min(clearance/step,Mathf.Sqrt(2*1000f*clearance));
        }
        float rampContactTime, flightStarted;
        public string ActiveJumpId => activeJump?.id ?? "";
        void EndJump(string reason) {activeJump=null;preparedAirSteer=0;airSteeringBlocked=false;}
        void ApplyGravity(bool updateDirection = true)
        {
            if (updateDirection)
            {
                roadBelow = false;
                // Probe toward the local road, independently of the car's pitch/roll.
                // A nearest-point query also selects road beside a gap or a real off-road fall.
                var frame = track.Route.Evaluate(Distance);
                float height = Vector3.Dot(Body.position - frame.position, frame.normal);
                float lateral = Mathf.Abs(Vector3.Dot(Body.position - frame.position, frame.right));
                if (height >= -.05f && lateral <= frame.halfWidth &&
                    RaycastRoad(Body.position, -frame.normal, Mathf.Max(2f, height + 4f), Distance, out var hit))
                {
                    gravityDirection = -hit.normal.normalized;
                    roadBelow = true;
                }
                else if (activeJump != null && track.Route.InFlightCorridor(activeJump, Body.position, Distance))
                {
                    // Continue the takeoff plane into the authored gap, blending to the
                    // landing road. In a gap there is intentionally no collider to raycast.
                    float gapStart = 30 + activeJump.gapStartIndex * 5;
                    float gapEnd = 30 + activeJump.gapEndIndex * 5;
                    float t = Mathf.InverseLerp(gapStart, gapEnd, Distance);
                    gravityDirection = -Vector3.Slerp(launchNormal, frame.normal, t).normalized;
                }
                // No road/corridor: keep the last direction, including during recovery.
            }
            AddForce(GravityAcceleration, ForceMode.Acceleration);
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
        public void SetDrivingGeneration(int generation) { if(handlingGeneration==generation)return;ClearSideContacts();CancelAirSteering();ResetPresentation();handlingGeneration=generation;handling.Reset();reconciliation.Invalidate("generation");handlingTick=0;recovery.Reset();recoveryEvents.Clear();recoverySequence=0; }
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

        public void SetInput(DrivingInput value) {
            input = FinishedCoasting ? default : value;
            // Preserve a tap across render frames until a physics step consumes it.
            pendingThrottle=Mathf.Max(pendingThrottle,input.throttle);pendingNitro|=input.nitro;
        }

        public void Hold(bool held) {
            // RaceDirector repeats this request every tick. Only a real state
            // transition may discard spring/contact/force/presentation history.
            if(Body.isKinematic==held)return;
            ClearSideContacts();ClearPreparedForces();CancelAirSteering();
            if(held){pendingThrottle=0;pendingNitro=acceptedBoost=false;track?.TireMarks?.Break(Seat);}
            ResetSuspensionHistory();
            if(held) reconciliation.Invalidate("hold");
            if (held && !Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = held;ResetPresentation();
        }

        public void BeginFinishCoast() {
            CancelAirSteering();
            if (FinishedCoasting) return;
            ClearSideContacts();
            FinishedCoasting = true; input = default;pendingThrottle=0;pendingNitro=acceptedBoost=false;Drive?.StopBoost();handling.Reset();reconciliation.Invalidate("finish");
            ResetSuspensionHistory();
            recovery.Reset();recoveryEvents.Clear();
            ClearPreparedForces();
            chassis.enabled=true;
            var f = track.Route.Evaluate(track.Route.FinishDistance);
            float speed = Mathf.Max(0, Vector3.Dot(Body.linearVelocity, f.tangent));
            Body.rotation = f.Rotation; Body.angularVelocity = Vector3.zero;
            Body.linearVelocity = f.tangent * speed;
            // The finish straight is parallel to world Z. PhysX retains forward inertia and suspension.
            ResetPresentation();
            Body.constraints = track.Route.Definition == null ? RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezeRotation : RigidbodyConstraints.FreezeRotation;
            BeginGhosting();
        }

        public void ResetAt(float distance, float lateral = 0f)
        {
            ClearSideContacts();
            if (track == null || track.Route == null) return;
            PositionRevision++; ResetSuspensionHistory();track.TireMarks?.Break(Seat);
            EndJump("reset");unlandedJump=false;roadProtection=false;lastRampSupport=null;
            FinishedCoasting = false; StoppedAtWall = false;
            Body.isKinematic = false; Body.constraints = RigidbodyConstraints.None;
            Distance = track.Route.Wrap(distance);
            Frame = track.Route.Evaluate(Distance);
            gravityDirection = -Frame.normal.normalized;
            Vector3 point = Frame.position + Frame.right * lateral + Frame.normal * RestBodyHeight;
            Body.position = point;
            Body.rotation = Frame.Rotation;
            // Reset is a teleport: also synchronize the rendered pose before countdown makes the body kinematic.
            transform.SetPositionAndRotation(point, Frame.Rotation);ResetPresentation();
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            lastSafePosition = point;
            lastSafeRotation = Frame.Rotation;
            lastSafeDistance = Distance;
            lastSafeValid=false;
            TryStoreSafePoint(point,Frame.Rotation,Distance,true);
            offRouteTime = 0f;
            recovery.Reset(); recoveryEvents.Clear(); recoverySequence=0;
            ClearPreparedForces();
            chassis.enabled=true;
            input = default;pendingThrottle=0;pendingNitro=acceptedBoost=false;presentationThrottle=0;presentationEnabled=false;nitroFeedbackActive=false;visual?.Exhaust.ResetEffect();
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
            ClearSideContacts();
            PositionRevision++; ResetSuspensionHistory();track.TireMarks?.Break(Seat);
            handling.Reset();reconciliation.Invalidate("recovery");
            EndJump("recovery");unlandedJump=false;roadProtection=false;lastRampSupport=null;
            Body.position = lastSafePosition;
            Body.rotation = lastSafeRotation;
            ClearPreparedForces();
            chassis.enabled=true;
            Frame = track.Route.Evaluate(lastSafeDistance);
            gravityDirection = -Frame.normal.normalized;
            Body.linearVelocity = Frame.tangent.normalized * (Drive == null ? 0f : Drive.BaseSpeed * .5f);
            Body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(lastSafePosition,lastSafeRotation);ResetPresentation();
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
            Body = GetComponent<Rigidbody>();
            if (Body == null) Body = gameObject.AddComponent<Rigidbody>();
            Body.mass = Mass;
            Body.useGravity = false;
            Body.linearDamping = .04f;
            Body.angularDamping = .8f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            // Fixed sweep CCD; contained thin faces cover small-motion road contacts.
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.centerOfMass = new Vector3(0f, -.18f, 0f);
            chassis = GetComponent<BoxCollider>();
            if (chassis == null) chassis = gameObject.AddComponent<BoxCollider>();
            chassis.hasModifiableContacts = true;
            chassis.size = VehicleGeometry.ChassisSize;
            chassis.center = VehicleGeometry.ChassisCenter;
            Body.inertiaTensor = VehicleGeometry.BaselineInertia;
            Body.inertiaTensorRotation = VehicleGeometry.BaselineInertiaRotation;
            RacePhysicsStepper.Register(this);
            PhysicsMaterial roadSafeMaterial = new PhysicsMaterial("MagneticCarChassis") {
                dynamicFriction = .12f,
                staticFriction = .12f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            chassis.material = roadSafeMaterial;
            gameObject.layer=VehicleCollisionLayer;
            float mass=Body.mass;var centre=Body.centerOfMass;
            for(int i=0;i<roadCcdFaces.Length;i++) {
                if(roadCcdFaces[i]==null) {
                    var face=new GameObject("Road CCD face "+i);face.transform.SetParent(transform,false);
                    face.layer=VehicleCollisionLayer;roadCcdFaces[i]=face.AddComponent<BoxCollider>();
                }
                int axis=i<2?0:2;float sign=i%2==0?-1:1;
                var size=chassis.size;var origin=chassis.center;
                size[axis]=.02f;origin[axis]+=sign*(chassis.size[axis]-.02f)*.5f;
                // Inset each vertical guard lower edge while preserving its upper edge.
                // The unchanged root covers the underside and all car pairs.
                size.y-=.01f;origin.y+=.005f;
                var guard=roadCcdFaces[i];guard.size=size;guard.center=origin;
                guard.sharedMaterial=roadSafeMaterial;
                // The original chassis alone owns car pairs, including ghost/fall.
                // Every guard remains inside it. Side faces retain top CCD; end
                // faces reinforce exterior/bottom contacts without duplicate top contacts.
                guard.excludeLayers=(1<<VehicleCollisionLayer)|(1<<TrackBuilder.BarrierCollisionLayer)|(i>=2?1<<TrackBuilder.RoadCollisionLayer:0);
            }
            Body.mass=mass;Body.centerOfMass=centre;
            Body.inertiaTensor=VehicleGeometry.BaselineInertia;
            Body.inertiaTensorRotation=VehicleGeometry.BaselineInertiaRotation;
        }

        void CreateVisuals(Color color, bool human)
        {
            if (GetComponentInChildren<StaticVehicleVisual>() != null) return;
            presentationRoot=new GameObject("Vehicle presentation").transform;presentationRoot.SetParent(transform,false);
            visual = StaticVehicleVisual.Create(presentationRoot, color, human);
        }

        public void PrepareProjection(){if(initialized && !recovery.Falling && track!=null){Frame=track.Route.Project(Body.position,Distance,30f);Distance=Frame.distance;}}
        bool RaycastRoad(Vector3 origin,Vector3 direction,float length,float progress,out RaycastHit hit)
        {
            return progress.Equals(Distance)
                ?track.RaycastRoadWithNormal(origin,direction,length,progress,Frame.normal,out hit)
                :track.RaycastRoad(origin,direction,length,progress,out hit);
        }
        public event System.Action<Collision> ContactObserved;
        void FixedUpdate()
        {
            ClearPreparedForces();roadProtection=false;
            if (!initialized || track == null || track.Route == null || Body.isKinematic) { track?.TireMarks?.Break(Seat); return; }
            float dt = Time.fixedDeltaTime;
            physicsInterval++;neutralContactRemaining=Mathf.Max(0,neutralContactRemaining-dt);
            if(Mathf.Abs(input.steer)>=.01f||input.drift||handling.driftIntentRemaining>0)neutralContactRemaining=0;
            recoveryTick++;
            recovery.StepGhost(dt);
            if(recovery.Falling) {
                pendingThrottle=0;pendingNitro=acceptedBoost=false;
                track.TireMarks?.Break(Seat);
                ApplyGravity(false);
                if(recovery.AdvanceFall(dt))ResetToCheckpoint();
                Telemetry.recovering=recovery.Falling;
                Telemetry.speedKmh=Body.linearVelocity.magnitude*3.6f;
                if(!IsGhosting && !IsFalling)RestoreCarCollisions();
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
            var driveInput=input;driveInput.throttle=Mathf.Max(driveInput.throttle,pendingThrottle);driveInput.nitro|=pendingNitro;
            pendingThrottle=0;pendingNitro=false;
            driveAcceleration=Drive==null?0:humanDriver
                ?Drive.StepHuman(driveInput,(float)HandlingObserved.Speed,dt,raceActive && !FinishedCoasting,grounded && !IsGhosting,place,entrantCount,handling)
                :Drive.Step(driveInput,Vector3.Dot(Body.linearVelocity,transform.forward),dt,raceActive && !FinishedCoasting,grounded && !IsGhosting,place,entrantCount);
            acceptedBoost|=Drive!=null && Drive.Active;
            if(grounded) EndJump("landing");
            else if(wasGrounded && lastRampSupport!=null && Time.time-rampContactTime<.1f &&
                    track.Route.CanLaunch(lastRampSupport,Body.position,Distance,Body.linearVelocity)) {
                handling.Reset();reconciliation.Invalidate("launch"); // Source confirmed launch resets handling intent without writing the physical pose.
                activeJump=lastRampSupport;unlandedJump=true;launchNormal=-gravityDirection;
                airSteeringNormal=Frame.normal.normalized;airSteeringBlocked=false;
                flightStarted=Time.time;
            }
            if(activeJump!=null && (Time.time-flightStarted>=4 || !track.Route.InFlightCorridor(activeJump,Body.position,Distance)))EndJump("corridor-or-timeout");

            ApplyGravity();
            if(grounded && contacts==4 && roadBelow && Vector3.Dot(transform.up,Frame.normal)>.7f)unlandedJump=false;
            var protectedFrame=track.Route.Evaluate(Distance);
            float protectedLane=Vector3.Dot(Body.position-protectedFrame.position,protectedFrame.right);
            roadProtection=AdditionalMagneticAdhesion && roadBelow && !Frame.gap && activeJump==null && !unlandedJump && !IsGhosting &&
                track.Route.TryRoadBounds(Distance,protectedLane,out protectedLeft,out protectedRight);
            protectedOrigin=protectedFrame.position;protectedNormal=protectedFrame.normal;protectedRightAxis=protectedFrame.right;
            if(humanDriver && activeJump!=null && !grounded && raceActive && !airSteeringBlocked && !IsGhosting && !FinishedCoasting && !recovery.Falling)
                preparedAirSteer=float.IsNaN(input.steer)?0:Mathf.Clamp(input.steer,-1,1);
            // Contact loss on a crest must not switch off the force that counters
            // centrifugal separation. Authored ramp flight retains its ballistic arc.
            if (grounded && roadBelow && routeNear && AdditionalMagneticAdhesion &&
                !Frame.gap && activeJump == null && !unlandedJump && !onRampSupport) {
                // On supported road, supply signed centripetal acceleration directly.
                // An unsigned inward pull doubles the suspension load in a concave
                // loop, compressing the chassis onto the road and creating friction.
                var before = track.Route.Evaluate(Distance - 1f);
                var after = track.Route.Evaluate(Distance + 1f);
                Vector3 roadNormal = -gravityDirection;
                float arc = Vector3.Distance(before.position, after.position);
                float curvature = arc > .001f ? Vector3.Dot(after.tangent-before.tangent,roadNormal)/arc : 0;
                float forwardSpeed = Vector3.Dot(Body.linearVelocity, Frame.tangent);
                AddForce(gravityDirection * 14f + roadNormal * (curvature * forwardSpeed * forwardSpeed),
                    ForceMode.Acceleration);
            }
            else if (grounded || (roadBelow && activeJump == null && !unlandedJump && !onRampSupport))
                AddForce(MagneticAdhesionAcceleration, ForceMode.Acceleration);
            if (grounded)
            {
                ApplyMagnetAndDrive(dt);
                if(strongContacts.Count>0)strongContacts.RemoveWhere(other=>other==null || other.IsGhosting || other.IsFalling || !other.gameObject.activeInHierarchy);
                // A small lateral force opens space after a side rub. Contact impulses
                // still own impact response; there is no longitudinal compensation.
                if(sideContactSamples>0 && sideContactRevision==PositionRevision && sideContactInterval==physicsInterval-1 && !IsGhosting) {
                    separationDirection=sideContactNormal/sideContactSamples;
                    separationRemaining=.25f;
                    if(Mathf.Abs(input.steer)<.01f && !input.drift)contactYawRemaining=.25f;
                }
                if(Mathf.Abs(input.steer)>=.01f || input.drift)contactYawRemaining=0;
                if(separationRemaining>0 && strongContacts.Count==0 && !IsGhosting && activeJump==null && !unlandedJump && !onRampSupport) {
                    // The AI's four tyre forces damp lateral motion strongly. Open
                    // the same small clearance without relying on collision spin.
                    bool neutralAi=!humanDriver&&Mathf.Abs(input.steer)<.01f&&!input.drift;
                    preparedContactSeparation=Frame.right*((neutralAi?12f:4f)*separationDirection);
                    AddForce(preparedContactSeparation,ForceMode.Acceleration);
                    // Dissipate contact spin, never command a heading. Steering/drift
                    // retain their complete existing angular command.
                    if(contactYawRemaining>0 && roadBelow && !Frame.gap) {
                        float yaw=Vector3.Dot(Body.angularVelocity,Frame.normal);
                        float damping=Mathf.Clamp(yaw*8f,-8f,8f);
                        damping=Mathf.Clamp(damping,-Mathf.Abs(yaw)/dt,Mathf.Abs(yaw)/dt);
                        preparedContactYawTorque=-Frame.normal*damping;
                        AddTorque(preparedContactYawTorque,ForceMode.Acceleration);
                    }
                }
                if (contacts >= (track.Route.Definition == null ? 3 : 4) && !Frame.gap && Vector3.Dot(transform.up, Frame.normal) > .7f)
                    StoreSafePoint();
                offRouteTime = routeNear ? 0f : offRouteTime + dt;
            }
            else
            {
                reconciliation.Invalidate("flight");
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
            ClearSideSamples();
            separationRemaining=grounded && !IsGhosting?Mathf.Max(0,separationRemaining-dt):0;
            contactYawRemaining=grounded && !IsGhosting?Mathf.Max(0,contactYawRemaining-dt):0;
            // Extra return force only above the working stroke. Signed damping eases
            // the return as the body descends, avoiding extra static suspension load.
            if(AdditionalMagneticAdhesion && roadBelow && routeNear && !Frame.gap &&
                activeJump==null && !unlandedJump && !onRampSupport && !IsGhosting) {
                float excess=Vector3.Dot(Body.position-Frame.position,Frame.normal)-RestBodyHeight-.04f;
                if(excess>0) {
                    float outward=Vector3.Dot(Body.linearVelocity,-gravityDirection);
                    float returnAcceleration=Mathf.Clamp(excess*60f+outward*12f,0f,60f);
                    AddForce(gravityDirection*returnAcceleration,ForceMode.Acceleration);
                }
            }
            AlignToRoad(grounded);
            if(!humanDriver&&NeutralContactRecovery){
                var localTorque=Quaternion.Inverse(Body.rotation*Body.inertiaTensorRotation)*preparedForces.torque;
                var localAcceleration=new Vector3(localTorque.x/Body.inertiaTensor.x,localTorque.y/Body.inertiaTensor.y,localTorque.z/Body.inertiaTensor.z);
                var torqueAcceleration=Body.rotation*Body.inertiaTensorRotation*localAcceleration;
                float existing=Vector3.Dot(preparedForces.angularAcceleration+torqueAcceleration,Frame.normal);
                AddTorque(Frame.normal*((ContactRecoveryYaw()-Vector3.Dot(Body.angularVelocity,Frame.normal))/dt-existing),ForceMode.Acceleration);
            }
            UpdateWheels();

            wasGrounded = grounded;
            Telemetry.recovering = offRouteTime > .05f;
            float lateralFromSupportedSpan=Mathf.Abs(Vector3.Dot(Body.position-Frame.position,Frame.right));
            float heightFromRoad=Vector3.Dot(Body.position-Frame.position,Frame.normal);
            float headingCos=Mathf.Abs(Vector3.Dot(transform.forward,Frame.tangent));
            float headingSin=Mathf.Abs(Vector3.Dot(transform.forward,Frame.right));
            float footprintExtent=headingCos*1.38f+headingSin*2.1f;
            bool unsupported = !grounded && !roadBelow && activeJump==null &&
                (Frame.gap || !routeNear || lateralFromSupportedSpan-footprintExtent>Frame.halfWidth || heightFromRoad< -2f);
            float along = Vector3.Dot(Body.linearVelocity,Frame.tangent);
            if(recovery.Observe(dt,raceActive,raceElapsed,FinishedCoasting,activeJump!=null,unsupported,
                along,Mathf.Clamp01(input.throttle),Mathf.Clamp01(input.brake))) {
                Drive?.StopBoost();handling.Reset();reconciliation.Invalidate("fall");
                // A falling chassis still collides with every road face. Only
                // car pairs are ignored, as before when its collider was disabled.
                BeginGhosting();
                recoveryEvents.Add(new RecoveryEvent(RecoveryEventKind.Fall,Seat,handlingGeneration,recovery.Episode,
                    PositionRevision,PositionRevision,recoveryTick,++recoverySequence));
            }
            if(recovery.Falling)Telemetry.recovering=true;
            if(!IsGhosting && !IsFalling)RestoreCarCollisions();

            Telemetry.speedKmh = Body.linearVelocity.magnitude * 3.6f;
            Telemetry.nitro01 = Drive==null?0:Drive.Charge/Drive.Capacity;
            Telemetry.progress01 = track.Route.Length > .01f ? Distance / track.Route.Length : 0f;
            Telemetry.grounded = grounded;
            Telemetry.compression = contacts == 0 ? 0f : Telemetry.compression / contacts;
            track.TireMarks?.Observe(Seat,PositionRevision,Body.linearVelocity,Body.rotation*Vector3.forward,Frame.normal,
                suspensionSupports[2],suspensionSupports[3],track,
                raceActive && grounded && activeJump==null && !Frame.gap && !IsGhosting && !recovery.Falling && Time.timeScale>0,Time.time);
        }

        void StoreSafePoint()
        {
            if(track.Route.Definition != null) {
                // A recovery must leave enough runway to accelerate again; a stationary
                // reset on the lip would turn one missed jump into an endless failure loop.
                foreach(var jump in track.Route.Definition.jumps) {
                    if(Distance>=30+jump.rampStartIndex*5-80 && Distance<=30+jump.landingEndIndex*5)return;
                }
                TryStoreSafePoint(Frame.position+Frame.normal*RestBodyHeight,Frame.Rotation,Distance,false,true);
                return;
            }
            float lateral = Mathf.Clamp(Vector3.Dot(Body.position - Frame.position, Frame.right), -Frame.halfWidth * .72f, Frame.halfWidth * .72f);
            TryStoreSafePoint(Frame.position + Frame.right * lateral + Frame.normal * RestBodyHeight,Frame.Rotation,Distance,false);
        }

        void TryStoreSafePoint(Vector3 proposed,Quaternion rotation,float distance,bool initial,bool useHitPoint=false) {
            Vector3 centre=proposed-rotation*Vector3.up*RestBodyHeight;
            Vector3 up=rotation*Vector3.up;
            if(!RaycastRoad(centre+up*2f,-up,4f,distance,out var hit) ||
                Vector3.Distance(hit.point,centre)>.5f || Vector3.Dot(hit.normal,up)<.7f)return;
            if(useHitPoint)proposed=hit.point+up*RestBodyHeight;
            var centreSupport=new CheckpointAdmission.Support(centre+up*2f,-up,4f,hit);
            var supports=suspensionSupports;
            if(initial) {
                supports=new CheckpointAdmission.Support[4];
                for(int i=0;i<4;i++) {
                    float x=(i%2==0?-1f:1f)*TrackWidth*.5f;
                    float z=(i<2?1f:-1f)*Wheelbase*.5f;
                    Vector3 origin=proposed+rotation*(new Vector3(x,VehicleGeometry.SuspensionMountHeight,z));
                    float length=SuspensionTravel+WheelRadius+.25f;
                    if(!RaycastRoad(origin,-up,length,distance,out var wheelHit))return;
                    supports[i]=new CheckpointAdmission.Support(origin,-up,length,wheelHit);
                }
            }
            var result=CheckpointAdmission.Check(track,proposed,rotation,distance,centreSupport,supports,RestBodyHeight);
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

        void ResetSuspensionHistory()
        {
            System.Array.Clear(previousSuspensionContacts,0,previousSuspensionContacts.Length);
        }

        int ApplySuspension(float dt)
        {
            int contacts = 0;
            suspensionNormal=Vector3.zero;onRampSupport=false;
            float totalCompression = 0f;
            for(int i=0;i<suspensionSupports.Length;i++)suspensionSupports[i]=default;
            for (int i = 0; i < SuspensionOffsets.Length; i++)
            {
                bool previousContact = previousSuspensionContacts[i];
                previousSuspensionContacts[i] = false;
                Vector3 origin = transform.TransformPoint(SuspensionOffsets[i] + Vector3.up * VehicleGeometry.SuspensionMountHeight);
                if (!RaycastRoad(origin, -transform.up, SuspensionTravel + WheelRadius + .25f, Distance, out RaycastHit hit)) continue;
                suspensionSupports[i]=new CheckpointAdmission.Support(origin,-transform.up,SuspensionTravel+WheelRadius+.25f,hit);
                var supportedRamp=track.Route.RampAtContact(hit.point,Distance);
                if(supportedRamp!=null){lastRampSupport=supportedRamp;rampContactTime=Time.time;onRampSupport=true;}
                float length = Mathf.Max(0f, hit.distance - WheelRadius);
                float compression = Mathf.Clamp01((SuspensionTravel - length) / SuspensionTravel);
                // Support follows the road, not the chassis' delayed pitch. Chassis-up
                // springs introduce a backwards component while climbing a loop.
                Vector3 supportNormal = hit.normal;suspensionNormal+=supportNormal;
                float pointVelocity = Vector3.Dot(Body.GetPointVelocity(origin), supportNormal);
                float lengthVelocity = previousContact && dt>0 ? (length-previousSuspensionLengths[i])/dt : pointVelocity;
                previousSuspensionLengths[i] = length;
                previousSuspensionContacts[i] = true;
                // Damp actual spring travel, not the velocity sampled on a rotating
                // road normal. Discrete motion on a fast loop biases that dot product
                // inward even when the spring length is constant, creating lift.
                float force = compression * 62000f - lengthVelocity * 7000f;
                AddForceAtPosition(supportNormal * Mathf.Max(0f, force), origin, ForceMode.Force);
                contacts++;
                totalCompression += compression;
                if (i < wheelVisuals.Count) wheelVisuals[i].localPosition = SuspensionOffsets[i] + Vector3.up * VehicleGeometry.SuspensionMountHeight + Vector3.down * length;
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
                if(NeutralContactRecovery){
                    float yaw=ContactRecoveryYaw();
                    angularAcceleration+=Frame.normal*((yaw-Vector3.Dot(Body.angularVelocity,Frame.normal))/dt-Vector3.Dot(angularAcceleration,Frame.normal));
                    handling.yawRate=-yaw;
                }
                var engineAcceleration=Vector3.ProjectOnPlane(Body.rotation*Vector3.forward,Frame.normal).normalized*driveAcceleration;
                preparedHandlingCorrection=acceleration-engineAcceleration;preparedHandlingAngular=angularAcceleration;
                reconciliation.Record(handling,Body.angularVelocity,angularAcceleration,Frame,dt,physicsInterval,handlingGeneration,PositionRevision);
                AddForce(acceleration,ForceMode.Acceleration);AddTorque(angularAcceleration,ForceMode.Acceleration);
            }
            else if(!humanDriver) AddForce(transform.forward * driveAcceleration, ForceMode.Acceleration);

            if(!humanDriver) {

            float steerAngle = input.steer * Mathf.Lerp(25f, 9f, Mathf.InverseLerp(12f, 48f, Mathf.Abs(speedAlong)));
            float lateralGrip = input.drift ? 3500f : 10500f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 front = transform.TransformPoint(new Vector3(side * TrackWidth * .5f, -.15f, Wheelbase * .5f));
                Vector3 wheelRight = Quaternion.AngleAxis(steerAngle, transform.up) * transform.right;
                float lateralSpeed = Vector3.Dot(Body.GetPointVelocity(front), wheelRight);
                AddForceAtPosition(-wheelRight * Mathf.Clamp(lateralSpeed * lateralGrip, -12000f, 12000f), front, ForceMode.Force);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 rear = transform.TransformPoint(new Vector3(side * TrackWidth * .5f, -.15f, -Wheelbase * .5f));
                float lateralSpeed = Vector3.Dot(Body.GetPointVelocity(rear), transform.right);
                AddForceAtPosition(-transform.right * Mathf.Clamp(lateralSpeed * lateralGrip * 1.15f, -14000f, 14000f), rear, ForceMode.Force);
            }
            }
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
            Vector3 desiredUp = grounded && onRampSupport && suspensionNormal.sqrMagnitude>.1f?suspensionNormal.normalized:Frame.normal;
            Vector3 axis = Vector3.Cross(transform.up, desiredUp);
            Vector3 swing = Vector3.ProjectOnPlane(Body.angularVelocity, desiredUp);
            Vector3 targetSwing = Vector3.zero;
            if (grounded && !IsExpectedJump()) {
                // Track the road's angular motion. Damping toward zero angular speed
                // makes the nose lag a loop, scrape the road and dissipate forward speed.
                Vector3 aheadNormal = track.Route.Evaluate(Distance + 1f).normal;
                targetSwing = Vector3.Cross(desiredUp, aheadNormal) *
                    Vector3.Dot(Body.linearVelocity, Frame.tangent);
            }
            Vector3 acceleration = axis * (grounded ? 65f : 24f) -
                (swing - targetSwing) * (grounded ? 13f : 7f) + targetSwing * Body.angularDamping;
            AddTorque(Vector3.ClampMagnitude(acceleration, 70f), ForceMode.Acceleration);
        }

        void UpdateWheels()
        {
            float spin = Vector3.Dot(Body.linearVelocity, transform.forward) / WheelRadius * Time.fixedDeltaTime * Mathf.Rad2Deg;
            foreach (Transform wheel in wheelVisuals) wheel.Rotate(Vector3.up, spin, Space.Self);
        }

        void ObserveSideContact(Collision collision)
        {
            var other=collision.rigidbody==null?null:collision.rigidbody.GetComponent<MagneticVehicle>();
            if(other==null)return;

            if(collision.relativeVelocity.magnitude>WeakContactRelativeSpeed){CancelSideAssist();strongContacts.Add(other);return;}
            if(strongContacts.Contains(other))return;
            if(IsGhosting || other.IsGhosting || IsFalling || other.IsFalling ||
                Vector3.Dot(Frame.normal,other.Frame.normal)<.7f)return;
            if(sideContactInterval!=physicsInterval || sideContactRevision!=PositionRevision)ClearSideSamples();
            for(int i=0;i<collision.contactCount;i++) {
                float lateral=Vector3.Dot(collision.GetContact(i).normal,Frame.right);
                if(Mathf.Abs(lateral)<.7f)continue;
                sideContactNormal+=lateral;sideContactSamples++;sideContactRevision=PositionRevision;sideContactInterval=physicsInterval;
            }
        }
        void OnCollisionExit(Collision collision){if(collision.rigidbody!=null)strongContacts.Remove(collision.rigidbody.GetComponent<MagneticVehicle>());}
        void OnCollisionStay(Collision collision) { CancelAirSteering();lastHandlingContactInterval=physicsInterval; ObserveSideContact(collision);ObserveRoadContact(collision);ContactObserved?.Invoke(collision); }

        void OnCollisionEnter(Collision collision)
        {
            CancelAirSteering();lastHandlingContactInterval=physicsInterval;
            ObserveSideContact(collision);ObserveRoadContact(collision);
            ContactObserved?.Invoke(collision);
            if (FinishedCoasting && collision.gameObject.name == "Finish wall") {
                Hold(true); StoppedAtWall = true; Telemetry.speedKmh = 0;
            }

            MagneticVehicle other = collision.rigidbody == null ? null : collision.rigidbody.GetComponent<MagneticVehicle>();
            if (other != null && (IsGhosting || IsFalling))
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
                if (vehicle==null || (!vehicle.IsGhosting && !vehicle.IsFalling)) Physics.IgnoreCollision(chassis, other, false);
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

        void OnDisable() { CancelAirSteering();ResetPresentation();ClearSideContacts();ClearPreparedForces();track?.TireMarks?.Break(Seat); }
        void OnDestroy() { ClearPreparedForces();RacePhysicsStepper.Unregister(this);RestoreCarCollisions(); }
    }
}
