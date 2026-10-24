using UnityEngine;
namespace StarRacingPrototype {
 // Canonical visual metadata only: never registered as a physical TrackSurface.
 public sealed class RoadRenderSource : MonoBehaviour {
  public int[] triangleSamples;
  public RoadFace[] triangleFaces;
 }
}
