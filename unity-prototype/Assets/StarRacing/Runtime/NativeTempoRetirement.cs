using UnityEngine;
namespace StarRacingPrototype {
 public sealed class NativeTempoRetirement : MonoBehaviour {
  void Update()=>NativeTempoPlayback.DrainRetired();
 }
}
