using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype
{
// A single triangle source is used by rendering and collision. The source lateral axis is
// opposite vehicle-right, so spans are converted at the vertex boundary only.
public static class ProceduralTrackMesh
{
    sealed class Geometry
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>(), samples = new List<int>();
        public readonly List<RoadFace> faces = new List<RoadFace>();
        public int quadCount;
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int sample, Vector3 up,
            RoadFace face = default)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), up) > 0)
                triangles.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
            else
                triangles.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 });
            samples.Add(sample);
            samples.Add(sample);
            faces.Add(face);
            faces.Add(face);
        }
        public void Create(Transform parent, string name, Material material, bool road)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (road)
            {
                var surface=go.AddComponent<TrackSurface>();
                surface.triangleSamples=samples.ToArray();
                surface.triangleFaces=faces.ToArray();
                surface.sourceVertices=vertices.ToArray();
                surface.sourceTriangles=triangles.ToArray();
            }
        }
    }
    static Vector3 Point(TrackFrame f, double lateral,
                         float height = 0) => f.position - f.right * (float)lateral + f.normal * height;
    static RoadFace Face(Geometry road, int sample, TrackFrame first, TrackFrame second,
        double left0, double right0, double left1, double right1, bool ramp = false) =>
        new RoadFace(sample, road.quadCount++, first.distance, second.distance,
            (float)left0, (float)right0, (float)left1, (float)right1, ramp);
    static float RampHeight(Jump jump, float distance)
    {
        float start = 30 + jump.rampStartIndex * 5, launch = 30 + jump.launchIndex * 5,
              end = 30 + jump.gapEndIndex * 5;
        if (distance <= launch)
        {
            float t = Mathf.Clamp01((distance - start) / (launch - start));
            return 2.8f * (3 * t * t - 2 * t * t * t) + .12f * (launch - start) * (t * t * t - t * t);
        }
        float u = Mathf.Clamp01((distance - launch) / (end - launch));
        // Full protection has a continuous bridge: same lip slope, then a smooth flat landing.
        return (2 * u * u * u - 3 * u * u + 1) * 2.8f + (u * u * u - 2 * u * u + u) * .12f * (end - launch);
    }
    public static void Build(Transform parent, TrackRoute route, Material roadMaterial, Material railMaterial)
    {
        var road = new Geometry();
        var rails = new Geometry();
        var d = route.Definition;
        for (int i = 0; i < route.Samples.Length - 1; i++)
        {
            var a = route.Samples[i];
            var b = route.Samples[i + 1];
            bool runout = a.distance < 30 || a.distance >= route.FinishDistance;
            SpanPair[] pairs =
                runout ? new[] { new SpanPair(new Span(0, a.halfWidth), new Span(0, b.halfWidth)) }
                       : Layout.Pairs(d, route.SourceAt(a.distance), route.SourceAt(b.distance));
            var ramp = System.Array.Find(
                d.jumps,
                jump =>
                    a.distance >= 30 + jump.rampStartIndex * 5 &&
                    b.distance <= 30 + (d.guardrailMode == "full" ? jump.gapEndIndex : jump.launchIndex) * 5);
            if (ramp != null)
            {
                double left = ramp.lateralCenter - ramp.lateralHalfWidth,
                       right = ramp.lateralCenter + ramp.lateralHalfWidth;
                for (float distance = a.distance; distance < b.distance; distance += 1)
                {
                    var first = route.Evaluate(distance);
                    var second = route.Evaluate(Mathf.Min(b.distance, distance + 1));
                    float h1 = RampHeight(ramp, first.distance), h2 = RampHeight(ramp, second.distance);
                    if (left > -a.halfWidth + .01)
                        road.Quad(Point(first, -a.halfWidth), Point(second, -b.halfWidth), Point(first, left),
                                  Point(second, left), i, a.normal,
                                  Face(road,i,first,second,-a.halfWidth,left,-b.halfWidth,left));
                    road.Quad(Point(first, left, h1), Point(second, left, h2), Point(first, right, h1),
                              Point(second, right, h2), i, a.normal,
                              Face(road,i,first,second,left,right,left,right,true));
                    if (right < a.halfWidth - .01)
                        road.Quad(Point(first, right), Point(second, right), Point(first, a.halfWidth),
                                  Point(second, b.halfWidth), i, a.normal,
                                  Face(road,i,first,second,right,a.halfWidth,right,b.halfWidth));
                }
            }
            else
                foreach (var pair in pairs)
                    road.Quad(Point(a, pair.first.Left), Point(b, pair.second.Left),
                              Point(a, pair.first.Right), Point(b, pair.second.Right), i, a.normal,
                              Face(road,i,a,b,pair.first.Left,pair.first.Right,pair.second.Left,pair.second.Right));
            var sa = route.SourceAt(a.distance);
            var sb = route.SourceAt(b.distance);
            var la = runout ? (d.guardrailMode == "none" ? new double[0]
                                                         : new double[] { -a.halfWidth, a.halfWidth })
                            : Layout.Rails(Layout.Lanes(d, sa));
            var lb = runout ? (d.guardrailMode == "none" ? new double[0]
                                                         : new double[] { -b.halfWidth, b.halfWidth })
                            : Layout.Rails(Layout.Lanes(d, sb));
            foreach (double offset in la)
            {
                double closest = double.PositiveInfinity, other = 0;
                foreach (double candidate in lb)
                {
                    double diff = System.Math.Abs(candidate - offset);
                    if (diff < closest)
                    {
                        closest = diff;
                        other = candidate;
                    }
                }
                if (closest > 3)
                    continue;
                // No rail bridges a physical jump gap, except full mode's continuous road.
                if (d.guardrailMode != "full" &&
                    (Layout.Paved(d, sa).Length == 0 || Layout.Paved(d, sb).Length == 0))
                    continue;
                Vector3 pa = Point(a, offset,
                                   ramp != null && System.Math.Abs(offset - ramp.lateralCenter) <=
                                                       ramp.lateralHalfWidth
                                       ? RampHeight(ramp, a.distance)
                                       : 0),
                        pb = Point(b, other,
                                   ramp != null && System.Math.Abs(other - ramp.lateralCenter) <=
                                                       ramp.lateralHalfWidth
                                       ? RampHeight(ramp, b.distance)
                                       : 0),
                        side = a.right * .15f;
                rails.Quad(pa - side, pb - side, pa - side + a.normal * 1.3f, pb - side + b.normal * 1.3f, i,
                           -a.right);
                rails.Quad(pa + side, pb + side, pa + side + a.normal * 1.3f, pb + side + b.normal * 1.3f, i,
                           a.right);
                rails.Quad(pa - side + a.normal * 1.3f, pb - side + b.normal * 1.3f,
                           pa + side + a.normal * 1.3f, pb + side + b.normal * 1.3f, i, a.normal);
            }
        }
        road.Create(parent, "Procedural road", roadMaterial, true);
        rails.Create(parent, "Procedural guard rails", railMaterial, false);
    }
}
}
