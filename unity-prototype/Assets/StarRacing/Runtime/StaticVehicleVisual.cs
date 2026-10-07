using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype
{
    /// <summary>STRIDE v3 static art; magnetic driving and collision remain independent.</summary>
    public sealed class StaticVehicleVisual : MonoBehaviour
    {
        public const string SourceRevision = "8951583";
        public bool Human { get; private set; }
        public float ModelScale { get; private set; }
        public Bounds LocalBounds { get; private set; }
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static StaticVehicleVisual Create(Transform parent, Color color, bool human)
        {
            var source = Resources.Load<GameObject>("Vehicle/Stride");
            if (source == null) throw new InvalidOperationException("Missing STRIDE vehicle asset");
            var root = new GameObject("STRIDE static vehicle");
            root.transform.SetParent(parent, false);
            Instantiate(source, root.transform, false);
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
                // Exported object names are the eight authored material names.
                var material = Resources.Load<Material>("Vehicle/StrideMaterials/" + renderer.name);
                if (material == null) throw new InvalidOperationException("Missing STRIDE material: " + renderer.name);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (renderer.name == "PlayerPaint") renderer.SetPropertyBlock(paintBlock);
            }
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
                throw new InvalidOperationException("Empty STRIDE vehicle geometry");
            return result;
        }
        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f,
            ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    }
}
