using UnityEngine;
namespace StarRacingPrototype {
 // Public positive steer means native right; the source policy receives an inverted value.
 public struct DrivingInput { public float throttle, brake, steer; public bool drift, nitro; }
 public struct TrackFrame {
  public float distance, halfWidth; public int segmentId; public Vector3 position, tangent, normal, right; public bool gap;
  public Quaternion Rotation => Quaternion.LookRotation(tangent, normal);
 }
 public struct VehicleTelemetry { public float speedKmh, nitro01, progress01, compression; public bool grounded, recovering; }
}
