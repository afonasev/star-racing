using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 public static class GraphCatalogEnvironmentChecks {
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Catalog environment: "+why);}
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CATALOG_ENV_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Evidence required");Directory.CreateDirectory(output);assertions=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   foreach(string mode in new[]{"normal","none","full"}){
    var graph=GraphCommonCatalogChecks.Geometry(mode);var root=new GameObject("Catalog environment compiler");var environment=new TrackEnvironmentBuilder();
    try{
     var surface=IndependentTrackMesh.BuildCommonCatalog(root.transform,graph,1,null,null,(_,__)=>{});
     foreach(string theme in new[]{"cloud-city","space-station"}){
      environment.BuildIndependent(surface,77,theme);var g=environment.GraphGeometry;
      Check(g.Boundaries.Count==g.BoundaryNormals.Count,"every boundary retains local normals");
      Check(g.BoundaryNormals.SelectMany(n=>n).Any(n=>Vector3.Dot(n,Vector3.up)<-.99f),"inverted boundary basis present");
      Check(g.BoundaryNormals.SelectMany(n=>n).Any(n=>Math.Abs(Vector3.Dot(n,Vector3.up))<.02f),"vertical bank boundary basis present");
      var markings=environment.Root.GetComponentsInChildren<MeshFilter>().Single(f=>f.sharedMesh.name=="Graph boundary markings").sharedMesh;var v=markings.vertices;int at=0;
      for(int chain=0;chain<g.Boundaries.Count;chain++)for(int i=0;i+1<g.Boundaries[chain].Length;i++){
       Vector3 first=v[at+2]-v[at],second=v[at+3]-v[at+1];
       Check(Math.Abs(first.magnitude-.2f)<.001f&&Math.Abs(second.magnitude-.2f)<.001f,"no collapsed paint strip on loop or bank");
       Check(Math.Abs(Vector3.Dot(first,g.BoundaryNormals[chain][i]))<.001f&&Math.Abs(Vector3.Dot(second,g.BoundaryNormals[chain][i+1]))<.001f,"paint width follows swept physical tangent plane");at+=4;
      }
      Check(at==v.Length,"all real marking quads verified");
      foreach(var lamp in environment.Lamps)Check(g.Clear(lamp.basePoint,Vector3.one*.1f,0),"lamp bases remain outside the entire road envelope");
      File.WriteAllText(Path.Combine(output,theme+"-"+mode+".txt"),$"CATALOG_ENV_OK graph={graph.Identity} quads={at/4} lamps={environment.LampCount}\n");
      environment.Clear();
     }
    }finally{environment.Clear();UnityEngine.Object.DestroyImmediate(root);}
   }
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_ENV_QA_DIR",Path.Combine(output,"existing-environment"));GraphEnvironmentChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),$"CATALOG_ENV_AFFECTED_OK assertions={assertions} themes=2 railModes=3 originalEnvironmentAndMarks=passed\n");Debug.Log("CATALOG_ENV_AFFECTED_OK assertions="+assertions);
  }
 }
}
