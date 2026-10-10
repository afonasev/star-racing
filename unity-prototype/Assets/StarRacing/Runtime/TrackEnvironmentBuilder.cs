using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype {
    // Owns render resources only; route and physical road are never modified.
    public sealed class TrackEnvironmentBuilder {
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> materials = new List<Material>();
        readonly Dictionary<Vector4, Mesh> mappedCubes = new Dictionary<Vector4, Mesh>();
        GameObject root;
        Mesh cube, sphere;
        MeshRenderer roadRenderer;
        bool roadRendererOriginalEnabled;
        public EnvironmentPlan Plan { get; private set; }
        public int LampCount { get; private set; }
        public struct LampPlacement { public float distance; public int side; public Vector3 basePoint; }
        public readonly List<LampPlacement> Lamps = new List<LampPlacement>();
        public TrackSky Sky { get; private set; }
        public Transform Root => root == null ? null : root.transform;

        public void Build(TrackRoute route, Transform roadRoot, uint? skySeed = null) {
            Clear();
            Plan = EnvironmentPlan.Create(route, skySeed);
            root = new GameObject("Track environment " + Plan.theme);
            cube = CreateCube(); sphere = CreateSphere(12, 16);
            BuildRoadDistricts(route, roadRoot);
            if (Plan.theme == "cloud-city") BuildCity(route);
            else BuildSpace(route);
            Sky = new TrackSky(Plan, route, root.transform);

        }

        public void Clear() {
            Sky?.Clear(); Sky = null;
            if (roadRenderer != null) roadRenderer.enabled = roadRendererOriginalEnabled;
            roadRenderer = null;
            roadRendererOriginalEnabled = false;
            if (root != null) { root.SetActive(false); Dispose(root); root = null; }
            foreach (var mesh in meshes) Dispose(mesh);
            foreach (var material in materials) Dispose(material);
            meshes.Clear(); materials.Clear(); mappedCubes.Clear(); cube = sphere = null; Plan = null; LampCount = 0; Lamps.Clear();
        }

        static void Dispose(Object item) {
            if (item == null) return;
            if (Application.isPlaying) Object.Destroy(item); else Object.DestroyImmediate(item);
        }

        Material Lit(Color color, float metallic = .1f, bool glow = false, string texture = "Hull", float smoothness = .35f) {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (!glow && texture != null) {
                material.mainTexture = Resources.Load<Texture2D>("Environment/Textures/" + texture);
                if (material.mainTexture == null) Debug.LogError("Missing environment texture: " + texture);
            }
            if (glow) {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 1.4f);
            }
            materials.Add(material); return material;
        }

        GameObject Shape(string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale, bool shadows = true) {
            if (mesh == cube && material.mainTexture != null) {
                float tile = material.mainTexture.name == "Facade" ? 12f : 4f;
                var key = new Vector4(scale.x, scale.y, scale.z, tile);
                if (!mappedCubes.TryGetValue(key, out var mapped)) {
                    mapped = Object.Instantiate(cube); mapped.name = "Mapped environment cube";
                    var uv = new Vector2[mapped.vertexCount];
                    var vertices = mapped.vertices; var normals = mapped.normals;
                    for (int i = 0; i < uv.Length; i++) uv[i] = CubeUv(vertices[i], normals[i], scale, tile);
                    mapped.uv = uv; mappedCubes.Add(key, mapped); meshes.Add(mapped);
                }
                mesh = mapped;
            }
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            if (mesh == sphere && material.mainTexture != null && material.mainTexture.name == "Hull") {
                var block = new MaterialPropertyBlock();
                block.SetVector("_BaseMap_ST", new Vector4(Mathf.Max(1, Mathf.Round(scale.x * Mathf.PI / 8f)),
                    Mathf.Max(1, Mathf.Round(scale.y * Mathf.PI / 16f)), 0, 0));
                renderer.SetPropertyBlock(block);
            }
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        void BuildRoadDistricts(TrackRoute route, Transform roadRoot) {
            MeshFilter sourceFilter = null;
            foreach (var filter in roadRoot.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null && filter.sharedMesh.name == "Procedural road") { sourceFilter = filter; break; }
            if (sourceFilter == null) return;
            var source = sourceFilter.sharedMesh;
            var surface = sourceFilter.GetComponent<TrackSurface>();
            if (surface == null || surface.triangleSamples.Length != source.triangles.Length / 3) return;
            var groups = new[] { new List<int>(), new List<int>(), new List<int>(),
                                 new List<int>(), new List<int>(), new List<int>() };
            var triangles = source.triangles;
            for (int face = 0; face < surface.triangleSamples.Length; face++) {
                int sample = Mathf.Clamp(surface.triangleSamples[face], 0, route.Samples.Length - 1);
                float distance = route.Samples[sample].distance;
                int district = Plan.District(distance);
                // A darker 5 m band every 20 m gives broad panel seams without coplanar decals.
                int material = district * 2 + (Mathf.FloorToInt(distance / 5f) % 4 == 0 ? 1 : 0);
                groups[material].Add(triangles[face * 3]);
                groups[material].Add(triangles[face * 3 + 1]);
                groups[material].Add(triangles[face * 3 + 2]);
            }
            var visual = new Mesh { name = "Environment road visual", indexFormat = source.indexFormat };
            visual.vertices = source.vertices;
            visual.normals = source.normals;
            // Render-only mapping uses authored face distances and lateral edges, including ramps
            // and branches. The original collision mesh and its metadata remain untouched.
            var uv = new Vector2[source.vertexCount];
            foreach (var face in surface.triangleFaces) {
                int at = face.quad * 4;
                uv[at] = new Vector2(face.leftStart / 8f, face.startDistance / 8f);
                uv[at + 1] = new Vector2(face.leftEnd / 8f, face.endDistance / 8f);
                uv[at + 2] = new Vector2(face.rightStart / 8f, face.startDistance / 8f);
                uv[at + 3] = new Vector2(face.rightEnd / 8f, face.endDistance / 8f);
            }
            visual.uv = uv;
            visual.subMeshCount = groups.Length;
            for (int i = 0; i < groups.Length; i++) visual.SetTriangles(groups[i], i);
            visual.RecalculateBounds(); meshes.Add(visual);
            bool space = Plan.theme == "space-station";
            Material[] palette = space
                ? new[] { Lit(new Color(.24f, .34f, .44f), .65f), Lit(new Color(.16f, .25f, .33f), .65f),
                          Lit(new Color(.36f, .45f, .51f), .65f), Lit(new Color(.24f, .32f, .38f), .65f),
                          Lit(new Color(.095f, .13f, .2f), .55f), Lit(new Color(.055f, .085f, .14f), .55f) }
                : new[] { Lit(new Color(.52f, .63f, .72f), .45f), Lit(new Color(.39f, .51f, .62f), .45f),
                          Lit(new Color(.81f, .86f, .88f), .38f), Lit(new Color(.65f, .72f, .76f), .38f),
                          Lit(new Color(.19f, .26f, .34f), .45f), Lit(new Color(.12f, .17f, .23f), .45f) };
            foreach (var material in palette) {
                material.mainTexture = Resources.Load<Texture2D>("Environment/Textures/Road");
                material.SetFloat("_Smoothness", .22f);
                // Neutral albedo supplies the detail; district tint keeps the existing themes readable.
                Color tint = material.color;
                tint = Color.Lerp(tint, space ? new Color(.72f, .8f, .88f) : new Color(.91f, .95f, 1f), .65f);
                material.color = tint; material.SetColor("_BaseColor", tint);
            }
            var road = new GameObject("Visual road districts"); road.transform.SetParent(root.transform, false);
            road.AddComponent<MeshFilter>().sharedMesh = visual;
            road.AddComponent<MeshRenderer>().sharedMaterials = palette;
            roadRenderer = sourceFilter.GetComponent<MeshRenderer>();
            if (roadRenderer != null) {
                roadRendererOriginalEnabled = roadRenderer.enabled;
                roadRenderer.enabled = false;
            }
            // The copied render mesh has the exact original vertices and triangles. The collider stays on the original object.
        }

        void BuildCity(TrackRoute route) {
            var silver = Lit(new Color(.85f, .91f, .95f), .48f);
            var facade = Lit(new Color(.86f, .94f, 1f), .35f, false, "Facade", .55f);
            var blue = Lit(new Color(.65f, .8f, .95f), .35f, false, "Facade", .55f);
            var dark = Lit(new Color(.6f, .73f, .84f), .35f, false, "Facade", .55f);
            var teal = Lit(new Color(.12f, .86f, .8f), .05f, true);
            var amber = Lit(new Color(1, .65f, .24f), .05f, true);
            for (int i = 0; i < Plan.towers.Count; i++) {
                var tower = Plan.towers[i];
                var material = i % 3 == 0 ? facade : i % 3 == 1 ? blue : dark;
                Shape("City tower", cube, material, tower.position, Quaternion.identity,
                      new Vector3(tower.width, tower.height, tower.width), i < 10);
                var top = tower.position + Vector3.up * (tower.height * .5f);
                BuildTowerTrim(tower.position, top, tower.width, tower.height, silver);
                if (i % 3 == 0)
                    Shape("Navigation light", cube, tower.amber ? amber : teal, top + Vector3.up * 1.5f,
                          Quaternion.identity, new Vector3(2.2f, 3f, 2.2f), false);
            }
        }

        void BuildTowerTrim(Vector3 center, Vector3 top, float width, float height, Material silver) {
            var vertices = new List<Vector3>(cube.vertexCount * 5);
            var normals = new List<Vector3>(cube.vertexCount * 5);
            var triangles = new List<int>(cube.triangles.Length * 5);
            var uv = new List<Vector2>(cube.vertexCount * 5);
            var sourceVertices = cube.vertices;
            var sourceNormals = cube.normals;
            var sourceTriangles = cube.triangles;
            AddTrimBox(top - Vector3.up * 2 - center, new Vector3(width * 1.12f, 4, width * 1.12f));
            for (int floor = 1; floor <= 4; floor++) {
                float y = Plan.cityBaseY + height * floor / 5f;
                AddTrimBox(new Vector3(0, y - center.y, 0), new Vector3(width * 1.015f, .65f, width * 1.015f));
            }
            var mesh = new Mesh { name = "City tower trim" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); meshes.Add(mesh);
            Shape("City tower trim", mesh, silver, center, Quaternion.identity, Vector3.one, false);

            void AddTrimBox(Vector3 offset, Vector3 scale) {
                int start = vertices.Count;
                for (int i = 0; i < sourceVertices.Length; i++) {
                    vertices.Add(Vector3.Scale(sourceVertices[i], scale) + offset);
                    normals.Add(sourceNormals[i]);
                    uv.Add(CubeUv(sourceVertices[i], sourceNormals[i], scale, 4f));
                }
                for (int i = 0; i < sourceTriangles.Length; i++) triangles.Add(start + sourceTriangles[i]);
            }
        }

        void BuildSpace(TrackRoute route) {
            var hull = Lit(new Color(.51f, .62f, .72f), .72f);
            var shadow = Lit(new Color(.22f, .31f, .44f), .63f);
            var cyan = Lit(new Color(.18f, .86f, 1), .1f, true);
            var gold = Lit(new Color(1, .74f, .3f), .1f, true);
            BuildEdgeRibbons(route, cyan);
            Shape("Station core", sphere, hull, Plan.station, Quaternion.identity, Vector3.one * 80);
            Shape("Station spindle", cube, shadow, Plan.station, Quaternion.Euler(0, 30, 0), new Vector3(130, 18, 30));
            var random = new Procedural.SeedRandom(Plan.seed ^ 0x5aceu);
            for (int i = 0; i < 6; i++) {
                float angle = (i / 6f) * Mathf.PI * 2 + (float)random.Range(-.18, .18);
                var outward = new Vector3(Mathf.Cos(angle), (float)random.Range(-.18, .18), Mathf.Sin(angle));
                Vector3 node = Plan.station + outward * (float)random.Range(80, 130);
                Shape(i % 2 == 0 ? "Satellite" : "Station node", i % 2 == 0 ? sphere : cube,
                      i % 2 == 0 ? hull : shadow, node, Quaternion.identity, Vector3.one * (i % 2 == 0 ? 25 : 19));
                Vector3 link = node - Plan.station;
                Shape("Communication link", cube, cyan, (node + Plan.station) * .5f,
                      Quaternion.FromToRotation(Vector3.up, link), new Vector3(.8f, link.magnitude, .8f), false);
            }
            for (float distance = 0; distance < route.Length; distance += 80f) {
                var spans = route.PavedAt(distance);
                if (route.IsJumpRegion(distance) || spans.Length == 0) continue;
                var frame = route.Evaluate(distance);
                for (int side = -1; side <= 1; side += 2) {
                    // Source lateral increases opposite frame.right. Use the actual outer paved edges,
                    // which can both lie on one world side of the route center inside a displaced branch.
                    Vector3 basePoint = LampBase(route, distance, side);
                    if (!ClearPole(route, basePoint, frame.normal)) continue;
                    var rotation = Quaternion.FromToRotation(Vector3.up, frame.normal);
                    Shape("Road lamp", cube, shadow, basePoint + frame.normal * 3,
                          rotation, new Vector3(.42f, 6, .42f), false);
                    LampCount++;
                    Lamps.Add(new LampPlacement { distance = distance, side = side, basePoint = basePoint });
                    Shape("Road lamp emitter", cube, distance % 240 < 80 ? gold : cyan,
                          basePoint + frame.normal * 6, rotation, new Vector3(1.4f, .7f, 1.4f), false);
                }
            }
        }

        public static Vector3 LampBase(TrackRoute route, float distance, int side) {
            var spans = route.PavedAt(distance);
            if (spans.Length == 0 || (side != -1 && side != 1)) throw new System.ArgumentException("Paved distance and side required");
            float edge = side < 0 ? float.NegativeInfinity : float.PositiveInfinity;
            foreach (var span in spans)
                edge = side < 0 ? Mathf.Max(edge, (float)span.Right) : Mathf.Min(edge, (float)span.Left);
            float allowance = .95f;
            float sourceOffset = side < 0 ? edge + allowance : edge - allowance;
            var frame = route.Evaluate(distance);
            return frame.position - frame.right * sourceOffset;
        }

        public static bool ClearPole(TrackRoute route, Vector3 basePoint, Vector3 normal) {
            Vector3 axis = normal.normalized * 6f;
            float axisLengthSquared = axis.sqrMagnitude;
            foreach (var frame in route.Samples) {
                foreach (var span in route.PavedAt(frame.distance)) {
                    Vector3 laneCenter = frame.position - frame.right * (float)span.offset;
                    float t = Mathf.Clamp01(Vector3.Dot(laneCenter - basePoint, axis) / axisLengthSquared);
                    Vector3 nearest = basePoint + axis * t;
                    float clearance = (float)span.halfWidth + .8f;
                    if ((nearest - laneCenter).sqrMagnitude <= clearance * clearance) return false;
                }
            }
            return true;
        }

        void BuildEdgeRibbons(TrackRoute route, Material material) {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < route.Samples.Length - 1; i++) {
                var a = route.Samples[i]; var b = route.Samples[i + 1];
                if (route.IsJumpRegion(a.distance) || route.IsJumpRegion(b.distance)) continue;
                var spansA = route.PavedAt(a.distance); var spansB = route.PavedAt(b.distance);
                if (spansA.Length == 0 || spansB.Length == 0) continue;
                for (int side = -1; side <= 1; side += 2) {
                    float lateralA = side < 0 ? float.NegativeInfinity : float.PositiveInfinity;
                    float lateralB = lateralA;
                    foreach (var span in spansA)
                        lateralA = side < 0 ? Mathf.Max(lateralA, (float)span.Right) : Mathf.Min(lateralA, (float)span.Left);
                    foreach (var span in spansB)
                        lateralB = side < 0 ? Mathf.Max(lateralB, (float)span.Right) : Mathf.Min(lateralB, (float)span.Left);
                    Vector3 p = a.position - a.right * lateralA + a.normal * .045f;
                    Vector3 q = b.position - b.right * lateralB + b.normal * .045f;
                    Vector3 r = a.position - a.right * (lateralA + side * .24f) + a.normal * .045f;
                    Vector3 s = b.position - b.right * (lateralB + side * .24f) + b.normal * .045f;
                    int at = vertices.Count; vertices.AddRange(new[] { p, q, r, s });
                    if (Vector3.Dot(Vector3.Cross(q - p, r - p), a.normal) >= 0)
                        triangles.AddRange(new[] { at, at + 1, at + 2, at + 2, at + 1, at + 3 });
                    else triangles.AddRange(new[] { at, at + 2, at + 1, at + 2, at + 3, at + 1 });
                }
            }
            var mesh = new Mesh { name = "Space road visual edges", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(mesh);
            Shape("Emissive road edges", mesh, material, Vector3.zero, Quaternion.identity, Vector3.one, false);
        }

        Mesh CreateCube() {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var triangles = new List<int>();
            var uv = new List<Vector2>();
            Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down };
            foreach (var normal in directions) {
                Vector3 a = Vector3.Cross(normal, Mathf.Abs(normal.y) > .5f ? Vector3.forward : Vector3.up).normalized * .5f;
                Vector3 b = Vector3.Cross(normal, a).normalized * .5f;
                int at = vertices.Count; Vector3 face = normal * .5f;
                vertices.Add(face - a - b); vertices.Add(face + a - b); vertices.Add(face - a + b); vertices.Add(face + a + b);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one });
                triangles.AddRange(new[] { at, at + 1, at + 2, at + 1, at + 3, at + 2 });
            }
            var mesh = new Mesh { name = "Environment cube" }; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            meshes.Add(mesh); return mesh;
        }

        Mesh CreateSphere(int latitudes, int longitudes) {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var uv = new List<Vector2>();
            for (int y = 0; y <= latitudes; y++) {
                float latitude = Mathf.PI * y / latitudes;
                for (int x = 0; x <= longitudes; x++) {
                    float longitude = 2 * Mathf.PI * x / longitudes;
                    vertices.Add(new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude),
                                             Mathf.Sin(latitude) * Mathf.Sin(longitude)) * .5f);
                    uv.Add(new Vector2((float)x / longitudes, 1f - (float)y / latitudes));
                }
            }
            for (int y = 0; y < latitudes; y++) for (int x = 0; x < longitudes; x++) {
                int a = y * (longitudes + 1) + x, b = a + longitudes + 1;
                triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
            var mesh = new Mesh { name = "Environment sphere" }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = vertices[i].normalized;
            mesh.normals = normals;
            meshes.Add(mesh); return mesh;
        }

        static Vector2 CubeUv(Vector3 vertex, Vector3 normal, Vector3 scale, float tile) {
            var horizontal = Vector3.Cross(normal, Mathf.Abs(normal.y) > .5f ? Vector3.forward : Vector3.up).normalized;
            var vertical = Vector3.Cross(normal, horizontal).normalized;
            var point = Vector3.Scale(vertex, scale);
            return new Vector2(Vector3.Dot(point, horizontal), Vector3.Dot(point, vertical)) / tile;
        }
    }
}
