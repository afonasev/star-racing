using System;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 public static class SplitScreenCameraChecks {
  static readonly MethodInfo Tick=typeof(ChaseCamera).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
  static readonly MethodInfo FollowTick=typeof(ChaseCamera).GetMethod("UpdateCamera",BindingFlags.Instance|BindingFlags.NonPublic);
  static int assertions;
  static void Check(bool value,string message){assertions++;if(!value)throw new Exception("Split-screen camera: "+message);}
  public static void Run(){
   assertions=0;int underRoad=0,distant=0;float worstClearance=float.PositiveInfinity;
   foreach(var theme in new[]{"cloud-city","space-station"}){
    var root=new GameObject("camera checks");
    try{
     var trackGo=new GameObject("track");trackGo.transform.SetParent(root.transform);
     var track=trackGo.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,true,true)));
     var carGo=new GameObject("car");carGo.transform.SetParent(root.transform);
     var car=carGo.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text));
     var cameraGo=new GameObject("view");cameraGo.transform.SetParent(root.transform);
     var camera=cameraGo.AddComponent<Camera>();camera.fieldOfView=65;
     var follow=cameraGo.AddComponent<ChaseCamera>();follow.target=car;
     foreach(int humans in new[]{1,2,3,4}){
      for(int seat=0;seat<humans;seat++){
       camera.rect=RaceViewports.For(humans,seat);camera.aspect=(1920f/1080)*camera.rect.width/camera.rect.height;
       for(float distance=35;distance<track.Route.FinishDistance;distance+=5){
        car.ResetAt(distance);follow.Snap();Tick.Invoke(follow,null);
        var road=track.Route.Project(cameraGo.transform.position,car.Distance,30);
        float clearance=Vector3.Dot(cameraGo.transform.position-road.position,road.normal);
        worstClearance=Mathf.Min(worstClearance,clearance);
        if(clearance<1.5f)underRoad++;
        float behind=Vector3.Dot(car.RenderPose.position-cameraGo.transform.position,car.Frame.tangent);
        if(humans>1&&behind>8.5f)distant++;
        var screen=camera.WorldToViewportPoint(car.RenderPose.position);
        Check(screen.z>0&&screen.x>0&&screen.x<1&&screen.y>0&&screen.y<1,$"framing {theme}/{humans}/{seat}/{distance}: {screen}");
       }
      }
      car.ResetAt(35);follow.Snap();FollowTick.Invoke(follow,new object[]{1f/60});
      for(float distance=36.25f;distance<track.Route.FinishDistance;distance+=1.25f){
       car.Distance=distance;car.Frame=track.Route.Evaluate(distance);
       car.Body.position=car.Frame.position+car.Frame.normal*VehicleGeometry.AdheredBodyHeight;car.Body.rotation=car.Frame.Rotation;
       car.transform.SetPositionAndRotation(car.Body.position,car.Body.rotation);
       FollowTick.Invoke(follow,new object[]{1f/60});
       var road=track.Route.Project(cameraGo.transform.position,car.Distance,30);
       float clearance=Vector3.Dot(cameraGo.transform.position-road.position,road.normal);
       worstClearance=Mathf.Min(worstClearance,clearance);if(clearance<1.5f)underRoad++;
       float behind=Vector3.Dot(car.RenderPose.position-cameraGo.transform.position,car.Frame.tangent);
       if(humans>1&&behind>8.5f)distant++;
       var screen=camera.WorldToViewportPoint(car.RenderPose.position);
       Check(screen.z>0&&screen.x>0&&screen.x<1&&screen.y>0&&screen.y<1,$"moving framing {theme}/{humans}/{distance}: {screen}");
      }
      car.ResetAt(80);CameraFlightChecks.Run(car,Array.Find(track.Route.Definition.jumps,j=>j.kind=="mandatory"),camera.rect);
     }
     car.ResetAt(80);follow.Snap();FollowTick.Invoke(follow,new object[]{1f/60});
     // A discontinuous old camera pose must be corrected on the first rendered frame.
     cameraGo.transform.position=car.Frame.position-car.Frame.normal*5-car.Frame.tangent*7;
     FollowTick.Invoke(follow,new object[]{1f/60});
     var safeRoad=track.Route.Project(cameraGo.transform.position,car.Distance,30);
     Check(Vector3.Dot(cameraGo.transform.position-safeRoad.position,safeRoad.normal)>=1.99f,"below-road pose survived smoothing");
     car.ResetAt(80);follow.Snap();Tick.Invoke(follow,null);var position=cameraGo.transform.position;var rotation=cameraGo.transform.rotation;
     follow.LockAtFinish();car.ResetAt(100);Tick.Invoke(follow,null);
     Check(cameraGo.transform.position==position&&cameraGo.transform.rotation==rotation,"finish lock moved");
     follow.Snap();Tick.Invoke(follow,null);Check(!follow.FinishLocked&&cameraGo.transform.position!=position,"snap failed to unlock");
     var velocity=car.Body.linearVelocity;Tick.Invoke(follow,null);Check(car.Body.linearVelocity==velocity,"camera changed physics");
     // Presentation resets and target changes snap even without an explicit Snap call.
     car.ResetAt(150);Tick.Invoke(follow,null);position=cameraGo.transform.position;
     follow.Snap();Tick.Invoke(follow,null);Check(Vector3.Distance(position,cameraGo.transform.position)<.001f,"presentation reset retained old camera offset");
     var otherGo=new GameObject("other car");otherGo.transform.SetParent(root.transform);
     var other=otherGo.AddComponent<MagneticVehicle>();other.Initialize(track,1,Color.red,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text));other.ResetAt(200);
     follow.target=other;Tick.Invoke(follow,null);position=cameraGo.transform.position;
     follow.Snap();Tick.Invoke(follow,null);Check(Vector3.Distance(position,cameraGo.transform.position)<.001f,"target change retained previous car offset");
     follow.target=null;Tick.Invoke(follow,null);Check(cameraGo.transform.position==position,"unassigned camera moved");
    }finally{UnityEngine.Object.DestroyImmediate(root);}
   }
   Debug.Log($"CAMERA_CLEARANCE_DIAGNOSTIC underRoad={underRoad} distant={distant} worstClearance={worstClearance:F3}");
   Check(underRoad==0,"camera penetrated road "+underRoad+" times");Check(distant==0,"split-screen camera stayed too far "+distant+" times");
   Debug.Log($"SPLIT_SCREEN_CAMERA_OK assertions={assertions} humans=1,2,3,4 themes=2");
  }
 }
}
