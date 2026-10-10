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
            public readonly Vector3 Velocity;
            public readonly bool Steers;
            public ContactVehicle(MagneticVehicle car){Velocity=car.Body.linearVelocity;Steers=car.RearContactSteeringRequested;}
        }
        static readonly Dictionary<int,ContactVehicle> contactVehicles=new Dictionary<int,ContactVehicle>(64);
        static bool contactHooked;
        static readonly Dictionary<int,Vector3> roadProtected=new Dictionary<int,Vector3>(64);
        static readonly HashSet<int> barriers=new HashSet<int>();
        static readonly HashSet<int> barrierTracks=new HashSet<int>();
        internal static bool IsBarrier(Collider collider)=>barriers.Contains(collider.GetInstanceID());
        static void ModifyRearContacts(PhysicsScene scene,NativeArray<ModifiableContactPair> pairs)
        {
            for(int index=0;index<pairs.Length;index++) {
                var pair=pairs[index];
                bool first=roadProtected.TryGetValue(pair.bodyInstanceID,out var firstNormal);
                bool second=roadProtected.TryGetValue(pair.otherBodyInstanceID,out var secondNormal);
                bool carPair=contactVehicles.ContainsKey(pair.bodyInstanceID)&&contactVehicles.ContainsKey(pair.otherBodyInstanceID);
                // Scale only contact-induced rotation. Linear impulses still stop a
                // direct hit; steering, suspension and jump torques retain inertia.
                if((carPair && (!first || !second || Vector3.Dot(firstNormal,secondNormal)>.7f)) ||
                    (first && barriers.Contains(pair.otherColliderInstanceID)) || (second && barriers.Contains(pair.colliderInstanceID))) {
                    var stableMass=pair.massProperties;
                    if(first)stableMass.inverseInertiaScale=0;
                    if(second)stableMass.otherInverseInertiaScale=0;
                    pair.massProperties=stableMass;
                }
                if(!contactVehicles.TryGetValue(pair.bodyInstanceID,out var firstContact) || !contactVehicles.TryGetValue(pair.otherBodyInstanceID,out var secondContact))continue;
                Vector3 forward=pair.rotation*Vector3.forward,otherForward=pair.otherRotation*Vector3.forward;
                if(Vector3.Dot(forward,otherForward)<.8f)continue;
                Vector3 delta=pair.otherPosition-pair.position;
                float along=Vector3.Dot(delta,forward),otherAlong=Vector3.Dot(delta,otherForward);
                if(Mathf.Abs(along)<1f || along*otherAlong<=0 || pair.contactCount==0)continue;
                bool rear=true;
                for(int i=0;i<pair.contactCount;i++)
                    if(Mathf.Abs(Vector3.Dot(pair.GetNormal(i),forward))<.7f || Mathf.Abs(Vector3.Dot(pair.GetNormal(i),otherForward))<.7f){rear=false;break;}
                if(!rear)continue;
                // The leading car keeps its momentum; the follower still receives the
                // solver's stopping/separation impulse. No pose or speed correction.
                var mass=pair.massProperties;
                if(along<0){mass.inverseMassScale=0;mass.inverseInertiaScale=0;}
                else {mass.otherInverseMassScale=0;mass.otherInverseInertiaScale=0;}
                bool weak=(firstContact.Velocity-secondContact.Velocity).sqrMagnitude<=MagneticVehicle.WeakContactRelativeSpeed*MagneticVehicle.WeakContactRelativeSpeed;
                if(weak && (along>0?firstContact.Steers:secondContact.Steers)) {
                    if(along>0)mass.inverseInertiaScale=0;
                    else mass.otherInverseInertiaScale=0;
                }
                pair.massProperties=mass;
            }
        }
        SimulationMode previousMode;bool owns;
        public static IReadOnlyList<MagneticVehicle> Vehicles=>vehicles;
        public static long NativeSteps {get;private set;}
        public static double SimulatedSeconds {get;private set;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry(){
            Physics.ContactModifyEvent-=ModifyRearContacts;Physics.ContactModifyEventCCD-=ModifyRearContacts;
            contactHooked=false;contactVehicles.Clear();roadProtected.Clear();barriers.Clear();barrierTracks.Clear();vehicles.Clear();owner=null;NativeSteps=0;SimulatedSeconds=0;
        }
        internal static void Register(MagneticVehicle vehicle){
            if(!contactHooked){Physics.ContactModifyEvent+=ModifyRearContacts;Physics.ContactModifyEventCCD+=ModifyRearContacts;contactHooked=true;}
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
        public static void Simulate(float duration,Action afterSubstep=null)=>SimulatePrepared(vehicles,duration,Substeps,afterSubstep);
        public static void SimulatePrepared(IReadOnlyList<MagneticVehicle> cars,float duration,int count=Substeps,Action afterSubstep=null)
        {
            if(duration<=0 || float.IsNaN(duration) || float.IsInfinity(duration) || count<=0)throw new ArgumentOutOfRangeException(nameof(duration));
            if(Physics.simulationMode!=SimulationMode.Script)throw new InvalidOperationException("Race stepper requires Script simulation mode");
            contactVehicles.Clear();roadProtected.Clear();barriers.Clear();barrierTracks.Clear();
            for(int i=0;i<cars.Count;i++) {
                var car=cars[i];
                if(car!=null && car.gameObject.activeInHierarchy && car.Body!=null && !car.Body.isKinematic && !car.IsGhosting && !car.IsFalling && !car.FinishedCoasting)
                {
                    contactVehicles.Add(car.Body.GetInstanceID(),new ContactVehicle(car));
                    // Inventory only live barriers from these cars' current track builds.
                    // No retained collider IDs survive rebuilds/domain reloads.
                    if(car.SourceTrack!=null&&barrierTracks.Add(car.SourceTrack.GetInstanceID()))foreach(var collider in car.SourceTrack.BarrierColliders)
                        if(collider!=null&&collider.enabled&&collider.gameObject.activeInHierarchy)barriers.Add(collider.GetInstanceID());
                    if(car.ProtectRoadContacts)roadProtected.Add(car.Body.GetInstanceID(),car.ProtectedRoadNormal);
                }
            }
            float step=duration/count;
            try {
                for(int part=0;part<count;part++) {
                    for(int i=0;i<cars.Count;i++)if(cars[i]!=null)cars[i].ApplyPreparedForces(step);
                    Physics.Simulate(step);NativeSteps++;SimulatedSeconds+=step;afterSubstep?.Invoke();
                }
                // Publish once per controller tick, never once per native substep.
                for(int i=0;i<cars.Count;i++)if(cars[i]!=null && cars[i].gameObject.activeInHierarchy)cars[i].CapturePresentation(Time.fixedTimeAsDouble,duration);
            }finally {contactVehicles.Clear();roadProtected.Clear();barriers.Clear();barrierTracks.Clear();for(int i=0;i<cars.Count;i++)if(cars[i]!=null)cars[i].ClearPreparedForces();}
        }
    }
}
