using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype {
    /// <summary>Builds procedural surfaces. Route data remains usable without this component.</summary>
    public sealed class TrackBuilder : MonoBehaviour {
        public TrackRoute Route;
        public GraphRoadSurface GraphSurface { get; private set; }
        public const int RoadCollisionLayer=9;
        public const int BarrierCollisionLayer=10;

        readonly List<Collider> barriers=new List<Collider>();
        internal IReadOnlyList<Collider> BarrierColliders=>barriers;
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        TrackSurface[] roadSurfaces=System.Array.Empty<TrackSurface>();
        MeshCollider[] roadColliders=System.Array.Empty<MeshCollider>();
        int buildRevision;
        public int BuildRevision => buildRevision;
        RoadSurfaceQuery nearestRoad;
        public RoadSurfaceQuery NearestRoad => nearestRoad;
        public DriftTireMarks TireMarks { get; private set; }
        Material roadMaterial;
        Material railMaterial;
        PhysicsMaterial roadContactMaterial;
        PhysicsMaterial barrierContactMaterial;

        public void Build(TrackRoute route)=>LoadingWork.Drain(BuildSteps(route));
        IEnumerator BuildSteps(TrackRoute route) {
            buildRevision++;
            Route = route ?? throw new System.ArgumentNullException(nameof(route));
            ClearGenerated();
            GraphSurface=null;
            roadMaterial = CreateMaterial(new Color(.055f, .07f, .11f), .18f);
            railMaterial = CreateMaterial(new Color(.18f, .7f, .85f), .4f, "Hull");
            // Driving forces own traction and braking. Chassis contact must not
            // introduce extra resistance proportional to a road bend's load.
            roadContactMaterial = new PhysicsMaterial("Road contact") {
                staticFriction = 0, dynamicFriction = 0, bounciness = 0,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            // Chassis/car impact friction stays at its certified value. Only barriers
            // receive low sliding friction; their normal impulses still stop direct hits.
            barrierContactMaterial = new PhysicsMaterial("Sliding barriers") {
                staticFriction = .02f, dynamicFriction = .02f, bounciness = 0,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            yield return ProceduralTrackMesh.BuildSteps(transform, Route, roadMaterial, railMaterial);
            var budget=new LoadingWork.Budget();
            foreach(var collider in GetComponentsInChildren<MeshCollider>())
            {
                if(budget.Due){yield return null;budget.Restart();}
                if(collider.sharedMesh!=null && collider.sharedMesh.name=="Procedural guard rails")
                {
                    collider.gameObject.layer=BarrierCollisionLayer;
                    collider.sharedMaterial=barrierContactMaterial;
                    barriers.Add(collider);
                }
            }
            roadSurfaces=GetComponentsInChildren<TrackSurface>();roadColliders=new MeshCollider[roadSurfaces.Length];
            for(int i=0;i<roadSurfaces.Length;i++) {
                if(budget.Due){yield return null;budget.Restart();}
                var surface=roadSurfaces[i];roadColliders[i]=surface.GetComponent<MeshCollider>();
                surface.gameObject.layer=RoadCollisionLayer;
                surface.owner=this;
                surface.buildRevision=buildRevision;
                surface.GetComponent<MeshCollider>().sharedMaterial = roadContactMaterial;
            }
            foreach (var collider in GetComponentsInChildren<MeshCollider>())
            {
                if(budget.Due){yield return null;budget.Restart();}
                if (collider.sharedMesh != null && (collider.sharedMesh.name == RoadVolumeMesh.BottomName || collider.sharedMesh.name == RoadVolumeMesh.ExteriorName || collider.sharedMesh.name == RoadVolumeMesh.SupportName))
                    collider.sharedMaterial = roadContactMaterial;
            }
            // Only the active road collider inventory of this build enters airborne gravity.
            nearestRoad = new RoadSurfaceQuery((TrackSurface[])roadSurfaces.Clone(), this, buildRevision);
            var marks = new GameObject("Drift tire marks"); marks.transform.SetParent(transform, false);
            TireMarks = marks.AddComponent<DriftTireMarks>();
            var markBounds = new Bounds(Route.Samples[0].position, Vector3.one * 40);
            foreach (var sample in Route.Samples) markBounds.Encapsulate(new Bounds(sample.position, Vector3.one * (sample.halfWidth * 2 + 40)));
            TireMarks.Initialize(markBounds);
            foreach(var filter in GetComponentsInChildren<MeshFilter>()) {
                if(budget.Due){yield return null;budget.Restart();}
                var mesh=filter.sharedMesh;
                if(mesh!=null&&(mesh.name=="Procedural road"||mesh.name=="Procedural guard rails"||mesh.name==RoadVolumeMesh.MeshName))ownedMeshes.Add(mesh);
            }
            foreach(var collider in GetComponentsInChildren<MeshCollider>())
            {
                if(budget.Due){yield return null;budget.Restart();}
                if(collider.sharedMesh!=null && (collider.sharedMesh.name==RoadVolumeMesh.BottomName || collider.sharedMesh.name==RoadVolumeMesh.ExteriorName || collider.sharedMesh.name==RoadVolumeMesh.SupportName))ownedMeshes.Add(collider.sharedMesh);
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
                wall.GetComponent<Collider>().sharedMaterial = barrierContactMaterial;
            }
        }

        void BuildLine(float distance, string label, Color color) {
            BuildFrameLine(Route.Evaluate(distance),label,color);
        }
        void BuildFrameLine(TrackFrame frame,string label,Color color) {
            var material = CreateMaterial(color, 0);
            for (int i = 0; i < 16; i++) {
                var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mark.name = label + " line"; mark.transform.SetParent(transform, false);
                mark.transform.position = frame.position + frame.right * (i - 7.5f) + frame.normal * .025f;
                mark.transform.rotation = frame.Rotation;
                mark.transform.localScale = new Vector3(.9f, .025f, .7f);
                mark.GetComponent<Renderer>().sharedMaterial = i % 2 == 0 ? material : roadMaterial;
                var markerCollider=mark.GetComponent<Collider>();markerCollider.enabled=false;
                if (Application.isPlaying) Destroy(markerCollider); else DestroyImmediate(markerCollider);
            }
        }

        public void BuildNativeCourse(NativeTrackCourse course)=>LoadingWork.Drain(BuildNativeSteps(course));
        internal IEnumerator BuildNativeSteps(NativeTrackCourse course) {
            if(!course.IsIndependent){yield return BuildSteps(new TrackRoute(course.Single));yield break;}
            yield return BuildIndependentSteps(course.Graph,(marks,bounds)=>marks.Initialize(bounds),true,true);
            TrackFrame Frame(Procedural.GraphFrame f)=>new TrackFrame{distance=(float)f.distance,position=f.position.Float,tangent=f.tangent.Float,normal=f.normal.Float,right=-f.right.Float,halfWidth=(float)f.halfWidth};
            BuildFrameLine(Frame(course.Graph.Road(0).Evaluate(course.Start)),"START",Color.cyan);
            var exit=course.Graph.Road(5);BuildFrameLine(Frame(exit.Evaluate(exit.PhysicalDistance(course.Finish))),"FINISH",Color.white);
            var end=Frame(exit.samples[exit.samples.Count-1]);var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Finish wall";wall.transform.SetParent(transform,false);wall.transform.position=end.position+end.normal*1.5f+end.tangent*.5f;wall.transform.rotation=end.Rotation;wall.transform.localScale=new Vector3(end.halfWidth*2,3,1);wall.GetComponent<Renderer>().sharedMaterial=railMaterial;wall.GetComponent<Collider>().sharedMaterial=barrierContactMaterial;
        }

        // Explicit experimental entry point; active Generator11 never calls this.
        public void BuildIndependent(Procedural.IndependentGraph graph)=>BuildIndependent(graph,(marks,bounds)=>marks.Initialize(bounds));
        public void BuildIndependentCommonVertical(Procedural.IndependentGraph graph)=>BuildIndependentCore(graph,(marks,bounds)=>marks.Initialize(bounds),true);
        public void BuildIndependentCommonCatalog(Procedural.IndependentGraph graph)=>BuildIndependentCore(graph,(marks,bounds)=>marks.Initialize(bounds),true,true);
        void BuildIndependent(Procedural.IndependentGraph graph,System.Action<DriftTireMarks,Bounds> initializeMarks)=>BuildIndependentCore(graph,initializeMarks,false);
        void BuildIndependentCore(Procedural.IndependentGraph graph,System.Action<DriftTireMarks,Bounds> initializeMarks,bool commonVertical,bool commonCatalog=false)=>LoadingWork.Drain(BuildIndependentSteps(graph,initializeMarks,commonVertical,commonCatalog));
        IEnumerator BuildIndependentSteps(Procedural.IndependentGraph graph,System.Action<DriftTireMarks,Bounds> initializeMarks,bool commonVertical,bool commonCatalog=false) {
            buildRevision++;ClearGenerated();Route=null;GraphSurface=null;
            roadMaterial=CreateMaterial(new Color(.055f,.07f,.11f),.18f);
            railMaterial=CreateMaterial(new Color(.18f,.7f,.85f),.4f,"Hull");
            roadContactMaterial=new PhysicsMaterial("Road contact") {
                staticFriction=0,dynamicFriction=0,bounciness=0,
                frictionCombine=PhysicsMaterialCombine.Minimum,bounceCombine=PhysicsMaterialCombine.Minimum
            };
            barrierContactMaterial=new PhysicsMaterial("Sliding barriers") {
                staticFriction=.02f,dynamicFriction=.02f,bounciness=0,
                frictionCombine=PhysicsMaterialCombine.Minimum,bounceCombine=PhysicsMaterialCombine.Minimum
            };
            var root=new GameObject("Independent road build");root.transform.SetParent(transform,false);
            bool complete=false;
            try {
                yield return IndependentTrackMesh.BuildSteps(root.transform,graph,buildRevision,roadMaterial,railMaterial,null,commonVertical,commonCatalog,value=>GraphSurface=value);
                var marks=new GameObject("Graph drift tire marks");marks.transform.SetParent(transform,false);TireMarks=marks.AddComponent<DriftTireMarks>();
                initializeMarks(TireMarks,new GraphEnvironmentGeometry(graph).Bounds);
                var budget=new LoadingWork.Budget();
                foreach(var collider in root.GetComponentsInChildren<MeshCollider>()) {
                    if(budget.Due){yield return null;budget.Restart();}
                    bool rail=collider.sharedMesh!=null && collider.sharedMesh.name=="Independent guard rails";
                    collider.sharedMaterial=rail?barrierContactMaterial:roadContactMaterial;
                    collider.gameObject.layer=rail?BarrierCollisionLayer:RoadCollisionLayer;
                    if(rail)barriers.Add(collider);
                }
                complete=true;
            } finally {if(!complete){ClearGenerated();GraphSurface=null;Route=null;}}
        }

        public bool RaycastRoad(Vector3 origin, Vector3 direction, float maxDistance, float progress, out RaycastHit hit) {
            hit=default;
            if(Route==null || direction.sqrMagnitude<.0001f)return false;
            return RaycastRoadWithNormal(origin,direction,maxDistance,progress,Route.Evaluate(progress).normal,out hit);
        }
        // Callers that already evaluated this progress can reuse its exact normal.
        internal bool RaycastRoadWithNormal(Vector3 origin,Vector3 direction,float maxDistance,float progress,Vector3 expectedNormal,out RaycastHit hit) {
            hit = default;
            if (Route == null || direction.sqrMagnitude < .0001f) return false;
            var ray=new Ray(origin,direction.normalized);
            float best = float.PositiveInfinity;
            for (int hitIndex=0;hitIndex<roadColliders.Length;hitIndex++) {
                var collider=roadColliders[hitIndex];
                if(collider==null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy || !collider.Raycast(ray,out var candidate,maxDistance))continue;
                TrackSurface surface = roadSurfaces[hitIndex];
                if (surface == null || surface.triangleSamples == null || candidate.triangleIndex < 0 || candidate.triangleIndex >= surface.triangleSamples.Length) continue;
                int start = surface.triangleSamples[candidate.triangleIndex];
                int end = (start + 1) % Route.Samples.Length;
                TrackFrame a = Route.Samples[start], b = Route.Samples[end];
                float segmentLength = end > start ? b.distance - a.distance : Route.Length - a.distance + b.distance;
                float faceDistance = Route.Wrap(a.distance + segmentLength * .5f);
                if (Mathf.Abs(Route.WrappedDelta(faceDistance, progress)) > 20f) continue;
                if (Vector3.Dot(candidate.normal, expectedNormal) < .15f) continue;
                if (candidate.distance < best) {
                    // PhysX returns a flat triangle normal. Use the same continuous vertex
                    // normals as rendering/nearest-road queries for magnetic support.
                    if (surface.sourceNormals != null) {
                        int offset = candidate.triangleIndex * 3;
                        var weights = candidate.barycentricCoordinate;
                        var normals = surface.sourceNormals; var indices = surface.sourceTriangles;
                        Vector3 normal = normals[indices[offset]] * weights.x +
                            normals[indices[offset + 1]] * weights.y + normals[indices[offset + 2]] * weights.z;
                        candidate.normal = surface.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normal).normalized;
                    }
                    best = candidate.distance; hit = candidate;
                }
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
            var inventory=new HashSet<Mesh>(ownedMeshes);
            foreach(var filter in GetComponentsInChildren<MeshFilter>(true)) {
                var mesh=filter.sharedMesh;if(mesh!=null&&(mesh.name=="Procedural road"||mesh.name=="Procedural guard rails"||mesh.name==RoadVolumeMesh.MeshName)&&inventory.Add(mesh))ownedMeshes.Add(mesh);
            }
            foreach(var collider in GetComponentsInChildren<MeshCollider>(true)) {
                var mesh=collider.sharedMesh;if(mesh!=null&&(mesh.name==RoadVolumeMesh.BottomName||mesh.name==RoadVolumeMesh.ExteriorName||mesh.name==RoadVolumeMesh.SupportName)&&inventory.Add(mesh))ownedMeshes.Add(mesh);
            }
            barriers.Clear();

            nearestRoad = null;
            roadSurfaces=System.Array.Empty<TrackSurface>();roadColliders=System.Array.Empty<MeshCollider>();
            if(barrierContactMaterial!=null){if(Application.isPlaying)Destroy(barrierContactMaterial);else DestroyImmediate(barrierContactMaterial);barrierContactMaterial=null;}
            if(roadContactMaterial!=null){if(Application.isPlaying)Destroy(roadContactMaterial);else DestroyImmediate(roadContactMaterial);roadContactMaterial=null;}
            foreach(var mesh in ownedMeshes)if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
            foreach(var material in ownedMaterials)if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
            ownedMeshes.Clear();ownedMaterials.Clear();
        }
        void OnDestroy()=>DisposeResources();
        void ClearGenerated() {
            TireMarks=null;
            DisposeResources();
            for (int i = transform.childCount - 1; i >= 0; i--) {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
        }
    }
}
