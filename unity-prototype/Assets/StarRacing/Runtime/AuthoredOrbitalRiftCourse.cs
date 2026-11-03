using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 // Blockout revision: the normal is authored with the path, including the station underside.
 public static class AuthoredOrbitalRiftCourse {
  public const string Id="orbital-rift",Title="Орбитальный разлом",Family="authored-orbital-rift";
  public const int Revision=1,PresentationRevision=1;
  public const uint ScenerySeed=271026;
  public static readonly Vector3 StationCenter=new Vector3(0,400,4400);
  public static readonly string[] Sections={"Край кратера","Горная выработка","Выход в пустоту","Каменный архипелаг","Посадка на корпус","Обход станции","Нижние доки","Корона"};
  public static bool Is(Definition d)=>d!=null&&d.family==Family;
  struct Knot {
   public DVec p,up;public double bank,width;public int section;
   public Knot(double x,double y,double z,int section,double bank=0,double width=11,DVec? up=null){p=new DVec(x,y,z);this.section=section;this.bank=bank*Math.PI/180;this.width=width;this.up=up??new DVec(0,1,0);}
  }
  static Knot[] Knots(){
   var k=new List<Knot>{
    new Knot(0,60,0,0,0,12),new Knot(0,60,240,0,0,12),new Knot(-100,80,490,0,-12,12),
    new Knot(-240,120,730,1,-10),new Knot(-170,150,970,1,12),new Knot(40,170,1220,1,16),new Knot(90,200,1460,1,0),
    new Knot(90,250,1680,2),new Knot(140,350,1930,2),new Knot(250,440,2170,2,12,12),
    new Knot(390,520,2410,3,18),new Knot(240,600,2670,3,-22),new Knot(40,690,2930,3,-14),
    new Knot(0,800,3220,4,0,12),new Knot(0,800,3460,4,0,12)
   };
   // Three-quarter helical orbit, radius 400m and ample separation along the station axis.
   for(int i=1;i<=12;i++){
    double a=i*Math.PI/8;var up=new DVec(Math.Sin(a),Math.Cos(a),0);
    k.Add(new Knot(400*Math.Sin(a),400+400*Math.Cos(a),3460+i*155,i==12?6:5,0,12,up));
   }
   k.Add(new Knot(-400,400,5520,6,0,12,new DVec(-1,0,0)));
   k.Add(new Knot(-430,470,5780,6,0,12,new DVec(-.707,.707,0)));
   k.Add(new Knot(-400,650,6030,7,0,12));
   k.Add(new Knot(-220,830,6280,7,16,12));k.Add(new Knot(80,880,6530,7,12,12));
   k.Add(new Knot(320,880,6770,7,0,12));k.Add(new Knot(540,880,7000,7,0,12));
   return k.ToArray();
  }
  struct Dense {public DVec p,up;public double distance,bank,width;public int section;}
  static double Lerp(double a,double b,double t)=>a+(b-a)*t;
  static DVec Tangent(Knot[] k,int i) {
   if(i==0)return k[1].p-k[0].p;
   if(i==k.Length-1)return k[i].p-k[i-1].p;
   var a=k[i].p-k[i-1].p;var b=k[i+1].p-k[i].p;
   return (a.Unit()+b.Unit()).Unit()*Math.Min(Math.Sqrt(a.Squared),Math.Sqrt(b.Squared));
  }
  public static Definition Compile() {
   var k=Knots();var dense=new List<Dense>();double distance=0;
   for(int i=0;i<k.Length-1;i++) {
    var a=k[i];var b=k[i+1];var m0=Tangent(k,i);var m1=Tangent(k,i+1);
    int steps=Math.Max(32,(int)Math.Ceiling(Math.Sqrt((b.p-a.p).Squared)));
    for(int j=0;j<steps;j++) {
     double t=(double)j/steps,t2=t*t,t3=t2*t,s=t2*(3-2*t);
     var p=a.p*(2*t3-3*t2+1)+m0*(t3-2*t2+t)+b.p*(-2*t3+3*t2)+m1*(t3-t2);
     if(dense.Count>0)distance+=Math.Sqrt((p-dense[dense.Count-1].p).Squared);
     dense.Add(new Dense{p=p,up=(a.up*(1-s)+b.up*s).Unit(),distance=distance,bank=Lerp(a.bank,b.bank,s),width=Lerp(a.width,b.width,s),section=a.section});
    }
   }
   var last=k[k.Length-1];distance+=Math.Sqrt((last.p-dense[dense.Count-1].p).Squared);
   dense.Add(new Dense{p=last.p,up=last.up,distance=distance,bank=last.bank,width=last.width,section=last.section});
   int count=(int)Math.Floor(distance/5)+1,cursor=0;var samples=new Sample[count];var banks=new double[count];var ups=new DVec[count];
   for(int i=0;i<count;i++) {
    double at=i*5;while(cursor<dense.Count-2&&dense[cursor+1].distance<at)cursor++;
    var a=dense[cursor];var b=dense[cursor+1];double t=(at-a.distance)/(b.distance-a.distance);
    samples[i]=new Sample{index=i,distance=at,progress=(double)i/(count-1),position=a.p+(b.p-a.p)*t,
     halfWidth=Lerp(a.width,b.width,t),segmentIndex=a.section,compositionIndex=a.section,compositionRole=Sections[a.section],
     kind=a.section==4?"hill":a.section==6?"sweeper":a.section==0?"straight":"turn",patternKind="authored",railLeft=true,railRight=true};
    banks[i]=Lerp(a.bank,b.bank,t);ups[i]=(a.up*(1-t)+b.up*t).Unit();
   }
   DVec baseNormal=new DVec(0,1,0),previous=new DVec(0,0,1);
   for(int i=0;i<count;i++) {
    var s=samples[i];var tangent=(samples[Math.Min(i+1,count-1)].position-samples[Math.Max(0,i-1)].position).Unit(previous);
    var up=ups[i];baseNormal=(up-tangent*DVec.Dot(up,tangent)).Unit();
    s.tangent=tangent;s.normal=baseNormal.Rotate(tangent,banks[i]).Unit();s.right=DVec.Cross(tangent,s.normal).Unit();
    previous=tangent;
   }
   var checkpoints=new List<Checkpoint>();
   for(int i=0;i<count;i++) {
    var s=samples[i];if(i%60==0||i==count-1)checkpoints.Add(new Checkpoint{index=checkpoints.Count,sampleIndex=i,distance=s.distance,position=s.position,tangent=s.tangent,normal=s.normal,right=s.right});
    s.checkpointIndex=checkpoints.Count-1;
   }
   var sections=new List<CompositionSection>();
   for(int i=0;i<Sections.Length;i++){int first=Array.FindIndex(samples,s=>s.segmentIndex==i),end=Array.FindLastIndex(samples,s=>s.segmentIndex==i);sections.Add(new CompositionSection{index=i,startIndex=first,endIndex=end,role=Sections[i]});}
   var d=new Definition{version=12,requestedSeed=ScenerySeed,seed=ScenerySeed,theme="space-station",guardrailMode="full",family=Family,
    samples=samples,branches=Array.Empty<Branch>(),jumps=Array.Empty<Jump>(),patterns=Array.Empty<Pattern>(),composition=sections.ToArray(),checkpoints=checkpoints.ToArray(),totalLength=(count-1)*5,estimatedDuration=(count-1)*5/45d};
   // This hash is additional source evidence; NativeTrackCourse also hashes all serialized geometry.
   using(var stream=new MemoryStream()) {
    using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){writer.Write(Id);writer.Write(Revision);foreach(var s in samples){writer.Write(s.position.x);writer.Write(s.position.y);writer.Write(s.position.z);writer.Write(s.normal.x);writer.Write(s.normal.y);writer.Write(s.normal.z);writer.Write(s.halfWidth);}}
    using(var sha=SHA256.Create())d.hash=BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
   }
   var errors=AuthoredCloudCityCourse.Validate(d);if(errors.Length>0)throw new ArgumentException("Authored course invalid: "+string.Join(", ",errors));
   return d;
  }
 }
}
