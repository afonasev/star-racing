using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
    public static class CloudCityChecks {
        static int assertions;
        static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_CITY_QA_DIR");
        static void Check(bool ok,string message){assertions++;if(!ok)throw new Exception("METROPOLIS: "+message);}
        public static void Run() {
            Directory.CreateDirectory(Output);assertions=0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            foreach(uint seed in new uint[]{77,78,139}) {
                var route=new TrackRoute(Procedural.Generator.Generate(seed,"full","cloud-city",true,true));
                Plan(EnvironmentPlan.Create(route),EnvironmentPlan.Create(route),route.Samples,(c,e,m)=>EnvironmentPlan.ClearOfRoute(route,c,e,m));
                var space=new TrackRoute(Procedural.Generator.Generate(seed,"full","space-station",true,true));
                Check(EnvironmentPlan.Create(space).hash==CloudCityBaseline.EnvironmentPlan.Create(space).hash,"exact unchanged space presentation hash");
                Build(route,null,"legacy-"+seed,seed==77);
            }
            foreach(bool transformed in new[]{false,true})foreach(string mode in new[]{"normal","full","none"}) {
                var graph=GraphCommonCatalogChecks.Geometry(mode,true,transformed);var geometry=new GraphEnvironmentGeometry(graph);var identity=graph.Identity;
                Plan(EnvironmentPlan.Create(geometry,20,"cloud-city"),EnvironmentPlan.Create(geometry,20,"cloud-city"),geometry.Samples,geometry.Clear);
                Check(identity==graph.Identity&&graph.Validate().Length==0,"graph identity/topology untouched");
                Check(EnvironmentPlan.Create(geometry,20,"space-station").hash==CloudCityBaseline.EnvironmentPlan.Create(geometry,20,"space-station").hash,"exact space graph hash");
                Build(null,graph,"native-"+mode+"-"+transformed,!transformed&&mode=="normal");
            }
            EnvironmentTextureChecks.Run();SkyboxChecks.Run();DaylightVisualChecks.Run();
            File.WriteAllText(Path.Combine(Output,"contracts.json"),"{\"success\":true,\"assertions\":"+assertions+",\"cases\":9,\"spaceBaselineExact\":true}");
            Debug.Log("CLOUD_CITY_CONTRACTS_OK assertions="+assertions);
            GraphicsEffectsPlayChecks.Run();
        }
        static void Plan(EnvironmentPlan plan,EnvironmentPlan repeat,TrackFrame[] frames,Func<Vector3,Vector3,float,bool> clear) {
            Check(plan.hash==repeat.hash,"repeat identity");Check(plan.towers.Count>=300&&plan.towers.Count<=EnvironmentPlan.MaximumCityTowers,"dense bounded city count "+plan.towers.Count);
            Check(plan.NearTowerCount>=48&&plan.NearTowerCount<=112,"near route towers "+plan.NearTowerCount);
            Check(plan.towers.Max(t=>t.width)-plan.towers.Min(t=>t.width)>40&&plan.towers.Max(t=>t.height)-plan.towers.Min(t=>t.height)>350,"silhouette diversity");
            var bounds=new Bounds(frames[0].position,Vector3.zero);foreach(var frame in frames)bounds.Encapsulate(frame.position);
            var sectors=new int[8];
            for(int i=0;i<plan.towers.Count;i++) {
                var tower=plan.towers[i];Check(clear(tower.position,EnvironmentPlan.TowerExtents(tower),EnvironmentPlan.CorridorMargin),"whole tower/trim/lamp clear of every road");
                for(int j=0;j<i;j++){var other=plan.towers[j];float gap=(tower.width+other.width)*.56f+12;Check(Mathf.Abs(tower.position.x-other.position.x)>=gap||Mathf.Abs(tower.position.z-other.position.z)>=gap,"nonoverlapping footprints");}
                var delta=tower.position-bounds.center;int sector=Mathf.FloorToInt((Mathf.Atan2(delta.z,delta.x)+Mathf.PI)/(2*Mathf.PI)*8)%8;sectors[sector]++;
            }
            Check(sectors.All(n=>n>=12),"all eight surrounding districts "+string.Join(",",sectors));
            File.AppendAllText(Path.Combine(Output,"layouts.txt"),$"seed={plan.seed} hash={plan.hash} count={plan.towers.Count} near={plan.NearTowerCount} sectors={string.Join(",",sectors)}\n");
        }
        static void Build(TrackRoute route,Procedural.IndependentGraph graph,string name,bool render) {
            var root=new GameObject("City fixture");var builder=root.AddComponent<TrackBuilder>();var env=new TrackEnvironmentBuilder();
            try {
                if(graph!=null)builder.BuildIndependentCommonCatalog(graph);else builder.Build(route);
                var colliders=root.GetComponentsInChildren<Collider>();var filters=root.GetComponentsInChildren<MeshFilter>();
                var meshes=filters.Select(f=>f.sharedMesh).ToArray();var vertices=meshes.Select(m=>m.vertices).ToArray();var indices=meshes.Select(m=>m.triangles).ToArray();
                if(graph!=null)env.BuildIndependent(builder.GraphSurface,20,"cloud-city");else env.Build(builder.Route,root.transform);
                Check(env.Root.GetComponentsInChildren<Collider>().Length==0&&root.GetComponentsInChildren<Collider>().SequenceEqual(colliders),"exact collider inventory");
                for(int i=0;i<meshes.Length;i++)Check(filters[i].sharedMesh==meshes[i]&&meshes[i].vertices.SequenceEqual(vertices[i])&&meshes[i].triangles.SequenceEqual(indices[i]),"physical road bytes unchanged");
                var city=env.Root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="City tower"||r.name=="City tower trim"||r.name=="City district").ToArray();
                int triangles=city.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);int materials=city.Select(r=>r.sharedMaterial).Distinct().Count();
                Check(city.Length<=192&&triangles<=40000&&materials==6,"bounded city opaque resources "+city.Length+" / "+triangles+" / "+materials);
                Check(city.Count(r=>r.shadowCastingMode!=ShadowCastingMode.Off)==10,"existing shadow caster budget");
                var mist=env.Root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="Tower cloud mist").ToArray();Check(mist.Length<=72,"bounded transparent mist");
                env.Sky.SetMistEnabled(false);Check(mist.All(r=>!r.enabled),"mist off");env.Sky.SetMistEnabled(true);Check(mist.All(r=>r.enabled),"mist on");
                File.AppendAllText(Path.Combine(Output,"resources.txt"),$"{name} buildings={env.Plan.towers.Count} renderers={city.Length} triangles={triangles} materials={materials} mist={mist.Length}\n");
                if(render)Render(env,route!=null?builder.Route.Samples:env.GraphGeometry.Samples,name);
                var owned=env.Root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Where(m=>!meshes.Contains(m)).Distinct().ToArray();var ownedMaterials=env.Root.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                env.Clear();Check(env.Root==null&&owned.All(m=>m==null)&&ownedMaterials.All(m=>m==null),"owned mesh/material teardown");
            } finally {env.Clear();UnityEngine.Object.DestroyImmediate(root);}
        }
        static void Render(TrackEnvironmentBuilder env,TrackFrame[] frames,string name) {
            env.Sky.Activate();var sun=new GameObject("City preview sun").AddComponent<Light>();sun.type=LightType.Directional;
            var camera=new GameObject("City preview camera").AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=65;camera.nearClipPlane=.2f;camera.clearFlags=CameraClearFlags.Skybox;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;var chase=camera.gameObject.AddComponent<ChaseCamera>();
            var volume=new GameObject("City preview tone mapping").AddComponent<Volume>();volume.isGlobal=true;var profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            try {
                RaceLightingController.Apply(env.Plan.lighting,sun,null,null);
                var bounds=new Bounds(frames[0].position,Vector3.zero);foreach(var f in frames)bounds.Encapsulate(f.position);
                camera.transform.position=bounds.center+new Vector3(450,850,-900);camera.transform.LookAt(bounds.center+Vector3.up*70);chase.SetSkyline(env.Plan);Capture(camera,name+"-overview");
                var frame=frames[frames.Length/3];var forward=Vector3.ProjectOnPlane(frame.tangent,Vector3.up).normalized;
                camera.transform.position=frame.position-forward*15+Vector3.up*7;camera.transform.LookAt(frame.position+forward*110+Vector3.up*8);chase.SetSkyline(env.Plan);
                foreach(float density in new[]{.00155f,.0006f,.00045f}){RenderSettings.fogDensity=density;Capture(camera,name+"-fog-"+density.ToString("F5",System.Globalization.CultureInfo.InvariantCulture));}
                RenderSettings.fogDensity=env.Plan.lighting.fogDensity;
                for(int i=0;i<4;i++){float angle=i*Mathf.PI*.5f;var direction=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));camera.transform.LookAt(camera.transform.position+direction*1000+Vector3.up*30);Capture(camera,name+"-direction-"+i);}
            } finally {UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(volume.gameObject);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(sun.gameObject);}
        }
        static void Capture(Camera camera,string name) {
            var target=new RenderTexture(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
