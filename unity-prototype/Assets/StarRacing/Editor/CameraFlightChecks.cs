using System;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 public static class CameraFlightChecks {
  public static void Run(MagneticVehicle car, Procedural.Jump jump, Rect? viewport=null) {
   var position=car.Body.position;var rotation=car.Body.rotation;var velocity=car.Body.linearVelocity;
   var active=typeof(MagneticVehicle).GetField("activeJump",BindingFlags.Instance|BindingFlags.NonPublic);
   var original=active.GetValue(car);var go=new GameObject("camera flight check");
   try {
    var view=go.AddComponent<Camera>();view.fieldOfView=65;
    if(viewport.HasValue){view.rect=viewport.Value;view.aspect=(1920f/1080)*view.rect.width/view.rect.height;}
    var follow=go.AddComponent<ChaseCamera>();follow.target=car;
    var tick=typeof(ChaseCamera).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
    active.SetValue(car,null);car.Body.position=car.Frame.position+car.Frame.normal*1.1f;
    car.transform.SetPositionAndRotation(car.Body.position,rotation);car.ResetPresentation();follow.Snap();tick.Invoke(follow,null);
    float ground=Vector3.Dot(go.transform.position-car.Frame.position,car.Frame.normal);
    active.SetValue(car,jump);car.Body.position+=car.Frame.normal*6;
    car.transform.SetPositionAndRotation(car.Body.position,rotation);car.ResetPresentation();follow.Snap();tick.Invoke(follow,null);
    float air=Vector3.Dot(go.transform.position-car.Frame.position,car.Frame.normal);
    var screen=view.WorldToViewportPoint(car.transform.position);
    if(air-ground>2||air-ground<1||screen.z<=0||screen.x<0||screen.x>1||screen.y<0||screen.y>1)throw new Exception("Flight camera lost road proximity or car framing: ground="+ground+" air="+air+" screen="+screen);
    if(car.Body.linearVelocity!=velocity)throw new Exception("Camera changed physical velocity");
    Debug.Log($"CAMERA_FLIGHT_OK roadHeightGround={ground:F2} roadHeightAir={air:F2} carViewport={screen} physicalVelocityUnchanged");
   } finally {active.SetValue(car,original);car.Body.position=position;car.Body.rotation=rotation;car.transform.SetPositionAndRotation(position,rotation);car.ResetPresentation();UnityEngine.Object.DestroyImmediate(go);Physics.SyncTransforms();}
  }
 }
}
