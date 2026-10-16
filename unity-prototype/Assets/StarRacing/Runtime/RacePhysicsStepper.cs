using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

namespace StarRacingPrototype
{
    // World-space forces computed once by the existing 10 ms controller tick.
    public struct VehicleForceFrame
    {
        public Vector3 force, acceleration, torque, angularAcceleration;
        public void AddForce(Vector3 value, ForceMode mode)
        {
            if(mode==ForceMode.Force)force+=value;
            else if(mode==ForceMode.Acceleration)acceleration+=value;
            else throw new ArgumentException("Continuous vehicle forces require Force or Acceleration");
        }
        public void AddTorque(Vector3 value, ForceMode mode)
        {
            if(mode==ForceMode.Force)torque+=value;
            else if(mode==ForceMode.Acceleration)angularAcceleration+=value;
            else throw new ArgumentException("Continuous vehicle torque requires Force or Acceleration");
        }
        public void AddForceAtPosition(Vector3 value, Vector3 position, Rigidbody body)
        {
            force+=value;torque+=Vector3.Cross(position-body.worldCenterOfMass,value);
        }
        public void Apply(Rigidbody body,float step)
        {
            if(body==null || body.isKinematic || !body.gameObject.activeInHierarchy)return;
            if(!force.Equals(Vector3.zero))body.AddForce(force*step,ForceMode.Impulse);
            if(!acceleration.Equals(Vector3.zero))body.AddForce(acceleration*step,ForceMode.VelocityChange);
            if(!torque.Equals(Vector3.zero))body.AddTorque(torque*step,ForceMode.Impulse);
            if(!angularAcceleration.Equals(Vector3.zero))body.AddTorque(angularAcceleration*step,ForceMode.VelocityChange);
        }
    }

