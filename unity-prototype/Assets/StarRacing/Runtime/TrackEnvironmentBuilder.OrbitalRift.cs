using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 public sealed partial class TrackEnvironmentBuilder {
  IEnumerator BuildOrbitalRiftSteps(TrackRoute route){
   var stone=Lit(new Color(.46f,.34f,.24f),.03f,texture:"SpaceRock",smoothness:.08f);
   var hull=Lit(new Color(.88f,.93f,1),.3f,texture:"SpaceHull",smoothness:.26f);
   var dark=Lit(new Color(.18f,.25f,.34f),.35f,texture:"SpaceHull",smoothness:.22f);
   var cyan=Lit(new Color(.12f,.76f,1),.1f,true);var amber=Lit(new Color(1,.57f,.18f),.1f,true);
   foreach(var m in new[]{stone,hull,dark}){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",m.color*.08f);}
   BuildEdgeRibbons(route,cyan);
   Shape("Primary asteroid",AsteroidMesh(3),stone,new Vector3(-40,-360,750),Quaternion.identity,new Vector3(1400,650,2100));
   // A broad authored rocky surface meets the road, while the bulk stays below it.
   var surface=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
   var section=route.Definition.composition[1];int end=section.endIndex;
   for(int i=0;i<=end;i+=4){
    var f=route.Evaluate(30+i*5);int row=surface.Count/7;
    for(int j=0;j<7;j++){
     float side=j-3;float x=side==0?0:Mathf.Sign(side)*(j==2||j==4?18:Mathf.Abs(side)==2?130:320);
     float y=Mathf.Abs(side)<=1?-1.7f:Mathf.Abs(side)==2?-12-Mathf.Sin(i*.12f)*12:-100;
     surface.Add(f.position+f.right*x+f.normal*y);uv.Add(new Vector2(x/35,i*5/35f));
     if(row>0&&j>0){int a=surface.Count-1,b=a-1,c=a-7,d=c-1;tris.AddRange(new[]{d,b,a,d,a,c});}
    }
   }
   AddMesh("Asteroid driving shelf",surface,uv,tris,stone);
   yield return null;
   // Open-ended, double-sided rock tunnel. Its crown clears both road and chase camera.
   surface.Clear();uv.Clear();tris.Clear();int rowIndex=0;
   for(int i=section.startIndex;i<=section.endIndex;i+=4){
    var f=route.Evaluate(30+i*5);
    for(int j=0;j<=20;j++){
     float a=j*Mathf.PI*2/20,r=74+Mathf.Sin(i*.07f+j*1.9f)*4;
     surface.Add(f.position+f.normal*16+(f.right*Mathf.Cos(a)+f.normal*Mathf.Sin(a))*r);uv.Add(new Vector2(j*1.4f,i*5/32f));
     if(rowIndex>0&&j>0){int n=surface.Count-1;tris.AddRange(new[]{n-22,n-1,n,n-22,n,n-21,n,n-1,n-22,n-21,n,n-22});}
    }
    rowIndex++;
   }
   AddMesh("Open asteroid cavern",surface,uv,tris,stone);
   // Discrete large rocks frame the departure; never intrude on the reserved road corridor.
   for(int i=0;i<18;i++){
    float distance=1850+i*90;var f=route.Evaluate(distance);float side=i%2==0?1:-1;
    Vector3 pos=f.position+f.right*(side*(190+i%3*70))+f.normal*(-80+i%4*90);
    float size=70+i%4*22;
    if(EnvironmentPlan.ClearOfRoute(route,pos,Vector3.one*size,EnvironmentPlan.CorridorMargin))Shape("Satellite asteroid "+i,AsteroidMesh(i%12),stone,pos,Quaternion.Euler(i*19,i*31,0),Vector3.one*size);
   }
   yield return null;
   // Faceted axial hull: race road wraps outside it. Thin seams make rotation legible.
   for(int i=0;i<40;i++){
    float a=i*Mathf.PI*2/40;var radial=new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);var rotation=Quaternion.Euler(0,0,-a*Mathf.Rad2Deg);
    Shape("Station hull panel "+i,cube,i%5==0?dark:hull,AuthoredOrbitalRiftCourse.StationCenter+radial*380,rotation,new Vector3(62,28,2400));
    if(i%2==0)Shape("Station orientation stripe "+i,cube,i%4==0?cyan:amber,AuthoredOrbitalRiftCourse.StationCenter+radial*395,rotation,new Vector3(3,1,2050),false);
   }
   // Large structures stand beside the helical road and establish the hull's scale.
   for(int j=0;j<7;j++)for(int i=0;i<6;i++){
    float a=(i*60+j*13)*Mathf.Deg2Rad;var radial=new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);
    var center=new Vector3(0,400,3350+j*330)+radial*445;var rotation=Quaternion.Euler(0,0,-a*Mathf.Rad2Deg);var size=new Vector3(100,110,130);
    // Conservative world bounds retain the same reserved architecture corridor.
    var ext=new Vector3(Mathf.Abs(Mathf.Cos(a))*50+Mathf.Abs(Mathf.Sin(a))*55,Mathf.Abs(Mathf.Sin(a))*50+Mathf.Abs(Mathf.Cos(a))*55,65);
    if(!EnvironmentPlan.ClearOfRoute(route,center,ext,EnvironmentPlan.CorridorMargin))continue;
    Shape("Station superstructure "+j+"-"+i,cube,j%3==0?dark:hull,center,rotation,size);
    Shape("Superstructure marker "+j+"-"+i,cube,j%3==0?amber:cyan,center+radial*56,rotation,new Vector3(70,2,6),false);
   }
   Ring("Arrival ring",new Vector3(0,400,3100),520,32,40,hull);
   Ring("Dock ring",new Vector3(0,400,5760),560,28,36,dark);
   // Dock silhouettes lie off the driving envelope, with a wide view between them.
   Shape("Lower docking block",cube,dark,new Vector3(-670,150,5550),Quaternion.identity,new Vector3(190,200,540));
   Shape("Dock amber aperture",cube,amber,new Vector3(-572,150,5550),Quaternion.identity,new Vector3(2,90,260),false);
   Shape("Cargo vessel blockout",cube,hull,new Vector3(-730,680,5650),Quaternion.identity,new Vector3(100,70,420));
   Ring("Finish crown",new Vector3(180,880,6630),110,12,16,hull);
   yield return null;
   void Ring(string name,Vector3 center,float radius,float thickness,float depth,Material material){
    const int count=40;for(int i=0;i<count;i++){float a=i*Mathf.PI*2/count;Shape(name+" "+i,cube,material,center+new Vector3(Mathf.Sin(a),Mathf.Cos(a),0)*radius,Quaternion.Euler(0,0,-a*Mathf.Rad2Deg),new Vector3(2*radius*Mathf.Tan(Mathf.PI/count),thickness,depth));}
   }
  }
  void AddMesh(string name,List<Vector3> vertices,List<Vector2> uv,List<int> triangles,Material material){
   var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);
   Shape(name,mesh,material,Vector3.zero,Quaternion.identity,Vector3.one);
  }
 }
}
