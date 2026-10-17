using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype {
    // Authored hard-surface vocabulary, merged by material per station. Coordinates normalize to a unit sphere.
    internal sealed class SpaceModelGeometry {
        internal sealed class Part {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
        }
        public readonly Part[] parts = { new Part(), new Part(), new Part(), new Part(), new Part() };
        readonly Vector3[] cubeVertices, cubeNormals;
        readonly int[] cubeTriangles;
        public SpaceModelGeometry(Mesh cube) { cubeVertices = cube.vertices; cubeNormals = cube.normals; cubeTriangles = cube.triangles; }
        void Box(int material, Vector3 center, Vector3 scale, Quaternion rotation) {
            var p = parts[material]; int start = p.vertices.Count;
            for (int i = 0; i < cubeVertices.Length; i++) {
                var v = Vector3.Scale(cubeVertices[i], scale);
                p.vertices.Add(center + rotation * v); p.normals.Add(rotation * cubeNormals[i]);
                p.uv.Add(new Vector2((v.x + v.z) / 4, v.y / 4));
            }
            foreach (int i in cubeTriangles) p.triangles.Add(start + i);
        }
        void Box(int material, Vector3 center, Vector3 scale) => Box(material, center, scale, Quaternion.identity);
        void Beam(int material, Vector3 a, Vector3 b, float width) => Box(material, (a + b) * .5f, new Vector3(width, width, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a));
        void Cylinder(int material, Vector3 a, Vector3 b, float radius, float endRadius = -1, int sides = 16) {
            if (endRadius < 0) endRadius = radius;
            var q = Quaternion.LookRotation(b - a); var p = parts[material]; float length = Vector3.Distance(a, b);
            for (int i = 0; i < sides; i++) {
                float t = i * Mathf.PI * 2 / sides, next = (i + 1) * Mathf.PI * 2 / sides;
                var n0 = new Vector3(Mathf.Cos(t), Mathf.Sin(t), (radius - endRadius) / length).normalized;
                var n1 = new Vector3(Mathf.Cos(next), Mathf.Sin(next), (radius - endRadius) / length).normalized;
                int at = p.vertices.Count;
                p.vertices.AddRange(new[] { a + q * new Vector3(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius, 0), a + q * new Vector3(Mathf.Cos(next) * radius, Mathf.Sin(next) * radius, 0), b + q * new Vector3(Mathf.Cos(t) * endRadius, Mathf.Sin(t) * endRadius, 0), b + q * new Vector3(Mathf.Cos(next) * endRadius, Mathf.Sin(next) * endRadius, 0) });
                p.normals.AddRange(new[] { q * n0, q * n1, q * n0, q * n1 });
                p.uv.AddRange(new[] { new Vector2(i * radius / 10, 0), new Vector2((i + 1) * radius / 10, 0), new Vector2(i * radius / 10, length / 6), new Vector2((i + 1) * radius / 10, length / 6) });
                p.triangles.AddRange(new[] { at, at + 1, at + 2, at + 2, at + 1, at + 3 });
                Cap(a, radius, -1); Cap(b, endRadius, 1);
                void Cap(Vector3 center, float r, int sign) {
                    int k = p.vertices.Count;
                    p.vertices.AddRange(new[] { center, center + q * new Vector3(Mathf.Cos(t) * r, Mathf.Sin(t) * r, 0), center + q * new Vector3(Mathf.Cos(next) * r, Mathf.Sin(next) * r, 0) });
                    for (int j = 0; j < 3; j++) { p.normals.Add(q * Vector3.forward * sign); p.uv.Add(Vector2.zero); }
                    if (sign > 0) p.triangles.AddRange(new[] { k, k + 1, k + 2 }); else p.triangles.AddRange(new[] { k, k + 2, k + 1 });
                }
            }
        }
        void Module(Vector3 a, Vector3 b, float radius, bool windows = true) {
            var direction = (b - a).normalized;
            Cylinder(0, a, b, radius);
            Cylinder(1, a - direction * radius * .35f, a + direction * .9f, radius * .7f, radius * 1.025f);
            Cylinder(1, b - direction * .9f, b + direction * radius * .35f, radius * 1.025f, radius * .7f);
            for (int i = 1; i < 5; i++) {
                var point = Vector3.Lerp(a, b, i / 5f);
                Cylinder(1, point - direction * .22f, point + direction * .22f, radius * 1.025f);
                if (windows) {
                    var q = Quaternion.LookRotation(direction);
                    for (int side = 0; side < 8; side++) {
                        var radial = q * (Quaternion.AngleAxis(side * 45, Vector3.forward) * Vector3.up);
                        Box(3, point + radial * (radius * 1.015f), new Vector3(radius * .35f, .18f, 1.5f), Quaternion.LookRotation(direction, radial));
                    }
                }
            }
        }
        void Panel(Vector3 center, float width, float height, Quaternion q) {
            Box(1, center, new Vector3(width + .8f, height + .8f, .65f), q);
            Box(2, center + q * Vector3.forward * .36f, new Vector3(width, height, .16f), q);
            Box(2, center - q * Vector3.forward * .36f, new Vector3(width, height, .16f), q);
            // Solar cells remain readable on both faces, even when viewed from behind in a loop.
            for (int x = 1; x < 5; x++) Box(1, center + q * new Vector3(-width / 2 + width * x / 5, 0, 0), new Vector3(.16f, height, .88f), q);
            for (int y = 1; y < 12; y++) Box(1, center + q * new Vector3(0, -height / 2 + height * y / 12, 0), new Vector3(width, .12f, .88f), q);
        }
        void Truss(Vector3 a, Vector3 b, float half) {
            var q = Quaternion.LookRotation(b - a); float length = Vector3.Distance(a, b);
            for (int side = 0; side < 4; side++) {
                var corner = new Vector3((side % 2 == 0 ? -1 : 1) * half, (side < 2 ? -1 : 1) * half, 0);
                Beam(1, a + q * corner, b + q * corner, .6f);
            }
            int count = Mathf.CeilToInt(length / (half * 3));
            for (int i = 0; i < count; i++) for (int side = 0; side < 4; side++) {
                var p0 = a + (b - a) * (i / (float)count); var p1 = a + (b - a) * ((i + 1) / (float)count);
                var u = side < 2 ? Vector3.right : Vector3.up; var v = side < 2 ? Vector3.up : Vector3.right;
                float sign = side % 2 == 0 ? -1 : 1;
                Beam(1, p0 + q * (u * half * sign - v * half), p1 + q * (u * half * sign + v * half), .38f);
            }
        }
        void Dock(Vector3 center, Vector3 direction) {
            var end = center + direction * 17;
            Cylinder(0, center, end, 3.6f); Cylinder(1, end, end + direction * 2, 5.5f);
            Cylinder(4, end + direction * 2.02f, end + direction * 2.1f, 3.8f);
        }
        void Antenna(Vector3 center) {
            Beam(1, center, center + Vector3.up * 18, .5f);
            Beam(1, center + Vector3.up * 12 - Vector3.right * 6, center + Vector3.up * 12 + Vector3.right * 6, .35f);
            Cylinder(0, center + Vector3.up * 18, center + Vector3.up * 22, 1, 6, 20);
            Beam(1, center + Vector3.up * 20, center + Vector3.up * 27, .3f);
        }
        public IEnumerator Build(SpaceSceneryPlan.Kind kind, int variant) {
            if (kind == SpaceSceneryPlan.Kind.Ring) {
                // Broad rectangular habitat ring; repeated modules, windows and ribs define human scale.
                const int segments = 96;
                for (int i = 0; i < segments; i++) {
                    float angle = i * 360f / segments; var q = Quaternion.AngleAxis(-angle, Vector3.forward);
                    var radial = q * Vector3.up; var tangent = q * Vector3.right;
                    Box(0, radial * 80, new Vector3(5.5f, 11, 15), q);
                    Box(1, radial * 86, new Vector3(5.6f, .65f, 16), q);
                    Box(1, radial * 74, new Vector3(5.6f, .5f, 15), q);
                    for (int window = -1; window <= 1; window++) {
                        Box(3, radial * 80 + tangent * (window * 1.55f) + Vector3.forward * 7.62f, new Vector3(.8f, 2, .18f), q);
                        Box(3, radial * 80 + tangent * (window * 1.55f) - Vector3.forward * 7.62f, new Vector3(.8f, 2, .18f), q);
                    }
                    if (i % 4 == 0) Box(1, radial * 80, new Vector3(.5f, 11.8f, 15.8f), q);
                    if (i % 12 == 0) { yield return null; Truss(Vector3.zero, radial * 74, 1.5f); Module(radial * 87, radial * 96, 2.7f); }
                }
                Module(new Vector3(0, 0, -44), new Vector3(0, 0, 44), 9);
                for (int side = -1; side <= 1; side += 2) {
                    Dock(new Vector3(0, 0, side * 46), Vector3.forward * side);
                    for (int i = -1; i <= 1; i += 2) {
                        Beam(1, new Vector3(0, 0, side * 32), new Vector3(i * 36, 0, side * 32), 1.5f);
                        Panel(new Vector3(i * 30, 0, side * 32), 20, 37, Quaternion.identity);
                    }
                }
                Antenna(new Vector3(0, 10, 0));
            } else if (kind == SpaceSceneryPlan.Kind.Shipyard) {
                Truss(new Vector3(-100, 0, 0), new Vector3(100, 0, 0), 4);
                for (int i = -2; i <= 2; i++) {
                    float x = i * 38;
                    Module(new Vector3(x - 13, 0, 0), new Vector3(x + 13, 0, 0), 8);
                    for (int side = -1; side <= 1; side += 2) {
                        Truss(new Vector3(x, 0, side * 7), new Vector3(x, 0, side * 43), 1.7f);
                        Truss(new Vector3(x, -10, side * 45), new Vector3(x, 28, side * 45), 1.7f);
                        Truss(new Vector3(x, 28, side * 45), new Vector3(x, 28, side * 14), 1.7f);
                        Box(4, new Vector3(x, 27, side * 31), new Vector3(1.5f, .5f, 10));
                        Dock(new Vector3(x, 0, side * 15), Vector3.forward * side);
                        if (i % 2 == 0) Panel(new Vector3(x, -23, side * 20), 26, 21, Quaternion.Euler(55, 0, 0));
                    }
                    yield return null;
                }
                Antenna(new Vector3(-80, 8, 0)); Antenna(new Vector3(80, 8, 0));
            } else {
                bool large = kind == SpaceSceneryPlan.Kind.Modular;
                Truss(new Vector3(-65, 0, 0), new Vector3(65, 0, 0), 2);
                Module(new Vector3(-28, 0, 0), new Vector3(28, 0, 0), 7);
                Module(new Vector3(0, -15, 0), new Vector3(0, 15, 0), 6);
                for (int side = -1; side <= 1; side += 2) {
                    Module(new Vector3(side * 30, 0, 0), new Vector3(side * 54, 0, 0), 6);
                    Dock(new Vector3(side * 55, 0, 0), Vector3.right * side);
                    for (int offset = -1; offset <= 1; offset += 2) {
                        Beam(1, new Vector3(side * 42, 0, 0), new Vector3(side * 42, offset * 26, 0), .8f);
                        Panel(new Vector3(side * 42, offset * 28, 0), 23, 38, Quaternion.Euler(0, side * 10, 0));
                        if (large) { Module(new Vector3(side * 13, 0, offset * 8), new Vector3(side * 13, 0, offset * 35), 5); Dock(new Vector3(side * 13, 0, offset * 36), Vector3.forward * offset); }
                    }
                    yield return null;
                }
                Antenna(new Vector3(0, 16, 0));
                // Habitation/observation cupola.
                Cylinder(4, new Vector3(0, -20, 0), new Vector3(0, -24, 0), 6, 3);
            }
            float radius = 0;
            foreach (var part in parts) foreach (var vertex in part.vertices) radius = Mathf.Max(radius, vertex.magnitude);
            foreach (var part in parts) for (int i = 0; i < part.vertices.Count; i++) part.vertices[i] /= radius * 1.002f;
        }
        public Mesh Mesh(int slot) {
            var p = parts[slot]; var mesh = new Mesh { name = "Space station material " + slot, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(p.vertices); mesh.SetNormals(p.normals); mesh.SetUVs(0, p.uv); mesh.SetTriangles(p.triangles, 0); mesh.RecalculateBounds(); return mesh;
        }
    }
}
