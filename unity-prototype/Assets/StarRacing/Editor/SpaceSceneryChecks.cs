using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StarRacingPrototype {
    public static class SpaceSceneryChecks {
        public static string Output => Environment.GetEnvironmentVariable("STAR_RACING_SPACE_QA_DIR");
        static int assertions;
        static void Check(bool ok, string why) { assertions++; if (!ok) throw new Exception("SPACE_SCENERY: " + why); }
        public static void Run() {
            if (string.IsNullOrEmpty(Output)) throw new Exception("STAR_RACING_SPACE_QA_DIR required");
            Directory.CreateDirectory(Output); assertions = 0;
            var shader = Resources.Load<Shader>("Environment/SpaceRock");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "rock shader compiles");
            // Baseline core/planet is protected independently from the new complete presentation identity.
            foreach (uint seed in new uint[] { 77, 78, 79, 139, 300, 997 }) {
                var route = new TrackRoute(Procedural.Generator.Generate(seed, "full", "space-station", true, true));
                var plan = EnvironmentPlan.Create(route); var baseline = CloudCityBaseline.EnvironmentPlan.Create(route);
                Check(plan.hash == baseline.hash && plan.station == baseline.station && plan.skyPlanetDirection == baseline.skyPlanetDirection && plan.skyPlanetVariant == baseline.skyPlanetVariant, "exact original space identity and planet");
                Plan(plan, EnvironmentPlan.Create(route), SpaceSceneryPlan.LegacyClearance(route));
            }
            foreach (bool transformed in new[] { false, true }) foreach (string mode in new[] { "normal", "full", "none" }) {
                var graph = GraphCommonCatalogChecks.Geometry(mode, true, transformed); var geometry = new GraphEnvironmentGeometry(graph);
                var plan = EnvironmentPlan.Create(geometry, 77, "space-station");
                Check(plan.hash == CloudCityBaseline.EnvironmentPlan.Create(geometry, 77, "space-station").hash, "exact graph core identity");
                Plan(plan, EnvironmentPlan.Create(geometry, 77, "space-station"), geometry.Clear);
                Check(graph.Validate().Length == 0, "graph topology unchanged");
            }
            var cityRoute = new TrackRoute(Procedural.Generator.Generate(77, "normal", "cloud-city", false, true));
            var city = EnvironmentPlan.Create(cityRoute); Check(city.space == null && city.presentationHash == city.hash, "city identity has no space component");
            // Sparse fixture proves continuous cubic hull clearance, including the middle between samples.
            var legacy = new TrackRoute(Procedural.Generator.Generate(77, "full", "space-station", true, true));
            var clear = SpaceSceneryPlan.LegacyClearance(legacy);
            for (int i = 0; i + 1 < legacy.Samples.Length; i += 31) {
                var f = legacy.Evaluate((legacy.Samples[i].distance + legacy.Samples[i + 1].distance) / 2);
                Check(!clear(f.position, Vector3.one, 0), "interpolated road excluded");
                foreach (var paved in legacy.PavedAt(f.distance)) Check(!clear(f.position - f.right * (float)paved.offset, Vector3.one, EnvironmentPlan.CorridorMargin), "every branch excluded");
            }
            Build(false); Build(true);
            Additional();
            SkyboxChecks.Run(); DaylightVisualChecks.Run();
            File.WriteAllText(Path.Combine(Output, "contracts.json"), "{\"success\":true,\"assertions\":" + assertions + ",\"coreBaselineExact\":true,\"legacyAndNative12\":true,\"playerBuild\":false}");
            Debug.Log("SPACE_SCENERY_CHECKS_OK assertions=" + assertions);
        }
        // Focused detail regression: placement baseline, actual UV/tangents/imports, rebuild and cleanup.
        public static void Detail() {
            Directory.CreateDirectory(Output); assertions = 0;
            var shader = Resources.Load<Shader>("Environment/SpaceRock");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "detailed stone shader compiles");
            foreach (string name in new[] { "SpaceHull", "SpaceHullNormal", "SpaceRock", "SpaceRockNormal" }) {
                string path = "Assets/StarRacing/Resources/Environment/Textures/" + name + ".png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = name.EndsWith("Normal"); var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Debug.Log($"SPACE_IMPORT {name} size={texture?.width}x{texture?.height} format={texture?.format}");
                Check(texture != null && texture.width > 1024 && texture.width <= 2048, "preserved source detail " + name);
                Check(importer.maxTextureSize == 2048 && importer.anisoLevel == 8 && importer.mipmapEnabled && !importer.isReadable, "scoped texture budget " + name);
                Check(importer.convertToNormalmap == normal && importer.sRGBTexture == !normal && (importer.textureType == TextureImporterType.NormalMap) == normal, "normal conversion and color space " + name);
                Check(importer.wrapMode == TextureWrapMode.Repeat && importer.filterMode == FilterMode.Trilinear, "filtered repeat " + name);
            }
            foreach (uint seed in new uint[] { 77, 78, 79, 139, 300, 997 }) {
                var route = new TrackRoute(Procedural.Generator.Generate(seed, "full", "space-station", true, true));
                var plan = EnvironmentPlan.Create(route); var clear = SpaceSceneryPlan.LegacyClearance(route);
                Compare(plan.space, new SpaceSceneryBaselineV1(plan.seed, route.Samples, plan.station, clear));
                Plan(plan, EnvironmentPlan.Create(route), clear);
                Check(plan.hash == CloudCityBaseline.EnvironmentPlan.Create(route).hash, "legacy core unchanged");
            }
            foreach (bool transformed in new[] { false, true }) foreach (string mode in new[] { "normal", "full", "none" }) {
                var geometry = new GraphEnvironmentGeometry(GraphCommonCatalogChecks.Geometry(mode, true, transformed));
                var plan = EnvironmentPlan.Create(geometry, 77, "space-station");
                Compare(plan.space, new SpaceSceneryBaselineV1(77, geometry.Samples, plan.station, geometry.Clear));
                Plan(plan, EnvironmentPlan.Create(geometry, 77, "space-station"), geometry.Clear);
                Check(plan.hash == CloudCityBaseline.EnvironmentPlan.Create(geometry,77,"space-station").hash, "graph core unchanged");
            }
            Build(false); Build(true);
            File.WriteAllText(Path.Combine(Output,"detail-contracts.json"), "{\"success\":true,\"assertions\":"+assertions+",\"placementBaseline\":\"5b54604a\",\"legacyAndNative12\":true,\"uvTangents\":true,\"textureImport\":true,\"playerBuild\":false}");
            Debug.Log("SPACE_SCENERY_DETAIL_OK assertions="+assertions);
        }
        static void DetailSurface(Mesh mesh, SpaceSceneryPlan.Kind kind) {
            var vertices=mesh.vertices;var uv=mesh.uv;var triangles=mesh.triangles;
            Check(mesh.tangents.Length==vertices.Length,"complete tangent basis");
            foreach(var t in mesh.tangents) Check(!float.IsNaN(t.x+t.y+t.z+t.w) && new Vector3(t.x,t.y,t.z).sqrMagnitude>.9f,"finite normalized tangents");
            for(int i=0;i<triangles.Length;i+=3) {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<1e-18f)continue;
                var u=uv[b]-uv[a];var v=uv[c]-uv[a];
                Check(Mathf.Abs(u.x*v.y-u.y*v.x)>1e-12f,"non-degenerate UV on every surface "+kind+"/"+mesh.name);
            }
        }
        static void Compare(SpaceSceneryPlan plan, SpaceSceneryBaselineV1 baseline) {
            Check(plan.items.Count == baseline.items.Count && plan.bounds == baseline.bounds, "placement count and bounds exact");
            Check(plan.hash != baseline.hash, "detail version changes presentation identity");
            for (int i=0;i<plan.items.Count;i++) {
                var a=plan.items[i];var b=baseline.items[i];
                Check((int)a.kind==(int)b.kind && a.position.Equals(b.position) && a.rotation.Equals(b.rotation) && a.size==b.size && a.variant==b.variant, "every item transform and variant exact");
            }
        }
        public static void Additional() {
            Directory.CreateDirectory(Output);
            foreach (uint seed in new uint[] { 77, 78, 79, 139, 300, 997 }) {
                var route = new TrackRoute(Procedural.Generator.Generate(seed, "full", "space-station", true, true));
                var plan = EnvironmentPlan.Create(route);
                Check(plan.space.items.Any(x => x.kind != SpaceSceneryPlan.Kind.Asteroid && Vector3.Distance(x.position, plan.station) < .01f), "approved nearby station actually instantiated");
            }
            Cancel(false); Cancel(true);
            // Existing graph fixture retains its complete assertions; old space hashes are protected above
            // against the frozen pre-metropolis class, while this baseline records the compatible core layout.
            string graphOutput = Path.Combine(Output, "graph-environment");
            Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_ENV_QA_DIR", graphOutput);
            GraphEnvironmentChecks.CaptureLegacy();
            Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_ENV_BASELINE", Path.Combine(graphOutput, "legacy-environment.txt"));
            GraphEnvironmentChecks.Run();
            File.WriteAllText(Path.Combine(Output, "additional.json"), "{\"success\":true,\"nearStations\":true,\"cancelLegacyAndGraph\":true,\"existingGraphFixture\":true}");
            Debug.Log("SPACE_SCENERY_ADDITIONAL_OK nearStations=true cancellation=true graphFixture=true");
        }
        static void Cancel(bool graphMode) {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Cancellation fixture"); var env = new TrackEnvironmentBuilder();
            try {
                var track = root.AddComponent<TrackBuilder>();
                if (graphMode) track.BuildIndependentCommonCatalog(GraphCommonCatalogChecks.Geometry("normal"));
                else track.Build(new TrackRoute(Procedural.Generator.Generate(77, "normal", "space-station", false, true)));
                var sources = root.GetComponentsInChildren<MeshRenderer>(); var states = sources.Select(x => x.enabled).ToArray();
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var method = typeof(TrackEnvironmentBuilder).GetMethod(graphMode ? "BuildIndependentSteps" : "BuildSteps", flags);
                var parameters = graphMode ? new object[] { track.GraphSurface, (uint)77, "space-station", (uint?)77, false } : new object[] { track.Route, track.transform, (uint?)77, false };
                using (var work = new LoadingWork((System.Collections.IEnumerator)method.Invoke(env, parameters))) {
                    bool partial = false;
                    for (int step = 0; step < 10000 && work.MoveNext(); step++) {
                        if (env.Root == null) continue;
                        foreach (Transform group in env.Root) if (group.name.StartsWith("Space ") && group.childCount == 0) partial = true;
                        if (partial) break;
                    }
                    Check(partial && work.IsPending && work.Error == null, "cancel inside unfinished station model");
                    var meshes = ((System.Collections.Generic.List<Mesh>)typeof(TrackEnvironmentBuilder).GetField("meshes", flags).GetValue(env)).ToArray();
                    var materials = ((System.Collections.Generic.List<Material>)typeof(TrackEnvironmentBuilder).GetField("materials", flags).GetValue(env)).ToArray();
                    work.Dispose();
                    Check(work.Error == null && env.Root == null && env.Plan == null, "partial construction rolls back");
                    Check(meshes.All(x => x == null) && materials.All(x => x == null), "partial resources released");
                    for (int i = 0; i < sources.Length; i++) Check(sources[i].enabled == states[i], "cancel restores source renderers");
                }
            } finally { env.Clear(); UnityEngine.Object.DestroyImmediate(root); }
        }
        static void Plan(EnvironmentPlan plan, EnvironmentPlan repeat, Func<Vector3, Vector3, float, bool> clear) {
            var space = plan.space;
            Check(space != null && space.hash == repeat.space.hash && plan.presentationHash == repeat.presentationHash, "complete deterministic identity");
            Check(plan.presentationHash != plan.hash, "new presentation has versioned identity");
            Check(space.items.Count(x => x.kind == SpaceSceneryPlan.Kind.Asteroid) >= 60, "substantial asteroid belt");
            Check(space.items.Count(x => x.kind != SpaceSceneryPlan.Kind.Asteroid) >= 7, "stations distributed along route");
            Check(space.items.Count <= SpaceSceneryPlan.MaximumAsteroids + SpaceSceneryPlan.MaximumStations, "bounded object count");
            foreach (var kind in new[] { SpaceSceneryPlan.Kind.Ring, SpaceSceneryPlan.Kind.Shipyard, SpaceSceneryPlan.Kind.Modular, SpaceSceneryPlan.Kind.Research })
                Check(space.items.Any(x => x.kind == kind), "recognizable station family " + kind);
            Check(space.items.Any(x => x.kind == SpaceSceneryPlan.Kind.Ring && x.size >= 480), "giant wheel seed=" + plan.seed);
            var sizes = space.items.Where(x => x.kind == SpaceSceneryPlan.Kind.Asteroid).Select(x => x.size).ToArray();
            Check(sizes.Min() < 20 && sizes.Max() > 95, "small and enormous rocks");
            foreach (var item in space.items) {
                Check(clear(item.position, item.Envelope.extents, EnvironmentPlan.CorridorMargin), "full continuous all-road clearance");
                Check(space.bounds.Contains(item.Envelope.min) && space.bounds.Contains(item.Envelope.max), "visibility union contains complete object");
            }
            foreach (var f in new[] { Vector3.zero, space.bounds.center + Vector3.one * 5000 }) {
                float far = ChaseCamera.SkylineFarClip(f, space.bounds);
                for (int i = 0; i < 8; i++) {
                    var corner = space.bounds.center + Vector3.Scale(space.bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Check(Vector3.Distance(f, corner) < far, "all object corners inside far distance");
                }
            }
        }
        static void Build(bool graphMode) {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Space contracts"); var environment = new TrackEnvironmentBuilder();
            try {
                TrackRoute route = null;
                GraphRoadSurface graphSurface = null;
                if (graphMode) graphSurface = IndependentTrackMesh.BuildCommonCatalog(root.transform, GraphCommonCatalogChecks.Geometry("normal"), 1, null, null, (_, __) => { });
                else { var track = root.AddComponent<TrackBuilder>(); route = new TrackRoute(Procedural.Generator.Generate(77, "normal", "space-station", false, true)); track.Build(route); }
                var colliders = root.GetComponentsInChildren<Collider>();
                var sources = root.GetComponentsInChildren<MeshFilter>(); var meshes = sources.Select(x => x.sharedMesh).ToArray();
                var verts = meshes.Select(x => x.vertices).ToArray(); var indices = meshes.Select(x => x.triangles).ToArray();
                var enabled = sources.Select(x => x.GetComponent<Renderer>().enabled).ToArray();
                for (int cycle = 0; cycle < 2; cycle++) {
                    if (graphMode) environment.BuildIndependent(graphSurface, 77, "space-station"); else environment.Build(route, root.transform);
                    Check(environment.Root.GetComponentsInChildren<Collider>().Length == 0 && root.GetComponentsInChildren<Collider>().SequenceEqual(colliders), "collider inventory exact");
                    int itemIndex = 0, triangleCount = 0;
                    foreach (Transform group in environment.Root) {
                        if (!group.name.StartsWith("Space ")) continue;
                        var item = environment.Plan.space.items[itemIndex++];
                        var renderers = group.GetComponentsInChildren<MeshRenderer>();
                        Check(renderers.Length <= 5, "bounded per-object material batches");
                        foreach (var filter in group.GetComponentsInChildren<MeshFilter>()) {
                            var mesh = filter.sharedMesh; triangleCount += mesh.triangles.Length / 3;
                            var material = filter.GetComponent<Renderer>().sharedMaterial;
                            if (item.kind != SpaceSceneryPlan.Kind.Asteroid) {
                                DetailSurface(mesh,item.kind);
                                if (mesh.name.EndsWith(" 0") || mesh.name.EndsWith(" 1"))
                                    Check(material.mainTexture == Resources.Load<Texture2D>("Environment/Textures/SpaceHull") && material.GetTexture("_BumpMap") == Resources.Load<Texture2D>("Environment/Textures/SpaceHullNormal") && material.IsKeywordEnabled("_NORMALMAP"), "station albedo and normal bound");
                            } else Check(material.GetTexture("_BaseMap") == Resources.Load<Texture2D>("Environment/Textures/SpaceRock") && material.GetTexture("_BumpMap") == Resources.Load<Texture2D>("Environment/Textures/SpaceRockNormal"), "triplanar rock textures bound");
                            foreach (var v in mesh.vertices) {
                                Check(v.magnitude <= 1.00001f, "every actual model vertex in unit envelope");
                                Check(item.Envelope.Contains(filter.transform.TransformPoint(v)), "every world vertex in placed envelope");
                            }
                        }
                    }
                    Check(itemIndex == environment.Plan.space.items.Count, "full inventory instantiated");
                    Check(triangleCount < 1000000, "bounded complete scenery triangle budget");
                    for (int i = 0; i < sources.Length; i++) Check(meshes[i] == sources[i].sharedMesh && verts[i].SequenceEqual(meshes[i].vertices) && indices[i].SequenceEqual(meshes[i].triangles), "exact physical source mesh");
                    var ownedMeshes = environment.Root.GetComponentsInChildren<MeshFilter>().Select(x => x.sharedMesh).Distinct().ToArray();
                    var ownedMaterials = environment.Root.GetComponentsInChildren<Renderer>().SelectMany(x => x.sharedMaterials).Distinct().ToArray();
                    File.AppendAllText(Path.Combine(Output, "inventory.txt"), $"graph={graphMode} cycle={cycle} items={itemIndex} renderers={environment.Root.GetComponentsInChildren<Renderer>().Length} triangles={triangleCount} meshes={ownedMeshes.Length} materials={ownedMaterials.Length} identity={environment.Plan.presentationHash}\n");
                    var cameraObject = new GameObject("Far clip contract"); var camera = cameraObject.AddComponent<Camera>(); var chase = cameraObject.AddComponent<ChaseCamera>();
                    chase.SetSkyline(environment.Plan); Check(camera.farClipPlane > 1800, "space visibility active"); chase.SetSkyline(null); Check(camera.farClipPlane == 1800, "far clip reset"); UnityEngine.Object.DestroyImmediate(cameraObject);
                    environment.Clear();
                    Check(ownedMeshes.All(x => x == null) && ownedMaterials.All(x => x == null), "owned meshes and materials released");
                    for (int i = 0; i < sources.Length; i++) Check(sources[i].GetComponent<Renderer>().enabled == enabled[i], "source enabled state restored");
                }
            } finally { environment.Clear(); UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
