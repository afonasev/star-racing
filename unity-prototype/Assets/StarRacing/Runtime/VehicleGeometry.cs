using UnityEngine;
namespace StarRacingPrototype {
 public static class VehicleGeometry {
  public const float HalfWidth=1.38f, HalfLength=2.1f, VisualScale=.72f;
  public const float Width=HalfWidth*2, Length=HalfLength*2;
  // Measured before resize in baseline-inertia.json (Unity 6000.3.23f1).
  public static readonly Vector3 BaselineInertia=new Vector3(1548.108154296875f,2104.799560546875f,712.9083862304688f);
  public static readonly Quaternion BaselineInertiaRotation=Quaternion.identity;
  public const float SourceMinY=.008150219917297364f, SourceMaxY=1.4560149908065797f;
  // Four normalized-compression springs support the body. Raise the mounting
  // points instead of offsetting the art away from its physical chassis.
  public const float SuspensionMountHeight=.76f;
  public const float NeutralBodyHeight=.78f+.34f-SuspensionMountHeight-.78f*1000f*MagneticVehicle.GravityMagnitude/(4f*62000f);
  public const float AdheredBodyHeight=NeutralBodyHeight-.78f*1000f*14f/(4f*62000f);
  // Retain the chassis collision envelope. The lower rest height intentionally
  // reduces working stroke above the physical chassis stop.
  public const float BaselineChassisMinY=.05f-.55f*.5f;
  public const float UnloadedBodyHeight=.78f+.34f-SuspensionMountHeight;
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
