using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using BigInteger=System.Numerics.BigInteger;
namespace StarRacingPrototype {
 public static class ApexPredicateChecks {
  // Independent integer grid, one unit=2^-149. This reference deliberately
  // preserves the ORIGINAL partial cache publication and mutable-index reads.
  readonly struct Point {
   readonly BigInteger x,y,z;
   public Point(Vector3 p){x=I(p.x);y=I(p.y);z=I(p.z);}
   Point(BigInteger x,BigInteger y,BigInteger z){this.x=x;this.y=y;this.z=z;}
   static BigInteger I(float f){uint b=(uint)BitConverter.SingleToInt32Bits(f);int e=(int)((b>>23)&255);if(e==255)throw new ArgumentException("Nonfinite support coordinate");BigInteger m=b&0x7fffff;if(e!=0)m=(m+0x800000)<<(e-1);return (b&0x80000000)==0?m:-m;}
   public static Point operator -(Point a,Point b)=>new Point(a.x-b.x,a.y-b.y,a.z-b.z);
   public static Point Cross(Point a,Point b)=>new Point(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
   public static int Dot(Point a,Point b)=>(a.x*b.x+a.y*b.y+a.z*b.z).Sign;
  }
  sealed class Original {
   public readonly RoadVolumeMesh.SupportCell owner;Point[] corners,normals;
   public Original(RoadVolumeMesh.SupportCell owner){this.owner=owner;}
   public bool Contains(Vector3 apex){
    if(corners==null){
     corners=new Point[8];for(int i=0;i<8;i++)corners[i]=new Point(owner.corners[i]);
     normals=new Point[owner.triangles.Length/3];
     for(int i=0;i<normals.Length;i++)normals[i]=Point.Cross(corners[owner.triangles[i*3+1]]-corners[owner.triangles[i*3]],corners[owner.triangles[i*3+2]]-corners[owner.triangles[i*3]]);
    }
    var p=new Point(apex);for(int i=0;i<normals.Length;i++)if(Point.Dot(p-corners[owner.triangles[i*3]],normals[i])>0)return false;return true;
   }
   public int Sign(int i,Vector3 p)=>Point.Dot(new Point(p)-corners[owner.triangles[i*3]],normals[i]);
  }
  [Serializable] sealed class Report {public int rawCases,certified,positive,negative,uncertain,zero,containsCalls,containsCertified,containsFallback,exceptions,checks;public string oracle="independent original partial-cache flow/global2^-149 integers";}
  static Report report;static uint random;static readonly Dictionary<RoadVolumeMesh.SupportCell,Original> refs=new Dictionary<RoadVolumeMesh.SupportCell,Original>();
  static void Check(bool ok,string message){report.checks++;if(!ok)throw new Exception(message);}
  static float F(uint bits)=>BitConverter.Int32BitsToSingle(unchecked((int)bits));
  static float RandomFloat(){random^=random<<13;random^=random>>17;random^=random<<5;uint bits=random;if((bits&0x7f800000)==0x7f800000)bits^=0x00800000;return F(bits);}
  static Vector3 RandomPoint()=>new Vector3(RandomFloat(),RandomFloat(),RandomFloat());
  static void Raw(Vector3 a,Vector3 b,Vector3 c,Vector3 origin,Vector3 point){
   int expected=Point.Dot(new Point(point)-new Point(origin),Point.Cross(new Point(b)-new Point(a),new Point(c)-new Point(a)));
   bool fast=RoadVolumeMesh.EditorApexSign(a,b,c,origin,point,out int actual);report.rawCases++;
   if(expected==0)report.zero++;
   if(fast){report.certified++;if(actual>0)report.positive++;else report.negative++;Check(actual==expected,"Certified determinant sign differs from integer oracle");}
   else report.uncertain++;
  }
  static RoadVolumeMesh.SupportCell Cell(float shift=0,float warp=0){
   var origin=new Vector3(shift,shift/16,shift/4);
   var p=new[]{origin,origin+new Vector3(0,warp,1),origin+Vector3.right,origin+new Vector3(1,warp,1)};
   var cell=new RoadVolumeMesh.SupportCell(refs.Count,new RoadFace{startDistance=0,endDistance=1,leftStart=0,leftEnd=0,rightStart=1,rightEnd=1},p,Enumerable.Repeat(Vector3.up,4).ToArray(),new[]{0,1,3,2,0},new[]{0,1,2,2,1,3},0);
   refs[cell]=new Original(cell);return cell;
  }
  static void Query(RoadVolumeMesh.SupportCell cell,Vector3 p){
   bool expected=false,actual=false;Exception ee=null,ae=null;
   try{expected=refs[cell].Contains(p);}catch(Exception e){ee=e;}
   try{actual=cell.ContainsApex(p);}catch(Exception e){ae=e;}
   report.containsCalls++;Check(ee?.GetType()==ae?.GetType()&&ee?.Message==ae?.Message,"Contains exception type/message/order differs");
   if(ee!=null)report.exceptions++;else Check(expected==actual,"Contains result differs from original cache state");
  }
  public static void Run(){
   report=new Report();random=0x85ebca6b;refs.Clear();
   RoadVolumeMesh.ExactSignObserved=(owner,i,p,sign,certified)=>{if(certified){report.containsCertified++;Check(sign==refs[owner].Sign(i,p),"Cached certified face sign differs from original");}else report.containsFallback++;};
   try{
    for(int i=0;i<100000;i++)Raw(RandomPoint(),RandomPoint(),RandomPoint(),RandomPoint(),RandomPoint());
    float[] values={0f,F(0x80000000),F(1),-F(1),F(0x007fffff),F(0x00800000),-F(0x00800000),1f,-1f,F(0x3f800001),F(0x3f7fffff),4096f,F(0x7f7fffff),-F(0x7f7fffff)};
    foreach(float scale in values){
     var a=Vector3.zero;var b=new Vector3(scale,scale,0);var c=new Vector3(0,scale,scale);
     foreach(float p in values){Raw(a,b,c,a,new Vector3(p,p,p));Raw(a,b,c,new Vector3(p,0,0),new Vector3(0,p,0));}
     Raw(a,b,c,a,a);Raw(a,b,c,a,b);Raw(a,b,c,a,c);
    }
    // Fixed near-coplanar inputs, one binary32 ULP on either side of a plane.
    foreach(float y in new[]{F(0x3f7fffff),1f,F(0x3f800001)})Raw(Vector3.zero,new Vector3(1,1,0),new Vector3(0,1,1),Vector3.zero,new Vector3(1,y,0));
    foreach(float shift in new[]{0f,1024f,-4096f})foreach(float warp in new[]{0f,.001f,.3f}){
     var cell=Cell(shift,warp);var origin=cell.corners[0];
     foreach(var p in new[]{new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.1f,.5f),Vector3.zero,Vector3.one,new Vector3(-1,-.5f,.5f),new Vector3(.5f,-1,.5f)}){Query(cell,origin+p);Query(cell,origin+p);}
     foreach(var corner in cell.corners)Query(cell,corner);
     Query(cell,new Vector3(float.NaN,0,0));Query(cell,new Vector3(0,float.PositiveInfinity,0));
    }
    var inside=new Vector3(.5f,-.5f,.5f);var outside=new Vector3(.5f,.5f,.5f);var nan=new Vector3(float.NaN,0,0);
    var mutable=Cell();Query(mutable,inside);
    for(int i=0;i<8;i++)mutable.corners[i]=new Vector3(float.NaN,17,19);Query(mutable,inside);
    mutable.triangles[0]=4;Query(mutable,inside);mutable.triangles[1]=99;mutable.triangles[2]=-1;Query(mutable,inside);
    mutable.triangles[0]=99;Query(mutable,nan);Query(mutable,inside);
    var late=Cell();Query(late,inside);late.triangles[33]=99;Query(late,outside);Query(late,inside);
    foreach(int bad in new[]{-1,99}){
     var broken=Cell();broken.triangles[4]=bad;Query(broken,nan);Query(broken,inside);Query(broken,nan);broken.triangles[0]=99;Query(broken,inside);
    }
    foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
     var broken=Cell();broken.corners[3]=new Vector3(bad,0,0);Query(broken,nan);Query(broken,inside);Query(broken,nan);
    }
    Check(report.positive>0&&report.negative>0&&report.uncertain>0&&report.zero>0&&report.exceptions>0&&report.containsCertified>0&&report.containsFallback>0,"Missing sign/fallback/cache/error coverage");
    string output=Environment.GetEnvironmentVariable("STAR_RACING_WORLD_QA_DIR");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"exact-predicates.json"),JsonUtility.ToJson(report,true));
    Debug.Log("APEX_PREDICATE_CONTRACTS_OK checks="+report.checks);
   }finally{RoadVolumeMesh.ExactSignObserved=null;refs.Clear();}
  }
  public static void RunAffected(){Run();OwnerPreparationChecks.RunDataContracts();Debug.Log("APEX_AFFECTED_CONTRACTS_OK");}
 }
}