    [DefaultExecutionOrder(1000)]
    public sealed class RacePhysicsStepper : MonoBehaviour
    {
        public const int Substeps=8;
        static readonly List<MagneticVehicle> vehicles=new List<MagneticVehicle>(64);
        static RacePhysicsStepper owner;
        // Read-only while PhysX invokes callbacks on its worker threads.
        readonly struct ContactVehicle {
            public readonly Vector3 Velocity,Angular,Center,Normal;public readonly bool Eligible;public readonly float Radius;
            public ContactVehicle(MagneticVehicle car){Velocity=car.Body.linearVelocity;Angular=car.Body.angularVelocity;Center=car.Body.worldCenterOfMass;Normal=car.Frame.normal;Radius=car.ContactRadius;Eligible=!car.IsGhosting&&!car.IsFalling&&!car.FinishedCoasting;}
            public Vector3 At(Vector3 point)=>Velocity+Vector3.Cross(Angular,point-Center);
        }
        static readonly Dictionary<int,ContactVehicle> contactVehicles=new Dictionary<int,ContactVehicle>(64);
        static readonly List<ContactVehicle> contactSnapshot=new List<ContactVehicle>(64);
        static bool contactHooked;
        static readonly Dictionary<int,Vector3> roadProtected=new Dictionary<int,Vector3>(64);
        static readonly HashSet<int> barriers=new HashSet<int>();
        static readonly HashSet<int> barrierTracks=new HashSet<int>();
        internal static bool IsBarrier(Collider collider)=>barriers.Contains(collider.GetInstanceID());
        static void ModifyRearContacts(PhysicsScene scene,NativeArray<ModifiableContactPair> pairs)=>ModifyContacts(pairs,false);
        static void ModifyCcdContacts(PhysicsScene scene,NativeArray<ModifiableContactPair> pairs)=>ModifyContacts(pairs,true);
        static void ModifyContacts(NativeArray<ModifiableContactPair> pairs,bool ccd) {
            for(int index=0;index<pairs.Length;index++) {
                var pair=pairs[index];
                bool first=contactVehicles.TryGetValue(pair.bodyInstanceID,out var a);
                bool second=contactVehicles.TryGetValue(pair.otherBodyInstanceID,out var b);
                if(!first&&!second)continue;
                bool carPair=first&&second;
                float peak=0;bool solid=false;
                for(int i=0;i<pair.contactCount;i++) {
                    var normal=pair.GetNormal(i);var point=pair.GetPoint(i);
                    if(!carPair&&RoadSupportContact.TryNormal(first?pair.otherColliderInstanceID:pair.colliderInstanceID,pair,i,point,out var exteriorNormal)) {
                        // Discrete normals point from the second shape to the first;
                        // Unity exposes CCD normals inverted and restores them afterwards.
                        var authored=exteriorNormal*(first?1f:-1f)*(ccd?-1f:1f);
                        if(Vector3.Dot(normal,authored)<.95f){normal=authored;pair.SetNormal(i,normal);}
                    }
                    if(!(first&&a.Eligible||second&&b.Eligible))continue;
                    float speed=Mathf.Abs(Vector3.Dot((first?a.At(point):Vector3.zero)-(second?b.At(point):Vector3.zero),normal));
                    bool exterior=carPair||Mathf.Abs(Vector3.Dot(normal,first?a.Normal:b.Normal))<.65f;
                    if(!exterior)continue; // Keep road-top support entirely in the existing solver path.
                    solid=true;peak=Mathf.Max(peak,speed);
                    float strength=Mathf.InverseLerp(4f,12f,speed);
                    pair.SetStaticFriction(i,.02f);pair.SetDynamicFriction(i,.02f);
                    pair.SetBounciness(i,strength*(carPair?.12f:.2f));
                }
                if(!solid||!carPair)continue;
                // Both masses always participate. Weak rubbing damps incidental spin;
                // strong impacts keep the real inertia and their point-of-impact torque.
                float inertia=Mathf.Lerp(0f,1f,Mathf.InverseLerp(4f,12f,peak));
                var mass=pair.massProperties;
                if(first)mass.inverseInertiaScale=inertia;
                if(second)mass.otherInverseInertiaScale=inertia;
                pair.massProperties=mass;
            }
        }
        SimulationMode previousMode;bool owns;
        public static IReadOnlyList<MagneticVehicle> Vehicles=>vehicles;
        public static long NativeSteps {get;private set;}
        public static double SimulatedSeconds {get;private set;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry(){
            Physics.ContactModifyEvent-=ModifyRearContacts;Physics.ContactModifyEventCCD-=ModifyCcdContacts;
            contactHooked=false;contactSnapshot.Clear();contactVehicles.Clear();roadProtected.Clear();barriers.Clear();barrierTracks.Clear();vehicles.Clear();owner=null;NativeSteps=0;SimulatedSeconds=0;
        }
        internal static void Register(MagneticVehicle vehicle){
            if(!contactHooked){Physics.ContactModifyEvent+=ModifyRearContacts;Physics.ContactModifyEventCCD+=ModifyCcdContacts;contactHooked=true;}
            if(!vehicles.Contains(vehicle))vehicles.Add(vehicle);
        }
        internal static void Unregister(MagneticVehicle vehicle){vehicles.Remove(vehicle);}
        void OnEnable()
        {
            if(!Application.isPlaying)return;
            if(owner!=null && owner!=this)throw new InvalidOperationException("A physics scene already has a race stepper");
            owner=this;previousMode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;owns=true;
        }
        void FixedUpdate(){if(owns)SimulatePrepared(vehicles,Time.fixedDeltaTime);}
        void OnDisable()
        {
            if(!owns)return;
            foreach(var vehicle in vehicles)if(vehicle!=null)vehicle.ClearPreparedForces();
            Physics.simulationMode=previousMode;owner=null;owns=false;
        }
        static float PairStepLimit(IReadOnlyList<ContactVehicle> cars,float remaining,float limit) {
            for(int i=0;i<cars.Count;i++) {
                var a=cars[i];if(!a.Eligible)continue;
                for(int j=i+1;j<cars.Count;j++) {
                    var b=cars[j];if(!b.Eligible)continue;
                    var velocity=b.Velocity-a.Velocity;float speedSquared=velocity.sqrMagnitude;
                    if(speedSquared<=144f)continue;
                    var delta=b.Center-a.Center;float distanceSquared=delta.sqrMagnitude;
                    float speed=Mathf.Sqrt(speedSquared),reach=a.Radius+b.Radius+speed*remaining+.04f;
                    if(distanceSquared>reach*reach)continue;
                    if(distanceSquared>1e-10f&&-Vector3.Dot(delta,velocity)<=12f*Mathf.Sqrt(distanceSquared))continue;
                    // Resolution only: a predicted contact never changes handling or emits an impact.
                    limit=Mathf.Min(limit,.02f/speed);
                }
            }return limit;
        }
        static bool Live(MagneticVehicle car)=>car!=null&&car.gameObject.activeInHierarchy&&car.Body!=null&&!car.Body.isKinematic;
        public static void Simulate(float duration,Action afterSubstep=null)=>SimulatePrepared(vehicles,duration,Substeps,afterSubstep);
        public static void SimulatePrepared(IReadOnlyList<MagneticVehicle> cars,float duration,int count=Substeps,Action afterSubstep=null)
        {
            if(duration<=0 || float.IsNaN(duration) || float.IsInfinity(duration) || count<=0)throw new ArgumentOutOfRangeException(nameof(duration));
            if(Physics.simulationMode!=SimulationMode.Script)throw new InvalidOperationException("Race stepper requires Script simulation mode");
            contactVehicles.Clear();roadProtected.Clear();barriers.Clear();barrierTracks.Clear();
            for(int i=0;i<cars.Count;i++) {
                var car=cars[i];
                if(car!=null && car.gameObject.activeInHierarchy && car.Body!=null && !car.Body.isKinematic)
                {
                    contactVehicles.Add(car.Body.GetInstanceID(),new ContactVehicle(car));
                    // Inventory only live barriers from these cars' current track builds.
                    // No retained collider IDs survive rebuilds/domain reloads.
                    if(car.SourceTrack!=null&&barrierTracks.Add(car.SourceTrack.GetInstanceID())) {
                        // Indexed traversal avoids an interface enumerator in this
                        // 100 Hz path; continue filtering live colliders every tick.
                        var colliders=car.SourceTrack.BarrierColliders;
                        for(int barrierIndex=0;barrierIndex<colliders.Count;barrierIndex++) {
                            var collider=colliders[barrierIndex];
                            if(collider!=null&&collider.enabled&&collider.gameObject.activeInHierarchy)barriers.Add(collider.GetInstanceID());
                        }
                    }
                    if(car.ProtectRoadContacts)roadProtected.Add(car.Body.GetInstanceID(),car.ProtectedRoadNormal);
                }
            }
            float step=duration/count;
            try {
                for(int part=0;part<count;part++) {
                    float remaining=step;
                    while(remaining>0) {
                        float limit=remaining;
                        for(int i=0;i<cars.Count;i++)if(Live(cars[i]))limit=Mathf.Min(limit,cars[i].LimitNativeStep(remaining));
                        contactVehicles.Clear();contactSnapshot.Clear();
                        for(int i=0;i<cars.Count;i++)if(Live(cars[i])) {
                            var car=cars[i];var snapshot=new ContactVehicle(car);
                            contactVehicles.Add(car.Body.GetInstanceID(),snapshot);contactSnapshot.Add(snapshot);
                        }
                        limit=PairStepLimit(contactSnapshot,remaining,limit);
                        // Equal slices of the remainder avoid a tiny trailing interval.
                        float slice=remaining/Mathf.Max(1,Mathf.CeilToInt(remaining/limit));
                        for(int i=0;i<cars.Count;i++)if(Live(cars[i])){cars[i].AdvanceImpactResolution(slice,step);cars[i].ApplyPreparedForces(slice);}
                        using(GraphRuntimeProfile.NativePhysics.Auto())Physics.Simulate(slice);
                        NativeSteps++;SimulatedSeconds+=slice;afterSubstep?.Invoke();
                        remaining=slice>=remaining?0:remaining-slice;
                    }
                }
                // Publish once per controller tick, never once per native substep.
                for(int i=0;i<cars.Count;i++)if(cars[i]!=null && cars[i].gameObject.activeInHierarchy)cars[i].CapturePresentation(Time.fixedTimeAsDouble,duration);
            }finally {contactSnapshot.Clear();contactVehicles.Clear();roadProtected.Clear();barriers.Clear();barrierTracks.Clear();for(int i=0;i<cars.Count;i++)if(cars[i]!=null)cars[i].ClearPreparedForces();}
        }
    }
}
