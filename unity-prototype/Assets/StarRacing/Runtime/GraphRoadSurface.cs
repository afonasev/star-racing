using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using StarRacingPrototype.Procedural;

namespace StarRacingPrototype {
    // Physical hit inventory. Allowed geometry IDs are supplied by a traversal/admission
    // policy; this component deliberately never grants race progress or chooses a branch.
    public sealed class GraphRoadSurface:MonoBehaviour {
        public IndependentGraph Graph {get;private set;}
        public int BuildRevision {get;private set;}
        TrackSurface[] surfaces;
        MeshCollider[] colliders;
        int[] homogeneousGeometry;RoadFace[][] metadataArrays;
        BoundsSlab[] queryBounds; int[] boundsEpoch; int queryDepth, queryEpoch;
        int[] queryInventory, queryCandidates, queryWhitelist;
        int inventoryEpoch, inventoryCount, candidateCount, whitelistCount;
        bool hasWhitelist;
        int[] eligibilityEpoch;bool[] queryEligible;
        const int QueryGroupSize=16;
        BoundsSlab[] queryGroups;bool groupsReady;
        // Ordered groups only reject distant candidates. Every surviving chunk
        // still uses its original padded slab and native raycast in inventory order.
        void PrepareQueryGroups(int probe) {
            using var profile=GraphRuntimeProfile.SupportGroups.Auto();
            int groups=(candidateCount+QueryGroupSize-1)/QueryGroupSize;
            if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.groupBuilds++;GraphRuntimeProfile.SupportCounters.groupsBuilt+=groups;}
            for(int group=0;group<groups;group++) {
                int start=group*QueryGroupSize,end=Math.Min(candidateCount,start+QueryGroupSize);
                int index=queryCandidates[start];var bounds=LiveBounds(index,colliders[index],probe,true);
                for(int entry=start+1;entry<end;entry++) {
                    index=queryCandidates[entry];bounds=bounds.Union(LiveBounds(index,colliders[index],probe,true));
                }
                queryGroups[group]=bounds;
            }
            groupsReady=true;
        }
        // Only synchronous runtime operations use this scope. They do not change
        // road transforms/colliders or invoke user callbacks between their rays.
        // Outside a scope, every public query reads live native bounds as before.
        internal QueryBatch BeginQueryBatch() {
            if (queryDepth++ == 0) {
                if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.batches++;
                if (queryEpoch == int.MaxValue) { Array.Clear(boundsEpoch, 0, boundsEpoch.Length); Array.Clear(eligibilityEpoch, 0, eligibilityEpoch.Length); inventoryEpoch = 0; queryEpoch = 0; }
                queryEpoch++;
            }
            return new QueryBatch(this);
        }
        internal readonly struct QueryBatch : IDisposable {
            readonly GraphRoadSurface owner;
            internal QueryBatch(GraphRoadSurface owner) { this.owner = owner; }
            public void Dispose() { if (!ReferenceEquals(owner, null)) owner.queryDepth--; }
        }
        BoundsSlab LiveBounds(int index, MeshCollider collider,int probe,bool indexed) {
            if(GraphRuntimeProfile.SupportCountersEnabled){
                GraphRuntimeProfile.SupportCounters.boundsRequests++;GraphRuntimeProfile.SupportProbes[probe].boundsRequests++;
                if(indexed)GraphRuntimeProfile.SupportProbes[probe].indexedBoundsRequests++;else GraphRuntimeProfile.SupportProbes[probe].unindexedBoundsRequests++;
            }
            if (queryDepth == 0) {
                if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.boundsSnapshots++;GraphRuntimeProfile.SupportProbes[probe].boundsSnapshots++;
                    if(indexed)GraphRuntimeProfile.SupportProbes[probe].indexedBoundsSnapshots++;else GraphRuntimeProfile.SupportProbes[probe].unindexedBoundsSnapshots++;}
                return new BoundsSlab(collider.bounds);
            }
            if (boundsEpoch[index] != queryEpoch) {
                if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.boundsSnapshots++;GraphRuntimeProfile.SupportProbes[probe].boundsSnapshots++;
                    if(indexed)GraphRuntimeProfile.SupportProbes[probe].indexedBoundsSnapshots++;else GraphRuntimeProfile.SupportProbes[probe].unindexedBoundsSnapshots++;}
                using var profile=GraphRuntimeProfile.Detail(GraphRuntimeProfile.SupportBoundsSnapshot);
                queryBounds[index] = new BoundsSlab(collider.bounds); boundsEpoch[index] = queryEpoch;
            }
            return queryBounds[index];
        }
        // Editor diagnostics cross the assembly boundary through this small API.
        // These totals never affect road eligibility, hits or movement.
        public static bool SupportCountersEnabled=>GraphRuntimeProfile.SupportCountersEnabled;
        public static void ResetSupportCounters()=>GraphRuntimeProfile.ResetSupportCounters();
        public static string GetSupportCounterReport(int warmupTicks,int sampleTicks) {
            GraphRuntimeProfile.ValidateSupportCounters();
            return JsonUtility.ToJson(new GraphRuntimeProfile.SupportCounterReport {
                enabled=SupportCountersEnabled,warmupTicks=warmupTicks,sampleTicks=sampleTicks,groupSize=QueryGroupSize,
                counts=GraphRuntimeProfile.SupportCounters,probes=GraphRuntimeProfile.ProbeReports(),limitation="Opt-in instrumented attribution window after warmup; timing is not matched A/B baseline or acceptance"
            },true)+"\n";
        }
        readonly HashSet<Mesh> ownedMeshes=new HashSet<Mesh>();
        internal void Initialize(IndependentGraph graph,int revision) {
            Graph=graph;BuildRevision=revision;surfaces=GetComponentsInChildren<TrackSurface>();
            queryBounds=new BoundsSlab[surfaces.Length];boundsEpoch=new int[surfaces.Length];
            eligibilityEpoch=new int[surfaces.Length];queryEligible=new bool[surfaces.Length];
            queryInventory=new int[surfaces.Length];queryCandidates=new int[surfaces.Length];queryWhitelist=Array.Empty<int>();
            queryGroups=new BoundsSlab[(surfaces.Length+QueryGroupSize-1)/QueryGroupSize];groupsReady=false;
            inventoryEpoch=inventoryCount=candidateCount=whitelistCount=0;hasWhitelist=false;
            colliders=new MeshCollider[surfaces.Length];homogeneousGeometry=new int[surfaces.Length];metadataArrays=new RoadFace[surfaces.Length][];
            for(int i=0;i<surfaces.Length;i++){var surface=surfaces[i];surface.graphIdentity=graph.Identity;surface.graphBuildRevision=revision;colliders[i]=surface.GetComponent<MeshCollider>();RefreshChunk(i,surface);}
            foreach(var filter in GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh!=null)ownedMeshes.Add(filter.sharedMesh);
            foreach(var collider in GetComponentsInChildren<MeshCollider>())if(collider.sharedMesh!=null)ownedMeshes.Add(collider.sharedMesh);
        }
        void RefreshChunk(int index,TrackSurface surface){
            var faces=surface.triangleFaces;metadataArrays[index]=faces;int id=faces.Length==0?-1:faces[0].geometryId;
            for(int i=1;i<faces.Length;i++)if(faces[i].geometryId!=id){id=-1;break;}
            homogeneousGeometry[index]=id;
        }
        MeshCollider CurrentCollider(int index,TrackSurface surface) {
            var collider=colliders[index];
            if(collider==null) {colliders[index]=collider=surface.GetComponent<MeshCollider>();boundsEpoch[index]=0;eligibilityEpoch[index]=0;}
            return collider;
        }
        bool Eligible(int index,TrackSurface surface) {
            // Road/collider mutations are forbidden inside the existing batch.
            // Newly selected geometry reads live flags once in this same epoch.
            if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.eligibilityRequests++;
            if(eligibilityEpoch[index]!=queryEpoch) {
                if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.eligibilitySnapshots++;
                using var profile=GraphRuntimeProfile.Detail(GraphRuntimeProfile.SupportEligibility);
                var collider=CurrentCollider(index,surface);
                queryEligible[index]=collider!=null&&collider.enabled&&!collider.isTrigger&&collider.gameObject.activeInHierarchy;
                eligibilityEpoch[index]=queryEpoch;
            }
            return queryEligible[index];
        }
        void PrepareQueryCandidates(int[] ids,List<int> list) {
            // The existing scope forbids road/collider/metadata mutation and user
            // callbacks. Vehicle movement and whitelist value changes are allowed.
            // Only arrays and concrete Lists use this path; custom collections
            // retain the original live scan and their original enumeration behavior.
            if(inventoryEpoch!=queryEpoch) {
                if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.inventoryBuilds++;
                using var inventory=GraphRuntimeProfile.SupportInventory.Auto();
                inventoryCount=0;hasWhitelist=false;groupsReady=false;
                for(int i=0;i<surfaces.Length;i++) {
                    var surface=surfaces[i];
                    if(surface==null||surface.graphIdentity!=Graph.Identity||surface.graphBuildRevision!=BuildRevision)continue;
                    if(!ReferenceEquals(surface.triangleFaces,metadataArrays[i]))RefreshChunk(i,surface);
                    queryInventory[inventoryCount++]=i;
                }
                inventoryEpoch=queryEpoch;
                if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.inventoryEntries+=inventoryCount;
            }
            using var candidates=GraphRuntimeProfile.SupportCandidates.Auto();
            int count=ids!=null?ids.Length:list.Count;
            bool same=hasWhitelist&&count==whitelistCount;
            for(int i=0;same&&i<count;i++)same=queryWhitelist[i]==(ids!=null?ids[i]:list[i]);
            if(same)return;
            if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.candidateBuilds++;
            if(queryWhitelist.Length<count)queryWhitelist=new int[Math.Max(count,queryWhitelist.Length*2)];
            for(int i=0;i<count;i++)queryWhitelist[i]=ids!=null?ids[i]:list[i];
            whitelistCount=count;hasWhitelist=true;candidateCount=0;groupsReady=false;
            for(int i=0;i<inventoryCount;i++) {
                int index=queryInventory[i],geometry=homogeneousGeometry[index];
                bool allowed=geometry<0;
                for(int j=0;!allowed&&j<count;j++)allowed=queryWhitelist[j]==geometry;
                if(allowed&&CurrentCollider(index,surfaces[index])!=null)queryCandidates[candidateCount++]=index;
            }
            if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.candidateEntries+=candidateCount;
        }
        public bool Raycast(Ray ray,float maxDistance,IReadOnlyCollection<int> allowedGeometry,string identity,int revision,out RaycastHit hit,out RoadFace face)
            =>RaycastCore(ray,maxDistance,allowedGeometry,identity,revision,out hit,out face,GraphRuntimeProfile.SupportProbe.Unspecified);
        internal bool RaycastWithOrigin(Ray ray,float maxDistance,IReadOnlyCollection<int> allowedGeometry,string identity,int revision,out RaycastHit hit,out RoadFace face,GraphRuntimeProfile.SupportProbe origin)
            =>RaycastCore(ray,maxDistance,allowedGeometry,identity,revision,out hit,out face,origin);
        bool RaycastCore(Ray ray,float maxDistance,IReadOnlyCollection<int> allowedGeometry,string identity,int revision,out RaycastHit hit,out RoadFace face,GraphRuntimeProfile.SupportProbe originProbe) {
            using var profile=GraphRuntimeProfile.Support.Auto();
            int probe=GraphRuntimeProfile.ProbeIndex(originProbe); // diagnostic only; never an algorithm/cache key
            if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.raycasts++;GraphRuntimeProfile.SupportProbes[probe].rays++;}
            hit=default;face=default;if(Graph==null || identity!=Graph.Identity || revision!=BuildRevision || allowedGeometry==null)return false;
            if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.validRaycasts++;GraphRuntimeProfile.SupportProbes[probe].validRaycasts++;}
            var ids=allowedGeometry as int[];var list=allowedGeometry as List<int>;
            bool indexed=queryDepth>0&&(ids!=null||list!=null);
            if(GraphRuntimeProfile.SupportCountersEnabled) {
                if(indexed){GraphRuntimeProfile.SupportCounters.indexedRaycasts++;GraphRuntimeProfile.SupportProbes[probe].indexedRaycasts++;}
                else if(queryDepth==0)GraphRuntimeProfile.SupportProbes[probe].outsideBatch++;
                else GraphRuntimeProfile.SupportProbes[probe].inBatchUnsupportedCollection++;
            }
            if(indexed) {using var prepare=GraphRuntimeProfile.SupportPrepare.Auto();PrepareQueryCandidates(ids,list);}
            float best=float.PositiveInfinity;
            int count=indexed?candidateCount:surfaces.Length;
            using var scan=GraphRuntimeProfile.SupportScan.Auto();
            // Non-finite/negative queries retain the original IEEE/native behavior.
            var origin=ray.origin;var direction=ray.direction;
            bool grouped=indexed&&maxDistance>=0&&!float.IsInfinity(maxDistance)&&
                Finite(origin.x)&&Finite(origin.y)&&Finite(origin.z)&&Finite(direction.x)&&Finite(direction.y)&&Finite(direction.z);
            if(grouped&&GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.groupedRaycasts++;GraphRuntimeProfile.SupportProbes[probe].groupedRaycasts++;GraphRuntimeProfile.SupportCounters.groupedCandidateSlots+=count;}
            if(grouped&&!groupsReady)PrepareQueryGroups(probe);
            for(int entry=0;entry<count;entry++) {
                if(grouped&&entry%QueryGroupSize==0) {
                    if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.groupTests++;
                    if(!queryGroups[entry/QueryGroupSize].Intersects(ray,maxDistance)) {
                        if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.groupsRejected++;GraphRuntimeProfile.SupportCounters.groupCandidateSlotsSkipped+=Math.Min(QueryGroupSize,count-entry);}
                        entry+=QueryGroupSize-1;continue;
                    }
                }
                int surfaceIndex=indexed?queryCandidates[entry]:entry;
                var surface=surfaces[surfaceIndex];
                var collider=colliders[surfaceIndex];
                if(!indexed) {
                    if(surface==null || surface.graphIdentity!=identity || surface.graphBuildRevision!=revision)continue;
                    if(!ReferenceEquals(surface.triangleFaces,metadataArrays[surfaceIndex]))RefreshChunk(surfaceIndex,surface);
                    // Mixed chunks retain native face filtering. In-place changes
                    // to geometry IDs require the existing metadata replacement contract.
                    int geometry=homogeneousGeometry[surfaceIndex];
                    if(geometry>=0&&!Allows(allowedGeometry,geometry))continue;
                    if(collider==null)colliders[surfaceIndex]=collider=surface.GetComponent<MeshCollider>();
                    if(collider==null)continue;
                }
                // Live PhysX bounds cheaply exclude distant ramp chunks before
                // native state/raycast calls. Conservative padding only widens the
                // candidate set; actual hit metadata and normals stay authoritative.
                if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.chunkTests++;GraphRuntimeProfile.SupportProbes[probe].chunkTests++;if(grouped)GraphRuntimeProfile.SupportCounters.groupedChunkTests++;}
                if(!LiveBounds(surfaceIndex,collider,probe,indexed).Intersects(ray,maxDistance)) {
                    if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.chunkRejects++;GraphRuntimeProfile.SupportProbes[probe].chunkRejects++;}
                    continue;
                }
                if(indexed?!Eligible(surfaceIndex,surface):(!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy))continue;
                RaycastHit candidate;
                if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.nativeTests++;GraphRuntimeProfile.SupportProbes[probe].nativeTests++;}
                using(GraphRuntimeProfile.SupportNative.Auto()) {if(!collider.Raycast(ray,out candidate,maxDistance))continue;}
                if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.nativeHits++;GraphRuntimeProfile.SupportProbes[probe].nativeHits++;}
                if(candidate.triangleIndex<0 || candidate.triangleIndex>=surface.triangleFaces.Length)continue;
                var metadata=surface.triangleFaces[candidate.triangleIndex];
                if(!Allows(allowedGeometry,metadata.geometryId) || candidate.distance>=best)continue;
                if(GraphRuntimeProfile.SupportCountersEnabled){GraphRuntimeProfile.SupportCounters.acceptedHits++;GraphRuntimeProfile.SupportProbes[probe].acceptedHits++;}
                // Match legacy suspension: interpolate the actual emitted top normals.
                if(surface.sourceNormals!=null) {
                    if(GraphRuntimeProfile.SupportCountersEnabled)GraphRuntimeProfile.SupportCounters.normalInterpolations++;
                    using var normalProfile=GraphRuntimeProfile.SupportNormal.Auto();
                    int at=candidate.triangleIndex*3;var weights=candidate.barycentricCoordinate;
                    var indices=surface.sourceTriangles;var normals=surface.sourceNormals;
                    var normal=normals[indices[at]]*weights.x+normals[indices[at+1]]*weights.y+normals[indices[at+2]]*weights.z;
                    candidate.normal=surface.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normal).normalized;
                }
                best=candidate.distance;hit=candidate;face=metadata;
            }
            return best<float.PositiveInfinity;
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool Allows(IReadOnlyCollection<int> allowed,int geometry){
            // Mono's array ICollection.Contains boxes each compared int. These
            // admission whitelists are arrays; compare values without boxing.
            if(allowed is int[] ids){for(int i=0;i<ids.Length;i++)if(ids[i]==geometry)return true;return false;}
            if(allowed is HashSet<int> set)return set.Contains(geometry);
            if(allowed is IReadOnlyList<int> list){for(int i=0;i<list.Count;i++)if(list[i]==geometry)return true;return false;}
            foreach(int id in allowed)if(id==geometry)return true;return false;
        }
        readonly struct BoundsSlab {
            readonly double loX,loY,loZ,hiX,hiY,hiZ;
            public BoundsSlab(Bounds bounds) {
                var lo=bounds.min;var hi=bounds.max;
                // Identical float magnitude and double padding to the original
                // query. Prepare once per live bounds snapshot, never per ray.
                double magnitude=Math.Max(Math.Max(Math.Abs(lo.x),Math.Abs(hi.x)),Math.Max(Math.Max(Math.Abs(lo.y),Math.Abs(hi.y)),Math.Max(Math.Abs(lo.z),Math.Abs(hi.z))));
                double padding=.001+magnitude*1e-6;
                loX=lo.x-padding;loY=lo.y-padding;loZ=lo.z-padding;
                hiX=hi.x+padding;hiY=hi.y+padding;hiZ=hi.z+padding;
            }
            BoundsSlab(double loX,double loY,double loZ,double hiX,double hiY,double hiZ) {
                this.loX=loX;this.loY=loY;this.loZ=loZ;this.hiX=hiX;this.hiY=hiY;this.hiZ=hiZ;
            }
            public BoundsSlab Union(BoundsSlab other)=>new BoundsSlab(
                Math.Min(loX,other.loX),Math.Min(loY,other.loY),Math.Min(loZ,other.loZ),
                Math.Max(hiX,other.hiX),Math.Max(hiY,other.hiY),Math.Max(hiZ,other.hiZ));
            static bool Axis(double a,double b,double o,double d,ref double enter,ref double leave) {
                if(d==0)return !(o<a||o>b);
                double x=(a-o)/d,y=(b-o)/d;if(x>y){var swap=x;x=y;y=swap;}
                enter=Math.Max(enter,x);leave=Math.Min(leave,y);return !(enter>leave);
            }
            public bool Intersects(Ray ray,float distance) {
                var origin=ray.origin;var direction=ray.direction;
                double enter=0,leave=distance;
                // Original axis order and arithmetic; avoid Vector3 indexers.
                return Axis(loX,hiX,origin.x,direction.x,ref enter,ref leave)&&
                    Axis(loY,hiY,origin.y,direction.y,ref enter,ref leave)&&
                    Axis(loZ,hiZ,origin.z,direction.z,ref enter,ref leave);
            }
        }
        public bool TryBounds(GraphFrame frame,int geometry,float lane,out float left,out float right) {
            left=right=0;var intervals=new List<Vector2>();
            foreach(var surface in surfaces)for(int q=0;q<surface.sourceVertices.Length/4;q++) {
                if(surface.triangleFaces[q*2].geometryId!=geometry)continue;
                var cuts=new List<float>();int[] order={0,1,3,2,0};
                for(int k=0;k<4;k++) {
                    Vector3 a=surface.transform.TransformPoint(surface.sourceVertices[q*4+order[k]])-frame.position.Float;
                    Vector3 b=surface.transform.TransformPoint(surface.sourceVertices[q*4+order[k+1]])-frame.position.Float;
                    // Canonical endpoint order and double interpolation make a shared edge
                    // yield the same cut from either polygon. No gap-filling tolerance.
                    bool reverse=a.x>b.x || a.x==b.x && (a.y>b.y || a.y==b.y && a.z>b.z);
                    if(reverse){var swap=a;a=b;b=swap;}
                    var da=new DVec(a.x,a.y,a.z);var db=new DVec(b.x,b.y,b.z);
                    double x=DVec.Dot(da,frame.tangent),y=DVec.Dot(db,frame.tangent);
                    if(x==0)cuts.Add((float)-DVec.Dot(da,frame.right));
                    if(y==0)cuts.Add((float)-DVec.Dot(db,frame.right));
                    if(x*y<0)cuts.Add((float)-DVec.Dot(da+(db-da)*(-x/(y-x)),frame.right));
                }
                if(cuts.Count>=2)intervals.Add(new Vector2(cuts.Min(),cuts.Max()));
            }
            intervals.Sort((a,b)=>a.x.CompareTo(b.x));
            for(int i=0;i<intervals.Count;i++) {
                var span=intervals[i];while(i+1<intervals.Count && intervals[i+1].x<=span.y){span.y=Mathf.Max(span.y,intervals[++i].y);}
                if(lane>=span.x && lane<=span.y){left=span.x;right=span.y;return true;}
            }
            return false;
        }
        void OnDestroy(){foreach(var mesh in ownedMeshes)if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}ownedMeshes.Clear();}
    }
}
