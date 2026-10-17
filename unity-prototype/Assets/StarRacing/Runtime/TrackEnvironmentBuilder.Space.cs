using System.Collections;
using UnityEngine;

namespace StarRacingPrototype {
    public sealed partial class TrackEnvironmentBuilder {
        IEnumerator BuildSpaceScenerySteps() {
            if (Plan.space == null) yield break;
            var hull = Lit(new Color(1.18f, 1.23f, 1.30f), .22f, false, "SpaceHull", .27f);
            var trim = Lit(new Color(.33f, .39f, .46f), .4f, false, "SpaceHull", .3f);
            var solar = Lit(new Color(.08f, .20f, .62f), .35f, false, null, .48f);
            var windows = Lit(new Color(1f, .72f, .35f), .1f, true, null);
            var glass = Lit(new Color(.12f, .52f, .69f), .5f, false, null, .6f);
            // A restrained earthshine floor preserves shaded hard-surface silhouettes in the black sky.
            hull.EnableKeyword("_EMISSION"); hull.SetColor("_EmissionColor", new Color(.14f, .16f, .20f));
            trim.EnableKeyword("_EMISSION"); trim.SetColor("_EmissionColor", new Color(.035f, .045f, .065f));
            solar.EnableKeyword("_EMISSION"); solar.SetColor("_EmissionColor", new Color(.015f, .045f, .14f));
            foreach (var metal in new[] { hull, trim }) {
                metal.EnableKeyword("_NORMALMAP");
                metal.SetTexture("_BumpMap", SharedRaceAssets.Load<Texture2D>("Environment/Textures/SpaceHullNormal"));
                metal.SetFloat("_BumpScale", .45f);
            }
            var palette = new[] { hull, trim, solar, windows, glass };
            var shader = SharedRaceAssets.Load<Shader>("Environment/SpaceRock");
            if (shader == null) throw new System.InvalidOperationException("Missing SpaceRock shader");
            var rock = new Material(shader) { name = "Cratered asteroid stone" }; materials.Add(rock);
            rock.SetTexture("_BaseMap", SharedRaceAssets.Load<Texture2D>("Environment/Textures/SpaceRock"));
            rock.SetTexture("_BumpMap", SharedRaceAssets.Load<Texture2D>("Environment/Textures/SpaceRockNormal"));
            var rocks = new Mesh[12];
            var budget = new LoadingWork.Budget();
            foreach (var item in Plan.space.items) {
                if (budget.Due) { yield return null; budget.Restart(); }
                var group = new GameObject("Space " + item.kind); group.transform.SetParent(root.transform, false);
                group.transform.SetPositionAndRotation(item.position, item.rotation); group.transform.localScale = Vector3.one * item.size;
                if (item.kind == SpaceSceneryPlan.Kind.Asteroid) {
                    if (rocks[item.variant] == null) rocks[item.variant] = AsteroidMesh(item.variant);
                    Add(rocks[item.variant], rock);
                } else {
                    var model = new SpaceModelGeometry(cube);
                    yield return model.Build(item.kind, item.variant);
                    for (int material = 0; material < palette.Length; material++) {
                        if (model.parts[material].vertices.Count == 0) continue;
                        var mesh = model.Mesh(material); meshes.Add(mesh); Add(mesh, palette[material]);
                        yield return null;
                    }
                }
                void Add(Mesh mesh, Material material) {
                    var part = new GameObject(material.name); part.transform.SetParent(group.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }
        Mesh AsteroidMesh(int variant) {
            const int latitude = 28, longitude = 44;
            var random = new Procedural.SeedRandom((uint)variant ^ 0xc2a7e8u);
            var craters = new Vector3[14]; var radii = new float[craters.Length];
            for (int i = 0; i < craters.Length; i++) { craters[i] = new Vector3((float)random.Range(-1, 1), (float)random.Range(-1, 1), (float)random.Range(-1, 1)).normalized; radii[i] = (float)random.Range(.16, .4); }
            var vertices = new Vector3[(latitude + 1) * (longitude + 1)]; var uv = new Vector2[vertices.Length]; var triangles = new int[latitude * longitude * 6];
            float offset = variant * 7.371f + 11;
            for (int y = 0; y <= latitude; y++) for (int x = 0; x <= longitude; x++) {
                float theta = y * Mathf.PI / latitude, phi = x * Mathf.PI * 2 / longitude;
                var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                float coarse = Mathf.PerlinNoise(n.x * 2 + offset, n.y * 2 + n.z * .8f + offset);
                float medium = Mathf.PerlinNoise(n.z * 6 + offset, n.x * 6 + n.y * 2 + offset);
                float radius = .71f + coarse * .23f + (medium - .5f) * .10f;
                for (int index = 0; index < craters.Length; index++) {
                    float distance = Vector3.Distance(n, craters[index]) / radii[index];
                    if (distance < 1) radius -= .13f * (1 - distance * distance);
                    if (distance > .78f && distance < 1.18f) radius += .025f * Mathf.Sin((distance - .78f) / .4f * Mathf.PI);
                }
                // Broad planar fractures interrupt the old rounded silhouette without more tessellation.
                for (int cut = 0; cut < 5; cut++) {
                    var plane = craters[cut]; float facing = Vector3.Dot(n, plane);
                    if (facing > .48f) radius = Mathf.Min(radius, (.58f + cut * .025f) / facing);
                }
                var p = Vector3.Scale(n * radius, new Vector3(1, .73f + variant % 3 * .07f, .79f + variant % 4 * .05f));
                int at = y * (longitude + 1) + x; vertices[at] = p; uv[at] = new Vector2(x / (float)longitude, y / (float)latitude);
            }
            int t = 0;
            for (int y = 0; y < latitude; y++) for (int x = 0; x < longitude; x++) {
                int a = y * (longitude + 1) + x, b = a + longitude + 1;
                triangles[t++] = a; triangles[t++] = a + 1; triangles[t++] = b;
                triangles[t++] = a + 1; triangles[t++] = b + 1; triangles[t++] = b;
            }
            // Normalize every variant to a proven unit envelope; arbitrary placement rotation remains safe.
            float max = 0; foreach (var vertex in vertices) max = Mathf.Max(max, vertex.magnitude);
            for (int i = 0; i < vertices.Length; i++) vertices[i] /= max * 1.002f;
            var mesh = new Mesh { name = "Cratered asteroid " + variant, vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh); return mesh;
        }
    }
}
