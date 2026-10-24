using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using BigInteger=System.Numerics.BigInteger;
namespace StarRacingPrototype {
 public static class ExactSupportArithmeticChecks {
  readonly struct Oracle {
   readonly BigInteger x,y,z;
   public Oracle(Vector3 p){x=I(p.x);y=I(p.y);z=I(p.z);}
   Oracle(BigInteger x,BigInteger y,BigInteger z){this.x=x;this.y=y;this.z=z;}
   // Independent baseline definition: all finite floats in units2^-149.
   static BigInteger I(float f){uint bits=(uint)BitConverter.SingleToInt32Bits(f);int e=(int)((bits>>23)&255);if(e==255)throw new ArgumentException();BigInteger m=bits&0x7fffff;if(e!=0)m=(m+0x800000)<<(e-1);return (bits&0x80000000)==0?m:-m;}
   public static Oracle operator -(Oracle a,Oracle b)=>new Oracle(a.x-b.x,a.y-b.y,a.z-b.z);
   public static Oracle Cross(Oracle a,Oracle b)=>new Oracle(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
   public static Oracle CrossMinusPoint(Oracle cross,Oracle point)=>cross-new Oracle(point.x<<149,point.y<<149,point.z<<149);
   public static int DotSign(Oracle a,Oracle b)=>(a.x*b.x+a.y*b.y+a.z*b.z).Sign;
  }
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_EXACT_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("STAR_RACING_EXACT_QA_DIR required");Directory.CreateDirectory(output);
   var type=typeof(RoadVolumeMesh).GetNestedType("ExactPoint",BindingFlags.NonPublic);var ctor=type.GetConstructor(new[]{typeof(Vector3)});var sub=type.GetMethod("op_Subtraction");var cross=type.GetMethod("Cross");var dot=type.GetMethod("DotSign");if(dot==null)throw new Exception("Expected dyadic implementation");
   object P(Vector3 p)=>ctor.Invoke(new object[]{p});object Sub(object a,object b)=>sub.Invoke(null,new[]{a,b});object Cross(object a,object b)=>cross.Invoke(null,new[]{a,b});int Dot(object a,object b)=>(int)dot.Invoke(null,new[]{a,b});
   uint state=0x72b59a31;uint Next(){state^=state<<13;state^=state>>17;state^=state<<5;return state;}
   float RandomFloat(){uint bits=Next();if((bits&0x7f800000)==0x7f800000)bits^=0x00800000;return BitConverter.Int32BitsToSingle((int)bits);}
   float[] special={0,BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),float.Epsilon,-float.Epsilon,BitConverter.Int32BitsToSingle(0x007fffff),BitConverter.Int32BitsToSingle(0x00800000),1,-1,BitConverter.Int32BitsToSingle(0x3f800001),BitConverter.Int32BitsToSingle(0x3f7fffff),float.MaxValue,-float.MaxValue,1024,-1024};
   int assertions=0;void Equal(int expected,int actual,string name){assertions++;if(expected!=actual)throw new Exception("Exact sign mismatch "+name+" expected="+expected+" actual="+actual);}
   for(int i=0;i<18000;i++){
    Vector3 V()=>i<9000?new Vector3(special[Next()%special.Length],special[Next()%special.Length],special[Next()%special.Length]):new Vector3(RandomFloat(),RandomFloat(),RandomFloat());
    Vector3 a=V(),b=V(),c=V(),d=V();if(i%5==0)d=a;if(i%7==0)c=b;if(i%11==0)a=Vector3.zero;
    var x=P(a);var y=P(b);var z=P(c);var w=P(d);var A=new Oracle(a);var B=new Oracle(b);var C=new Oracle(c);var D=new Oracle(d);
    Equal(Oracle.DotSign(A,B),Dot(x,y),"dot "+i);
    Equal(Oracle.DotSign(D-A,Oracle.Cross(B-A,C-A)),Dot(Sub(w,x),Cross(Sub(y,x),Sub(z,x))),"orientation "+i);
    Equal(Oracle.DotSign(Oracle.CrossMinusPoint(Oracle.Cross(A,B),C),D-A),Dot(Sub(Cross(x,y),z),Sub(w,x)),"mixed-scale "+i);
   }
   foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})for(int axis=0;axis<3;axis++){
    var v=Vector3.zero;v[axis]=value;try{P(v);throw new Exception("Accepted nonfinite coordinate");}catch(TargetInvocationException e) when(e.InnerException is ArgumentException){assertions++;}
   }
   File.WriteAllText(Path.Combine(output,"result.json"),"{\"assertions\":"+assertions+",\"finiteCases\":18000,\"mismatches\":0,\"oracle\":\"baseline global2^-149 independent BigInteger\"}\n");Debug.Log("EXACT_SUPPORT_PARITY_OK assertions="+assertions);
  }
 }
}
