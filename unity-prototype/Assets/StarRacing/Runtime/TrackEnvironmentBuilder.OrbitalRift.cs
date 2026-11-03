using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed partial class TrackEnvironmentBuilder {
  IEnumerator BuildOrbitalRiftSteps(TrackRoute route){
   var stone=OrbitalStone(new Color(.43f,.38f,.31f));var charcoal=OrbitalStone(new Color(.26f,.29f,.32f));
   var hull=OrbitalMetal(new Color(.78f,.84f,.89f),.30f,.33f);
   var ivory=OrbitalMetal(new Color(.79f,.71f,.57f),.22f,.22f);
   var dark=OrbitalMetal(new Color(.12f,.18f,.25f),.58f,.36f);
   var bronze=OrbitalMetal(new Color(.55f,.27f,.10f),.63f,.31f);
   var grille=OrbitalMetal(new Color(.22f,.29f,.34f),.45f,.18f,"RoadRibbed");
   var glass=OrbitalMetal(new Color(.07f,.24f,.36f),.65f,.72f,"EyeGlass");
   var cyan=Lit(new Color(.16f,.77f,1),.1f,true);var amber=Lit(new Color(1,.52f,.13f),.1f,true);
   var white=Lit(new Color(.65f,.83f,1),.1f,true);var red=Lit(new Color(1,.13f,.05f),.1f,true);
   var batch=new OrbitalBatches(this);BuildEdgeRibbons(route,cyan);
   var rockMeshes=new Mesh[12];Mesh Rock(int i){i%=12;return rockMeshes[i]??(rockMeshes[i]=AsteroidMesh(i));}
   Shape("Primary asteroid",Rock(3),stone,new Vector3(-40,-360,750),Quaternion.identity,new Vector3(1400,650,2100));
   var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
   var cave=route.Definition.composition[1];int end=cave.endIndex;
   for(int i=0;i<=end;i+=2){
    var f=route.Evaluate(30+i*5);int row=v.Count/11;
    for(int j=0;j<11;j++){
     float side=j-5;float x=side==0?0:Mathf.Sign(side)*(Mathf.Abs(side)==1?18:18+(Mathf.Abs(side)-1)*76);
     float y=Mathf.Abs(side)<=1?-1.7f:Mathf.Abs(side)<5?-8+Mathf.Sin(i*.027f+j*2.1f)*17:-110;
     v.Add(f.position+f.right*x+f.normal*y);uv.Add(new Vector2(x/8,i*5/8f));
     if(row>0&&j>0){int a=v.Count-1;t.AddRange(new[]{a-12,a-1,a,a-12,a,a-11});}
    }
   }
   OrbitalMesh("Fractured asteroid surface",v,uv,t,stone);yield return null;
   // Independent faces with inward normals; avoid opposite triangles cancelling each other's normals.
   v.Clear();uv.Clear();t.Clear();int rowIndex=0;
   for(int i=cave.startIndex;i<=cave.endIndex;i+=2){
    var f=route.Evaluate(30+i*5);
    for(int j=0;j<=32;j++){
     float a=j*Mathf.PI*2/32;
     float r=76+8*Mathf.Sin(i*.037f+j*.43f)+5*Mathf.Sin(i*.017f-j*1.2f);
     v.Add(f.position+f.normal*16+(f.right*Mathf.Cos(a)+f.normal*Mathf.Sin(a))*r);uv.Add(new Vector2(j*1.6f,i*5/8f));
     if(rowIndex>0&&j>0){int n=v.Count-1;t.AddRange(new[]{n-34,n-1,n,n-34,n,n-33});}
    }
    rowIndex++;
   }
   OrbitalMesh("Fractured cavern walls",v,uv,t,charcoal);
   for(float d=80;d<30+cave.endIndex*5;d+=95){
    var f=route.Evaluate(d);bool tunnel=d>30+cave.startIndex*5;
    for(int side=-1;side<=1;side+=2){
     var q=Quaternion.LookRotation(f.tangent,f.normal);var basePoint=f.position+f.right*(side*(tunnel?29:45));
     if(tunnel){
      batch.Box(dark,basePoint+f.normal*12,new Vector3(3,27,4),q);
      batch.Box(bronze,basePoint+f.normal*26,new Vector3(6,2,9),q);
      batch.Box(amber,basePoint+f.normal*24-f.right*side*1.8f,new Vector3(1,2,6),q);
      batch.Beam(dark,basePoint+f.normal*26,f.position+f.normal*64+f.right*(side*13),2.6f);
      for(int cable=0;cable<2;cable++)batch.Beam(bronze,basePoint+f.normal*(8+cable*3)-f.tangent*45,basePoint+f.normal*(8+cable*3)+f.tangent*45,.8f);
      if(side==1)OrbitalLight("cavern work lamp",f.position+f.normal*20+f.right*15,new Color(1,.55f,.22f),95,5);
     }else{
      var at=basePoint+f.right*side*40+f.normal*16;
      if(EnvironmentPlan.ClearOfRoute(route,at,Vector3.one*28,EnvironmentPlan.CorridorMargin))Shape("Crater rim outcrop",Rock((int)d%12),stone,at,Quaternion.Euler(d,d*.3f,0),new Vector3(28,48,45));
      batch.Box(dark,basePoint,new Vector3(5,4,8),q);batch.Box(cyan,basePoint+f.normal*2.2f,new Vector3(2,.6f,6),q);
     }
    }
   }
   yield return null;
   for(int i=0;i<55;i++){
    float distance=1720+i*38;var f=route.Evaluate(distance);float side=i%2==0?1:-1;
    Vector3 pos=f.position+f.right*(side*(180+i%5*70))+f.normal*(-160+i%6*75);
    float size=i<18?70+i%4*22:14+i%5*8;
    if(EnvironmentPlan.ClearOfRoute(route,pos,Vector3.one*size,EnvironmentPlan.CorridorMargin))Shape("Satellite asteroid "+i,Rock(i),i%3==0?charcoal:stone,pos,Quaternion.Euler(i*19,i*31,0),Vector3.one*size);
   }
   // Pressure hull, individually plated districts and recessed service channels.
   for(int i=0;i<40;i++){
    float a=i*Mathf.PI*2/40;var radial=new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);var q=Quaternion.Euler(0,0,-a*Mathf.Rad2Deg);
    for(int j=0;j<20;j++){
     var center=new Vector3(0,400,3260+j*120);
     batch.Box(dark,center+radial*380,new Vector3(62,28,120),q,24);
     batch.Box((i+j)%9==0?ivory:hull,center+radial*394,new Vector3(58,1.2f,114),q,24);
     for(int side=-1;side<=1;side+=2)batch.Box(grille,center+radial*395+q*Vector3.right*(side*23),new Vector3(3,.4f,86),q,8);
     if((i+j)%3==0)batch.Box((i%5==0)?amber:cyan,center+radial*395+q*Vector3.right*17,new Vector3(1,.5f,18),q);
    }
   }
   yield return null;
   for(int j=0;j<7;j++)for(int i=0;i<6;i++){
    float a=(i*60+j*13)*Mathf.Deg2Rad;var radial=new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);
    var center=new Vector3(0,400,3350+j*330)+radial*445;var q=Quaternion.Euler(0,0,-a*Mathf.Rad2Deg);var size=new Vector3(100,110,130);
    var ext=new Vector3(Mathf.Abs(Mathf.Cos(a))*62+Mathf.Abs(Mathf.Sin(a))*75,Mathf.Abs(Mathf.Sin(a))*62+Mathf.Abs(Mathf.Cos(a))*75,85);
    if(!EnvironmentPlan.ClearOfRoute(route,center,ext,EnvironmentPlan.CorridorMargin))continue;
    batch.Box(dark,center,new Vector3(100,105,130),q,16);
    batch.Box(j%3==0?ivory:hull,center+radial*47,new Vector3(110,16,140),q,24);
    batch.Box(glass,center+radial*57,new Vector3(84,2,94),q,18);
    for(int k=-2;k<=2;k++){
     batch.Box(hull,center+radial*59+q*new Vector3(k*19,0,0),new Vector3(2,3,104),q);
     batch.Box(white,center+radial*22+q*new Vector3(k*18,0,-65.7f),new Vector3(9,3,1),q);
     batch.Box(grille,center+radial*2+q*new Vector3(k*18,0,-66),new Vector3(11,18,1),q,4);
    }
    batch.Box(bronze,center+radial*60+q*new Vector3(-51,0,0),new Vector3(5,4,120),q);
    batch.Box(cyan,center+radial*61+q*new Vector3(51,0,0),new Vector3(1,2,110),q);
   }
   Ring("Arrival ring",new Vector3(0,400,3100),520,32,40);
   Ring("Dock ring",new Vector3(0,400,5760),560,28,36);
   // Pylons, solar radiators and docking spines establish an asymmetric inhabited silhouette.
   for(int i=0;i<4;i++){
    var origin=new Vector3(i%2==0?730:-730,400,3600+i*520);var q=Quaternion.identity;
    if(!EnvironmentPlan.ClearOfRoute(route,origin,new Vector3(165,240,110),EnvironmentPlan.CorridorMargin))continue;
    batch.Beam(dark,new Vector3(Mathf.Sign(origin.x)*380,400,origin.z),origin,16);
    batch.Box(ivory,origin,new Vector3(60,85,160),q,20);
    for(int side=-1;side<=1;side+=2){
     var panel=origin+Vector3.up*side*170;
     batch.Beam(dark,origin,panel,8);batch.Box(dark,panel,new Vector3(250,130,10),q,20);
     for(int col=0;col<10;col++)for(int row=0;row<5;row++)batch.Box(glass,panel+new Vector3(-112+col*25,-50+row*25,-6),new Vector3(23,23,1),q,24);
     batch.Box(amber,panel+new Vector3(-124,0,-7),new Vector3(2,110,1),q);
    }
   }
   // Recessed hangar aperture framed by ribs; no glowing flat substitute for the opening.
   var dock=new Vector3(-670,150,5550);var dq=Quaternion.identity;
   batch.Box(dark,dock,new Vector3(190,200,540),dq,24);
   batch.Box(grille,dock+Vector3.right*97,new Vector3(2,110,310),dq,12);
   for(int side=-1;side<=1;side+=2){batch.Box(hull,dock+new Vector3(106,side*66,0),new Vector3(22,22,355),dq);batch.Box(bronze,dock+new Vector3(108,0,side*166),new Vector3(28,154,18),dq);batch.Box(amber,dock+new Vector3(121,side*55,0),new Vector3(1,3,300),dq);}
   for(int i=-4;i<=4;i++)batch.Box(hull,dock+new Vector3(108,-82,i*53),new Vector3(30,12,8),dq);
   OrbitalLight("hangar flood",dock+new Vector3(150,0,0),new Color(1,.59f,.27f),220,6);
   Ship(new Vector3(-730,680,5650),Quaternion.Euler(0,-12,8),4.5f);
   Ship(new Vector3(680,1150,3000),Quaternion.Euler(0,140,-8),2);
   Ship(new Vector3(-740,-180,4640),Quaternion.Euler(25,20,6),1.4f);
   for(int i=0;i<7;i++){
    var at=new Vector3(-780-i%2*85,360+i%3*60,5300+i*75);var q=Quaternion.Euler(0,i*23,10);
    batch.Box(i%2==0?ivory:hull,at,new Vector3(25,22,48),q,12);
    for(int k=-1;k<=1;k++)batch.Box(dark,at+q*Vector3.forward*(k*18),new Vector3(27,24,2),q);
    batch.Box(cyan,at+q*new Vector3(0,12,0),new Vector3(16,1,2),q);
   }
   Ring("Finish crown",new Vector3(180,880,6630),110,12,16);
   // Near-road beacons remain outside the wheel and chase-camera envelope. Only a few emit real light.
   for(float d=3200;d<route.FinishDistance;d+=95){
    var f=route.Evaluate(d);var q=Quaternion.LookRotation(f.tangent,f.normal);
    for(int side=-1;side<=1;side+=2){var at=f.position+f.right*side*(f.halfWidth+4);
     batch.Box(dark,at+f.normal*1.5f,new Vector3(1.3f,3,2),q);batch.Box(d%380<190?cyan:amber,at+f.normal*3,new Vector3(1.6f,.8f,2.5f),q);
    }
   }
   foreach(int section in new[]{4,5,6}){
    var s=route.Definition.composition[section];int count=section==5?8:2;
    for(int i=0;i<count;i++){
     float d=30+Mathf.Lerp(s.startIndex,s.endIndex,(i+.5f)/count)*5;var f=route.Evaluate(d);
     var q=Quaternion.LookRotation(f.tangent,f.normal);var at=f.position+f.right*(f.halfWidth+7);
     batch.Box(dark,at+f.normal*4,new Vector3(1.5f,8,2),q);
     batch.Box(hull,at+f.normal*8,new Vector3(4,1.5f,5),q);
     batch.Box(section==6?amber:cyan,at+f.normal*7-f.right*1.5f,new Vector3(1,1,4),q);
     OrbitalLight("service beacon",at+f.normal*7-f.right*3,section==6?new Color(1,.56f,.23f):new Color(.23f,.64f,1),210,8);
    }
   }
   yield return batch.Flush();
   void Ring(string name,Vector3 center,float radius,float thickness,float depth){
    const int count=80;for(int i=0;i<count;i++){
     float a=i*Mathf.PI*2/count;var radial=new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);var q=Quaternion.Euler(0,0,-a*Mathf.Rad2Deg);float width=2*radius*Mathf.Tan(Mathf.PI/count);
     batch.Box(dark,center+radial*radius,new Vector3(width,thickness,depth),q,16);
     batch.Box(i%5==0?bronze:hull,center+radial*(radius+thickness*.5f),new Vector3(width*.88f,2,depth+4),q,12);
     batch.Box(i%5==0?amber:cyan,center+radial*(radius-thickness*.5f),new Vector3(width*.72f,1,3),q);
     if(i%5==0)batch.Box(grille,center+radial*(radius+thickness*.5f+2),new Vector3(width*.65f,2,depth*.6f),q,4);
    }
   }
   void Ship(Vector3 at,Quaternion q,float scale){
    void Part(Material m,Vector3 p,Vector3 size)=>batch.Box(m,at+q*(p*scale),size*scale,q,8);
    Part(dark,Vector3.zero,new Vector3(17,12,80));Part(hull,new Vector3(0,4,5),new Vector3(24,11,54));
    Part(ivory,new Vector3(0,0,37),new Vector3(12,8,22));Part(glass,new Vector3(0,10,22),new Vector3(14,2,15));
    Part(dark,new Vector3(0,-3,-12),new Vector3(48,5,22));
    for(int side=-1;side<=1;side+=2){Part(hull,new Vector3(side*23,-2,-19),new Vector3(9,9,35));Part(bronze,new Vector3(side*23,-2,-37),new Vector3(11,11,3));Part(cyan,new Vector3(side*23,-2,-39),new Vector3(7,7,1));Part(red,new Vector3(side*28,0,-10),new Vector3(1,2,3));}
    for(int i=0;i<5;i++)Part(grille,new Vector3(0,10,-20+i*7),new Vector3(19,2,3));
   }
  }
 }
}
