using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Debug=UnityEngine.Debug;
namespace StarRacingPrototype {
 public static class OwnerPreparationChecks {
  static int checks;
  sealed class ReferenceCell {
   const float Thickness=RoadVolumeMesh.Thickness;
   public readonly int id;public readonly RoadFace face;public readonly Vector3[] corners;public readonly int[] triangles;public readonly int[][] sides;public readonly bool[] sections;
            public ReferenceCell(int id,RoadFace face,Vector3[] top,Vector3[] normals,int[] perimeter,IList<int> topTriangles,int topOffset)
            {
                this.id=id;this.face=face;corners=new Vector3[8];sides=new int[4][];sections=new bool[4];
                for(int i=0;i<4;i++) {corners[i]=top[i];corners[i+4]=top[i]-normals[i]*Thickness;}
                var indices=new List<int>();
                for(int i=0;i<6;i++)indices.Add(topTriangles[topOffset*6+i]-topOffset*4);
                AddQuad(indices,4,5,6,7,-normals[0]);
                var distances=new[]{face.startDistance,face.endDistance,face.startDistance,face.endDistance};
                var laterals=new[]{face.leftStart,face.leftEnd,face.rightStart,face.rightEnd};
                for(int i=0;i<4;i++) {
                    int a=perimeter[i],b=perimeter[i+1];bool section=distances[a]==distances[b];
                    float x=section?laterals[a]:distances[a],y=section?laterals[b]:distances[b];int low=x<y?a:b,high=x<y?b:a;sides[i]=new[]{low,high,low+4,high+4};sections[i]=section;
                    // Match EmitBands' parameter order and diagonal, including reversed edges.
                    AddQuad(indices,low,high,low+4,high+4,Vector3.Cross(top[high]-top[low],normals[low])*(x<y?1:-1));
                }
                triangles=indices.ToArray();
            }
            void AddQuad(List<int> indices,int a,int b,int c,int d,Vector3 outward)
            {
                if(Vector3.Dot(Vector3.Cross(corners[b]-corners[a],corners[c]-corners[a]),outward)>0)indices.AddRange(new[]{a,b,c,c,b,d});
                else indices.AddRange(new[]{a,c,b,c,d,b});
            }
        }
  static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
  static bool Bits(Vector3 a,Vector3 b)=>Enumerable.Range(0,3).All(i=>BitConverter.SingleToInt32Bits(a[i])==BitConverter.SingleToInt32Bits(b[i]));
  static void Same(ReferenceCell a,RoadVolumeMesh.SupportCell b){
   Check(a.id==b.id,"owner id");for(int i=0;i<8;i++)Check(Bits(a.corners[i],b.corners[i]),"owner corner bits");
   Check(a.triangles.SequenceEqual(b.triangles),"owner triangle order/winding");Check(a.sections.SequenceEqual(b.sections),"owner section order");
   for(int i=0;i<4;i++)Check(a.sides[i].SequenceEqual(b.sides[i]),"owner side order");
  }
  public static void Run(){
   checks=0;int cells=0;
   foreach(bool reverse in new[]{false,true})foreach(float shift in new[]{0f,1024f,-4096f})foreach(float warp in new[]{0f,.001f,.3f})foreach(float length in new[]{.0001f,1f,5f})foreach(bool port in new[]{false,true}){
    var origin=new Vector3(shift,shift/16,shift/4);
    var points=new[]{origin,origin+new Vector3(0,warp,length),origin+new Vector3(1,0,0),origin+new Vector3(1,warp,length)};
    var normals=new[]{Vector3.up,new Vector3(warp,1,0),Vector3.up,new Vector3(warp,1,0)};
    var perimeter=reverse?new[]{0,2,3,1,0}:new[]{0,1,3,2,0};
    var face=new RoadFace{startDistance=0,endDistance=length,leftStart=0,leftEnd=0,rightStart=1,rightEnd=1,startPort=port?17:0,endPort=port?18:0,canonicalFlatPorts=port};
    var indices=new[]{0,1,2,2,1,3,4,5,6,6,5,7};
    var reference=new ReferenceCell(cells,face,points,normals,perimeter,indices,1);
    int materialized=0;RoadVolumeMesh.CellMaterialized=(_,__)=>materialized++;
    try{
     var get=RoadVolumeMesh.EditorPendingCell(cells,face,points,normals,perimeter,indices,1);
     Check(materialized==0,"pending cell is lazy");
     for(int i=0;i<4;i++){points[i]=Vector3.one*999;normals[i]=Vector3.zero;perimeter[i]=0;}Array.Clear(indices,0,indices.Length);
     var actual=get();Same(reference,actual);Check(materialized==1&&ReferenceEquals(actual,get())&&materialized==1,"one materialization/shared owner");cells++;
    }finally{RoadVolumeMesh.CellMaterialized=null;}
   }
   string output=Environment.GetEnvironmentVariable("STAR_RACING_WORLD_QA_DIR");Directory.CreateDirectory(output);
   File.WriteAllText(Path.Combine(output,"owner-contracts.json"),$"{{\"checks\":{checks},\"cells\":{cells},\"oracle\":\"frozen original eager constructor\"}}");
   SupportLoadingProfile.RunContracts();
   Debug.Log("OWNER_PREPARATION_CONTRACTS_OK checks="+checks+" cells="+cells);
  }
  public static void RunDataContracts(){Run();SupportLoadingProfile.RunIndependentContracts();Debug.Log("OWNER_PREPARATION_DATA_OK");}
  public static void RunAll(){
   Run();SupportLoadingProfile.RunIndependentContracts();RunWorkers();Debug.Log("OWNER_PREPARATION_ALL_OK");
  }
  public static void RunWorkers(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_WORLD_QA_DIR");Directory.CreateDirectory(output);
   var course=NativeTrackGenerator.Generate(5,new StarRacingPrototype.Procedural.GraphCourseSettings("space-station","normal",true),12);
   var root=new GameObject("Owner worker data source");var source=new List<RoadVolumeMesh.SupportTriangle>();BeginBenchmark();
   try{
    var surface=IndependentTrackMesh.BuildCommonCatalog(root.transform,course.Graph,12,null,null,(_,triangle)=>source.Add(triangle));
    if(source.Count!=72608)throw new Exception("Worker source coverage changed: "+source.Count);
    Benchmark(source.ToArray(),output);
    Debug.Log("OWNER_WORKER_BENCHMARK_OK supports="+source.Count);
   }finally{
    RoadVolumeMesh.CellMaterialized=null;factories.Clear();
    foreach(var collider in root.GetComponentsInChildren<MeshCollider>(true))if(collider.sharedMesh!=null)UnityEngine.Object.DestroyImmediate(collider.sharedMesh);
    foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh!=null)UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
    UnityEngine.Object.DestroyImmediate(root);
   }
  }
  static readonly Dictionary<RoadVolumeMesh.SupportCell,Func<RoadVolumeMesh.SupportCell>> factories=new Dictionary<RoadVolumeMesh.SupportCell,Func<RoadVolumeMesh.SupportCell>>();
  public static void BeginBenchmark(){factories.Clear();RoadVolumeMesh.CellMaterialized=(cell,create)=>factories.Add(cell,create);}
  struct Result {public Vector3 apex;public bool fallback;}
  [Serializable] sealed class Trial {public string kind,mode;public double seconds;public int triangles,owners;}
  public static void Benchmark(RoadVolumeMesh.SupportTriangle[] source,string output){
   RoadVolumeMesh.CellMaterialized=null;
   try{
    var clock=Stopwatch.StartNew();
    var groups=source.Select((t,i)=>(t,i)).GroupBy(x=>x.t.owner).Select(g=>(create:factories[g.Key],items:g.ToArray())).ToArray();
    double grouping=clock.Elapsed.TotalSeconds;var expected=new Result[source.Length];
    for(int i=0;i<source.Length;i++){expected[i].apex=source[i].SelectApex(out expected[i].fallback);}
    var trials=new List<Trial>();
    foreach(bool apex in new[]{false,true})foreach(int degree in new[]{1,2,2,1}){
     var results=new Result[source.Length];var owners=new RoadVolumeMesh.SupportCell[groups.Length];clock.Restart();
     void Work(int at){var group=groups[at];var owner=group.create();owners[at]=owner;if(!apex)return;foreach(var item in group.items){var t=item.t;var copy=new RoadVolumeMesh.SupportTriangle(owner,t.side,t.a,t.b,t.c,t.uvA,t.uvB,t.uvC,t.diagonal);results[item.i].apex=copy.SelectApex(out results[item.i].fallback);}}
     if(degree==1){for(int i=0;i<groups.Length;i++)Work(i);}else Parallel.For(0,groups.Length,new ParallelOptions{MaxDegreeOfParallelism=degree},Work);
     clock.Stop();trials.Add(new Trial{kind=apex?"owners+apex":"owners",mode=degree==1?"serial":"parallel2",seconds=clock.Elapsed.TotalSeconds,triangles=source.Length,owners=groups.Length});
     for(int i=0;i<groups.Length;i++){var before=groups[i].items[0].t.owner;var after=owners[i];if(before.id!=after.id||!before.triangles.SequenceEqual(after.triangles)||!before.sections.SequenceEqual(after.sections))throw new Exception("Worker owner metadata mismatch");for(int v=0;v<8;v++)if(!Bits(before.corners[v],after.corners[v]))throw new Exception("Worker corner bits mismatch");for(int side=0;side<4;side++)if(!before.sides[side].SequenceEqual(after.sides[side]))throw new Exception("Worker side mismatch");}
     if(apex)for(int i=0;i<source.Length;i++)if(!Bits(results[i].apex,expected[i].apex)||results[i].fallback!=expected[i].fallback)throw new Exception("Worker apex/order mismatch at "+i);
    }
    File.WriteAllText(Path.Combine(output,"owner-worker-benchmark.json"),"{\"groupingSeconds\":"+grouping.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"trials\":["+string.Join(",",trials.Select(t=>JsonUtility.ToJson(t)))+"],\"limitation\":\"Editor-only fresh per-owner construction and exact apex; no Runtime worker or end-to-end gain claim\"}");
   }finally{factories.Clear();RoadVolumeMesh.CellMaterialized=null;}
  }
 }
}
