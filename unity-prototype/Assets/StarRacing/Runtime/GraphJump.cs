using System;
using System.Linq;
using System.Collections.Generic;
namespace StarRacingPrototype.Procedural {
 // Immutable road-local physical metres; sample offsets are the unchanged native10/11 contract.
 public sealed class GraphJump {
  public const int Profile=1; // native lip2.8m/slope.12 and same-road20m/4s flight envelope.
  public readonly string id;
  public readonly int roadId;
  public readonly bool mandatory;
  public readonly double runwayStart,lateralCenter,lateralHalfWidth;
  public double runwayEnd=>runwayStart+Generator.NativeJumpStraightLength;
  public double rampStart=>runwayStart+12*5;
  public double launch=>runwayStart+18*5;
  public double gapStart=>launch+5;
  public double gapEnd=>launch+5*5;
  public double tacticalLanding=>launch+19*5;
  public double ballisticLanding=>launch+Generator.NativeBallisticLandingSamples*5;
  public GraphJump(string id,int roadId,double runwayStart,bool mandatory) {
   this.id=id;this.roadId=roadId;this.runwayStart=runwayStart;this.mandatory=mandatory;
   lateralCenter=mandatory?0:-IndependentGraph.HalfWidth*2/3;
   lateralHalfWidth=mandatory?IndependentGraph.HalfWidth:IndependentGraph.HalfWidth/3;
  }
  public bool ContainsLane(double lateral,double margin=0)=>Math.Abs(lateral-lateralCenter)<=lateralHalfWidth-margin;
  public bool Gap(double distance,double lateral,string mode)=>mode!="full"&&distance>=gapStart&&distance<gapEnd&&ContainsLane(lateral);
  public bool Ramp(double from,double to,string mode)=>from>=rampStart&&to<=(mode=="full"?gapEnd:launch);
  public double Height(double distance,string mode) {
   if(distance<rampStart || distance>(mode=="full"?gapEnd:launch))return 0;
   if(distance<=launch) {double t=(distance-rampStart)/(launch-rampStart);return 2.8*(3*t*t-2*t*t*t)+.12*(launch-rampStart)*(t*t*t-t*t);}
   double u=(distance-launch)/(gapEnd-launch);return (2*u*u*u-3*u*u+1)*2.8+(u*u*u-2*u*u+u)*.12*(gapEnd-launch);
  }
  public double Slope(double distance,string mode) {
   if(distance<=launch){double t=(distance-rampStart)/(launch-rampStart);return 2.8*(6*t-6*t*t)/(launch-rampStart)+.12*(3*t*t-2*t);}
   double u=(distance-launch)/(gapEnd-launch);return (6*u*u-6*u)*2.8/(gapEnd-launch)+(3*u*u-4*u+1)*.12;
  }
  public bool Valid(IndependentGraph graph) {
   if(string.IsNullOrEmpty(id)||!IndependentGraph.Finite(runwayStart)||runwayStart<0||roadId!=0&&roadId!=5||graph.legacy)return false;
   var road=graph.Road(roadId);
   // Whole runway, including the exact ballistic tail and 50m turn reserve, remains common and straight.
   if(runwayEnd>road.Length || ballisticLanding+50>runwayEnd || roadId==0&&runwayEnd>road.Length-50 || roadId==5&&runwayStart<50)return false;
   var a=road.Evaluate(runwayStart);var b=road.Evaluate(runwayEnd);
   if(Math.Abs(Math.Sqrt((b.position-a.position).Squared)-480)>1e-7)return false;
   foreach(var f in road.samples.Where(f=>f.distance>=runwayStart&&f.distance<=runwayEnd).Concat(new[]{a,b}))
    if((f.position-a.position-a.tangent*(f.distance-runwayStart)).Squared>1e-14 || (f.tangent-a.tangent).Squared>1e-16||(f.normal-a.normal).Squared>1e-16||(f.right-a.right).Squared>1e-16)return false;
   return true;
  }
 }
 public static class GraphCommonCourse {
  // Explicit candidate compiler, not active revision12 selection/fallback. Two runways on each common road.
  public static IndependentGraph Create(DVec origin,DVec tangent,DVec normal,DVec right,string mode="normal",bool jumps=true) {
   const double length=1185;
   var graph=IndependentGraph.Create(origin,tangent,normal,right,mode,180,length);
   var roads=new List<GraphRoad>();
   foreach(var road in graph.roads) {
    if(road.id!=0&&road.id!=5){roads.Add(road);continue;}
    var a=road.samples[0];var frames=new List<GraphFrame>();
    for(int i=0;i<=237;i++)frames.Add(new GraphFrame(i*5,a.position+a.tangent*(i*5),a.tangent,a.normal,a.right,a.halfWidth,a.railLeft,a.railRight));
    roads.Add(new GraphRoad(road.id,road.kind,road.fromPort,road.toPort,road.branch,frames,road.progressStart,road.progressEnd));
   }
   var contracts=new List<GraphJump>();
   if(jumps)foreach(int road in new[]{0,5})for(int i=0;i<2;i++)contracts.Add(new GraphJump("common-jump-"+(contracts.Count+1),road,80+i*575,i==1));
   return IndependentGraph.FromGeometry(roads,graph.junctions,mode,false,contracts);
  }
 }
}
