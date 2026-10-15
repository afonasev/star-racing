using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.SceneManagement;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphJumpChecks {
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph jump: "+why);}
  public static IndependentGraph Geometry(string mode="normal",bool jumps=true,bool transformed=false) {
   DVec t=new DVec(0,0,1),n=new DVec(0,1,0),r=new DVec(-1,0,0),o=new DVec();
   if(transformed){var axis=new DVec(.3,.7,.2).Unit();t=t.Rotate(axis,.81);n=n.Rotate(axis,.81);r=r.Rotate(axis,.81);o=new DVec(200,70,-350);}
   return GraphCommonCourse.Create(o,t,n,r,mode,jumps);
  }
  public static void Run() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_JUMP_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Evidence required");Directory.CreateDirectory(output);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);assertions=0;
   foreach(string mode in new[]{"normal","none","full"})Case(mode,output,mode=="full");
   var disabled=Geometry(jumps:false);Check(disabled.jumps.Count==0&&disabled.Identity!=Geometry().Identity,"toggle part of identity and zero enabled contracts");
   foreach(var j in Geometry().jumps) {
    Check(j.launch-j.runwayStart==18*5&&j.gapEnd-j.launch==5*5&&j.tacticalLanding-j.launch==19*5&&j.ballisticLanding-j.launch==68*5,"exact native offsets");
    Check(j.runwayEnd-j.ballisticLanding>=50&&j.Valid(Geometry()),"480m runway and50m turn reserve");
   }
   bool bad=false;var graph=Geometry();try{IndependentGraph.FromGeometry(graph.roads,graph.junctions,"normal",false,new[]{new GraphJump("bad",1,0,true)});}catch(ArgumentException){bad=true;}
   Check(bad,"standalone jump import rejected");bad=false;try{IndependentGraph.FromGeometry(graph.roads,graph.junctions,"normal",false,new[]{new GraphJump("bad",0,graph.Road(0).Length-480,true)});}catch(ArgumentException){bad=true;}
   Check(bad,"50m junction collar protected");
   var old=graph.Road(0);var frames=new[]{new GraphFrame(0,old.samples[0].position,old.samples[0].tangent,new DVec(0,1,0).Rotate(new DVec(0,0,1),.1),DVec.Cross(old.samples[0].tangent,new DVec(0,1,0).Rotate(new DVec(0,0,1),.1)),8.5,true,true),old.samples.Last()};
   bad=false;try{IndependentGraph.FromGeometry(graph.roads.Select(r=>r.id==0?new GraphRoad(r.id,r.kind,r.fromPort,r.toPort,r.branch,frames,r.progressStart,r.progressEnd):r),graph.junctions,"normal",false,graph.jumps);}catch(ArgumentException){bad=true;}
   Check(bad,"sparse banked runway import rejected");
   File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_JUMP_OK assertions={assertions} modes=3 ramps=physical gaps=physical generator=inactive graph={graph.Identity}\n");Debug.Log("GRAPH_JUMP_OK assertions="+assertions);
  }
  public static void RunAffectedAndPlay() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_JUMP_QA_DIR");
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_JUMP_QA_DIR",Path.Combine(output,"physical"));Run();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_JUMP_QA_DIR",Path.Combine(output,"regression"));RunRegression();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_JUMP_PLAY_QA_DIR",Path.Combine(output,"play"));GraphJumpPlayChecks.Run();
  }
  public static void RunRegression() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_JUMP_QA_DIR");Directory.CreateDirectory(output);
   void Env(string variable,string folder)=>Environment.SetEnvironmentVariable(variable,Path.Combine(output,folder));
   Env("STAR_RACING_GRAPH_QA_DIR","domain");Env("STAR_RACING_COMPOSITION_QA_DIR","composition");IndependentTrackGraphChecks.Run();
   Env("STAR_RACING_GRAPH_RACE_QA_DIR","race");GraphRaceChecks.Run();
   Env("STAR_RACING_GRAPH_AI_QA_DIR","ai");GraphAiChecks.Run();
   Env("STAR_RACING_GRAPH_ENV_QA_DIR","environment");GraphEnvironmentChecks.Run();
   Env("STAR_RACING_GRAPH_VERTICAL_QA_DIR","vertical");GraphCommonVerticalChecks.Run();
   Env("STAR_RACING_GRAPH_MESH_QA_DIR","planar");Env("STAR_RACING_ROAD_QA_DIR","planar");Environment.SetEnvironmentVariable("STAR_RACING_ROAD_CELL_FIXTURE","flat");IndependentTrackMeshChecks.Run();
   NativeRunwayChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),"GRAPH_JUMP_AFFECTED_OK domain race admission projection presentation ai environment vertical planar nativeRunway legacy10/11\n");
  }
  static void Case(string mode,string output,bool transformed) {
   var graph=Geometry(mode,transformed:transformed);Check(graph.Validate().Length==0&&graph.jumps.Count==4,"common pair composition");Check(graph.Identity==Geometry(mode,transformed:transformed).Identity,"exact repeat identity");
   var root=new GameObject("Jump contract geometry");var cooked=new HashSet<int>();int supports=0;Transform canonical=null;
   string Key(RoadFace f)=>$"{f.geometryId}/{f.sample}/{f.startDistance:R}/{f.endDistance:R}/{f.leftStart:R}/{f.rightStart:R}";
   var owners=new Dictionary<string,RoadVolumeMesh.SupportCell>();
   try {
    bool rejected=false;try{IndependentTrackMesh.Build(root.transform,graph,41,null,null);}catch(ArgumentException){rejected=true;}Check(rejected&&root.transform.childCount==0,"planar path cannot flatten ramps");
    var surface=IndependentTrackMesh.BuildCommonVertical(root.transform,graph,41,null,null,(context,tri)=>{canonical=context;owners[Key(tri.owner.face)]=tri.owner;supports++;Check(tri.owner.ContainsApex(tri.SelectApex(out _)),"actual support apex remains in physical ramp/gap owner");if(cooked.Add(tri.owner.face.geometryId)){var hull=RoadVolumeMesh.CreateSupport(context,tri);Check(hull.convex&&hull.sharedMesh!=null,"representative actual hull cooking");}});
    Physics.SyncTransforms();var addresses=new HashSet<string>();int matched=0;
    foreach(var top in root.GetComponentsInChildren<TrackSurface>())for(int q=0;q<top.sourceVertices.Length/4;q++) {
     var face=top.triangleFaces[q*2];Check(face.quad==q,"actual local quad metadata");Check(addresses.Add(Key(face)),"each physical quad emitted once");
     if(owners.TryGetValue(Key(face),out var owner)) {
      matched++;Vector3 offset=top.transform.parent==canonical?Vector3.zero:top.transform.parent.localPosition;
      for(int corner=0;corner<4;corner++)Check((top.sourceVertices[q*4+corner]+offset).Equals(owner.corners[corner]),"bit-exact canonical top/support correspondence across local origins");
     }
    }
    Check(matched==owners.Count,"all support owners map to emitted physical tops");
    Check(cooked.Count==6,"only six logical physical geometries");
    foreach(var j in graph.jumps) {
     var road=graph.Road(j.roadId);
     for(double at=j.rampStart+.25;at<j.gapEnd+5;at+=.5) {
      var f=road.Evaluate(at);foreach(double lane in new[]{j.lateralCenter,0,7.5,-7.5}) {
       var p=f.position+f.right*lane;bool gap=j.Gap(at,lane,mode);
       bool hit=surface.Raycast(new Ray((p+f.normal*7).Float,-f.normal.Float),8,new[]{j.roadId},graph.Identity,41,out var h,out var face);
       Check(hit!=gap,"physical gap mode="+mode+" jump="+j.id+" at="+at+" lane="+lane+" hit="+hit+" gap="+gap);
       if(hit) {double expected=j.ContainsLane(lane)?j.Height(at,mode):0;
        Check(Math.Abs(DVec.Dot(new DVec(h.point.x,h.point.y,h.point.z)-p,f.normal)-expected)<.002,"actual ramp/bridge/landing height");
        Check(face.geometryId==j.roadId,"support retains road-local metadata");
       }
      }
     }
     var launch=road.Evaluate(j.launch);Check(j.Height(j.launch,mode)==2.8&&Math.Abs(j.Slope(j.launch,mode)-.12)<1e-10,"unchanged physical lip height/slope");
     var env=new GraphEnvironmentGeometry(graph);Check(env.Bounds.Contains((launch.position+launch.normal*2.8).Float),"environment encloses physical ramp");
    }
    File.WriteAllText(Path.Combine(output,"case-"+mode+".txt"),$"GRAPH_JUMP_CASE_OK supports={supports} graph={graph.Identity}\n");
   }finally{foreach(var mesh in root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Concat(root.GetComponentsInChildren<MeshCollider>().Select(c=>c.sharedMesh)).Where(m=>m!=null).Distinct().ToArray())UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(root);}
  }
 }
}
