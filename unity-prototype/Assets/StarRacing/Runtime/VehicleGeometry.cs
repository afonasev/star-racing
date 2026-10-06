using UnityEngine;
namespace StarRacingPrototype {
 public static class VehicleGeometry {
  public const float HalfWidth=1.38f, HalfLength=2.1f, VisualScale=.72f;
  public const float Width=HalfWidth*2, Length=HalfLength*2;
  // Measured before resize in baseline-inertia.json (Unity 6000.3.23f1).
  public static readonly Vector3 BaselineInertia=new Vector3(1548.108154296875f,2104.799560546875f,712.9083862304688f);
  public static readonly Quaternion BaselineInertiaRotation=Quaternion.identity;
  public const float SourceMinY=.008150219917297364f, SourceMaxY=1.4560149908065797f;
  // Frozen suspension law: four normalized-compression springs support mass*g.
  public const float NeutralBodyHeight=.78f+.34f-.2f-.78f*1000f*9.81f/(4f*62000f);
  // Preserve the previously available stroke: baseline box center .05 / height .55.
  // The nominal .78 travel already lost its final .085 to the old chassis.
  public const float BaselineChassisMinY=.05f-.55f*.5f;
  public const float UnloadedBodyHeight=.78f+.34f-.2f;
  public const float BaselineStopHeight=-BaselineChassisMinY;
  public const float AvailableWorkingStroke=UnloadedBodyHeight-BaselineStopHeight;
  public const float NeutralWorkingClearance=NeutralBodyHeight-BaselineStopHeight;
  public const float SourceAttachmentY=BaselineChassisMinY-SourceMinY;
  public const float ChassisMinY=SourceMinY+SourceAttachmentY, ChassisMaxY=SourceMaxY+SourceAttachmentY;
  public static readonly Vector3 ChassisSize=new Vector3(Width,ChassisMaxY-ChassisMinY,Length);
  public static readonly Vector3 ChassisCenter=new Vector3(0,(ChassisMinY+ChassisMaxY)*.5f,0);
  public const float PairRearmDistance=Length+1, PairRearmLateral=Width+1;
  public static float LateralExtent(float heading)=>Mathf.Abs(Mathf.Cos(heading))*HalfWidth+Mathf.Abs(Mathf.Sin(heading))*HalfLength;
 }
}
