using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 // Presentation inventory, not a navigation route. Junctions occur once; guides never emit geometry.
 public sealed class GraphEnvironmentGeometry {
  public const int Version=2;
  public readonly IndependentGraph Graph;
  public readonly TrackFrame[] Samples;
  public readonly Bounds Bounds;
  public readonly List<Vector3[]> Boundaries=new List<Vector3[]>();
  public readonly List<Vector3[]> BoundaryNormals=new List<Vector3[]>();
  readonly List<(Vector3 a,Vector3 b,float radius)> corridors=new List<(Vector3,Vector3,float)>();
  public GraphEnvironmentGeometry(IndependentGraph graph) {
   if(graph==null||graph.legacy||graph.Validate().Length!=0)throw new ArgumentException("Validated independent graph required");Graph=graph;
   var samples=new List<TrackFrame>();var bounds=new Bounds(graph.Road(0).samples[0].position.Float,Vector3.zero);
   foreach(var road in graph.roads.Where(r=>r.id==0||r.id==1||r.id==2||r.id==5)) {
    foreach(var f in road.samples)Add(f.position.Float,f.tangent.Float,f.normal.Float,f.right.Float,(float)f.halfWidth);
    for(int i=0;i+1<road.samples.Count;i++){var a=road.samples[i];var b=road.samples[i+1];Corridor(a.position.Float,b.position.Float,(float)Math.Max(a.halfWidth,b.halfWidth));}
    for(int side=-1;side<=1;side+=2) {
     if(!graph.jumps.Any(j=>j.roadId==road.id)){Boundary(road.samples.Select(f=>(f.position+f.right*(side*(f.halfWidth-.35))+f.normal*.025).Float).ToArray(),road.samples.Select(f=>road.SurfaceNormal(f.distance,side*(f.halfWidth-.35)).Float).ToArray());continue;}
     var line=new List<Vector3>();var normals=new List<Vector3>();
     for(double at=0;at<=road.Length;at=Math.Min(road.Length,at+.5)) {
      var f=road.Evaluate(at);double lane=side*(f.halfWidth-.35);var jump=graph.jumps.FirstOrDefault(j=>j.roadId==road.id&&at>=j.rampStart&&at<=j.gapEnd);
      if(jump!=null&&jump.Gap(at,lane,graph.guardrailMode)){if(line.Count>1)Boundary(line.ToArray(),normals.ToArray());line.Clear();normals.Clear();}
      else {line.Add((f.position+f.right*lane+f.normal*(.025+(jump!=null&&jump.ContainsLane(lane)?jump.Height(at,graph.guardrailMode):0))).Float);normals.Add(jump!=null&&jump.ContainsLane(lane)?DVec.Cross(f.right,(f.tangent+f.normal*jump.Slope(at,graph.guardrailMode)).Unit()).Unit().Float:road.SurfaceNormal(at,lane).Float);}
      if(at==road.Length)break;
     }
     if(line.Count>1)Boundary(line.ToArray(),normals.ToArray());
    }
   }
   foreach(var node in graph.junctions) {
    foreach(var s in node.stations)Add(node.Point(0,s.z).Float,node.tangent.Float,node.normal.Float,node.right.Float,(float)s.outer);
    for(int i=0;i+1<node.stations.Count;i++) {
     var a=node.stations[i];var b=node.stations[i+1];Corridor(node.Point(0,a.z).Float,node.Point(0,b.z).Float,(float)Math.Max(a.outer,b.outer));
     for(int side=-1;side<=1;side+=2) {
      Boundary(new[]{(node.Point(side*(a.outer-.35),a.z)+node.normal*.025).Float,(node.Point(side*(b.outer-.35),b.z)+node.normal*.025).Float},new[]{node.normal.Float,node.normal.Float});
      // The shared centre before the divider has no painted internal seam.
      if(a.gapHalf>.35&&b.gapHalf>.35)Boundary(new[]{(node.Point(side*(a.gapHalf+.35),a.z)+node.normal*.025).Float,(node.Point(side*(b.gapHalf+.35),b.z)+node.normal*.025).Float},new[]{node.normal.Float,node.normal.Float});
     }
    }
   }
   Samples=samples.ToArray();Bounds=bounds;
   void Boundary(Vector3[] points,Vector3[] normals){if(points.Length!=normals.Length)throw new InvalidOperationException("Boundary basis mismatch");Boundaries.Add(points);BoundaryNormals.Add(normals);}
   void Add(Vector3 p,Vector3 tangent,Vector3 normal,Vector3 right,float half) {samples.Add(new TrackFrame{position=p,tangent=tangent,normal=normal,right=-right,halfWidth=half});bounds.Encapsulate(new Bounds(p,Vector3.one*(half*2+4)));}
   void Corridor(Vector3 a,Vector3 b,float radius){corridors.Add((a,b,radius));}
  }
  // Continuous conservative segment-vs-expanded-AABB clearance includes sparse160m common roads.
  public bool Clear(Vector3 center,Vector3 extents,float margin) {
   if(margin<0||extents.x<0||extents.y<0||extents.z<0)throw new ArgumentException("Nonnegative clearance required");
   foreach(var c in corridors){var box=new Bounds(center,(extents+Vector3.one*(c.radius+margin))*2);float enter=0,leave=1;var delta=c.b-c.a;bool hit=true;
    for(int axis=0;axis<3&&hit;axis++){float start=c.a[axis],step=delta[axis];if(Math.Abs(step)<1e-6f){if(start<box.min[axis]||start>box.max[axis])hit=false;}else{float low=(box.min[axis]-start)/step,high=(box.max[axis]-start)/step;if(low>high){float swap=low;low=high;high=swap;}enter=Mathf.Max(enter,low);leave=Mathf.Min(leave,high);hit=enter<=leave;}}
    if(hit)return false;
   }return true;
  }
 }
}
