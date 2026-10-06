using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype
{
    /// <summary>Static carMesh.ts renderer (0690da44). No collider, suspension or VFX state.</summary>
    public sealed class StaticVehicleVisual : MonoBehaviour
    {
        public const float ModelScale = .72f;
        // Already approved rigid source attachment in driving-plan-r3-working-clearance.
        public const float AttachmentY = -.2331502199172974f;
        public const string SourceRevision = "0690da44d894dc614a9de417c9400f80af682bf6";
        public bool Human { get; private set; }
        public Material Paint { get; private set; }
        public Material Accent { get; private set; }
        public Material Rear { get; private set; }
        public Material Glass { get; private set; }
        public Material Dark { get; private set; }
        Mesh box;
        public static StaticVehicleVisual Create(Transform parent, Color color, bool human)
        {
            var root = new GameObject("Source static vehicle");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0, AttachmentY, 0);
            root.transform.localScale = Vector3.one * ModelScale;
            var visual = root.AddComponent<StaticVehicleVisual>();
            visual.Build(color, human);
            return visual;
        }
        void Build(Color color, bool human)
        {
            Human = human; box = SourceBox();
            Paint = Material("paint", color, .28f, .58f, Color.black);
            Paint.SetTexture("_BaseMap", Resources.Load<Texture2D>("Vehicle/SourceCarPaint"));
            Accent = Material("accent", human ? color : Hex(0x1a2430), 0, 0,
                (human ? color.linear * 7.2f : Hex(0x0b1016).linear * .18f));
            Rear = Material("rear", human ? color : Hex(0x1a2430), 0, 0,
                (human ? color.linear * 5.4f : Hex(0x0b1016).linear * .18f));
            Dark = Material("dark", Hex(0x101722), .5f, .7f, Color.black);
            Glass = Material("glass", Hex(0x193148), .15f, .82f, (Hex(0x08243a).linear * .7f));
            var paint=Paint; var accent=Accent; var rear=Rear; var dark=Dark; var glass=Glass;
            Part("chassis", new Vector3(2.8f,0.68f,4.15f), new Vector3(0f,0.38f,0f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), dark, true);
            Part("body", new Vector3(2.55f,0.94f,3.35f), new Vector3(0f,0.82f,-0.05f), new Vector3(1f,1f,1f), new Quaternion(-0.0224981016f,0f,0f,0.999746886f), paint, true);
            Part("nose", new Vector3(1.86f,0.68f,1.55f), new Vector3(0f,0.83f,1.78f), new Vector3(0.78f,0.82f,1f), new Quaternion(-0.079914694f,0f,0f,0.996801706f), paint, true);
            Part("canopy", new Vector3(1.38f,0.92f,1.72f), new Vector3(0f,1.49f,-0.18f), new Vector3(0.88f,0.94f,1f), new Quaternion(-0.0599640065f,0f,0f,0.99820054f), glass, true);
            Part("part-4", new Vector3(1.7f,0.15f,1.85f), new Vector3(0f,1.13f,-0.16f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), accent, true);
            Part("pod-left-rear", new Vector3(0.72f,0.78f,1.45f), new Vector3(-1.44f,0.43f,-1.35f), new Vector3(1f,1f,1f), new Quaternion(-0.0199986667f,0f,0f,0.999800007f), dark, true);
            Part("part-6", new Vector3(0.13f,0.16f,0.82f), new Vector3(-1.82f,0.39f,-1.35f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), accent, true);
            Part("pod-left-front", new Vector3(0.72f,0.78f,1.45f), new Vector3(-1.44f,0.43f,1.35f), new Vector3(1f,1f,1f), new Quaternion(0.0199986667f,0f,0f,0.999800007f), dark, true);
            Part("part-8", new Vector3(0.13f,0.16f,0.82f), new Vector3(-1.82f,0.39f,1.35f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), accent, true);
            Part("part-9", new Vector3(0.62f,0.13f,0.18f), new Vector3(-0.68f,0.76f,2.28f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), accent, true);
            Part("part-10", new Vector3(0.7f,0.14f,0.16f), new Vector3(-0.68f,0.81f,-2.12f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), rear, true);
            Part("part-11", new Vector3(0.13f,0.72f,0.13f), new Vector3(-0.75f,1.28f,-1.72f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), dark, true);
            Part("pod-right-rear", new Vector3(0.72f,0.78f,1.45f), new Vector3(1.44f,0.43f,-1.35f), new Vector3(1f,1f,1f), new Quaternion(-0.0199986667f,0f,0f,0.999800007f), dark, true);
            Part("part-13", new Vector3(0.13f,0.16f,0.82f), new Vector3(1.82f,0.39f,-1.35f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), accent, true);
            Part("pod-right-front", new Vector3(0.72f,0.78f,1.45f), new Vector3(1.44f,0.43f,1.35f), new Vector3(1f,1f,1f), new Quaternion(0.0199986667f,0f,0f,0.999800007f), dark, true);
            Part("part-15", new Vector3(0.13f,0.16f,0.82f), new Vector3(1.82f,0.39f,1.35f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), accent, true);
            Part("part-16", new Vector3(0.62f,0.13f,0.18f), new Vector3(0.68f,0.76f,2.28f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), accent, true);
            Part("part-17", new Vector3(0.7f,0.14f,0.16f), new Vector3(0.68f,0.81f,-2.12f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), rear, true);
            Part("part-18", new Vector3(0.13f,0.72f,0.13f), new Vector3(0.75f,1.28f,-1.72f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), dark, true);
            Part("part-19", new Vector3(2.55f,0.14f,0.5f), new Vector3(0f,1.67f,-1.75f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), paint, true);
            Part("shoulder-left", new Vector3(0.46f,0.48f,2.45f), new Vector3(-1.25f,0.88f,-0.05f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,-0.0898785492f,0.995952733f), paint, true);
            Part("shoulder-right", new Vector3(0.46f,0.48f,2.45f), new Vector3(1.25f,0.88f,-0.05f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0.0898785492f,0.995952733f), paint, true);
            Part("part-22", new Vector3(2.35f,0.1f,3.5f), new Vector3(0f,0.08f,0f), new Vector3(1f,1f,1f), new Quaternion(0f,0f,0f,1f), accent, true);
        }
        void Part(string name, Vector3 size, Vector3 position, Vector3 scale, Quaternion rotation, Material material, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, true);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = Vector3.Scale(size, scale);
            go.AddComponent<MeshFilter>().sharedMesh = box;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true; // gameRenderer configureCarShadowFlags static path
        }
        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
        static Material Material(string name, Color color, float metallic, float smoothness, Color emission)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "SourceVehicle-" + name;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetVector("_EmissionColor", new Vector4(emission.r, emission.g, emission.b, 1));
            if (emission.maxColorComponent > 0) material.EnableKeyword("_EMISSION");
            return material;
        }
        // Separate face vertices and source UVs, not Unity's built-in primitive layout.
        static Mesh SourceBox()
        {
            var mesh = new Mesh { name = "ThreeBoxGeometry-source" };
            mesh.vertices = new Vector3[] {new Vector3(0.5f,0.5f,0.5f),new Vector3(0.5f,0.5f,-0.5f),new Vector3(0.5f,-0.5f,0.5f),new Vector3(0.5f,-0.5f,-0.5f),new Vector3(-0.5f,0.5f,-0.5f),new Vector3(-0.5f,0.5f,0.5f),new Vector3(-0.5f,-0.5f,-0.5f),new Vector3(-0.5f,-0.5f,0.5f),new Vector3(-0.5f,0.5f,-0.5f),new Vector3(0.5f,0.5f,-0.5f),new Vector3(-0.5f,0.5f,0.5f),new Vector3(0.5f,0.5f,0.5f),new Vector3(-0.5f,-0.5f,0.5f),new Vector3(0.5f,-0.5f,0.5f),new Vector3(-0.5f,-0.5f,-0.5f),new Vector3(0.5f,-0.5f,-0.5f),new Vector3(-0.5f,0.5f,0.5f),new Vector3(0.5f,0.5f,0.5f),new Vector3(-0.5f,-0.5f,0.5f),new Vector3(0.5f,-0.5f,0.5f),new Vector3(0.5f,0.5f,-0.5f),new Vector3(-0.5f,0.5f,-0.5f),new Vector3(0.5f,-0.5f,-0.5f),new Vector3(-0.5f,-0.5f,-0.5f)};
            mesh.normals = new Vector3[] {new Vector3(1f,0f,0f),new Vector3(1f,0f,0f),new Vector3(1f,0f,0f),new Vector3(1f,0f,0f),new Vector3(-1f,0f,0f),new Vector3(-1f,0f,0f),new Vector3(-1f,0f,0f),new Vector3(-1f,0f,0f),new Vector3(0f,1f,0f),new Vector3(0f,1f,0f),new Vector3(0f,1f,0f),new Vector3(0f,1f,0f),new Vector3(0f,-1f,0f),new Vector3(0f,-1f,0f),new Vector3(0f,-1f,0f),new Vector3(0f,-1f,0f),new Vector3(0f,0f,1f),new Vector3(0f,0f,1f),new Vector3(0f,0f,1f),new Vector3(0f,0f,1f),new Vector3(0f,0f,-1f),new Vector3(0f,0f,-1f),new Vector3(0f,0f,-1f),new Vector3(0f,0f,-1f)};
            mesh.uv = new Vector2[] {new Vector2(0f,1f),new Vector2(1f,1f),new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(0f,1f),new Vector2(1f,1f),new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(0f,1f),new Vector2(1f,1f),new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(0f,1f),new Vector2(1f,1f),new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(0f,1f),new Vector2(1f,1f),new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(0f,1f),new Vector2(1f,1f),new Vector2(0f,0f),new Vector2(1f,0f)};
            mesh.triangles = new int[] {0,2,1,2,3,1,4,6,5,6,7,5,8,10,9,10,11,9,12,14,13,14,15,13,16,18,17,18,19,17,20,22,21,22,23,21};
            mesh.RecalculateBounds();
            return mesh;
        }
        void OnDestroy()
        {
            Release(box); Release(Paint); Release(Accent); Release(Rear); Release(Dark); Release(Glass);
        }
        static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
