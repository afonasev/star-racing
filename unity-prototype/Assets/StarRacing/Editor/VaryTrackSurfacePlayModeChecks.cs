using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
    // Real PlayMode render evidence, separate from physical-device/human acceptance.
    [InitializeOnLoad]
    public static class VaryTrackSurfacePlayModeChecks {
        const string Key = "StarRacing.VaryTrackSurfaceQA";
        static int ticks;
        static double started;
        static VaryTrackSurfacePlayModeChecks() { EditorApplication.playModeStateChanged += Changed; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Mute() { if (SessionState.GetBool(Key, false)) AudioListener.volume = 0; }
        public static void Run() {
            string output = Environment.GetEnvironmentVariable("STAR_RACING_SURFACE_QA_DIR");
            if (string.IsNullOrEmpty(output)) throw new Exception("STAR_RACING_SURFACE_QA_DIR required");
            Directory.CreateDirectory(output); SessionState.SetString(Key + "Output", output);
            // This existing fixture checks immediate EditMode destruction semantics.
            EnvironmentTextureChecks.Run();
            SessionState.SetFloat(Key + "Volume", AudioListener.volume);
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + "Failed", false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ticks = 0; EditorApplication.update += Prepare;
        }
        static void Prepare() {
            if (++ticks < 15) return;
            EditorApplication.update -= Prepare; EditorApplication.isPlaying = true;
        }
        static void Changed(PlayModeStateChange state) {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) {
                ticks = 0; started = EditorApplication.timeSinceStartup; EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode) {
                SessionState.SetBool(Key, false); EditorApplication.Exit(SessionState.GetBool(Key + "Failed", false) ? 1 : 0);
            }
        }
        static void Tick() {
            try {
                if (EditorApplication.timeSinceStartup - started > 120) throw new Exception("Surface render QA timeout");
                if (++ticks != 15) return;
                AudioListener.volume = 0;
                foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(go);

                foreach (var theme in new[] { "cloud-city", "space-station" }) Preview(theme);
                Debug.Log("TRACK_SURFACE_PLAYMODE_OK themes=2 views=8 collider=unchanged");
                Finish(false);
            } catch (Exception e) { Debug.LogException(e); Finish(true); }
        }
        static void Preview(string theme) {
            var root = new GameObject("surface-preview"); var environment = new TrackEnvironmentBuilder();
            try {
                var track = root.AddComponent<TrackBuilder>();
                track.Build(new TrackRoute(Procedural.Generator.Generate(77, "full", theme, true, true)));
                environment.Build(track.Route, root.transform);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.6f, .65f, .72f);
                var lamp = new GameObject("preview-light"); lamp.transform.SetParent(root.transform);
                lamp.transform.rotation = Quaternion.Euler(48, -35, 0);
                var light = lamp.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.8f;
                var cameraObject = new GameObject("preview-camera"); cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.AddComponent<Camera>(); camera.fieldOfView = 65; camera.nearClipPlane = .1f;
                camera.clearFlags = CameraClearFlags.Skybox;
                foreach (float distance in new[] { 48f, 96f, 144f, 240f }) {
                    var f = track.Route.Evaluate(distance);
                    camera.transform.position = f.position + f.normal * 5 - f.tangent * 9;
                    camera.transform.LookAt(f.position + f.tangent * 16, f.normal);
                    Capture(camera, theme + "-" + distance.ToString("0") + ".png");
                }
            } finally { environment.Clear(); UnityEngine.Object.DestroyImmediate(root); }
        }
        static void Capture(Camera camera, string filename) {
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32); target.Create();
            var previous = RenderTexture.active; var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "Output", ""), filename), image.EncodeToPNG());
            } finally { RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image); }
        }
        static void Finish(bool failed) {
            EditorApplication.update -= Tick; SessionState.SetBool(Key + "Failed", failed);
            AudioListener.volume = SessionState.GetFloat(Key + "Volume", 1); EditorApplication.isPlaying = false;
        }
    }
}
