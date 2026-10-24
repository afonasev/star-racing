using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace StarRacingPrototype {
 // Native12 Single uses the validated simulation basis by default. The opt-out is for diagnostic comparisons.
 public static class BasisNativeExperiment {
  public static bool Enabled=>EditorOverride??(Array.IndexOf(Environment.GetCommandLineArgs(),"--race-basis-baseline")<0);
#if UNITY_EDITOR
  public static bool? EditorOverride;
#else
  static bool? EditorOverride=>null;
#endif
  public static Vector3 ToCanonical(Vector3 p)=>new Vector3(-p.z,p.y,p.x);
  public static Pose ToCanonical(Pose p)=>new Pose(ToCanonical(p.position),Quaternion.LookRotation(ToCanonical(p.rotation*Vector3.forward),ToCanonical(p.rotation*Vector3.up)));
  public static readonly bool Audit=Array.IndexOf(Environment.GetCommandLineArgs(),"--race-basis-audit")>=0;
  static readonly MethodInfo Copy=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
  static T Clone<T>(T obj)=>(T)Copy.Invoke(obj,null);
  static Procedural.DVec R(Procedural.DVec p)=>new Procedural.DVec(p.z,p.y,-p.x);
  static Procedural.DVec Inv(Procedural.DVec p)=>new Procedural.DVec(-p.z,p.y,p.x);
  public static Procedural.Definition Transform(Procedural.Definition source,bool inverse=false){
   var d=Clone(source);d.samples=new Procedural.Sample[source.samples.Length];
   for(int i=0;i<d.samples.Length;i++){var s=d.samples[i]=Clone(source.samples[i]);s.position=inverse?Inv(s.position):R(s.position);s.tangent=inverse?Inv(s.tangent):R(s.tangent);s.normal=inverse?Inv(s.normal):R(s.normal);s.right=inverse?Inv(s.right):R(s.right);}
   d.checkpoints=new Procedural.Checkpoint[source.checkpoints.Length];
   for(int i=0;i<d.checkpoints.Length;i++){var s=d.checkpoints[i]=Clone(source.checkpoints[i]);s.position=inverse?Inv(s.position):R(s.position);s.tangent=inverse?Inv(s.tangent):R(s.tangent);s.normal=inverse?Inv(s.normal):R(s.normal);s.right=inverse?Inv(s.right):R(s.right);}
   return d;
  }
  public static Procedural.Definition Prepare(Procedural.Definition source,bool transform){
   string canonical=JsonUtility.ToJson(source);var result=transform?Transform(source):source;
   if(transform&&(canonical!=JsonUtility.ToJson(Transform(result,true))||canonical!=JsonUtility.ToJson(source)))throw new Exception("Native basis canonical source conservation failed");
   using(var sha=SHA256.Create())Debug.Log("BASIS_NATIVE_SOURCE enabled="+transform+" requested="+source.requestedSeed+" actual="+source.seed+" sha256="+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-","").ToLowerInvariant());
   return result;
  }
 }
}
