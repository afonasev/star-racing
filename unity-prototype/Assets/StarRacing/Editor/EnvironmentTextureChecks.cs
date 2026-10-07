using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StarRacingPrototype {
    public static class EnvironmentTextureChecks {
        static void Check(bool valid, string message) {
            if (!valid) throw new Exception("ENVIRONMENT_TEXTURE: " + message);
        }

        public static void BuildCheckedMac() { Run(); PrototypeBuilder.BuildMac(); }

        public static void Run() {
            foreach (var name in new[] { "Road", "Hull", "Facade", "Planet" }) {
                var texture = Resources.Load<Texture2D>("Environment/Textures/" + name);
                Check(texture != null, "missing " + name);
                Check(texture.width <= 1024 && texture.height <= 1024 && texture.mipmapCount > 1,
                    "texture budget/mips " + name);
                Check(texture.wrapMode == TextureWrapMode.Repeat && texture.anisoLevel == 8 && !texture.isReadable,
                    "import settings " + name);
            }
            foreach (var theme in new[] { "cloud-city", "space-station" }) {
                foreach (uint seed in new uint[] { 77, 78 }) {
                    var root = new GameObject("texture-check");
                    var environment = new TrackEnvironmentBuilder();
                    try {
                        var track = root.AddComponent<TrackBuilder>();
                        track.Build(new TrackRoute(Procedural.Generator.Generate(seed, "full", theme, true, true)));
                        var surface = root.GetComponentInChildren<TrackSurface>();
                        var physical = surface.GetComponent<MeshCollider>().sharedMesh;
                        var vertices = physical.vertices; var triangles = physical.triangles;
                        var normals = physical.normals;
                        var originalRenderer = surface.GetComponent<MeshRenderer>();
                        for (int cycle = 0; cycle < 2; cycle++) {
                            environment.Build(track.Route, root.transform);
                            Check(surface.GetComponent<MeshCollider>().sharedMesh == physical &&
                                physical.vertices.SequenceEqual(vertices) && physical.triangles.SequenceEqual(triangles) &&
                                physical.normals.SequenceEqual(normals), "collision data changed");
                            var renderRoad = environment.Root.GetComponentsInChildren<MeshFilter>()
                                .Single(f => f.sharedMesh.name == "Environment road visual").sharedMesh;
                            Check(renderRoad.vertices.SequenceEqual(vertices), "visual/collision vertices differ");
                            var renderedTriangles = Enumerable.Range(0, renderRoad.subMeshCount)
                                .SelectMany(i => renderRoad.GetTriangles(i)).ToArray();
                            Check(renderedTriangles.Length == triangles.Length &&
                                renderedTriangles.OrderBy(i => i).SequenceEqual(triangles.OrderBy(i => i)),
                                "visual/collision triangle inventory differs");
                            var uv = renderRoad.uv;
                            Check(uv.Length == vertices.Length, "road has no complete UV map");
                            foreach (var face in surface.triangleFaces) {
                                int at = face.quad * 4;
                                Check(Mathf.Abs((uv[at + 1].y - uv[at].y) * 8f -
                                    (face.endDistance - face.startDistance)) < .003f, "road longitudinal scale");
                                Check(Mathf.Abs((uv[at + 2].x - uv[at].x) * 8f -
                                    (face.rightStart - face.leftStart)) < .003f, "road lateral scale");
                            }
                            foreach (var renderer in environment.Root.GetComponentsInChildren<MeshRenderer>()) {
                                foreach (var material in renderer.sharedMaterials) {
                                    Check(material != null && material.shader.name == "Universal Render Pipeline/Lit", "bad material");
                                    if (material.mainTexture == null) continue;
                                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                                    Check(mesh.uv.Length == mesh.vertexCount && mesh.uv.Any(p => p != mesh.uv[0]),
                                        "unmapped textured mesh: " + renderer.name);
                                }
                            }
                            var obsolete = environment.Root.gameObject;
                            environment.Clear();
                            Check(obsolete == null && environment.Root == null && originalRenderer.enabled,
                                "clear does not restore road/dispose environment");
                        }
                        Debug.Log("ENVIRONMENT_TEXTURE_THEME_OK " + theme + " seed=" + seed);
                    }
                    finally { environment.Clear(); UnityEngine.Object.DestroyImmediate(root); }
                }
            }
            StrideVisualChecks.Run();
            Debug.Log("ENVIRONMENT_TEXTURE_CHECKS_OK themes=2 seeds=2 rebuilds=2 collision=unchanged STRIDE=unchanged");
        }
    }
}
