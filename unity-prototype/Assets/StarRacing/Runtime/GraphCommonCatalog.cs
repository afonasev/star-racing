using System;
using System.Collections.Generic;
using System.Linq;
namespace StarRacingPrototype.Procedural {
 // Explicit geometry candidate only. Native12 selection/whole-track safety/fallback
 // must certify it before activation. Legacy10/11 and GraphCommonCourse are unchanged.
 public static class GraphCommonCatalog {
  public const int Profile=1;
  public static IndependentGraph Create(DVec origin,DVec tangent,DVec normal,DVec right,string mode="normal",bool jumps=true) {
   const double length=2600;
   var graph=IndependentGraph.Create(origin,tangent,normal,right,mode,180,length);
   var common=new Dictionary<int,GraphFrame[]>();
   foreach(int id in new[]{0,5}) {
    var first=graph.Road(id).samples[0];var frames=new List<GraphFrame>();double distance=0;DVec previous=default;
    void Add(DVec position,DVec t,DVec n,DVec r) {
     if(frames.Count>0)distance+=Math.Sqrt((position-previous).Squared);
     frames.Add(new GraphFrame(distance,position,t,n,r,first.halfWidth,first.railLeft,first.railRight));previous=position;
    }
    void Flat(double z)=>Add(first.position+first.tangent*z,first.tangent,first.normal,first.right);
    // Four unchanged480m runway slots precede any rolled/inverted geometry.
    for(int i=0;i<=250;i++)Flat(i*5);
    if(id==0) {
     // One vertical loop with80m lateral separation of its entrance/exit.
     // The return bend rejoins the common axis, far from the junction collar.
     const double radius=420/(2*Math.PI),shift=80;
     for(int i=1;i<=168;i++) {
      double u=i/168d,angle=2*Math.PI*u,smooth=u*u*(3-2*u);
      var p=first.position+first.tangent*(1250+radius*Math.Sin(angle))+first.normal*(radius*(1-Math.Cos(angle)))+first.right*(shift*smooth);
      var t=(first.tangent*Math.Cos(angle)+first.normal*Math.Sin(angle)+first.right*(shift*6*u*(1-u)/420)).Unit();
      var n=(first.normal*Math.Cos(angle)-first.tangent*Math.Sin(angle)).Unit();var r=DVec.Cross(t,n).Unit();n=DVec.Cross(r,t).Unit();
      if(i==168){p=first.position+first.tangent*1250+first.right*shift;t=first.tangent;n=first.normal;r=first.right;}
      Add(p,t,n,r);
     }
     for(int i=1;i<=60;i++) {
      double u=i/60d,offset=shift*(1-u*u*(3-2*u)),slope=-shift*6*u*(1-u)/300;
      var t=(first.tangent+first.right*slope).Unit();var r=DVec.Cross(t,first.normal).Unit();
      Add(first.position+first.tangent*(1250+i*5)+first.right*offset,t,first.normal,r);
     }
     for(int i=311;i<=520;i++)Flat(i*5);
    } else {
     // Full bank/corkscrew rotation, followed by a continuous sampled hill.
     for(int i=251;i<=520;i++) {
      double z=i*5,u=Math.Max(0,Math.Min(1,(z-1250)/480)),angle=2*Math.PI*u*u*(3-2*u);
      double h=0,slope=0,v=(z-1900)/240;
      if(v>0&&v<1){double a=Math.Sin(Math.PI*v),b=Math.Cos(Math.PI*v);h=8*a*a*a*a;slope=32*Math.PI*a*a*a*b/240;}
      var t=(first.tangent+first.normal*slope).Unit();var r=first.right;var n=DVec.Cross(r,t).Unit();
      if(z>1250&&z<1730){n=n.Rotate(t,angle).Unit();r=DVec.Cross(t,n).Unit();}
      Add(first.position+first.tangent*z+first.normal*h,t,n,r);
     }
    }
    common[id]=frames.ToArray();
   }
   double delta=common[0].Last().distance-graph.Road(0).Length;
   var roads=graph.roads.Select(r=>new GraphRoad(r.id,r.kind,r.fromPort,r.toPort,r.branch,common.ContainsKey(r.id)?common[r.id]:r.samples,
    r.progressStart+(r.id==0?0:delta),r.id==0?common[0].Last().distance:r.id==5?r.progressStart+delta+common[5].Last().distance:r.progressEnd+delta));
   var contracts=new List<GraphJump>();
   if(jumps)foreach(int id in new[]{0,5})for(int i=0;i<2;i++)contracts.Add(new GraphJump("common-jump-"+(contracts.Count+1),id,80+i*575,i==1));
   return IndependentGraph.FromGeometry(roads,graph.junctions,mode,false,contracts);
  }
 }
}
