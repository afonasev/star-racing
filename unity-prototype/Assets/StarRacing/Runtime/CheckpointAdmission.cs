using UnityEngine;
namespace StarRacingPrototype {
 // Validate a recovery pose against the current physical road and obstacles.
 public static class CheckpointAdmission {
  public const float HalfWidth=1.38f,HalfLength=2.1f,Bottom=-.225f,Top=1.2228648f;
  // CheckPose is synchronous on the Unity main thread. Never retain query references.
  static readonly Collider[] overlaps=new Collider[32];
  static int overlapFallbacks;
  public readonly struct Support {
   public readonly Vector3 origin,direction;public readonly float length;public readonly RaycastHit hit;
   public Support(Vector3 origin,Vector3 direction,float length,RaycastHit hit){this.origin=origin;this.direction=direction;this.length=length;this.hit=hit;}
  }
  public readonly struct Result {
   public readonly bool accepted;public readonly int revision;
   public Result(bool accepted,int revision){this.accepted=accepted;this.revision=revision;}
  }
  static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
  static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
  static bool CurrentRoad(TrackBuilder track,RaycastHit hit,Vector3 up){
   var surface=hit.collider==null?null:hit.collider.GetComponent<TrackSurface>();
   return surface!=null&&surface.owner==track&&surface.buildRevision==track.BuildRevision&&Finite(hit.point)&&Vector3.Dot(hit.normal,up)>.7f;
  }
  public static Result Check(TrackBuilder track,Vector3 pose,Quaternion rotation,float progress,Support centre,Support[] wheels,float bodyHeight=1.1f){
   Vector3 up=rotation*Vector3.up;
   if(track==null||wheels==null||wheels.Length!=4||!CurrentRoad(track,centre.hit,up))return new Result(false,track==null?0:track.BuildRevision);
   foreach(var wheel in wheels)if(!CurrentRoad(track,wheel.hit,up))return new Result(false,track.BuildRevision);
   return CheckPose(track,pose,rotation,progress,bodyHeight);
  }
  public static Result CheckPose(TrackBuilder track,Vector3 pose,Quaternion rotation,float progress,float bodyHeight=1.1f){
   int revision=track==null?0:track.BuildRevision;
   if(track==null||track.Route==null||!Finite(pose)||!Finite(progress)||!Finite(rotation.x)||!Finite(rotation.y)||!Finite(rotation.z)||!Finite(rotation.w))return new Result(false,revision);
   if(!Finite(bodyHeight)||bodyHeight<=0)return new Result(false,revision);
   Vector3 up=rotation*Vector3.up,basePoint=pose-up*bodyHeight;
   Vector3 expectedNormal=track.Route.Evaluate(progress).normal;
   // Centre, corners and edge midpoints must all have nearby road under the body.
   // A narrow branch, edge or gap cannot admit a pose using centre support alone.
   for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){
    Vector3 point=basePoint+rotation*new Vector3(x*HalfWidth,0,z*HalfLength);
    if(!track.RaycastRoadWithNormal(point+up*2f,-up,4f,progress,expectedNormal,out var hit)||!CurrentRoad(track,hit,up)||Vector3.Distance(hit.point,point)>.5f)return new Result(false,revision);
   }
   Vector3 centre=pose+rotation*new Vector3(0,(Bottom+Top)*.5f,0);
   try {
    int count=Physics.OverlapBoxNonAlloc(centre,new Vector3(HalfWidth,(Top-Bottom)*.5f,HalfLength),overlaps,rotation,~0,QueryTriggerInteraction.Ignore);
    Collider[] hits=overlaps;
    // A full buffer may have omitted a road obstacle, even when exactly 32 are returned.
    if(count==overlaps.Length){overlapFallbacks++;hits=Physics.OverlapBox(centre,new Vector3(HalfWidth,(Top-Bottom)*.5f,HalfLength),rotation,~0,QueryTriggerInteraction.Ignore);count=hits.Length;}
    for(int i=0;i<count;i++)if(hits[i]!=null&&hits[i].GetComponentInParent<TrackBuilder>()!=null)return new Result(false,revision);
    return new Result(true,revision);
   }finally{System.Array.Clear(overlaps,0,overlaps.Length);}
  }
 }
}
