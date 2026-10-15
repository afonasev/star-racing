using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 public static class GraphRaycastOptimizationChecks {
  static readonly MethodInfo BeginBatch=typeof(GraphRoadSurface).GetMethod("BeginQueryBatch",BindingFlags.Instance|BindingFlags.NonPublic);
  static IDisposable Batch(GraphRoadSurface surface)=>(IDisposable)BeginBatch.Invoke(surface,null);
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph live bounds parity: "+why);}
  static bool Reference(TrackSurface[] surfaces,Ray ray,float length,IReadOnlyCollection<int> allowed,string identity,int revision,out RaycastHit hit,out RoadFace face){
   hit=default;face=default;float best=float.PositiveInfinity;
   foreach(var surface in surfaces){if(surface==null||surface.graphIdentity!=identity||surface.graphBuildRevision!=revision)continue;var collider=surface.GetComponent<MeshCollider>();if(collider==null||!collider.enabled||collider.isTrigger||!collider.gameObject.activeInHierarchy||!collider.Raycast(ray,out var candidate,length))continue;
    if(candidate.triangleIndex<0||candidate.triangleIndex>=surface.triangleFaces.Length)continue;var metadata=surface.triangleFaces[candidate.triangleIndex];if(!allowed.Contains(metadata.geometryId)||candidate.distance>=best)continue;
    if(surface.sourceNormals!=null){int at=candidate.triangleIndex*3;var weights=candidate.barycentricCoordinate;var indices=surface.sourceTriangles;var normals=surface.sourceNormals;var normal=normals[indices[at]]*weights.x+normals[indices[at+1]]*weights.y+normals[indices[at+2]]*weights.z;candidate.normal=surface.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normal).normalized;}
    best=candidate.distance;hit=candidate;face=metadata;
   }return best<float.PositiveInfinity;
  }
  static void Compare(GraphRoadSurface surface,TrackSurface[] tops,Ray ray,float length,IReadOnlyCollection<int> allowed){
   bool a=Reference(tops,ray,length,allowed,surface.Graph.Identity,surface.BuildRevision,out var expected,out var before),b=surface.Raycast(ray,length,allowed,surface.Graph.Identity,surface.BuildRevision,out var actual,out var after);
   Check(a==b,"same hit/miss");if(!a)return;
   Check(expected.collider==actual.collider&&expected.triangleIndex==actual.triangleIndex&&expected.distance.Equals(actual.distance)&&expected.point.Equals(actual.point)&&expected.normal.Equals(actual.normal)&&expected.barycentricCoordinate.Equals(actual.barycentricCoordinate),"exact native hit/normal/owner bytes");
   Check(before.Equals(after),"all RoadFace fields exactly equal");
  }
  static void InventoryCases(GraphRoadSurface surface,GameObject root,Ray ray) {
   var tops=root.GetComponentsInChildren<TrackSurface>();
   Check(Reference(tops,ray,4,new[]{0},surface.Graph.Identity,surface.BuildRevision,out var initial,out _),"inventory case has actual native hit");
   var top=initial.collider.GetComponent<TrackSurface>();var original=top.triangleFaces;
   var copy=new GameObject("Equal-distance later owner");copy.transform.SetParent(top.transform.parent,false);
   copy.transform.localPosition=top.transform.localPosition;copy.transform.localRotation=top.transform.localRotation;copy.transform.localScale=top.transform.localScale;
   var duplicate=copy.AddComponent<TrackSurface>();duplicate.graphIdentity=top.graphIdentity;duplicate.graphBuildRevision=top.graphBuildRevision;
   duplicate.triangleFaces=(RoadFace[])original.Clone();duplicate.sourceTriangles=top.sourceTriangles;duplicate.sourceNormals=top.sourceNormals;
   copy.AddComponent<MeshCollider>().sharedMesh=((MeshCollider)initial.collider).sharedMesh;
   typeof(GraphRoadSurface).GetMethod("Initialize",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(surface,new object[]{surface.Graph,surface.BuildRevision});
   tops=root.GetComponentsInChildren<TrackSurface>();Physics.SyncTransforms();
   var ids=new[]{0};var list=new List<int>{0};
   using(Batch(surface)) {
    Check(surface.Raycast(ray,4,ids,surface.Graph.Identity,surface.BuildRevision,out var tied,out _)&&tied.collider==initial.collider,"equal-distance hit keeps original surface owner");
    Compare(surface,tops,ray,4,ids);ids[0]=991;Compare(surface,tops,ray,4,ids);ids[0]=0;
    using(Batch(surface)) {Compare(surface,tops,ray,4,ids);Compare(surface,tops,ray,4,list);list[0]=991;Compare(surface,tops,ray,4,list);}
    list[0]=0;list.Add(0);Compare(surface,tops,ray,4,list);list.Add(991);Compare(surface,tops,ray,4,list);
    list.RemoveAt(0);Compare(surface,tops,ray,4,list);list.Clear();Compare(surface,tops,ray,4,list);list.Add(0);Compare(surface,tops,ray,4,list);
    Compare(surface,tops,ray,4,Array.Empty<int>());Compare(surface,tops,ray,4,new[]{0,0});
    Compare(surface,tops,ray,4,new HashSet<int>{0});Compare(surface,tops,ray,4,new Queue<int>(new[]{0}));Compare(surface,tops,ray,4,ids);
   }
   // Start with an unrelated/empty whitelist, then select the actual geometry.
   // Native state must be observed for newly selected chunks in this same scope.
   void Deferred(){
    var mutable=new[]{991};var values=new List<int>();
    using(Batch(surface)) {
     Compare(surface,tops,ray,4,mutable);Compare(surface,tops,ray,4,values);
     mutable[0]=0;Compare(surface,tops,ray,4,mutable);
     values.Add(0);using(Batch(surface))Compare(surface,tops,ray,4,values);
     mutable[0]=991;Compare(surface,tops,ray,4,mutable);mutable[0]=0;Compare(surface,tops,ray,4,mutable);
    }
   }
   var exitFrame=surface.Graph.Road(5).samples[100];var exitRay=new Ray(exitFrame.position.Float+exitFrame.normal.Float*2,-exitFrame.normal.Float);
   Check(Reference(tops,exitRay,4,new[]{5},surface.Graph.Identity,surface.BuildRevision,out _,out _),"deferred exit probe has native support");
   using(Batch(surface)) {
    var values=new[]{0};Compare(surface,tops,ray,4,values);
    Compare(surface,tops,exitRay,4,values);values[0]=5;Compare(surface,tops,exitRay,4,values);
    values[0]=0;Compare(surface,tops,ray,4,values);values[0]=5;using(Batch(surface))Compare(surface,tops,exitRay,4,values);
   }
   // Each mutation occurs between scopes. Live public queries also see it immediately.
   void Both(){Compare(surface,tops,ray,4,ids);using(Batch(surface)){Compare(surface,tops,ray,4,ids);Compare(surface,tops,ray,4,list);}Deferred();}
   Both();top.graphIdentity="foreign";Both();top.graphIdentity=surface.Graph.Identity;
   top.graphBuildRevision++;Both();top.graphBuildRevision=surface.BuildRevision;
   var collider=top.GetComponent<MeshCollider>();collider.enabled=false;Both();collider.enabled=true;
   var triggerMesh=new Mesh();triggerMesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward};triggerMesh.triangles=new[]{0,2,1,0,1,3,0,3,2,1,2,3};
   var sourceMesh=collider.sharedMesh;collider.sharedMesh=triggerMesh;collider.convex=true;collider.isTrigger=true;Both();collider.isTrigger=false;collider.convex=false;collider.sharedMesh=sourceMesh;UnityEngine.Object.DestroyImmediate(triggerMesh);
   top.gameObject.SetActive(false);Both();top.gameObject.SetActive(true);
   top.transform.localScale*=1.03f;Physics.SyncTransforms();Both();top.transform.localScale=copy.transform.localScale;Physics.SyncTransforms();
   var mixed=(RoadFace[])original.Clone();for(int i=0;i<mixed.Length;i++){var face=mixed[i];face.geometryId=991;mixed[i]=face;}
   var selected=mixed[initial.triangleIndex];selected.geometryId=0;selected.arm=73;selected.startPort=101;selected.endPort=102;
   selected.leftBoundaryTag=201;selected.rightBoundaryTag=202;selected.startSeamOffset=.125f;selected.endSeamOffset=.375f;
   selected.canonicalFlatPorts=!selected.canonicalFlatPorts;selected.ramp=!selected.ramp;mixed[initial.triangleIndex]=selected;
   top.triangleFaces=mixed;Both();top.triangleFaces=original;Both();
   var mesh=collider.sharedMesh;UnityEngine.Object.DestroyImmediate(collider);top.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;Physics.SyncTransforms();Both();
   typeof(GraphRoadSurface).GetField("queryEpoch",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(surface,int.MaxValue);Both();
   Check(!surface.Raycast(ray,4,ids,"foreign",surface.BuildRevision,out _,out _),"reject foreign query identity");
   Check(!surface.Raycast(ray,4,ids,surface.Graph.Identity,surface.BuildRevision+1,out _,out _),"reject foreign query revision");
  }
  static void DeferredEligibilityGroupCases(GraphRoadSurface surface,GameObject root) {
   var tops=root.GetComponentsInChildren<TrackSurface>();var all=surface.Graph.roads.Select(r=>r.id).Distinct().ToArray();
   int[] slots={0,7,15,16,31,32};Check(tops.Length>33,"full ordered inventory covers group boundaries");
   var affected=slots.Select(i=>tops[i]).ToArray();var neighbors=slots.Select(i=>tops[i+1]).ToArray();
   Ray Probe(TrackSurface t){var mesh=t.GetComponent<MeshCollider>().sharedMesh;var v=mesh.vertices;var tri=mesh.triangles;var a=v[tri[0]];var b=v[tri[1]];var c=v[tri[2]];var n=t.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(Vector3.Cross(b-a,c-a).normalized).normalized;return new Ray(t.transform.TransformPoint((a+b+c)/3)+n*2,-n);}
   var rays=affected.Concat(neighbors).Select(Probe).ToArray();
   foreach(var ray in rays)Check(Reference(tops,ray,4,all,surface.Graph.Identity,surface.BuildRevision,out _,out _),"boundary/neighbor ray has actual native support");
   void CheckState(){Physics.SyncTransforms();using(Batch(surface)){
    Compare(surface,tops,new Ray(new Vector3(1000000,1000000,1000000),Vector3.down),4,all);
    foreach(var ray in rays){Compare(surface,tops,ray,4,all);using(Batch(surface))Compare(surface,tops,ray,4,all);}
    Compare(surface,tops,rays[0],4,Array.Empty<int>());Compare(surface,tops,rays[0],4,new[]{991});Compare(surface,tops,rays[0],4,all);
   }}
   var colliders=affected.Select(t=>t.GetComponent<MeshCollider>()).ToArray();var meshes=colliders.Select(c=>c.sharedMesh).ToArray();var enabled=colliders.Select(c=>c.enabled).ToArray();var active=affected.Select(t=>t.gameObject.activeSelf).ToArray();var positions=affected.Select(t=>t.transform.localPosition).ToArray();
   var tetra=new Mesh();tetra.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward};tetra.triangles=new[]{0,2,1,0,1,3,0,3,2,1,2,3};
   try{
    CheckState();foreach(var c in colliders)c.enabled=false;CheckState();for(int i=0;i<colliders.Length;i++)colliders[i].enabled=enabled[i];
    foreach(var t in affected)t.gameObject.SetActive(false);CheckState();for(int i=0;i<affected.Length;i++)affected[i].gameObject.SetActive(active[i]);
    for(int i=0;i<colliders.Length;i++){colliders[i].sharedMesh=tetra;colliders[i].convex=true;colliders[i].isTrigger=true;}CheckState();
    affected[0].transform.localPosition+=new Vector3(100000,50000,-100000);CheckState();affected[0].transform.localPosition=positions[0];
    for(int i=0;i<colliders.Length;i++){colliders[i].isTrigger=false;colliders[i].convex=false;colliders[i].sharedMesh=meshes[i];}CheckState();
    UnityEngine.Object.DestroyImmediate(colliders[0]);CheckState();colliders[0]=affected[0].gameObject.AddComponent<MeshCollider>();colliders[0].sharedMesh=meshes[0];colliders[0].enabled=enabled[0];CheckState();
    typeof(GraphRoadSurface).GetField("queryEpoch",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(surface,int.MaxValue);CheckState();
   }finally{
    for(int i=0;i<affected.Length;i++){affected[i].gameObject.SetActive(active[i]);affected[i].transform.localPosition=positions[i];if(colliders[i]==null)colliders[i]=affected[i].gameObject.AddComponent<MeshCollider>();colliders[i].isTrigger=false;colliders[i].convex=false;colliders[i].sharedMesh=meshes[i];colliders[i].enabled=enabled[i];}
    UnityEngine.Object.DestroyImmediate(tetra);Physics.SyncTransforms();
   }
  }
  static void GroupRayCases(GraphRoadSurface surface,TrackSurface[] tops) {
   var rng=new System.Random(761241);var all=new[]{0,1,2,3,4,5};
   using(Batch(surface)) {
    foreach(var road in surface.Graph.roads)for(int i=0;i<road.samples.Count;i+=17) {
     var frame=road.samples[i];
     foreach(var direction in new[]{frame.tangent.Float,-frame.tangent.Float,frame.right.Float,-frame.normal.Float,frame.normal.Float})
     foreach(float length in new[]{0f,4f,1000f})
      Compare(surface,tops,new Ray(frame.position.Float+frame.normal.Float*.1f,direction),length,all);
    }
    for(int i=0;i<512;i++) {
     var road=surface.Graph.roads[rng.Next(surface.Graph.roads.Count)];var frame=road.Evaluate(rng.NextDouble()*road.Length);
     var origin=frame.position.Float+new Vector3((float)(rng.NextDouble()*40-20),(float)(rng.NextDouble()*40-20),(float)(rng.NextDouble()*40-20));
     var direction=new Vector3((float)(rng.NextDouble()*2-1),(float)(rng.NextDouble()*2-1),(float)(rng.NextDouble()*2-1));
     Compare(surface,tops,new Ray(origin,direction),(float)(rng.NextDouble()*2000),all);
    }
   }
  }
  [Serializable] sealed class FlagsTiming {public string state;public int queriesPerPass,warmupPasses,samplePasses;public double medianPassMs,p95PassMs;}
  public static void RunDeferredEligibilityTiming(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio"))throw new Exception("Flags benchmark requires silent startup");
   var output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_FLAGS_QA_DIR");Directory.CreateDirectory(output);assertions=0;
   var graph=GraphCommonCatalogChecks.Geometry();var root=new GameObject("Flags-heavy exact native benchmark");var results=new List<FlagsTiming>();
   try{
    var surface=IndependentTrackMesh.BuildCommonCatalog(root.transform,graph,81,null,null,(_,__)=>{});Physics.SyncTransforms();
    var tops=root.GetComponentsInChildren<TrackSurface>();var colliders=tops.Select(t=>t.GetComponent<MeshCollider>()).ToArray();var meshes=colliders.Select(c=>c.sharedMesh).ToArray();var positions=tops.Select(t=>t.transform.localPosition).ToArray();
    var all=graph.roads.Select(r=>r.id).Distinct().ToArray();var rays=graph.roads.SelectMany(r=>r.samples.Where((_,i)=>i%17==0)).Take(128).Select(f=>new Ray(f.position.Float+f.normal.Float*2,-f.normal.Float)).ToArray();Check(rays.Length==128,"all128fixed real geometry rays preserved");
    var tetra=new Mesh();tetra.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward};tetra.triangles=new[]{0,2,1,0,1,3,0,3,2,1,2,3};
    try{
     foreach(string state in new[]{"active","half-disabled","half-inactive","half-trigger-with-far-union"}){
      for(int i=0;i<tops.Length;i++){
       tops[i].gameObject.SetActive(state!="half-inactive"||i%2!=0);colliders[i].enabled=state!="half-disabled"||i%2!=0;
       colliders[i].isTrigger=false;colliders[i].convex=false;colliders[i].sharedMesh=meshes[i];tops[i].transform.localPosition=positions[i];
       if(state=="half-trigger-with-far-union"&&i%2==0){colliders[i].sharedMesh=tetra;colliders[i].convex=true;colliders[i].isTrigger=true;}
      }
      if(state=="half-trigger-with-far-union")tops[0].transform.localPosition+=new Vector3(100000,50000,-100000);Physics.SyncTransforms();
      using(Batch(surface))foreach(var ray in rays)Compare(surface,tops,ray,4,all);
      void Pass(){for(int i=0;i<rays.Length;i+=4)using(Batch(surface))for(int j=i;j<i+4;j++)surface.Raycast(rays[j],4,all,surface.Graph.Identity,surface.BuildRevision,out _,out _);}
      for(int i=0;i<10;i++)Pass();var samples=new double[30];
      for(int i=0;i<samples.Length;i++){long start=System.Diagnostics.Stopwatch.GetTimestamp();Pass();samples[i]=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency;}
      Array.Sort(samples);results.Add(new FlagsTiming{state=state,queriesPerPass=128,warmupPasses=10,samplePasses=30,medianPassMs=samples[15],p95PassMs=samples[28]});
     }
    }finally{
     for(int i=0;i<tops.Length;i++){tops[i].gameObject.SetActive(true);tops[i].transform.localPosition=positions[i];colliders[i].enabled=true;colliders[i].isTrigger=false;colliders[i].convex=false;colliders[i].sharedMesh=meshes[i];}UnityEngine.Object.DestroyImmediate(tetra);Physics.SyncTransforms();
    }
   }finally{UnityEngine.Object.DestroyImmediate(root);}
   File.WriteAllText(Path.Combine(output,"flags-timing.ndjson"),string.Join("\n",results.Select(r=>JsonUtility.ToJson(r)))+"\n");
   File.WriteAllText(Path.Combine(output,"result.txt"),"GRAPH_FLAGS_TIMING_OK assertions="+assertions+" states=4 queries=128 warmupPasses=10 samplePasses=30 sameNativeHitFace scope=existing4rayBatch; microbenchmark includes shared reflection overhead, not whole64car acceptance\n");
  }
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_RAYCAST_QA_DIR");Directory.CreateDirectory(output);assertions=0;
   foreach(bool transformed in new[]{false,true})foreach(string mode in new[]{"normal","none","full"}){
    var graph=GraphCommonCatalogChecks.Geometry(mode,transformed:transformed);var root=new GameObject("Actual collider oracle");
    try{var surface=IndependentTrackMesh.BuildCommonCatalog(root.transform,graph,81,null,null,(_,__)=>{});Physics.SyncTransforms();var tops=root.GetComponentsInChildren<TrackSurface>();
     File.WriteAllText(Path.Combine(output,mode+"-"+transformed+".txt"),$"topChunks={tops.Length} approachChunks={tops.Count(t=>t.triangleFaces.All(f=>f.geometryId==0))}\n");
     using(Batch(surface))
     foreach(var road in graph.roads)foreach(var frame in road.samples)foreach(double lane in new[]{-8.5d,-7,0,7,8.5}){
      int geometry=road.id==10||road.id==11?3:road.id==12||road.id==13?4:road.id;var point=(frame.position+frame.right*lane).Float;
      foreach(float height in new[]{.1f,2f,5f})Compare(surface,tops,new Ray(point+frame.normal.Float*height,-frame.normal.Float),height+1,new[]{geometry});
     }
     GroupRayCases(surface,tops);
     // Collider flags and transforms are live, including same-frame mutations.
     var first=tops.First();var collider=first.GetComponent<MeshCollider>();var f=graph.Road(0).samples[10];var ray=new Ray(f.position.Float+f.normal.Float*2,-f.normal.Float);
     collider.enabled=false;using(Batch(surface))Compare(surface,tops,ray,4,new[]{0});collider.enabled=true;
     first.transform.localPosition+=new Vector3(.04f,.03f,.02f);Physics.SyncTransforms();using(Batch(surface))Compare(surface,tops,ray,4,new[]{0});
     first.gameObject.SetActive(false);using(Batch(surface))Compare(surface,tops,ray,4,new[]{0});first.gameObject.SetActive(true);Physics.SyncTransforms();using(Batch(surface))Compare(surface,tops,ray,4,new[]{0});
     InventoryCases(surface,root,ray);DeferredEligibilityGroupCases(surface,root);
    }finally{UnityEngine.Object.DestroyImmediate(root);}
   }
   File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_RAYCAST_EXACT_OK assertions={assertions} modes=3 transforms=2 lanes=5 heights=3 disabled moved inactive liveBounds scopedBounds originalColliderOracle whitelistValueAndCountChanges nestedScopes equalDistanceOwners mixedMetadataReplacement colliderReplacement stamps epochOverflow allFaceFields orderedGroupRays seed761241 deferredWhitelistEligibility lazyFlagsGroupBoundaryDisabledInactiveTriggerFarUnionMissingReplacementLateReached\n");Debug.Log("GRAPH_RAYCAST_EXACT_OK assertions="+assertions);
  }
 }
}
