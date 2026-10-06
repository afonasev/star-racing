using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype
{
    /// <summary>Nearest point on the actual road meshes, independent of race progress.</summary>
    public sealed class RoadSurfaceQuery
    {
        public struct Hit
        {
            public int triangleId, surfaceId, triangleOrdinal, sampleIndex, buildRevision;
            public Vector3 point, normal;
            public float distanceSquared;
        }

        struct Triangle
        {
            public int a, b, c, id, surface, ordinal, sample, revision;
            public Vector3 min, max, center;
        }

        struct Node
        {
            public Vector3 min, max;
            public int left, right, start, count;
        }

        sealed class CenterComparer : IComparer<Triangle>
        {
            readonly int axis;
            public CenterComparer(int axisIndex) { axis = axisIndex; }
            public int Compare(Triangle x, Triangle y)
            {
                int order = Coordinate(x.center, axis).CompareTo(Coordinate(y.center, axis));
                return order != 0 ? order : x.id.CompareTo(y.id);
            }
        }

        readonly Vector3[] vertices, normals;
        readonly Triangle[] triangles;
        readonly Node[] nodes;

        public int TriangleCount => triangles.Length;

        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
            !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);

        static string StablePath(Transform root, Transform child)
        {
            string path = child.name;
            for (Transform parent = child.parent; parent != null && parent != root; parent = parent.parent)
                path = parent.name + "/" + path;
            if (child != root && !child.IsChildOf(root)) throw new ArgumentException("Road surface outside builder");
            return path;
        }

        public RoadSurfaceQuery(TrackSurface[] surfaces, TrackBuilder owner, int revision)
        {
            if (surfaces == null || owner == null) throw new ArgumentNullException("road inventory");
            for (int i = 0; i < surfaces.Length; i++)
                if (surfaces[i] == null) throw new ArgumentException("Null road surface inventory");
            Array.Sort(surfaces, (a, b) => string.CompareOrdinal(StablePath(owner.transform, a.transform),
                                                                  StablePath(owner.transform, b.transform)));
            var points = new List<Vector3>();
            var authoredNormals = new List<Vector3>();
            var faces = new List<Triangle>();
            string previousPath = null;
            for (int surfaceId = 0; surfaceId < surfaces.Length; surfaceId++)
            {
                var surface = surfaces[surfaceId];
                if (surface == null || !surface.gameObject.activeInHierarchy || !surface.enabled ||
                    surface.owner != owner || surface.buildRevision != revision)
                    throw new ArgumentException("Inactive or stale road surface inventory");
                string path = StablePath(owner.transform, surface.transform);
                if (path == previousPath) throw new ArgumentException("Duplicate stable road path: " + path);
                previousPath = path;
                var collider = surface.GetComponent<MeshCollider>();
                var filter = surface.GetComponent<MeshFilter>();
                if (collider == null || !collider.enabled || collider.sharedMesh == null ||
                    filter == null || filter.sharedMesh != collider.sharedMesh)
                    throw new ArgumentException("Road collider/render mesh mismatch: " + path);
                Mesh mesh = collider.sharedMesh;
                Vector3[] localPoints = mesh.vertices, localNormals = mesh.normals;
                int[] indices = mesh.triangles;
                if (localPoints.Length != localNormals.Length || indices.Length % 3 != 0 ||
                    surface.triangleSamples == null || surface.triangleSamples.Length != indices.Length / 3 ||
                    surface.triangleFaces == null || surface.triangleFaces.Length != indices.Length / 3 ||
                    surface.sourceVertices == null || surface.sourceVertices.Length != localPoints.Length ||
                    surface.sourceTriangles == null || surface.sourceTriangles.Length != indices.Length)
                    throw new ArgumentException("Road triangle inventory mismatch: " + path);
                for (int i = 0; i < indices.Length; i++)
                    if (indices[i] < 0 || indices[i] >= localPoints.Length || indices[i] != surface.sourceTriangles[i])
                        throw new ArgumentException("Road triangle index/source mismatch: " + path);
                bool[] referenced = new bool[localPoints.Length];
                for (int i = 0; i < indices.Length; i++) referenced[indices[i]] = true;
                int offset = points.Count;
                Matrix4x4 normalMatrix = surface.transform.localToWorldMatrix.inverse.transpose;
                for (int i = 0; i < localPoints.Length; i++)
                {
                    if (!localPoints[i].Equals(surface.sourceVertices[i]) || !Finite(localPoints[i]) || !Finite(localNormals[i]))
                        throw new ArgumentException("Road vertex/source mismatch: " + path);
                    Vector3 point = surface.transform.TransformPoint(localPoints[i]);
                    Vector3 normal = normalMatrix.MultiplyVector(localNormals[i]);
                    if (!Finite(point) || !Finite(normal) || (referenced[i] && normal.sqrMagnitude <= 0f))
                        throw new ArgumentException("Invalid road point/normal transform: " + path);
                    points.Add(point);
                    // Interpolate the transformed vertex normals before the final normalization.
                    // Per-vertex normalization would change barycentric weights under nonuniform scale.
                    authoredNormals.Add(normal);
                }
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = offset + indices[i], b = offset + indices[i + 1], c = offset + indices[i + 2];
                    Vector3 pa = points[a], pb = points[b], pc = points[c];
                    if (Vector3.Cross(pb - pa, pc - pa).sqrMagnitude <= 0f)
                        throw new ArgumentException("Degenerate road triangle: " + path + "#" + (i / 3));
                    faces.Add(new Triangle {
                        a = a, b = b, c = c, id = faces.Count, surface = surfaceId,
                        ordinal = i / 3, sample = surface.triangleSamples[i / 3], revision = revision,
                        min = Vector3.Min(pa, Vector3.Min(pb, pc)),
                        max = Vector3.Max(pa, Vector3.Max(pb, pc)),
                        center = (pa + pb + pc) / 3f
                    });
                }
            }
            vertices = points.ToArray();
            normals = authoredNormals.ToArray();
            triangles = faces.ToArray();
            var tree = new List<Node>();
            if (triangles.Length > 0) BuildNode(tree, 0, triangles.Length);
            nodes = tree.ToArray();
        }

        static float Coordinate(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;

        int BuildNode(List<Node> tree, int start, int count)
        {
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            Vector3 centerMin = min, centerMax = max;
            for (int i = start; i < start + count; i++)
            {
                min = Vector3.Min(min, triangles[i].min);
                max = Vector3.Max(max, triangles[i].max);
                centerMin = Vector3.Min(centerMin, triangles[i].center);
                centerMax = Vector3.Max(centerMax, triangles[i].center);
            }
            int index = tree.Count;
            tree.Add(default);
            if (count <= 8)
            {
                tree[index] = new Node { min = min, max = max, start = start, count = count };
                return index;
            }
            Vector3 span = centerMax - centerMin;
            int axis = span.x >= span.y && span.x >= span.z ? 0 : span.y >= span.z ? 1 : 2;
            Array.Sort(triangles, start, count, new CenterComparer(axis));
            int half = count / 2;
            int left = BuildNode(tree, start, half);
            int right = BuildNode(tree, start + half, count - half);
            tree[index] = new Node { min = min, max = max, left = left, right = right };
            return index;
        }

        static float BoundsDistanceSquared(Vector3 p, Vector3 min, Vector3 max)
        {
            float x = Mathf.Max(0f, Mathf.Max(min.x - p.x, p.x - max.x));
            float y = Mathf.Max(0f, Mathf.Max(min.y - p.y, p.y - max.y));
            float z = Mathf.Max(0f, Mathf.Max(min.z - p.z, p.z - max.z));
            return x * x + y * y + z * z;
        }

        public bool Nearest(Vector3 point, out Hit hit)
        {
            hit = default;
            if (!Finite(point)) throw new ArgumentException("Non-finite road query point");
            if (nodes.Length == 0) return false;
            hit.distanceSquared = float.PositiveInfinity;
            hit.triangleId = int.MaxValue;
            Visit(0, point, ref hit);
            return hit.triangleId != int.MaxValue;
        }

        public bool NearestBruteForce(Vector3 point, out Hit hit)
        {
            hit = default;
            if (!Finite(point)) throw new ArgumentException("Non-finite road query point");
            if (triangles.Length == 0) return false;
            hit.distanceSquared = float.PositiveInfinity;
            hit.triangleId = int.MaxValue;
            for (int i = 0; i < triangles.Length; i++) TestTriangle(triangles[i], point, ref hit);
            return hit.triangleId != int.MaxValue;
        }

        void Visit(int nodeIndex, Vector3 point, ref Hit best)
        {
            Node node = nodes[nodeIndex];
            if (BoundsDistanceSquared(point, node.min, node.max) > best.distanceSquared + 1e-6f) return;
            if (node.count > 0)
            {
                for (int i = node.start; i < node.start + node.count; i++)
                    TestTriangle(triangles[i], point, ref best);
                return;
            }
            Node left = nodes[node.left], right = nodes[node.right];
            float dl = BoundsDistanceSquared(point, left.min, left.max);
            float dr = BoundsDistanceSquared(point, right.min, right.max);
            if (dl <= dr)
            {
                Visit(node.left, point, ref best);
                Visit(node.right, point, ref best);
            }
            else
            {
                Visit(node.right, point, ref best);
                Visit(node.left, point, ref best);
            }
        }

        void TestTriangle(Triangle face, Vector3 point, ref Hit best)
        {
            Vector3 nearest = ClosestPoint(point, vertices[face.a], vertices[face.b], vertices[face.c],
                                           out Vector3 weights);
            float distance = (point - nearest).sqrMagnitude;
            if (!Finite(nearest) || !Finite(weights) || float.IsNaN(distance) || float.IsInfinity(distance))
                throw new InvalidOperationException("Invalid nearest-road triangle geometry");
            if (distance > best.distanceSquared ||
                (distance == best.distanceSquared && face.id >= best.triangleId)) return;
            Vector3 normal = normals[face.a] * weights.x + normals[face.b] * weights.y +
                             normals[face.c] * weights.z;
            if (!Finite(normal) || normal.sqrMagnitude <= 0f)
                throw new InvalidOperationException("Invalid barycentric road normal");
            best = new Hit { triangleId = face.id, surfaceId = face.surface, triangleOrdinal = face.ordinal,
                             sampleIndex = face.sample, buildRevision = face.revision, point = nearest,
                             normal = normal.normalized, distanceSquared = distance };
        }

        static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 weights)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { weights = new Vector3(1f, 0f, 0f); return a; }
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { weights = new Vector3(0f, 1f, 0f); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                weights = new Vector3(1f - v, v, 0f);
                return a + ab * v;
            }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { weights = new Vector3(0f, 0f, 1f); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                weights = new Vector3(1f - w, 0f, w);
                return a + ac * w;
            }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / (d4 - d3 + d5 - d6);
                weights = new Vector3(0f, 1f - w, w);
                return b + (c - b) * w;
            }
            float denominator = 1f / (va + vb + vc);
            float baryB = vb * denominator, baryC = vc * denominator;
            weights = new Vector3(1f - baryB - baryC, baryB, baryC);
            return a + ab * baryB + ac * baryC;
        }
    }
}
