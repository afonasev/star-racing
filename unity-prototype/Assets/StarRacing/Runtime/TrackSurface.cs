using UnityEngine;

namespace StarRacingPrototype {
    /// <summary>Marks road colliders with their route position for local contact filtering.</summary>
    public sealed class TrackSurface : MonoBehaviour {
        public int segmentId;
        public float distance;
        // Each MeshCollider triangle maps to the route sample at the start of its road face.
        public int[] triangleSamples;
        // Parallel to the collider's triangle order. Metadata describes the emitted face,
        // including its actual lateral edges; a span ordinal is not a branch identity.
        public RoadFace[] triangleFaces;
        public TrackBuilder owner;
        public int buildRevision;
        [System.NonSerialized] public Vector3[] sourceNormals;
        [System.NonSerialized] public Vector3[] sourceVertices;
        [System.NonSerialized] public int[] sourceTriangles;
    }

    public struct RoadFace {
        public int sample;
        public int quad;
        public float startDistance, endDistance;
        public float leftStart, rightStart, leftEnd, rightEnd;
        public bool ramp;
        public RoadFace(int sample, int quad, float start, float end,
            float left0, float right0, float left1, float right1, bool ramp) {
            this.sample=sample;this.quad=quad;startDistance=start;endDistance=end;
            leftStart=left0;rightStart=right0;leftEnd=left1;rightEnd=right1;this.ramp=ramp;
        }
    }
}
