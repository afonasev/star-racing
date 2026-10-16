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
            visual.ModelScale = Mathf.Min(VehicleGeometry.VisualFitWidth / bounds.size.x,
                VehicleGeometry.VisualFitLength / bounds.size.z);
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

        static Bounds[] contactProfile;
        const string ProfileAlgorithm="c05-longitudinal-5-v1";
        static readonly float[] ProfileCuts={-2.1f,-1.65f,-1.25f,1.25f,1.65f,2.1f};
        static readonly int[] ProfileOrder={2,0,1,3,4};
        [Serializable] public sealed class ContactSection {public Vector3 center,size;}
        [Serializable] public sealed class ProfileDocument {
            public int version;public string algorithm,sourceRevision,modelSha256;
            public float fitWidth,fitLength,chassisMinY,skin;public float[] cuts;public int[] order;public ContactSection[] sections;
        }
        public Bounds[] ContactProfile() {
            if(contactProfile!=null)return contactProfile;
            var asset=Resources.Load<TextAsset>("Vehicle/CloudlineContactProfile");
            if(asset==null)throw new InvalidOperationException("Missing baked C05 contact profile");
            var data=JsonUtility.FromJson<ProfileDocument>(asset.text);
            if(data==null||data.version!=1||data.algorithm!=ProfileAlgorithm||data.sourceRevision!=SourceRevision||data.fitWidth!=VehicleGeometry.VisualFitWidth||data.fitLength!=VehicleGeometry.VisualFitLength||data.chassisMinY!=VehicleGeometry.ChassisMinY||data.skin!=.005f||data.cuts==null||data.cuts.Length!=6||data.order==null||data.order.Length!=5||data.sections==null||data.sections.Length!=5)
                throw new InvalidOperationException("Stale C05 contact profile contract");
            for(int i=0;i<6;i++)if(data.cuts[i]!=ProfileCuts[i])throw new InvalidOperationException("Stale contact section cuts");
            var result=new Bounds[5];
            for(int i=0;i<5;i++) {
                var section=data.sections[i];
                if(data.order[i]!=ProfileOrder[i]||section==null||!Finite(section.center)||!Finite(section.size)||section.size.x<=0||section.size.y<=0||section.size.z<=0)throw new InvalidOperationException("Invalid baked contact section");
                result[i]=new Bounds(section.center,section.size);
            }
            return contactProfile=result;
        }
        static bool Finite(Vector3 value)=>float.IsFinite(value.x)&&float.IsFinite(value.y)&&float.IsFinite(value.z);
#if UNITY_EDITOR
        // CPU mesh data is deliberately unavailable in Player. Bake in Editor and
        // independently remeasure in affected QA; shipping keeps Read/Write disabled.
        public ProfileDocument MeasureProfileDocument(Transform physicsRoot,string modelSha256) {
            var measured=MeasureContactProfile(physicsRoot);var sections=new ContactSection[5];
            for(int i=0;i<5;i++)sections[i]=new ContactSection{center=measured[i].center,size=measured[i].size};
            return new ProfileDocument{version=1,algorithm=ProfileAlgorithm,sourceRevision=SourceRevision,modelSha256=modelSha256,fitWidth=VehicleGeometry.VisualFitWidth,fitLength=VehicleGeometry.VisualFitLength,chassisMinY=VehicleGeometry.ChassisMinY,skin=.005f,cuts=ProfileCuts,order=ProfileOrder,sections=sections};
        }
        public Bounds[] MeasureContactProfile(Transform physicsRoot) {
            float[] cuts={-2.1f,-1.65f,-1.25f,1.25f,1.65f,2.1f};
            var bounds=new Bounds[5];var ready=new bool[5];
            void Include(int band,Vector3 point) {
                if(!ready[band]){bounds[band]=new Bounds(point,Vector3.zero);ready[band]=true;}
                else bounds[band].Encapsulate(point);
            }
            foreach(var filter in GetComponentsInChildren<MeshFilter>()) {
                if(filter.name.StartsWith("Exhaust plume"))continue;
                var matrix=physicsRoot.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;
                for(int v=0;v<vertices.Length;v++)vertices[v]=matrix.MultiplyPoint3x4(vertices[v]);
                for(int t=0;t<triangles.Length;t+=3)for(int band=0;band<5;band++) {
                    for(int e=0;e<3;e++) {
                        var a=vertices[triangles[t+e]];var b=vertices[triangles[t+(e+1)%3]];
                        if(a.z>=cuts[band]&&a.z<=cuts[band+1])Include(band,a);
                        for(int plane=band;plane<=band+1;plane++) {float z=cuts[plane];
                            if((a.z<z&&b.z>z)||(a.z>z&&b.z<z))Include(band,Vector3.Lerp(a,b,(z-a.z)/(b.z-a.z)));
                        }
                    }
                }
            }
            for(int i=0;i<5;i++) {
                if(!ready[i])throw new InvalidOperationException("Empty vehicle contact section");
                bounds[i].Expand(.01f); // 5mm skin, independently of the PhysX contact offset.
            }
            // The root component is the wide central body for existing diagnostics.
            return new[]{bounds[2],bounds[0],bounds[1],bounds[3],bounds[4]};
        }

        // Read imported geometry in wrapper-local space; root FBX axis transforms
        // are retained, including when the vehicle parent is rotated or translated.
#endif
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
