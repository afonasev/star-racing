using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
    [InitializeOnLoad] public static class SpaceSceneryPreview {
        const string Key = "StarRacing.SpaceSceneryPreview";
        static int ticks;
        static string Output => Environment.GetEnvironmentVariable("STAR_RACING_SPACE_QA_DIR");
        static SpaceSceneryPreview() { EditorApplication.playModeStateChanged += Changed; }
        public static void Detail() { SpaceSceneryChecks.Detail(); Run(); }
        public static void Review() { SpaceSceneryChecks.Additional(); Run(); }
        public static void Verify() { SpaceSceneryChecks.Run(); Run(); }
        public static void Run() {
            if (string.IsNullOrEmpty(Output)) throw new Exception("STAR_RACING_SPACE_QA_DIR required");
            Directory.CreateDirectory(Output);
            GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            QualitySettings.renderPipeline = GraphicsSettings.defaultRenderPipeline;
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + "Failed", false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ticks = 0; EditorApplication.update += Enter;
        }
        static void Enter() { if (++ticks < 12) return; EditorApplication.update -= Enter; EditorApplication.isPlaying = true; }
        static void Changed(PlayModeStateChange state) {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { ticks = 0; EditorApplication.update += Draw; }
            if (state == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Key, false); EditorApplication.Exit(SessionState.GetBool(Key + "Failed", false) ? 1 : 0); }
        }
        static void Draw() {
            if (++ticks < 12) return; EditorApplication.update -= Draw;
            try {
                string route = Environment.GetEnvironmentVariable("STAR_RACING_SPACE_PREVIEW_ROUTE");
                if (route != "native12") Render(false);
                if (route != "legacy") Render(true);
                File.WriteAllText(Path.Combine(Output, "preview.json"), "{\"success\":true,\"editorPlayMode\":true,\"route\":\"" + (route ?? "both") + "\",\"layouts\":[1,4],\"muted\":true,\"playerBuild\":false}");
                Debug.Log("SPACE_SCENERY_PREVIEW_OK editorPlayMode=true layouts=1,4 muted=true");
            } catch (Exception e) { SessionState.SetBool(Key + "Failed", true); Debug.LogException(e); }
            finally { EditorApplication.isPlaying = false; }
        }
        static void Render(bool independent) {
            var root = new GameObject("Space scenery fixture"); var environment = new TrackEnvironmentBuilder();
            try {
                var track = root.AddComponent<TrackBuilder>();
                if (independent) { track.BuildIndependentCommonCatalog(GraphCommonCatalogChecks.Geometry("normal")); environment.BuildIndependent(track.GraphSurface, 77, "space-station", 77); }
                else { track.Build(new TrackRoute(Procedural.Generator.Generate(77, "normal", "space-station", false, true))); environment.Build(track.Route, track.transform, 77); }
                environment.Sky.Activate(); Physics.SyncTransforms();
                var sun = new GameObject("Sun").AddComponent<Light>(); sun.transform.SetParent(root.transform, false); sun.type = LightType.Directional;
                var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(root.transform, false); fill.type = LightType.Directional;
                var volumeGo = new GameObject("Volume"); volumeGo.transform.SetParent(root.transform, false);
                var volume = volumeGo.AddComponent<Volume>(); volume.isGlobal = true; var profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.profile = profile;
                var bloom = profile.Add<Bloom>(); bloom.intensity.Override(.25f); bloom.threshold.Override(1.1f); profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
                var balance = ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                var cars = new MagneticVehicle[24]; var cameras = new ChaseCamera[4];
                for (int i = 0; i < cars.Length; i++) {
                    var go = new GameObject("Vehicle " + i); go.transform.SetParent(root.transform, false);
                    var car = go.AddComponent<MagneticVehicle>(); var color = i < 4 ? CloudlineSkin.PlayerColors[i] : Color.HSVToRGB((i * .618034f) % 1, .65f, .9f);
                    if (independent) {
                        var cursor = track.GraphSurface.Graph.Cursor(0, Math.Min(track.GraphSurface.Graph.Road(0).Length - 15, 280 + i / 4 * 9));
                        car.InitializeIndependent(track, cursor, i, color, balance, i < 4); car.ResetOnGraph(cursor, (i % 4 - 1.5f) * 2.3f);
                    }
                    else { car.Initialize(track, i, color, balance, i < 4); car.ResetAt(140 + i / 4 * 9, (i % 4 - 1.5f) * 2.9f); }
                    car.Hold(true); cars[i] = car;
                    if (i >= 4) continue;
                    var cameraGo = new GameObject("Chase camera " + i); cameraGo.transform.SetParent(root.transform, false); var camera = cameraGo.AddComponent<Camera>();
                    camera.enabled = false; camera.fieldOfView = 65; camera.nearClipPlane = .2f;
                    camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                    cameras[i] = cameraGo.AddComponent<ChaseCamera>(); cameras[i].target = car; cameras[i].SetSkyline(environment.Plan);
                }
                RaceLightingController.Apply(environment.Plan.lighting, sun, fill, cameras);
                string name = independent ? "native12" : "legacy";
                // Settle the first SRP material/volume upload before saving evidence.
                Capture(cameras, 1, name + "-warmup", false);
                Capture(cameras, 1, name + "-start"); Capture(cameras, 4, name + "-start");
                for (int stage = 0; stage < 3; stage++) {
                    float distance = new[] { 340f, 670f, 1080f }[stage];
                    for (int i = 0; i < cars.Length; i++) {
                        if (independent) { var graph = track.GraphSurface.Graph; int road = stage == 0 ? 0 : stage == 1 ? 1 : 2; var cursor = graph.Cursor(road, Math.Min(graph.Road(road).Length - 10, stage == 0 ? distance + i / 4 * 9 : 90 + i / 4 * 9)); cars[i].ResetOnGraph(cursor, (i % 4 - 1.5f) * 2.3f); }
                        else cars[i].ResetAt(distance + i / 4 * 9, (i % 4 - 1.5f) * 2.9f);
                    }
                    Capture(cameras, 1, name + "-section-" + stage); Capture(cameras, 4, name + "-section-" + stage);
                }
                // Model studies are real runtime geometry, separate from chase-view evidence.
                var groups = environment.Root.Cast<Transform>().Where(t => t.name.StartsWith("Space ")).ToArray();
                int itemIndex = -1; var studied = new System.Collections.Generic.HashSet<SpaceSceneryPlan.Kind>();
                foreach (var item in environment.Plan.space.items) {
                    itemIndex++;
                    if (!studied.Contains(item.kind) && (item.kind != SpaceSceneryPlan.Kind.Ring || item.size >= 480)) {
                        studied.Add(item.kind);
                        foreach (var renderer in root.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                        for (int i = 0; i < groups.Length; i++) groups[i].gameObject.SetActive(i == itemIndex);
                        foreach (Transform child in environment.Root) if (!child.name.StartsWith("Space ")) child.gameObject.SetActive(false);
                        var camera = cameras[0].GetComponent<Camera>();
                        camera.transform.position = item.position + item.rotation * new Vector3(item.size * .4f, item.size * .25f, item.size * 2.6f);
                        camera.transform.LookAt(item.position, item.rotation * Vector3.up); camera.rect = new Rect(0, 0, 1, 1); camera.aspect = 16f / 9;
                        Single(camera, Path.Combine(Output, name + "-model-" + item.kind + ".png"));
                        if (item.kind == SpaceSceneryPlan.Kind.Ring || item.kind == SpaceSceneryPlan.Kind.Modular || item.kind == SpaceSceneryPlan.Kind.Asteroid) {
                            var focus = item.position + item.rotation * (item.kind == SpaceSceneryPlan.Kind.Ring ? Vector3.up * item.size * .8f : Vector3.zero);
                            var offset = item.kind == SpaceSceneryPlan.Kind.Ring ? new Vector3(.15f,.08f,.70f) : new Vector3(.15f,.2f,item.kind == SpaceSceneryPlan.Kind.Asteroid ? 1.8f : .95f);
                            camera.transform.position = focus + item.rotation * (offset * item.size);
                            camera.transform.LookAt(focus,item.rotation * Vector3.up); camera.fieldOfView = 40;
                            Single(camera,Path.Combine(Output,name + "-detail-" + item.kind + ".png"));
                            camera.fieldOfView = 65;
                        }
                    }
                }
                UnityEngine.Object.DestroyImmediate(profile);
            } finally { environment.Clear(); UnityEngine.Object.DestroyImmediate(root); }
        }
        static void Capture(ChaseCamera[] follows, int seats, string name, bool save = true) {
            const int width = 1600, height = 900;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var tick = typeof(ChaseCamera).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            try {
                for (int seat = 0; seat < seats; seat++) {
                    var camera = follows[seat].GetComponent<Camera>(); var rect = RaceViewports.For(seats, seat); camera.rect = rect;
                    int w = (int)(width * rect.width), h = (int)(height * rect.height); camera.aspect = (float)w / h;
                    follows[seat].Snap(); tick.Invoke(follows[seat], null); camera.rect = new Rect(0, 0, 1, 1);
                    var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); rt.Create(); var previous = RenderTexture.active;
                    var part = new Texture2D(w, h, TextureFormat.RGB24, false);
                    try {
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                        RenderTexture.active = rt; part.ReadPixels(new Rect(0, 0, w, h), 0, 0); part.Apply(); image.SetPixels((int)(width * rect.x), (int)(height * rect.y), w, h, part.GetPixels());
                    } finally { RenderTexture.active = previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(part); }
                }
                image.Apply(); if (save) File.WriteAllBytes(Path.Combine(Output, name + "-" + seats + "-players.png"), image.EncodeToPNG());
            } finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        static void Single(Camera camera, string path) {
            var rt = new RenderTexture(1600, 900, 24); rt.Create(); var previous = RenderTexture.active; var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try { RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { RenderTexture.active = previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
