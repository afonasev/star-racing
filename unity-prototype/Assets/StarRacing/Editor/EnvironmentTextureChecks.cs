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
            foreach (var name in new[] { "Road", "Hull", "Facade", "RoadCeramic", "RoadPanels", "RoadRibbed", "RailCeramic", "RailPanels", "RailRibbed", "RoadBlend", "RailBlend" }) {
                var texture = Resources.Load<Texture2D>("Environment/Textures/" + name);
                Check(texture != null, "missing " + name);
                bool atlas = name == "RoadBlend" || name == "RailBlend";
                bool withinBudget = name == "RoadBlend" ? texture.width == 1024 && texture.height == 8192 :
                    name == "RailBlend" ? texture.width == 8192 && texture.height == 256 :
                    texture.width <= 1024 && texture.height <= 1024;
                Check(withinBudget && texture.mipmapCount > 1, "texture budget/mips " + name);
                // 8192 texels / 288 m: seams have several texels, unlike the old 3.6 texels/m atlas.
                if (atlas) Check(Mathf.Max(texture.width, texture.height) / 288f >= 28f,
                    "longitudinal texel density " + name);
                Check(texture.wrapMode == TextureWrapMode.Repeat && texture.anisoLevel == (atlas ? 16 : 8) && !texture.isReadable,
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
                        var railSource = root.GetComponentsInChildren<MeshFilter>().Single(f => f.sharedMesh.name == "Procedural guard rails");
                        var railPhysical = railSource.GetComponent<MeshCollider>().sharedMesh;
                        var railVertices = railPhysical.vertices; var railTriangles = railPhysical.triangles;
                        var originalRailRenderer = railSource.GetComponent<MeshRenderer>();
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
                            var renderRail = environment.Root.GetComponentsInChildren<MeshFilter>().Single(f => f.sharedMesh.name == "Environment rail visual");
                            Check(railSource.GetComponent<MeshCollider>().sharedMesh == railPhysical &&
                                railPhysical.vertices.SequenceEqual(railVertices) && railPhysical.triangles.SequenceEqual(railTriangles), "rail collider changed");
                            Check(renderRail.sharedMesh.vertices.SequenceEqual(railVertices) && renderRail.sharedMesh.triangles.SequenceEqual(railTriangles), "rail visual topology changed");
                            Check(!originalRenderer.enabled && !originalRailRenderer.enabled, "duplicate original renderer");
                            Check(renderRail.GetComponent<Collider>() == null, "visual rail has collider");
                            var railMaterial = renderRail.GetComponent<Renderer>().sharedMaterial;
                            var roadMaterial = environment.Root.GetComponentsInChildren<MeshRenderer>()
                                .Single(r => r.GetComponent<MeshFilter>().sharedMesh == renderRoad).sharedMaterial;
                            Check(railMaterial.mainTexture.name == "RailBlend", "rail blend not bound");
                            Check(roadMaterial.mainTexture.name == "RoadBlend" &&
                                roadMaterial != railMaterial && roadMaterial.mainTexture != railMaterial.mainTexture,
                                "road and wall must use independent materials/textures");
                            Check(renderRoad.subMeshCount == 1, "road draw pass proliferation");
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
                                    if (renderer.name == "Cloud sea" || renderer.name == "Tower cloud mist") {
                                        Check(material != null && material.shader.name == (renderer.name == "Cloud sea" ? "StarRacing/CloudDeck" : "StarRacing/CloudMist"), "bad cloud material");
                                        Check(renderer.GetComponent<Collider>() == null, "cloud collider");
                                        continue;
                                    }
                                    Check(material != null && material.shader.name == "Universal Render Pipeline/Lit", "bad material");
                                    if (material.mainTexture == null) continue;
                                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                                    Check(mesh.uv.Length == mesh.vertexCount && mesh.uv.Any(p => p != mesh.uv[0]),
                                        "unmapped textured mesh: " + renderer.name);
                                }
                            }
                            var obsolete = environment.Root.gameObject;
                            environment.Clear();
                            Check(obsolete == null && environment.Root == null && originalRenderer.enabled && originalRailRenderer.enabled,
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
