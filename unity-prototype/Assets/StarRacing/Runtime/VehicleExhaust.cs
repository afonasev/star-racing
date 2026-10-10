using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype
{
    public sealed class VehicleExhaust : MonoBehaviour
    {
        public ExhaustEnvelope Envelope { get; } = new ExhaustEnvelope();
        public bool Emitting => Envelope.Length > .002f;
        readonly Transform[] jets = new Transform[2];
        readonly MeshRenderer[] renderers = new MeshRenderer[2];
        MaterialPropertyBlock block;
        float flowPhase;
        static Mesh sharedMesh;
        static readonly int Nitro = Shader.PropertyToID("_Nitro");
        static readonly int Flow = Shader.PropertyToID("_FlowPhase");
        static readonly int Jet = Shader.PropertyToID("_JetPhase");
        public void Initialize(Transform left, Transform right)
        {
            block = new MaterialPropertyBlock();
            var material = Resources.Load<Material>("Vehicle/CloudlineExhaust");
            if (material == null) throw new System.InvalidOperationException("Missing Cloudline exhaust material");
            if (sharedMesh == null) sharedMesh = CreateMesh();
            var anchors = new[] { left, right };
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Exhaust plume " + i);go.transform.SetParent(transform, false);
                go.transform.localPosition = transform.InverseTransformPoint(anchors[i].position);
                go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
                var renderer = go.AddComponent<MeshRenderer>();renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                jets[i] = go.transform;renderers[i] = renderer;renderer.enabled = false;
            }
        }
        public void Step(float throttle, bool boostActive, bool enabled, float dt)
        {
            Envelope.Step(throttle, boostActive, enabled, dt);
            if (!enabled) flowPhase = 0;
            else if (dt > 0 && !float.IsNaN(dt) && !float.IsInfinity(dt))
                flowPhase = Mathf.Repeat(flowPhase + dt * 6.5f, Mathf.PI * 2);
            if (block == null) return;
            block.SetFloat(Nitro, Envelope.NitroBlend);
            block.SetFloat(Flow, flowPhase);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].enabled = Emitting;
                if (!Emitting) continue;
                float width = .36f + .10f * Envelope.NitroBlend;
                jets[i].localScale = new Vector3(width, width, Envelope.Length);
                block.SetFloat(Jet, i * 2.1f);
                renderers[i].SetPropertyBlock(block);
            }
        }
        public void ResetEffect() => Step(0, false, false, 0);
        void OnDisable() => ResetEffect();
        static Mesh CreateMesh()
        {
            // Three crossed, double-sided sheets. Shader shapes the turbulent transparent core.
            var vertices = new Vector3[12];var uv = new Vector2[12];var triangles = new int[18];
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI / 3;var r = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .5f;int n = i * 4;
                vertices[n] = -r;vertices[n+1] = r;vertices[n+2] = -r + Vector3.back;vertices[n+3] = r + Vector3.back;
                uv[n] = new Vector2(0,0);uv[n+1] = new Vector2(1,0);uv[n+2] = new Vector2(0,1);uv[n+3] = new Vector2(1,1);
                int t=i*6;triangles[t]=n;triangles[t+1]=n+2;triangles[t+2]=n+1;triangles[t+3]=n+1;triangles[t+4]=n+2;triangles[t+5]=n+3;
            }
            var mesh = new Mesh { name="Shared Cloudline exhaust sheets", vertices=vertices, uv=uv, triangles=triangles };mesh.RecalculateBounds();return mesh;
        }
    }
}
