using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype {
    /// <summary>Builds procedural surfaces. Route data remains usable without this component.</summary>
    public sealed class TrackBuilder : MonoBehaviour {
        public TrackRoute Route;

        readonly List<Material> ownedMaterials = new List<Material>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly RaycastHit[] roadHits = new RaycastHit[32];
        int buildRevision;
        public int BuildRevision => buildRevision;
        RoadSurfaceQuery nearestRoad;
        public RoadSurfaceQuery NearestRoad => nearestRoad;
        Material roadMaterial;
        Material railMaterial;

        public void Build(TrackRoute route) {
            buildRevision++;
            Route = route ?? throw new System.ArgumentNullException(nameof(route));
            ClearGenerated();
            roadMaterial = CreateMaterial(new Color(.055f, .07f, .11f), .18f);
            railMaterial = CreateMaterial(new Color(.18f, .7f, .85f), .4f, "Hull");

            ProceduralTrackMesh.Build(transform, Route, roadMaterial, railMaterial);
            foreach(var surface in GetComponentsInChildren<TrackSurface>()) {
                surface.owner=this;
                surface.buildRevision=buildRevision;
            }
            // Only the active road collider inventory of this build enters airborne gravity.
            nearestRoad = new RoadSurfaceQuery(GetComponentsInChildren<TrackSurface>(), this, buildRevision);
            foreach(var filter in GetComponentsInChildren<MeshFilter>()) {
                var mesh=filter.sharedMesh;
                if(mesh!=null&&(mesh.name=="Procedural road"||mesh.name=="Procedural guard rails"))ownedMeshes.Add(mesh);
            }
            if (!Route.IsClosed) {
                BuildLine(Route.StartDistance, "START", Color.cyan);
                BuildLine(Route.FinishDistance, "FINISH", Color.white);
                var end = Route.Evaluate(Route.Length);
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Finish wall"; wall.transform.SetParent(transform, false);
                wall.transform.position = end.position + end.normal * 1.5f + end.tangent * .5f;
                wall.transform.rotation = end.Rotation;
                wall.transform.localScale = new Vector3(end.halfWidth * 2, 3, 1);
                wall.GetComponent<Renderer>().sharedMaterial = railMaterial;
            }
        }

        void BuildLine(float distance, string label, Color color) {
            var frame = Route.Evaluate(distance);
            var material = CreateMaterial(color, 0);
            for (int i = 0; i < 16; i++) {
                var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mark.name = label + " line"; mark.transform.SetParent(transform, false);
                mark.transform.position = frame.position + frame.right * (i - 7.5f) + frame.normal * .025f;
                mark.transform.rotation = frame.Rotation;
                mark.transform.localScale = new Vector3(.9f, .025f, .7f);
                mark.GetComponent<Renderer>().sharedMaterial = i % 2 == 0 ? material : roadMaterial;
                if (Application.isPlaying) Destroy(mark.GetComponent<Collider>()); else DestroyImmediate(mark.GetComponent<Collider>());
            }
        }

        public bool RaycastRoad(Vector3 origin, Vector3 direction, float maxDistance, float progress, out RaycastHit hit) {
            hit = default;
            if (Route == null || direction.sqrMagnitude < .0001f) return false;
            int hitCount = Physics.RaycastNonAlloc(origin, direction.normalized, roadHits, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            TrackFrame expected = Route.Evaluate(progress);
            for (int hitIndex=0;hitIndex<hitCount;hitIndex++) {
                RaycastHit candidate=roadHits[hitIndex];
                TrackSurface surface = candidate.collider.GetComponent<TrackSurface>();
                if (surface == null || surface.triangleSamples == null || candidate.triangleIndex < 0 || candidate.triangleIndex >= surface.triangleSamples.Length) continue;
                int start = surface.triangleSamples[candidate.triangleIndex];
                int end = (start + 1) % Route.Samples.Length;
                TrackFrame a = Route.Samples[start], b = Route.Samples[end];
                float segmentLength = end > start ? b.distance - a.distance : Route.Length - a.distance + b.distance;
                float faceDistance = Route.Wrap(a.distance + segmentLength * .5f);
                if (Mathf.Abs(Route.WrappedDelta(faceDistance, progress)) > 20f) continue;
                if (Vector3.Dot(candidate.normal, expected.normal) < .15f) continue;
                if (candidate.distance < best) { best = candidate.distance; hit = candidate; }
            }
            return best < float.PositiveInfinity;
        }

        Material CreateMaterial(Color color, float metallic, string texture = "Road") {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { color = color };
            ownedMaterials.Add(material);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            material.mainTexture = Resources.Load<Texture2D>("Environment/Textures/" + texture);
            if (material.mainTexture == null) Debug.LogError("Missing track texture: " + texture);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .25f);
            return material;
        }

        void DisposeResources() {
            nearestRoad = null;
            foreach(var mesh in ownedMeshes)if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
            foreach(var material in ownedMaterials)if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
            ownedMeshes.Clear();ownedMaterials.Clear();
        }
        void OnDestroy()=>DisposeResources();
        void ClearGenerated() {
            DisposeResources();
            for (int i = transform.childCount - 1; i >= 0; i--) {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
        }
    }
}
