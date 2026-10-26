using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using StarRacingPrototype.Procedural;

namespace StarRacingPrototype {
 // Authored coordinates, not a generator preset. Runtime geometry format remains Native12.
 public static class AuthoredCloudCityCourse {
  public const string Id="cloud-city-eye", Title="Око облачного города", Family="authored-cloud-city-eye";
  public const int Revision=1, PresentationRevision=1;
  public const uint ScenerySeed=261026;
  public static readonly string[] Sections={"Панорамный старт","Городской слалом","Сквозной атриум","Око города","Нырок","Нижняя терраса","Двойная спираль","Верхний мост"};
  public static bool Is(Definition d)=>d!=null&&d.family==Family;
  public static readonly Vector3 Eye=new Vector3(0,580,2050);
  public static readonly Vector3 Atrium=new Vector3(0,500,1560);
  public static readonly Vector3 TowerA=new Vector3(680,0,3090),TowerB=new Vector3(1320,0,3500);

  struct Knot {
   public DVec p;public double bank,width;public int section;
   public Knot(double x,double y,double z,int section,double bank=0,double width=11){p=new DVec(x,y,z);this.section=section;this.bank=bank*Math.PI/180;this.width=width;}
  }
  // Each numbered district is composed deliberately. The dive stays just short of vertical;
  // its lower transition finishes before the terrace starts turning.
  static Knot[] Knots()=>new[]{
   new Knot(0,420,0,0,0,12),new Knot(0,420,220,0,0,12),new Knot(0,430,450,0,0,12),
   new Knot(-110,450,690,1,-16),new Knot(100,475,940,1,22),new Knot(-95,490,1200,1,-20),
   new Knot(0,500,1450,2),new Knot(0,500,1640,2),new Knot(0,545,1840,3),
   new Knot(0,580,2050,3),new Knot(0,580,2240,3),
   new Knot(0,550,2370,4),new Knot(0,425,2415,4),new Knot(0,220,2455,4),
   new Knot(0,80,2520,4),new Knot(0,25,2670,4),
   new Knot(90,30,2870,5,12,12),new Knot(280,45,2910,5,18,12),
   // First tower: an ascending 270 degree arc, radius about 235 metres.
   new Knot(445,80,3090,6,-24),new Knot(515,135,3255,6,-34),new Knot(680,185,3325,6,-38),
   new Knot(845,235,3255,6,-38),new Knot(915,285,3090,6,-34),new Knot(845,330,2925,6,-28),
   new Knot(680,365,2855,6,-18),new Knot(515,400,2925,6,-24),
   // Bridge crosses the first arc well above it, then winds around the second tower.
   new Knot(450,435,3110,6,-20),new Knot(670,470,3430,6,12),
   new Knot(1085,500,3500,6,32),new Knot(1155,535,3335,6,42),new Knot(1320,570,3265,6,48),
   new Knot(1485,605,3335,6,42),new Knot(1555,640,3500,6,36),new Knot(1485,675,3665,6,30),
   new Knot(1320,710,3735,7,18,12),new Knot(1090,740,3690,7,0,12),
   new Knot(790,760,3560,7,-26,12),new Knot(480,770,3440,7,24,12),
   new Knot(190,770,3320,7,0,12),new Knot(-100,770,3200,7,0,12)
  };
  struct Dense {public DVec p;public double distance,bank,width;public int section;}
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
     dense.Add(new Dense{p=p,distance=distance,bank=Lerp(a.bank,b.bank,s),width=Lerp(a.width,b.width,s),section=a.section});
    }
   }
   var last=k[k.Length-1];distance+=Math.Sqrt((last.p-dense[dense.Count-1].p).Squared);
   dense.Add(new Dense{p=last.p,distance=distance,bank=last.bank,width=last.width,section=last.section});
   int count=(int)Math.Floor(distance/5)+1,cursor=0;var samples=new Sample[count];var banks=new double[count];
   for(int i=0;i<count;i++) {
    double at=i*5;while(cursor<dense.Count-2&&dense[cursor+1].distance<at)cursor++;
    var a=dense[cursor];var b=dense[cursor+1];double t=(at-a.distance)/(b.distance-a.distance);
    samples[i]=new Sample{index=i,distance=at,progress=(double)i/(count-1),position=a.p+(b.p-a.p)*t,
     halfWidth=Lerp(a.width,b.width,t),segmentIndex=a.section,compositionIndex=a.section,compositionRole=Sections[a.section],
     kind=a.section==4?"hill":a.section==6?"sweeper":a.section==0?"straight":"turn",patternKind="authored",railLeft=true,railRight=true};
    banks[i]=Lerp(a.bank,b.bank,t);
   }
   DVec baseNormal=new DVec(0,1,0),previous=new DVec(0,0,1);
   for(int i=0;i<count;i++) {
    var s=samples[i];var tangent=(samples[Math.Min(i+1,count-1)].position-samples[Math.Max(0,i-1)].position).Unit(previous);
    var axis=DVec.Cross(previous,tangent);double sine=Math.Sqrt(axis.Squared);
    if(sine>1e-10)baseNormal=baseNormal.Rotate(axis,Math.Atan2(sine,DVec.Dot(previous,tangent)));
    baseNormal=(baseNormal-tangent*DVec.Dot(baseNormal,tangent)).Unit();
    // Gradual world-up anchoring removes long-arc transport drift without a vertical singularity.
    var up=new DVec(0,1,0);var projected=up-tangent*DVec.Dot(up,tangent);
    if(projected.Squared>.04)baseNormal=(baseNormal*.92+projected.Unit()*.08).Unit();
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
   var d=new Definition{version=12,requestedSeed=ScenerySeed,seed=ScenerySeed,theme="cloud-city",guardrailMode="full",family=Family,
    samples=samples,branches=Array.Empty<Branch>(),jumps=Array.Empty<Jump>(),patterns=Array.Empty<Pattern>(),composition=sections.ToArray(),checkpoints=checkpoints.ToArray(),totalLength=(count-1)*5,estimatedDuration=(count-1)*5/45d};
   // This hash is additional source evidence; NativeTrackCourse also hashes all serialized geometry.
   using(var stream=new MemoryStream()) {
    using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){writer.Write(Id);writer.Write(Revision);foreach(var s in samples){writer.Write(s.position.x);writer.Write(s.position.y);writer.Write(s.position.z);writer.Write(s.normal.x);writer.Write(s.normal.y);writer.Write(s.normal.z);writer.Write(s.halfWidth);}}
    using(var sha=SHA256.Create())d.hash=BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
   }
   var errors=Validate(d);if(errors.Length>0)throw new ArgumentException("Authored course invalid: "+string.Join(", ",errors));
   return d;
  }
  public static string[] Validate(Definition d) {
   var errors=new List<string>();void Check(bool ok,string why){if(!ok&&!errors.Contains(why))errors.Add(why);}
   Check(d.samples.Length>100&&d.composition.Length==Sections.Length,"section-count");
   for(int i=0;i<d.samples.Length;i++) {
    var s=d.samples[i];Check(!double.IsNaN(s.position.Squared)&&!double.IsInfinity(s.position.Squared),"nonfinite");
    Check(s.index==i&&s.distance==i*5&&s.halfWidth>=7.5&&s.halfWidth<=12,"sample-grid-width");
    Check(Math.Abs(s.tangent.Squared-1)<1e-6&&Math.Abs(s.normal.Squared-1)<1e-6&&Math.Abs(s.right.Squared-1)<1e-6&&Math.Abs(DVec.Dot(s.tangent,s.normal))<1e-6,"frame");
    if(i>0){var a=d.samples[i-1];Check(Math.Abs(s.halfWidth-a.halfWidth)<=.2,"width-slope");Check(DVec.Dot(a.tangent,s.tangent)>=Math.Cos(.12)&&DVec.Dot(a.normal,s.normal)>=Math.Cos(.14),"curvature-step");Check(Math.Abs(Math.Sqrt((s.position-a.position).Squared)-5)<.03,"spacing");}
   }
   Check(Validator.Support(d),"road-support");Check(!Validator.Intersects(d),"clearance");
   foreach(var section in d.composition)Check(section.startIndex>=0&&section.endIndex>=section.startIndex,"missing-section");
   for(int i=1;i<d.checkpoints.Length;i++)Check(d.checkpoints[i].distance>d.checkpoints[i-1].distance,"checkpoint-order");
   return errors.ToArray();
  }
 }

 public static class CourseSource {
  public static NativeTrackCourse Generate(LocalRaceConfig config,string theme)=>GenerateData(config,theme).Complete();
  internal static NativeTrackGenerator.Generated GenerateData(LocalRaceConfig config,string theme,bool fallback=false) {
   if(!config.IsAuthored)return NativeTrackGenerator.GenerateData(config.Seed,new GraphCourseSettings(theme,config.rails,config.jumps),config.generatorRevision,fallback);
   if(!config.Validate(out var error))throw new ArgumentException(error);
   return new NativeTrackGenerator.Generated{single=AuthoredCloudCityCourse.Compile(),settings=new GraphCourseSettings("cloud-city","full",false),courseId=AuthoredCloudCityCourse.Id,contentRevision=AuthoredCloudCityCourse.Revision};
  }
 }
}
