using System;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 public static class VehiclePresentationChecks {
  static void Check(bool ok,string why){if(!ok)throw new Exception("Vehicle presentation: "+why);}
  public static void Run(){
   foreach(int rate in new[]{60,120,144}) {
    var presentation=new VehiclePresentation();presentation.Reset(new Pose(Vector3.zero,Quaternion.identity));int ticks=0;
    for(int frame=1;frame<rate*2;frame++) {
     double time=(double)frame/rate;
     while((ticks+1)*.01<=time){ticks++;presentation.Capture(new Pose(Vector3.forward*(ticks*.01f*83.3f),Quaternion.Euler(0,ticks*.1f,0)),ticks*.01,.01f);}
     var pose=presentation.Sample(time);Check(Math.Abs(pose.position.z-Math.Max(0,time-.01)*83.3)<.005,"constant motion at "+rate+"Hz");
    }
    int epoch=presentation.Epoch;var teleport=new Pose(new Vector3(700,50,-30),Quaternion.Euler(5,180,6));presentation.Reset(teleport);
    Check(presentation.Epoch==epoch+1 && presentation.Sample(100).position==teleport.position,"teleport blended through old race");
    presentation.Capture(new Pose(teleport.position+Vector3.forward,teleport.rotation),100,.01f);
    var paused=presentation.Sample(100.003);Check(presentation.Sample(100.003).position==paused.position,"paused scaled clock changed pose");
   }
   var root=new GameObject("presentation isolation");
   try {
    var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(RoadVolumeChecks.FlatRoad()));
    var obj=new GameObject("presentation car");obj.transform.SetParent(root.transform);var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan);car.ResetAt(80);car.enabled=false;
    var first=car.Body.position;car.Body.position+=car.Frame.tangent*2;car.Body.linearVelocity=car.Frame.tangent*83.3f;typeof(MagneticVehicle).GetMethod("CapturePresentation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(car,new object[]{Time.timeAsDouble-.005,.01f});
    var expected=first+car.Frame.tangent;var physical=car.Body.position;var velocity=car.Body.linearVelocity;var angular=car.Body.angularVelocity;
    typeof(MagneticVehicle).GetMethod("UpdatePresentation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(car,null);
    var pivot=obj.transform.Find("Vehicle presentation");Check(Vector3.Distance(pivot.position,expected)<.001,"visual did not sample whole tick");
    Check(car.Body.position==physical && car.Body.linearVelocity==velocity && car.Body.angularVelocity==angular,"render wrote authoritative physics");
    Check(pivot.GetComponentsInChildren<Collider>().Length==0 && pivot.GetComponentsInChildren<Rigidbody>().Length==0,"visual pivot owns collision");
    int epoch=car.PresentationEpoch;car.Hold(false);Check(car.PresentationEpoch==epoch,"repeated release reset visual history");
    car.Hold(true);Check(car.PresentationEpoch!=epoch && car.RenderPose.position==car.Body.position,"hold transition blended");
    car.Hold(false);car.ResetAt(200);Check(car.RenderPose.position==car.Body.position,"reset blended old pose");
    car.SetDrivingGeneration(3);Check(car.RenderPose.position==car.Body.position,"generation blended old race");
   }finally {UnityEngine.Object.DestroyImmediate(root);}
   Debug.Log("VEHICLE_PRESENTATION_CHECKS_OK rates=60,120,144 teleport hold generation pause physicalIsolation");
  }
 }
}
