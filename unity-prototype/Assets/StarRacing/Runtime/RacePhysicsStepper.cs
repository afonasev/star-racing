using System;
using System.Collections.Generic;
using UnityEngine;

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
        SimulationMode previousMode;bool owns;
        public static IReadOnlyList<MagneticVehicle> Vehicles=>vehicles;
        public static long NativeSteps {get;private set;}
        public static double SimulatedSeconds {get;private set;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry(){vehicles.Clear();owner=null;NativeSteps=0;SimulatedSeconds=0;}
        internal static void Register(MagneticVehicle vehicle){if(!vehicles.Contains(vehicle))vehicles.Add(vehicle);}
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
            float step=duration/count;
            try {
                for(int part=0;part<count;part++) {
                    for(int i=0;i<cars.Count;i++)if(cars[i]!=null)cars[i].ApplyPreparedForces(step);
                    Physics.Simulate(step);NativeSteps++;SimulatedSeconds+=step;afterSubstep?.Invoke();
                }
                // Publish once per controller tick, never once per native substep.
                for(int i=0;i<cars.Count;i++)if(cars[i]!=null && cars[i].gameObject.activeInHierarchy)cars[i].CapturePresentation(Time.fixedTimeAsDouble,duration);
            }finally {for(int i=0;i<cars.Count;i++)if(cars[i]!=null)cars[i].ClearPreparedForces();}
        }
    }
}
