using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype {
    // Rendering data only. No Unity random state, clock, or mutable scene state is read here.
    public sealed class EnvironmentPlan {
        public const int Version = 2;
        public const float CorridorMargin = 38f;
        public readonly uint seed;
        public readonly string theme;
        public readonly RaceLightingProfile lighting;
        public readonly List<CityTower> towers = new List<CityTower>();
        public readonly Vector3 station;
        public readonly Vector3 skyPlanetDirection;
        public readonly int skyPlanetVariant;
        public const int SkyPlanetVariantCount = 4;
        public string SkyPlanetTexture => new[] { "PlanetSky", "PlanetGas", "PlanetRock", "PlanetIce" }[skyPlanetVariant];
        public readonly float cityBaseY;
        public readonly string hash;

        public struct CityTower {
            public Vector3 position;
            public float width, height;
            public bool amber;
        }

        EnvironmentPlan(TrackRoute route, uint? skySeed) {
            seed = route.Definition.seed;
            theme = route.Definition.theme;
            lighting = RaceLightingProfile.Create(seed, theme);
            var decor = new Procedural.SeedRandom(seed ^ 0xb17f00d5u);
            float lowest = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            foreach (var frame in route.Samples) {
                lowest = Mathf.Min(lowest, frame.position.y);
                maxX = Mathf.Max(maxX, frame.position.x);
                maxZ = Mathf.Max(maxZ, frame.position.z);
            }
            cityBaseY = lowest - 240f;
            if (theme == "cloud-city") {
                for (int attempt = 0; attempt < 240 && towers.Count < 24; attempt++) {
                    var frame = route.Samples[decor.Int(10, route.Samples.Length - 11)];
                    float width = (float)decor.Range(18, 32);
                    float lateral = frame.halfWidth + CorridorMargin + width + (float)decor.Range(18, 85);
                    float side = decor.Next() < .5 ? -1 : 1;
                    var center = frame.position + frame.right * (side * lateral);
                    float height = Mathf.Max(260, frame.position.y - cityBaseY + (float)decor.Range(40, 135));
                    center.y = cityBaseY + height * .5f;
                    if (!ClearOfRoute(route, center, new Vector3(width * .5f, height * .5f, width * .5f), CorridorMargin)) continue;
                    towers.Add(new CityTower { position = center, width = width, height = height, amber = decor.Next() < .35 });
                }
            }
            var space = new Procedural.SeedRandom(seed ^ 0x5aceu);
            var start = route.Samples[0];
            Vector3 forward = Vector3.ProjectOnPlane(start.tangent, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = start.tangent;
            Vector3 right = Vector3.ProjectOnPlane(start.right, Vector3.up).normalized;
            if (right.sqrMagnitude < .1f) right = start.right;
            float stationSide = space.Next() < .5 ? -1 : 1;
            Vector3 anchor = new Vector3(maxX + 650, lowest + 115, maxZ + 650);
            bool foundStation = false;
            for (int i = 0; i < 6 && !foundStation; i++) {
                float side = i % 2 == 0 ? stationSide : -stationSide;
                var candidate = start.position + forward * (780 + i / 2 * 110) +
                                right * side * (270 + i / 2 * 70) + Vector3.up * 50;
                if (!ClearOfRoute(route, candidate, Vector3.one * 170, CorridorMargin)) continue;
                anchor = candidate;
                foundStation = true;
            }
            for (int attempt = 0; attempt < 96 && !foundStation; attempt++) {
                var frame = route.Samples[space.Int(0, route.Samples.Length - 1)];
                float side = space.Next() < .5 ? -1 : 1;
                var candidate = frame.position + frame.right * (side * (float)space.Range(350, 650)) + Vector3.up * (float)space.Range(50, 150);
                if (!ClearOfRoute(route, candidate, Vector3.one * 170, CorridorMargin)) continue;
                anchor = candidate;
                foundStation = true;
            }
            station = anchor;
            // Background direction only: no world-space planet, radius or route clearance search.
            skyPlanetDirection = (forward * 1350 - right * stationSide * 410 + Vector3.up * 90).normalized;
            // Preserve the approved ocean world for seed 77; consecutive seeds cycle all four worlds.
            skyPlanetVariant = (int)(((skySeed ?? seed) % SkyPlanetVariantCount + 3) % SkyPlanetVariantCount);
            hash = ComputeHash();
        }

        public static EnvironmentPlan Create(TrackRoute route, uint? skySeed = null) {
            if (route == null || route.Definition == null) throw new ArgumentException("Procedural route required");
            return new EnvironmentPlan(route, skySeed);
        }

        public int District(float distance) => Mathf.FloorToInt((distance + (seed % 3) * 73f) / 240f) % 3;

        public static bool ClearOfRoute(TrackRoute route, Vector3 center, Vector3 extents, float margin) {
            // An AABB-vs-swept-sample conservative check includes loop undersides and camera room.
            foreach (var frame in route.Samples) {
                Vector3 delta = frame.position - center;
                float dx = Mathf.Max(0, Mathf.Abs(delta.x) - extents.x);
                float dy = Mathf.Max(0, Mathf.Abs(delta.y) - extents.y);
                float dz = Mathf.Max(0, Mathf.Abs(delta.z) - extents.z);
                float radius = frame.halfWidth + margin;
                if (dx * dx + dy * dy + dz * dz < radius * radius) return false;
            }
            return true;
        }

        string ComputeHash() {
            unchecked {
                uint value = 2166136261;
                Action<int> add = n => { value = (value ^ (uint)n) * 16777619; };
                add(Version); add((int)seed); add(theme == "space-station" ? 2 : 1);
                add(lighting.HashCode()); add(skyPlanetVariant);
                foreach (var tower in towers) {
                    add(Mathf.RoundToInt(tower.position.x * 100)); add(Mathf.RoundToInt(tower.position.y * 100));
                    add(Mathf.RoundToInt(tower.position.z * 100)); add(Mathf.RoundToInt(tower.width * 100));
                    add(Mathf.RoundToInt(tower.height * 100)); add(tower.amber ? 1 : 0);
                }
                add(Mathf.RoundToInt(station.x * 100)); add(Mathf.RoundToInt(station.y * 100)); add(Mathf.RoundToInt(station.z * 100));
                add(Mathf.RoundToInt(skyPlanetDirection.x * 10000)); add(Mathf.RoundToInt(skyPlanetDirection.y * 10000));
                add(Mathf.RoundToInt(skyPlanetDirection.z * 10000));
                return value.ToString("x8");
            }
        }
    }

    public struct RaceLightingProfile {
        public string kind;
        public Quaternion sunRotation;
        public Vector3 sunPosition;
        public Color sunColor, ambientColor, skyColor, fogColor;
        public float sunIntensity, fogDensity;

        public static RaceLightingProfile Create(uint seed, string theme) {
            var random = new Procedural.SeedRandom(seed ^ 0x51a710c7u);
            float azimuth = (float)random.Range(-180, 180);
            if (theme == "space-station") {
                int palette = random.Int(0, 2);
                var sun = new[] { new Color(.72f, .91f, 1), new Color(1, .79f, .67f), new Color(.94f, .72f, 1) }[palette];
                var sky = new[] { new Color(.025f, .06f, .14f), new Color(.09f, .04f, .15f), new Color(.065f, .035f, .13f) }[palette];
                Quaternion rotation = Quaternion.Euler((float)random.Range(24, 55), azimuth, 0);
                return new RaceLightingProfile { kind = "space", sunRotation = rotation, sunPosition = -(rotation * Vector3.forward) * 900,
                    sunColor = sun, sunIntensity = 2.3f,
                    ambientColor = Color.Lerp(sun, Color.white, .2f) * .48f, skyColor = sky, fogColor = sky, fogDensity = .00007f };
            }
            string kind = "day";
            float elevation = (float)random.Range(42, 68);
            Color sunColor = new Color(1, .95f, .88f);
            Color skyColor = new Color(.70f, .81f, .91f);
            Quaternion cityRotation = Quaternion.Euler(elevation, azimuth, 0);
            return new RaceLightingProfile { kind = kind, sunRotation = cityRotation,
                sunPosition = -(cityRotation * Vector3.forward) * 900, sunColor = sunColor,
                sunIntensity = kind == "sunset" ? 1.75f : 2.1f, ambientColor = Color.Lerp(skyColor, Color.white, .45f) * .66f,
                skyColor = skyColor, fogColor = skyColor, fogDensity = kind == "day" ? .00105f : kind == "golden-hour" ? .00115f : .00125f };
        }

        public int HashCode() {
            unchecked {
                int value = 17;
                value = value * 31 + Mathf.RoundToInt(sunRotation.eulerAngles.x * 100);
                value = value * 31 + Mathf.RoundToInt(sunRotation.eulerAngles.y * 100);
                value = value * 31 + Mathf.RoundToInt(sunPosition.x * 100);
                value = value * 31 + Mathf.RoundToInt(sunPosition.y * 100);
                value = value * 31 + Mathf.RoundToInt(sunPosition.z * 100);
                value = value * 31 + Mathf.RoundToInt(sunColor.r * 1000);
                value = value * 31 + Mathf.RoundToInt(sunColor.g * 1000);
                value = value * 31 + Mathf.RoundToInt(sunColor.b * 1000);
                value = value * 31 + Mathf.RoundToInt(ambientColor.r * 1000);
                value = value * 31 + Mathf.RoundToInt(ambientColor.g * 1000);
                value = value * 31 + Mathf.RoundToInt(ambientColor.b * 1000);
                value = value * 31 + Mathf.RoundToInt(skyColor.r * 1000);
                value = value * 31 + Mathf.RoundToInt(skyColor.g * 1000);
                value = value * 31 + Mathf.RoundToInt(skyColor.b * 1000);
                value = value * 31 + Mathf.RoundToInt(fogDensity * 1000000);
                return value;
            }
        }
    }
}
