using System;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 public static class CheckpointAdmissionChecks {
  static readonly FieldInfo Buffer=typeof(CheckpointAdmission).GetField("overlaps",BindingFlags.NonPublic|BindingFlags.Static);
  static readonly FieldInfo Fallbacks=typeof(CheckpointAdmission).GetField("overlapFallbacks",BindingFlags.NonPublic|BindingFlags.Static);
  static void Check(bool yes,string reason){if(!yes)throw new Exception("Checkpoint admission: "+reason);}
  static void Cleared(){foreach(var collider in (Collider[])Buffer.GetValue(null))Check(ReferenceEquals(collider,null),"query retained a collider reference");}
  static Collider[] AllocatingQuery(Vector3 pose,Quaternion rotation)=>Physics.OverlapBox(pose+rotation*new Vector3(0,(CheckpointAdmission.Bottom+CheckpointAdmission.Top)*.5f,0),new Vector3(CheckpointAdmission.HalfWidth,(CheckpointAdmission.Top-CheckpointAdmission.Bottom)*.5f,CheckpointAdmission.HalfLength),rotation,~0,QueryTriggerInteraction.Ignore);
  public static void Run(){GameObject root=null;int cases=0;
   try {
    root=new GameObject("checkpoint allocation parity");var road=new GameObject("road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(RoadVolumeChecks.FlatRoad()));
    var frame=track.Route.Evaluate(100);var rotation=Quaternion.LookRotation(frame.tangent,frame.normal);var pose=frame.position+frame.normal*VehicleGeometry.AdheredBodyHeight;Physics.SyncTransforms();
    Check(AllocatingQuery(pose,rotation).Length==0,"clean oracle pose overlaps road");Check(CheckpointAdmission.CheckPose(track,pose,rotation,100,VehicleGeometry.AdheredBodyHeight).accepted,"clean footprint refused");Cleared();
    foreach(int count in new[]{31,32,40})foreach(bool obstacle in new[]{false,true}) {
     var props=new GameObject("overlap props");props.transform.SetParent(root.transform);GameObject blocker=null;
     try {
      for(int i=0;i<count-(obstacle?1:0);i++){var obj=new GameObject("nonroad "+i);obj.transform.SetParent(props.transform);obj.transform.position=pose+rotation*new Vector3(0,.4f,0);obj.AddComponent<BoxCollider>().size=Vector3.one*.02f;}
      if(obstacle){blocker=new GameObject("road obstacle");blocker.transform.SetParent(road.transform);blocker.transform.position=pose+rotation*new Vector3(0,.4f,0);blocker.AddComponent<BoxCollider>().size=Vector3.one*.02f;}
      Physics.SyncTransforms();var oldHits=AllocatingQuery(pose,rotation);Check(oldHits.Length==count,"oracle did not construct exact saturation count");bool expected=true;foreach(var hit in oldHits)if(hit!=null&&hit.GetComponentInParent<TrackBuilder>()!=null)expected=false;
      int before=(int)Fallbacks.GetValue(null);var result=CheckpointAdmission.CheckPose(track,pose,rotation,100,VehicleGeometry.AdheredBodyHeight);
      Check(result.accepted==expected,"allocating oracle parity count="+count+" obstacle="+obstacle);Check(result.revision==track.BuildRevision,"wrong current revision");Check((int)Fallbacks.GetValue(null)-before==(count>=32?1:0),"saturation fallback missing or spurious");Cleared();cases++;
     }finally{if(blocker!=null)UnityEngine.Object.DestroyImmediate(blocker);UnityEngine.Object.DestroyImmediate(props);Physics.SyncTransforms();}
     Check(CheckpointAdmission.CheckPose(track,pose,rotation,100,VehicleGeometry.AdheredBodyHeight).accepted,"next clean query depends on prior contents");Cleared();
    }
    Check(track.RaycastRoad(frame.position+frame.normal*2,-frame.normal,4,100,out var oldHit),"support fixture ray");var support=new CheckpointAdmission.Support(frame.position+frame.normal*2,-frame.normal,4,oldHit);var wheels=new[]{support,support,support,support};
    var prior=CheckpointAdmission.Check(track,pose,rotation,100,support,wheels,VehicleGeometry.AdheredBodyHeight);Check(prior.accepted,"current supports refused");track.Build(new TrackRoute(RoadVolumeChecks.FlatRoad()));Physics.SyncTransforms();
    Check(prior.revision!=track.BuildRevision,"road revision did not change");Check(!CheckpointAdmission.Check(track,pose,rotation,100,support,wheels,VehicleGeometry.AdheredBodyHeight).accepted,"stale support admitted");var fresh=CheckpointAdmission.CheckPose(track,pose,rotation,100,VehicleGeometry.AdheredBodyHeight);Check(fresh.accepted&&fresh.revision==track.BuildRevision,"fresh revision refused");Cleared();
    Check(!CheckpointAdmission.CheckPose(track,new Vector3(float.NaN,0,0),rotation,100,VehicleGeometry.AdheredBodyHeight).accepted,"invalid pose admitted");Cleared();
   }finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);Physics.SyncTransforms();}
   Debug.Log("CHECKPOINT_ADMISSION_PARITY_OK cases="+cases+" below/exact/overflow accepted/rejected fallback cleared next-query current/stale support");
  }
 }
}
