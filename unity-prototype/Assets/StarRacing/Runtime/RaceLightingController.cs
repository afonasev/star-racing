using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype {
    public static class RaceLightingController {
        public static void Apply(RaceLightingProfile profile, Light sun, Light fill, ChaseCamera[] cameras) {
            if (sun == null) return;
            sun.transform.rotation = profile.sunRotation;
            sun.transform.position = profile.sunPosition;
            sun.color = profile.sunColor;
            sun.intensity = profile.sunIntensity;
            sun.shadows = LightShadows.Soft;
            if (fill != null) {
                fill.enabled = profile.kind == "space";
                fill.transform.rotation = profile.sunRotation * Quaternion.Euler(0, 125, 0);
                fill.color = Color.Lerp(profile.sunColor, Color.white, .6f);
                fill.intensity = .42f;
                fill.shadows = LightShadows.None;
            }
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = profile.ambientColor;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = profile.fogDensity;
            RenderSettings.fogColor = profile.fogColor;
            if (cameras == null) return;
            foreach (var chase in cameras) {
                if (chase == null) continue;
                var camera = chase.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = profile.skyColor;
            }
        }
    }
}
