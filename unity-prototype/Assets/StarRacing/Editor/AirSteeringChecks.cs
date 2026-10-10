using System;using System.Reflection;using UnityEngine;
namespace StarRacingPrototype {
 public static class AirSteeringChecks {
  static void Check(bool yes,string reason){if(!yes)throw new Exception("Air steering: "+reason);}
  static void Set(MagneticVehicle car,string name,object value)=>typeof(MagneticVehicle).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(car,value);
  public static void Run(){int cases=0;
   foreach(var normal in new[]{Vector3.up,Vector3.down,Vector3.forward,new Vector3(1,2,3).normalized})foreach(float speed in new[]{42f,83.3333f,138.8889f})foreach(float input in new[]{-1f,-.3f,0,.3f,1})foreach(float step in new[]{.00125f,.01f}){
    var n=normal.normalized;var tangent=Vector3.ProjectOnPlane(Mathf.Abs(n.z)<.9f?Vector3.forward:Vector3.up,n).normalized;var velocity=tangent*speed+n*7;var change=AirSteeringForces.VelocityChange(velocity,n,input,step);
    Check(Mathf.Abs(Vector3.Dot(change,n))<=.00001f,"vertical change");Check(Mathf.Abs(Vector3.ProjectOnPlane(velocity+change,n).magnitude-speed)<=.0001f,"horizontal energy");
    Check(change.magnitude/step<=AirSteeringForces.MaxAcceleration+.02f,"force bound");Check(Vector3.Angle(Vector3.ProjectOnPlane(velocity,n),Vector3.ProjectOnPlane(velocity+change,n))<=AirSteeringForces.MaxYawRate*step*Mathf.Rad2Deg+.03f,"rate bound");
    if(input==0)Check(change==Vector3.zero,"neutral altered flight");else Check(Mathf.Sign(Vector3.Dot(change,Vector3.Cross(n,tangent)))==Mathf.Sign(input),"steering sign");cases++;
   }
   Check(AirSteeringForces.VelocityChange(Vector3.forward*80,Vector3.up,0,.01f)==Vector3.zero,"neutral exact zero");Check(AirSteeringForces.VelocityChange(Vector3.forward,Vector3.up,float.NaN,.01f)==Vector3.zero,"invalid input");
   var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime;GameObject root=null;
   try{Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;root=new GameObject("air control lifecycle");var road=new GameObject("road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(RoadVolumeChecks.FlatRoad()));var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);var tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
    foreach(bool human in new[]{true,false}){var obj=new GameObject("air entrant");obj.transform.SetParent(root.transform);var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,balance,human);car.ResetAt(100);car.Body.position=car.Frame.position+car.Frame.normal*4;car.Body.linearVelocity=car.Frame.tangent*80;car.transform.position=car.Body.position;Physics.SyncTransforms();
     Set(car,"activeJump",new Procedural.Jump{id="test",rampStartIndex=10,launchIndex=11,landingEndIndex=50,ballisticLandingEndIndex=50});Set(car,"airSteeringNormal",car.Frame.normal);Set(car,"flightStarted",Time.time);car.SetRaceContext(true,1,1,1);car.SetInput(new DrivingInput{steer=.5f});tick.Invoke(car,null);
     Check(car.AirSteeringActive==human,"human-only arming");RacePhysicsStepper.Simulate(.01f);Check(car.AirSteeringSteps==(human?8:0),"native substep actuation");
     car.SetInput(default);tick.Invoke(car,null);Check(!car.AirSteeringActive,"release leaves command");long before=car.AirSteeringSteps;RacePhysicsStepper.Simulate(.01f);Check(car.AirSteeringSteps==before,"neutral substep force");
     car.SetInput(new DrivingInput{steer=.5f});tick.Invoke(car,null);car.Hold(true);Check(!car.AirSteeringActive,"hold command");car.Hold(false);tick.Invoke(car,null);Check(!car.AirSteeringActive,"held flight rearmed");car.ResetAt(100);Check(!car.AirSteeringActive&&car.ActiveJumpId=="","reset command");
     if(human){
      void Arm(){car.ResetAt(100);car.Body.position=car.Frame.position+car.Frame.normal*4;car.Body.linearVelocity=car.Frame.tangent*80;car.transform.position=car.Body.position;Physics.SyncTransforms();Set(car,"activeJump",new Procedural.Jump{id="lifecycle",landingEndIndex=50,ballisticLandingEndIndex=50});Set(car,"airSteeringNormal",car.Frame.normal);Set(car,"flightStarted",Time.time);car.SetRaceContext(true,1,1,1);car.SetInput(new DrivingInput{steer=.5f});tick.Invoke(car,null);Check(car.AirSteeringActive,"lifecycle arm");}
      Arm();car.SetDrivingGeneration(2);Check(!car.AirSteeringActive,"generation command");tick.Invoke(car,null);Check(!car.AirSteeringActive,"generation rearmed");
      Arm();car.BeginFinishCoast();Check(!car.AirSteeringActive,"finish command");
     }
     UnityEngine.Object.DestroyImmediate(obj);
    }
   }finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);Physics.simulationMode=mode;Time.fixedDeltaTime=dt;}
   Debug.Log("AIR_STEERING_CHECKS_OK pureCases="+cases+" speed normal sign bounds neutral rawAI hold reset generation finish; contactAndDisableProof=separateActualPlayMode");
  }
 }
}
