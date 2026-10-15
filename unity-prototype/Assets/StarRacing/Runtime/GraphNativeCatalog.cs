using System;
using System.Collections.Generic;
using System.Linq;
namespace StarRacingPrototype.Procedural {
 // Revision12 path-choice composition. The approved pair is the main phrase;
 // common supporting phrases vary independently of rigid world yaw.
 public static class GraphNativeCatalog {
  public const int ProfileBase=100;
  public const double GridStart=350,FirstRunway=450,CommonLength=2550,VerticalStart=1550;
  public static int ProfileFor(uint seed)=>1+(int)(new SeedRandom(seed^0xc071ab12u).Next()*4);
  public static IndependentGraph Candidate(uint seed,GraphCourseSettings settings) {
   double angle=(seed%360)*Math.PI/180;var normal=new DVec(0,1,0);var tangent=new DVec(Math.Sin(angle),0,Math.Cos(angle));
   return Create(new DVec(0,120,0),tangent,normal,DVec.Cross(tangent,normal).Unit(),settings.rails,settings.jumps,ProfileFor(seed));
  }
  public static IndependentGraph Create(DVec origin,DVec tangent,DVec normal,DVec right,string rails,bool jumps,int profile) {
   if(profile<1||profile>4)throw new ArgumentException("Unknown native path-choice composition");
   double reduction=jumps?0:440,verticalStart=VerticalStart-reduction,commonLength=CommonLength-reduction;int prefix=(int)(verticalStart/5),last=(int)(commonLength/5);
   var graph=IndependentGraph.Create(origin,tangent,normal,right,rails,180,commonLength);var common=new Dictionary<int,GraphFrame[]>();
   foreach(int id in new[]{0,5}) {
    var first=graph.Road(id).samples[0];var frames=new List<GraphFrame>();double distance=0;DVec previous=default;
    void Add(DVec p,DVec t,DVec n,DVec r){if(frames.Count>0)distance+=Math.Sqrt((p-previous).Squared);frames.Add(new GraphFrame(distance,p,t,n,r,first.halfWidth,first.railLeft,first.railRight));previous=p;}
    void Flat(double z)=>Add(first.position+first.tangent*z,first.tangent,first.normal,first.right);
    // The full64-car grid precedes the four unchanged480m runway contracts.
    // Geometry remains straight through every ballistic tail and turn reserve.
    for(int i=0;i<=prefix;i++)Flat(i*5);
    bool loop=profile==1&&id==0 || profile==3&&id==5;
    if(loop) {
     const double radius=420/(2*Math.PI),shift=80;
     for(int i=1;i<=168;i++) {
      double u=i/168d,a=2*Math.PI*u,s=u*u*(3-2*u);
      var p=first.position+first.tangent*(verticalStart+radius*Math.Sin(a))+first.normal*(radius*(1-Math.Cos(a)))+first.right*(shift*s);
      var t=(first.tangent*Math.Cos(a)+first.normal*Math.Sin(a)+first.right*(shift*6*u*(1-u)/420)).Unit();var n=(first.normal*Math.Cos(a)-first.tangent*Math.Sin(a)).Unit();var r=DVec.Cross(t,n).Unit();n=DVec.Cross(r,t).Unit();
      if(i==168){p=first.position+first.tangent*verticalStart+first.right*shift;t=first.tangent;n=first.normal;r=first.right;}
      Add(p,t,n,r);
     }
     for(int i=1;i<=60;i++) {
      double u=i/60d,x=shift*(1-u*u*(3-2*u)),slope=-shift*6*u*(1-u)/300;var t=(first.tangent+first.right*slope).Unit();var r=DVec.Cross(t,first.normal).Unit();
      Add(first.position+first.tangent*(verticalStart+i*5)+first.right*x,t,first.normal,r);
     }
     for(int i=prefix+61;i<=last;i++)Flat(i*5);
    } else for(int i=prefix+1;i<=last;i++) {
     double z=i*5,x=0,dx=0,h=0,dh=0;
     bool arc=(profile==1||profile==4)&&id==5,linked=profile==2&&id==0;
     double u=(z-verticalStart)/800;
     if((arc||linked)&&u>0&&u<1) {
      double a=Math.PI*u,s=Math.Sin(a),c=Math.Cos(a);
      if(arc){x=24*s*s;dx=48*Math.PI*s*c/800;}
      else {x=24*Math.Sin(2*a)*s*s;dx=24*(2*Math.PI*Math.Cos(2*a)*s*s+2*Math.PI*Math.Sin(2*a)*s*c)/800;}
     }
     bool bank=profile==2&&id==5 || profile==4&&id==0;
     bool hill=profile==2&&id==5 || profile==3&&id==0;
     double v=(z-(verticalStart+(bank?680:450)))/240;
     if(hill&&v>0&&v<1){double s=Math.Sin(Math.PI*v),c=Math.Cos(Math.PI*v);h=8*s*s*s*s;dh=32*Math.PI*s*s*s*c/240;}
     var t=(first.tangent+first.right*dx+first.normal*dh).Unit();var r=DVec.Cross(t,first.normal).Unit();var n=DVec.Cross(r,t).Unit();
     if(bank&&z>verticalStart&&z<verticalStart+480){double b=(z-verticalStart)/480;double a=2*Math.PI*b*b*(3-2*b);n=n.Rotate(t,a).Unit();r=DVec.Cross(t,n).Unit();}
     Add(first.position+first.tangent*z+first.right*x+first.normal*h,t,n,r);
    }
    common[id]=frames.ToArray();
   }
   double delta=common[0].Last().distance-graph.Road(0).Length;
   var roads=graph.roads.Select(r=>new GraphRoad(r.id,r.kind,r.fromPort,r.toPort,r.branch,common.ContainsKey(r.id)?common[r.id]:r.samples,
    r.progressStart+(r.id==0?0:delta),r.id==0?common[0].Last().distance:r.id==5?r.progressStart+delta+common[5].Last().distance:r.progressEnd+delta));
   var contracts=new List<GraphJump>();if(jumps)foreach(int id in new[]{0,5})for(int i=0;i<2;i++)contracts.Add(new GraphJump("common-jump-"+(contracts.Count+1),id,FirstRunway+i*575,i==1));
   return IndependentGraph.FromGeometry(roads,graph.junctions,rails,false,contracts);
  }
 }
}
