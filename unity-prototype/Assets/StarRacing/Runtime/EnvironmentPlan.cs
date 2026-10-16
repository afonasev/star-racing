using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype {
    // Rendering data only. No Unity random state, clock, or mutable scene state is read here.
    public sealed class EnvironmentPlan {
        public const int Version = 2;
        public const int CityLayoutVersion = 1;
        public const int MaximumCityTowers = 512;
        public const int MaximumMistTowers = 24;
        public int NearTowerCount { get; private set; }
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

        EnvironmentPlan(TrackRoute route, uint? skySeed):this(route.Definition.seed,route.Definition.theme,route.Samples,(c,e,m)=>ClearOfRoute(route,c,e,m),skySeed,null) {}

        EnvironmentPlan(uint sourceSeed,string sourceTheme,TrackFrame[] frames,Func<Vector3,Vector3,float,bool> clear,uint? skySeed,string identity) {
            seed = sourceSeed;
            theme = sourceTheme;
            lighting = RaceLightingProfile.Create(seed, theme);
            float lowest = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            foreach (var frame in frames) {
                lowest = Mathf.Min(lowest, frame.position.y);
                maxX = Mathf.Max(maxX, frame.position.x);
                maxZ = Mathf.Max(maxZ, frame.position.z);
            }
            cityBaseY = lowest - 240f;
            if (theme == "cloud-city") BuildMetropolis(frames, clear);
            var space = new Procedural.SeedRandom(seed ^ 0x5aceu);
            var start = frames[0];
            Vector3 forward = Vector3.ProjectOnPlane(start.tangent, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = start.tangent;
            Vector3 right = Vector3.ProjectOnPlane(start.right, Vector3.up).normalized;
            if (right.sqrMagnitude < .1f) right = start.right;
            float stationSide = space.Next() < .5 ? -1 : 1;
            Vector3 anchor = new Vector3(maxX + 650, lowest + 115, maxZ + 650);
            bool foundStation = false;
            bool nearStation = theme == "space-station";
            // Bring the existing cluster into the nearby scenery, retaining its full clearance envelope.
            for (int i = 0; i < 6 && !foundStation; i++) {
                float side = i % 2 == 0 ? stationSide : -stationSide;
                var candidate = start.position + forward * ((nearStation ? 240 : 780) + i / 2 * (nearStation ? 80 : 110)) +
                                right * side * ((nearStation ? 250 : 270) + i / 2 * (nearStation ? 35 : 70)) + Vector3.up * 50;
                if (!clear(candidate, Vector3.one * 170, CorridorMargin)) continue;
                anchor = candidate;
                foundStation = true;
            }
            for (int attempt = 0; attempt < 96 && !foundStation; attempt++) {
                var frame = frames[space.Int(0, frames.Length - 1)];
                float side = space.Next() < .5 ? -1 : 1;
                var candidate = frame.position + frame.right * (side * (float)space.Range(nearStation ? 260 : 350, nearStation ? 350 : 650)) + Vector3.up * (float)space.Range(50, nearStation ? 100 : 150);
                if (!clear(candidate, Vector3.one * 170, CorridorMargin)) continue;
                anchor = candidate;
                foundStation = true;
            }
            station = anchor;
            // Background direction only: no world-space planet, radius or route clearance search.
            skyPlanetDirection = (forward * 1350 - right * stationSide * 410 + Vector3.up * 90).normalized;
            // Preserve the approved ocean world for seed 77; consecutive seeds cycle all four worlds.
            skyPlanetVariant = (int)(((skySeed ?? seed) % SkyPlanetVariantCount + 3) % SkyPlanetVariantCount);
            hash = ComputeHash() + (identity==null?"":"-g"+GraphEnvironmentGeometry.Version+"-"+identity);
        }

        void BuildMetropolis(TrackFrame[] frames, Func<Vector3, Vector3, float, bool> clear) {
            // Independent presentation streams keep the track, planet and space station identities intact.
            var near = new Procedural.SeedRandom(seed ^ 0xb17f00d5u);
            var anchors = new List<TrackFrame>();
            foreach(var frame in frames)
                if(anchors.Count == 0 || (frame.position - anchors[anchors.Count-1].position).sqrMagnitude >= 70*70)
                    anchors.Add(frame);
            // Shuffle distance-spaced anchors so near buildings/mist/shadows cover the whole route,
            // including when the cap is reached before a pass ends.
            var order = new Procedural.SeedRandom(seed ^ 0x616e6368u);
            for(int i=anchors.Count-1;i>0;i--){int j=order.Int(0,i);var temp=anchors[i];anchors[i]=anchors[j];anchors[j]=temp;}
            // Visit the whole route on each pass, alternate sides, and never force a rejected placement.
            for(int pass=0;pass<8 && towers.Count<112;pass++) for(int i=0;i<anchors.Count && towers.Count<112;i++) {
                var frame=anchors[i];
                float width=(float)near.Range(18,54);
                var side=(pass+i)%2==0?1:-1;
                var lateral=frame.halfWidth+CorridorMargin+width*.56f+(float)near.Range(8,95);
                var right=Vector3.ProjectOnPlane(frame.right,Vector3.up).normalized;
                if(right.sqrMagnitude<.1f)right=Vector3.right;
                var position=frame.position+right*(side*lateral);
                float height=Mathf.Max(220,frame.position.y-cityBaseY+(float)near.Range(35,220));
                TryAdd(position,width,height,near.Next()<.35);
            }
            NearTowerCount=towers.Count;
            var bounds=new Bounds(frames[0].position,Vector3.zero);
            foreach(var frame in frames)bounds.Encapsulate(frame.position);
            // A jittered district grid surrounds the complete route; shuffled order avoids a directional bias.
            var districts=new Procedural.SeedRandom(seed ^ 0x6d656761u);
            float spanX=bounds.size.x+2400,spanZ=bounds.size.z+2400;
            var cells=new List<Vector2Int>();for(int z=0;z<20;z++)for(int x=0;x<20;x++)cells.Add(new Vector2Int(x,z));
            for(int i=cells.Count-1;i>0;i--){int j=districts.Int(0,i);var tmp=cells[i];cells[i]=cells[j];cells[j]=tmp;}
            foreach(var cell in cells) {
                var position=new Vector3(bounds.min.x-1200+(cell.x+.5f+(float)districts.Range(-.22,.22))*spanX/20,0,
                    bounds.min.z-1200+(cell.y+.5f+(float)districts.Range(-.22,.22))*spanZ/20);
                float width=(float)districts.Range(26,84);
                double kind=districts.Next();
                float height=(float)(kind<.25?districts.Range(180,280):kind<.78?districts.Range(300,490):districts.Range(530,780));
                TryAdd(position,width,height,districts.Next()<.35);
            }

            void TryAdd(Vector3 position,float width,float height,bool amber) {
                if(towers.Count>=MaximumCityTowers)return;
                position.y=cityBaseY+height*.5f;
                var candidate=new CityTower{position=position,width=width,height=height,amber=amber};
                var extents=TowerExtents(candidate);
                foreach(var other in towers) {
                    float spacing=extents.x+TowerExtents(other).x+12;
                    if(Mathf.Abs(position.x-other.position.x)<spacing && Mathf.Abs(position.z-other.position.z)<spacing)return;
                }
                if(clear(position,extents,CorridorMargin))towers.Add(candidate);
            }
        }

        // Roof trim is 1.12 widths wide; the navigation lamp rises 3m above the roof.
        public static Vector3 TowerExtents(CityTower tower) => new Vector3(tower.width*.56f,tower.height*.5f+3,tower.width*.56f);

        public static EnvironmentPlan Create(TrackRoute route, uint? skySeed = null) {
            if (route == null || route.Definition == null) throw new ArgumentException("Procedural route required");
            return new EnvironmentPlan(route, skySeed);
        }

        public static EnvironmentPlan Create(GraphEnvironmentGeometry geometry,uint seed,string theme,uint? skySeed=null) {
            if(geometry==null||(theme!="cloud-city"&&theme!="space-station"))throw new ArgumentException("Graph and supported theme required");
            return new EnvironmentPlan(seed,theme,geometry.Samples,geometry.Clear,skySeed,geometry.Graph.Identity);
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
                if(theme == "cloud-city") add(CityLayoutVersion);
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
        public Color sunColor, ambientColor, ambientEquatorColor, ambientGroundColor, skyColor, fogColor;
        public float sunIntensity, fogDensity, reflectionIntensity;

        public static RaceLightingProfile Create(uint seed, string theme) {
            var random = new Procedural.SeedRandom(seed ^ 0x51a710c7u);
            float azimuth = (float)random.Range(-180, 180);
            if (theme == "space-station") {
                int palette = random.Int(0, 2);
                var sun = new[] { new Color(.72f, .91f, 1), new Color(1, .79f, .67f), new Color(.94f, .72f, 1) }[palette];
                var sky = new[] { new Color(.025f, .06f, .14f), new Color(.09f, .04f, .15f), new Color(.065f, .035f, .13f) }[palette];
                Quaternion rotation = Quaternion.Euler((float)random.Range(24, 55), azimuth, 0);
                var ambient = Color.Lerp(sun, Color.white, .2f) * .48f;
                return new RaceLightingProfile { kind = "space", sunRotation = rotation, sunPosition = -(rotation * Vector3.forward) * 900,
                    sunColor = sun, sunIntensity = 2.3f,
                    ambientColor = ambient, ambientEquatorColor = ambient * .8f, ambientGroundColor = ambient * .65f,
                    reflectionIntensity = .35f, skyColor = sky, fogColor = sky, fogDensity = .00007f };
            }
            string kind = "day";
            float elevation = (float)random.Range(42, 68);
            Color sunColor = new Color(1, .95f, .88f);
            Color skyColor = new Color(.70f, .81f, .91f);
            Quaternion cityRotation = Quaternion.Euler(elevation, azimuth, 0);
            return new RaceLightingProfile { kind = kind, sunRotation = cityRotation,
                sunPosition = -(cityRotation * Vector3.forward) * 900, sunColor = sunColor,
                sunIntensity = 1.85f, ambientColor = new Color(.58f, .68f, .80f),
                ambientEquatorColor = new Color(.46f, .50f, .54f), ambientGroundColor = new Color(.38f, .40f, .43f),
                reflectionIntensity = .65f,
                // Distance haze separates the far skyline while leaving the first 100m legible.
                skyColor = skyColor, fogColor = new Color(.69f, .78f, .86f), fogDensity = .0006f };
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
                value = value * 31 + Mathf.RoundToInt(ambientEquatorColor.r * 1000);
                value = value * 31 + Mathf.RoundToInt(ambientEquatorColor.g * 1000);
                value = value * 31 + Mathf.RoundToInt(ambientEquatorColor.b * 1000);
                value = value * 31 + Mathf.RoundToInt(ambientGroundColor.r * 1000);
                value = value * 31 + Mathf.RoundToInt(ambientGroundColor.g * 1000);
                value = value * 31 + Mathf.RoundToInt(ambientGroundColor.b * 1000);
                value = value * 31 + Mathf.RoundToInt(reflectionIntensity * 1000);
                value = value * 31 + Mathf.RoundToInt(sunIntensity * 1000);
                value = value * 31 + Mathf.RoundToInt(skyColor.r * 1000);
                value = value * 31 + Mathf.RoundToInt(skyColor.g * 1000);
                value = value * 31 + Mathf.RoundToInt(skyColor.b * 1000);
                value = value * 31 + Mathf.RoundToInt(fogDensity * 1000000);
                value = value * 31 + Mathf.RoundToInt(fogColor.r * 1000);
                value = value * 31 + Mathf.RoundToInt(fogColor.g * 1000);
                value = value * 31 + Mathf.RoundToInt(fogColor.b * 1000);
                return value;
            }
        }
    }
}
