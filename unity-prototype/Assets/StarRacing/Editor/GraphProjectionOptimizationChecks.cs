using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphProjectionOptimizationChecks {
  delegate GraphFrame Projection(GraphRoad road,DVec position,double from,double to,out double score);
  static readonly Projection Project=(Projection)Delegate.CreateDelegate(typeof(Projection),typeof(GraphRoad).GetMethod("Project",BindingFlags.Instance|BindingFlags.NonPublic));
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph projection parity: "+why);}
  // Frozen pre-optimization C# oracle. Same arithmetic, full original scan and
  // endpoint/tie order; this verifies the new interval lookup, not a new model.
  static GraphFrame Reference(GraphRoad road,DVec position,double from,double to,out double score){
   from=Math.Max(0,from);to=Math.Min(road.Length,to);score=double.PositiveInfinity;double best=from;
   for(int i=0;i+1<road.samples.Count;i++){
    var a=road.samples[i];var b=road.samples[i+1];if(b.distance<from||a.distance>to)continue;var delta=b.position-a.position;
    double t=DVec.Dot(position-a.position,delta)/delta.Squared;double span=b.distance-a.distance;
    t=Math.Max(Math.Max(0,(from-a.distance)/span),Math.Min(Math.Min(1,(to-a.distance)/span),t));double candidate=(position-(a.position+delta*t)).Squared;
    if(candidate<score){score=candidate;best=a.distance+span*t;}
   }
   return road.Evaluate(best);
  }
  static void Exact(double a,double b)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),"canonical double bits");
  static void Vector(DVec a,DVec b){Exact(a.x,b.x);Exact(a.y,b.y);Exact(a.z,b.z);}
  static void Compare(GraphRoad road,DVec p,double from,double to){var a=Reference(road,p,from,to,out var scoreA);var b=Project(road,p,from,to,out var scoreB);Exact(scoreA,scoreB);Exact(a.distance,b.distance);Vector(a.position,b.position);Vector(a.tangent,b.tangent);Vector(a.normal,b.normal);Vector(a.right,b.right);Exact(a.halfWidth,b.halfWidth);Check(a.railLeft==b.railLeft&&a.railRight==b.railRight,"rail metadata parity");}
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_OPTIMIZATION_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Evidence required");Directory.CreateDirectory(output);assertions=0;
   try{IndependentGraph.FromGeometry(Array.Empty<GraphRoad>(),Array.Empty<GraphJunction>(),"normal");Check(false,"empty import rejected");}catch(ArgumentException){Check(true,"empty import preserves ArgumentException contract");}
   foreach(bool transformed in new[]{false,true})foreach(string mode in new[]{"normal","none","full"}){
    var graph=GraphCommonCatalogChecks.Geometry(mode,transformed:transformed);Exact(graph.CommonLength,graph.roads.Max(r=>r.progressEnd));
    foreach(var road in graph.roads)foreach(var frame in road.samples)foreach(double window in new[]{0d,.5,20,30,100})foreach(double lateral in new[]{-8d,0,8}){
     var p=frame.position+frame.right*lateral+frame.normal*.259;double from=frame.distance-window,to=frame.distance+window;
     Compare(road,p,from,to);
    }
   }
   int originalAssertions=assertions;
   foreach(bool transformed in new[]{false,true})foreach(string mode in new[]{"normal","none","full"}){
    var graph=GraphCommonCatalogChecks.Geometry(mode,transformed:transformed);
    foreach(var road in graph.roads){
     Compare(road,road.samples[0].position,-10,0);Compare(road,road.samples.Last().position,road.Length,road.Length+10);
     for(int i=0;i+1<road.samples.Count;i+=37)foreach(double t in new[]{.125,.5,.875}){
      var a=road.samples[i];var b=road.samples[i+1];double at=a.distance+(b.distance-a.distance)*t;
      var p=a.position+(b.position-a.position)*t+a.right*3.75+a.normal*.259;
      foreach(double window in new[]{0d,3.125,30})Compare(road,p,at-window,at+window);
     }
    }
   }
   var frames=new GraphFrame[4];for(int i=0;i<frames.Length;i++)frames[i]=new GraphFrame(i*10,new DVec(i%2*10,0,0),new DVec(1,0,0),new DVec(0,1,0),new DVec(0,0,1),4,i%2==0,i%2!=0);
   var tie=new GraphRoad(9,"tie",0,1,0,frames,0,30);frames[0]=default;
   Compare(tie,new DVec(5,2,0),0,30);Exact(Project(tie,new DVec(5,2,0),0,30,out _).distance,5);
   Compare(tie,new DVec(10,0,0),10,10);Compare(tie,new DVec(0,0,0),20,20);
   File.WriteAllText(Path.Combine(output,"projection.txt"),$"GRAPH_PROJECT_EXACT_OK assertions={assertions} windows=5 lateralCases=3 transformed=2 railModes=3 preOptimizationOracle=exactDoubleBits originalAssertions={originalAssertions} addedAssertions={assertions-originalAssertions} offSampleEndpointsTieImmutableCopy=passed\n");
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_VEHICLE_QA_DIR",Path.Combine(output,"vehicle"));GraphVehicleRouteChecks.Run();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_RACE_QA_DIR",Path.Combine(output,"race"));GraphRaceChecks.RunCourseContracts();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_AI_QA_DIR",Path.Combine(output,"ai"));GraphAiChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_OPTIMIZATION_AFFECTED_OK assertions={assertions} projection binding gates AI legacyProjection legacyAdmission legacyPresentation exactLegacyAI=passed\n");Debug.Log("GRAPH_OPTIMIZATION_AFFECTED_OK assertions="+assertions);
  }
 }
}
