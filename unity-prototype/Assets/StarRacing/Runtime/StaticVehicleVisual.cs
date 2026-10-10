using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype
{
    /// <summary>Cloudline C05 static art; magnetic driving and collision remain independent.</summary>
    public sealed class StaticVehicleVisual : MonoBehaviour
    {
        public const string SourceRevision = "c62eb88";
        public VehicleExhaust Exhaust { get; private set; }
        public bool Human { get; private set; }
        public float ModelScale { get; private set; }
        public Bounds LocalBounds { get; private set; }
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static StaticVehicleVisual Create(Transform parent, Color color, bool human)
        {
            var source = Resources.Load<GameObject>("Vehicle/Cloudline");
            if (source == null) throw new InvalidOperationException("Missing Cloudline C05 vehicle asset");
            var root = new GameObject("Cloudline C05 static vehicle");
            root.transform.SetParent(parent, false);
            var model = Instantiate(source, root.transform, false);
            var anchors = model.GetComponentsInChildren<Transform>();
            var left = System.Array.Find(anchors, t => t.name == "Exhaust_L");
            var right = System.Array.Find(anchors, t => t.name == "Exhaust_R");
            if (left == null || right == null) throw new InvalidOperationException("Missing C05 exhaust anchors");
            // Normalize the FBX forward convention before fitting the existing collision envelope.
            if (root.transform.InverseTransformPoint(left.position).z > 0)
                model.transform.localRotation = Quaternion.Euler(0, 180, 0) * model.transform.localRotation;
            var visual = root.AddComponent<StaticVehicleVisual>();
            visual.Human = human;
            var bounds = GeometryBounds(root.transform);
            visual.ModelScale = Mathf.Min(VehicleGeometry.Width / bounds.size.x,
                VehicleGeometry.Length / bounds.size.z);
            root.transform.localScale = Vector3.one * visual.ModelScale;
            root.transform.localPosition = new Vector3(-bounds.center.x * visual.ModelScale,
                VehicleGeometry.ChassisMinY - bounds.min.y * visual.ModelScale,
                -bounds.center.z * visual.ModelScale);
            visual.LocalBounds = new Bounds(root.transform.localPosition + bounds.center * visual.ModelScale,
                bounds.size * visual.ModelScale);
            var paintBlock = new MaterialPropertyBlock();
            paintBlock.SetColor(BaseColor, color);
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                // Exported object names are the ten authored material names.
                var material = Resources.Load<Material>("Vehicle/CloudlineMaterials/" + renderer.name);
                if (material == null) throw new InvalidOperationException("Missing Cloudline C05 material: " + renderer.name);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (renderer.name == "Liquid_Silver") renderer.SetPropertyBlock(paintBlock);
            }
            visual.Exhaust = root.AddComponent<VehicleExhaust>();
            visual.Exhaust.Initialize(left, right);
            return visual;
        }

        // Read imported geometry in wrapper-local space; root FBX axis transforms
        // are retained, including when the vehicle parent is rotated or translated.
        static Bounds GeometryBounds(Transform root)
        {
            Bounds result = default;
            bool initialized = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var bounds = filter.sharedMesh.bounds;
                var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z));
                    if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                    else result.Encapsulate(point);
                }
            }
            if (!initialized || result.size.x <= 0 || result.size.z <= 0)
                throw new InvalidOperationException("Empty Cloudline C05 vehicle geometry");
            return result;
        }
        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f,
            ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    }
}
