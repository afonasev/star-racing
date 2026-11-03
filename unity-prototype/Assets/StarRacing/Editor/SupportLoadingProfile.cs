using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using BigInteger=System.Numerics.BigInteger;
namespace StarRacingPrototype {
 public static class SupportLoadingProfile {
  const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  [Serializable] public sealed class Leaf {public string name;public int count;public double seconds,maximum;}
  [Serializable] public sealed class Report {public double coldSeconds;public long allocatedBytes;public bool allocationCounterAvailable;public int supports,initial,inward,centroid,frameQueries,uniqueFrames,cancelledPairCandidates,cancelledIntervals;public string inventory,apexIdentity,frameIdentity,config,preparationIdentity;public Leaf[] leaves,groupSizes;public string limitation="Exclusive headless Editor CPU; one cold world, assets prewarmed; observer identical in A/B. No Player/GPU/human acceptance. Nested iterator intervals must not be added.";}
  readonly struct Sample {public readonly RoadVolumeMesh.SupportTriangle triangle;public readonly Vector3 apex;public readonly bool fallback;public Sample(RoadVolumeMesh.SupportTriangle t,Vector3 a,bool f){triangle=t;apex=a;fallback=f;}}
  static readonly List<Sample> samples=new List<Sample>();static readonly Dictionary<string,Leaf> leaves=new Dictionary<string,Leaf>();
  static readonly List<(int geometry,int bits,TrackFrame frame)> frames=new List<(int,int,TrackFrame)>();
  static readonly HashSet<(int,int)> frameKeys=new HashSet<(int,int)>();
  static readonly Dictionary<int,int> groups=new Dictionary<int,int>();
  static readonly List<(RoadVolumeMesh.BoundaryScalar a,RoadVolumeMesh.BoundaryScalar b)> pairs=new List<(RoadVolumeMesh.BoundaryScalar,RoadVolumeMesh.BoundaryScalar)>();
  static readonly List<(RoadVolumeMesh.BoundaryScalar a,RoadVolumeMesh.BoundaryScalar b,float from,float to)> intervals=new List<(RoadVolumeMesh.BoundaryScalar,RoadVolumeMesh.BoundaryScalar,float,float)>();
  static Stopwatch clock;static long allocated;
  public static void Begin(){
   samples.Clear();leaves.Clear();oracle.Clear();frames.Clear();frameKeys.Clear();
   groups.Clear();pairs.Clear();intervals.Clear();
   RoadVolumeMesh.WorkMeasured=(name,time)=>{if(!leaves.TryGetValue(name,out var leaf))leaves[name]=leaf=new Leaf{name=name};leaf.count++;leaf.seconds+=time;leaf.maximum=Math.Max(leaf.maximum,time);};
   RoadVolumeMesh.SupportSelected=(triangle,apex,fallback)=>samples.Add(new Sample(triangle,apex,fallback));
   RoadVolumeMesh.FrameObserved=(face,distance,frame)=>{int bits=BitConverter.SingleToInt32Bits(distance);frames.Add((face.geometryId,bits,frame));frameKeys.Add((face.geometryId,bits));};
   RoadVolumeMesh.GroupObserved=(size,a,b,matched)=>{groups.TryGetValue(size,out int count);groups[size]=count+1;if(matched)pairs.Add((a,b));};
   RoadVolumeMesh.IntervalObserved=(a,b,from,to)=>intervals.Add((a,b,from,to));
   allocated=GC.GetAllocatedBytesForCurrentThread();clock=Stopwatch.StartNew();
  }
  public static void Abort(){RoadVolumeMesh.WorkMeasured=null;RoadVolumeMesh.SupportSelected=null;RoadVolumeMesh.FrameObserved=null;RoadVolumeMesh.GroupObserved=null;RoadVolumeMesh.IntervalObserved=null;}
  static string Hash(Action<BinaryWriter> write){using(var sha=SHA256.Create())using(var stream=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write))using(var buffer=new BufferedStream(stream,65536))using(var writer=new BinaryWriter(buffer)){write(writer);writer.Flush();buffer.Flush();stream.FlushFinalBlock();return BitConverter.ToString(sha.Hash).Replace("-","");}}
  static void V(BinaryWriter w,Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
  static bool Bits(Vector3 a,Vector3 b)=>Enumerable.Range(0,3).All(i=>BitConverter.SingleToInt32Bits(a[i])==BitConverter.SingleToInt32Bits(b[i]));
  public static void End(RaceDirector director,string output){
   clock.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;Abort();
   string resultPreparationIdentity=Hash(w=>{
    w.Write(director.RaceGeneration);w.Write(director.Cars.Length);w.Write(JsonUtility.ToJson(director.AppliedConfig));
    foreach(var e in director.Roster.Entrants){w.Write(e.Name);w.Write(e.GridSlot);w.Write(e.HumanSeat);w.Write(e.Seed);w.Write((int)e.Profile);}
    foreach(var car in director.Cars){V(w,car.Body.position);var q=car.Body.rotation;w.Write(q.x);w.Write(q.y);w.Write(q.z);w.Write(q.w);V(w,car.Body.linearVelocity);V(w,car.Body.angularVelocity);w.Write(car.PositionRevision);}
   });
   var colliders=director.Track.GetComponentsInChildren<MeshCollider>(true);
   var result=new Report{preparationIdentity=resultPreparationIdentity,coldSeconds=clock.Elapsed.TotalSeconds,allocatedBytes=bytes,allocationCounterAvailable=bytes>0,supports=samples.Count,config=JsonUtility.ToJson(director.AppliedConfig),leaves=leaves.Values.OrderBy(x=>x.name).ToArray()};
   var supportLeaves=leaves.Values.Where(l=>l.name.StartsWith("support.")).ToArray();
   if(samples.Count==0||colliders.Count(c=>c.name==RoadVolumeMesh.SupportName)!=samples.Count||supportLeaves.Length!=5||supportLeaves.Any(l=>l.count!=samples.Count))throw new Exception("Support stage counts/inventory disagree");
   result.frameQueries=frames.Count;result.uniqueFrames=frameKeys.Count;
   if(frames.Count==0||leaves["core.frames"].count*2!=frames.Count)throw new Exception("Frame observation coverage mismatch");
   result.frameIdentity=Hash(w=>{foreach(var row in frames){w.Write(row.geometry);w.Write(row.bits);var f=row.frame;w.Write(f.distance);w.Write(f.halfWidth);w.Write(f.segmentId);w.Write(f.gap);V(w,f.position);V(w,f.tangent);V(w,f.normal);V(w,f.right);}});
   result.cancelledPairCandidates=pairs.Count;result.groupSizes=groups.OrderBy(p=>p.Key).Select(p=>new Leaf{name=p.Key.ToString(),count=p.Value}).ToArray();
   foreach(var pair in pairs)if(ReferenceQuads(new[]{pair.a,pair.b})!=0)throw new Exception("Exact pair would emit baseline geometry");
   result.cancelledIntervals=intervals.Count;
   foreach(var pair in intervals)if(ReferenceQuads(new[]{pair.a,pair.b},pair.from,pair.to)!=0)throw new Exception("Exact interval would emit baseline geometry");
   foreach(var sample in samples){var expected=Reference(sample.triangle,out var fallback,out int phase);if(!Bits(expected,sample.apex)||fallback!=sample.fallback)throw new Exception("Independent old apex/fallback oracle mismatch");if(phase==0)result.initial++;else if(phase==1)result.inward++;else result.centroid++;}
   result.apexIdentity=Hash(w=>{foreach(var s in samples){w.Write(s.triangle.owner.id);w.Write(s.triangle.side);V(w,s.triangle.a);V(w,s.triangle.b);V(w,s.triangle.c);V(w,s.triangle.diagonal);V(w,s.apex);w.Write(s.fallback);}});
   result.inventory=Hash(w=>{
    var registry=(IDictionary)typeof(RoadSupportContact).GetField("surfaces",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
    foreach(var c in colliders){w.Write(c.name);w.Write(c.transform.GetSiblingIndex());V(w,c.transform.position);V(w,c.transform.lossyScale);var rotation=c.transform.rotation;w.Write(rotation.x);w.Write(rotation.y);w.Write(rotation.z);w.Write(rotation.w);
     w.Write(c.gameObject.layer);w.Write(c.convex);w.Write(c.enabled);w.Write(c.isTrigger);w.Write(c.contactOffset);w.Write((int)c.cookingOptions);w.Write(c.hasModifiableContacts);
     var material=c.sharedMaterial;w.Write(material!=null);if(material!=null){w.Write(material.name);w.Write(material.dynamicFriction);w.Write(material.staticFriction);w.Write(material.bounciness);w.Write((int)material.frictionCombine);w.Write((int)material.bounceCombine);}
     var mesh=c.sharedMesh;w.Write(mesh!=null);if(mesh!=null){w.Write(mesh.name);foreach(var vertex in mesh.vertices)V(w,vertex);w.Write(mesh.vertexCount);var indices=mesh.triangles;w.Write(indices.Length);foreach(int index in indices)w.Write(index);V(w,mesh.bounds.center);V(w,mesh.bounds.size);}
     var faces=(Array)registry[c.GetInstanceID()];w.Write(faces!=null);if(faces!=null){w.Write(faces.Length);foreach(var face in faces){var type=face.GetType();foreach(string name in new[]{"a","u","v","Normal"})V(w,(Vector3)type.GetField(name,Fields).GetValue(face));w.Write((bool)type.GetField("repairable",Fields).GetValue(face));}}
    }
   });
   File.WriteAllText(Path.Combine(output,"support-profile.json"),JsonUtility.ToJson(result,true));samples.Clear();oracle.Clear();frames.Clear();frameKeys.Clear();pairs.Clear();groups.Clear();intervals.Clear();
  }
  readonly struct ScalarLevel {
   readonly RoadVolumeMesh.BoundaryScalar edge;readonly float offset;
   public ScalarLevel(RoadVolumeMesh.BoundaryScalar edge,float offset){this.edge=edge;this.offset=offset;}
   public float At(float at)=>edge.Height(at)+offset;
  }
  // Frozen scalar part of original CloseGroup/EmitBands. A zero quad count
  // proves no vertices, indices, UV or support metadata can be emitted either.
  static int ReferenceQuads(RoadVolumeMesh.BoundaryScalar[] edges,float? intervalFrom=null,float? intervalTo=null){
   const float tolerance=.0001f;var cuts=new List<float>();foreach(var edge in edges){cuts.Add(edge.from);cuts.Add(edge.to);}if(intervalFrom.HasValue)cuts=new List<float>{intervalFrom.Value,intervalTo.Value};cuts.Sort();int quads=0;
   for(int i=0;i<cuts.Count-1;i++){float from=cuts[i],to=cuts[i+1];if(to-from<tolerance)continue;float mid=(from+to)*.5f;var active=edges.Where(e=>e.from<mid&&e.to>mid).ToList();
    foreach(var edge in active){var others=active.FindAll(e=>e.forward!=edge.forward);var slices=new List<float>{from,to};
     foreach(var other in others){float a=edge.Height(from)-other.Height(from),b=edge.Height(to)-other.Height(to);foreach(float threshold in new[]{-RoadVolumeMesh.Thickness,0f,RoadVolumeMesh.Thickness})if((a-threshold)*(b-threshold)<0)slices.Add(Mathf.Lerp(from,to,(threshold-a)/(b-a)));}slices.Sort();
     for(int s=0;s<slices.Count-1;s++){float lo=slices[s],hi=slices[s+1];if(hi-lo<tolerance)continue;float at=(lo+hi)*.5f;var bands=new List<(ScalarLevel low,ScalarLevel high)>{(new ScalarLevel(edge,-RoadVolumeMesh.Thickness),new ScalarLevel(edge,0))};
      foreach(var other in others){var low=new ScalarLevel(other,-RoadVolumeMesh.Thickness);var high=new ScalarLevel(other,0);var kept=new List<(ScalarLevel low,ScalarLevel high)>();foreach(var band in bands){float l=band.low.At(at),h=band.high.At(at),ol=low.At(at),oh=high.At(at);if(oh<=l+tolerance||ol>=h-tolerance){kept.Add(band);continue;}if(ol>l+tolerance)kept.Add((band.low,low));if(oh<h-tolerance)kept.Add((high,band.high));}bands=kept;}
      foreach(var band in bands)if(band.high.At(at)-band.low.At(at)>tolerance)quads++;
     }
    }
   }return quads;
  }
  // Independent original control flow with global2^-149 integer signs. It is
  // deliberately evaluated AFTER the cold timer, not inside the measured path.
  readonly struct Point {
   readonly BigInteger x,y,z;public Point(Vector3 p){x=I(p.x);y=I(p.y);z=I(p.z);}Point(BigInteger x,BigInteger y,BigInteger z){this.x=x;this.y=y;this.z=z;}
   static BigInteger I(float f){uint bits=(uint)BitConverter.SingleToInt32Bits(f);int e=(int)((bits>>23)&255);if(e==255)throw new ArgumentException("Nonfinite support coordinate");BigInteger m=bits&0x7fffff;if(e!=0)m=(m+0x800000)<<(e-1);return (bits&0x80000000)==0?m:-m;}
   public static Point operator -(Point a,Point b)=>new Point(a.x-b.x,a.y-b.y,a.z-b.z);
   public static Point Cross(Point a,Point b)=>new Point(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
   public static int Dot(Point a,Point b)=>(a.x*b.x+a.y*b.y+a.z*b.z).Sign;
  }
  static readonly Dictionary<RoadVolumeMesh.SupportCell,(Point[] corners,Point[] normals)> oracle=new Dictionary<RoadVolumeMesh.SupportCell,(Point[],Point[])>();
  static bool Contains(RoadVolumeMesh.SupportCell owner,Vector3 apex){
   if(!oracle.TryGetValue(owner,out var cache)){var corners=owner.corners.Select(p=>new Point(p)).ToArray();var normals=new Point[owner.triangles.Length/3];for(int i=0;i<normals.Length;i++)normals[i]=Point.Cross(corners[owner.triangles[i*3+1]]-corners[owner.triangles[i*3]],corners[owner.triangles[i*3+2]]-corners[owner.triangles[i*3]]);oracle[owner]=cache=(corners,normals);}
   var point=new Point(apex);for(int i=0;i<cache.normals.Length;i++)if(Point.Dot(point-cache.corners[owner.triangles[i*3]],cache.normals[i])>0)return false;return true;
  }
  static Vector3 Reference(RoadVolumeMesh.SupportTriangle t,out bool fallback,out int phase){
   var a=t.a;var b=t.b;var c=t.c;var diagonal=t.diagonal;var owner=t.owner;int side=t.side;phase=0;
   double bx=(double)b.x-a.x,by=(double)b.y-a.y,bz=(double)b.z-a.z,cx=(double)c.x-a.x,cy=(double)c.y-a.y,cz=(double)c.z-a.z;
   double nx=by*cz-bz*cy,ny=bz*cx-bx*cz,nz=bx*cy-by*cx;double inset=(side>=0?.02:.5)/Math.Sqrt(nx*nx+ny*ny+nz*nz);
   var apex=new Vector3((float)(diagonal.x-nx*inset),(float)(diagonal.y-ny*inset),(float)(diagonal.z-nz*inset));
   var origin=new Point(a);var normal=Point.Cross(new Point(b)-origin,new Point(c)-origin);
   fallback=!Contains(owner,apex)||Point.Dot(new Point(apex)-origin,normal)>=0;
   if(fallback){phase=1;double x=0,y=0,z=0;foreach(var p in owner.corners){x+=p.x*.125;y+=p.y*.125;z+=p.z*.125;}var centroid=new Vector3((float)x,(float)y,(float)z);double dx=x-diagonal.x,dy=y-diagonal.y,dz=z-diagonal.z;double fraction=Math.Min(1,.02/Math.Sqrt(dx*dx+dy*dy+dz*dz));apex=new Vector3((float)(diagonal.x+dx*fraction),(float)(diagonal.y+dy*fraction),(float)(diagonal.z+dz*fraction));if(!Contains(owner,apex)||Point.Dot(new Point(apex)-origin,normal)>=0){apex=centroid;phase=2;}}
   if(!Contains(owner,apex)||Point.Dot(new Point(apex)-origin,normal)>=0)throw new InvalidOperationException("No inward kernel apex for road owner "+owner.id+" side "+side);
   return apex;
  }
  [Serializable] sealed class PairReport {public int checks,fastPairs,intervalChecks;}
  public static void RunContracts(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_WORLD_QA_DIR");Directory.CreateDirectory(output);oracle.Clear();int checks=0,initial=0,inward=0,centroid=0,errors=0;
   void Test(RoadVolumeMesh.SupportTriangle triangle){
    Vector3 expected=default,actual=default;bool ef=false,af=false;int phase=0;Exception ee=null,ae=null;
    try{expected=Reference(triangle,out ef,out phase);}catch(Exception e){ee=e;}
    try{actual=triangle.SelectApex(out af);}catch(Exception e){ae=e;}
    checks++;if(ee?.GetType()!=ae?.GetType()||ee?.Message!=ae?.Message)throw new Exception("Apex exception oracle mismatch");
    if(ee!=null){errors++;return;}checks++;if(!Bits(expected,actual)||ef!=af)throw new Exception("Apex bits/fallback oracle mismatch");
    if(phase==0)initial++;else if(phase==1)inward++;else centroid++;
   }
   foreach(float offset in new[]{0f,1024f,-4096f}){
    var shift=new Vector3(offset,offset/16,offset/4);var top=new[]{shift,shift+Vector3.forward,shift+Vector3.right,shift+Vector3.right+Vector3.forward};
    var face=new RoadFace{startDistance=0,endDistance=1,leftStart=0,leftEnd=0,rightStart=1,rightEnd=1};
    var owner=new RoadVolumeMesh.SupportCell(31,face,top,Enumerable.Repeat(Vector3.up,4).ToArray(),new[]{0,1,3,2,0},new[]{0,1,2,2,1,3},0);
    int[] indices=owner.triangles;Vector3 a=default,b=default,c=default;bool found=false;
    for(int i=0;i<indices.Length;i+=3){a=owner.corners[indices[i]];b=owner.corners[indices[i+1]];c=owner.corners[indices[i+2]];if(Vector3.Cross(b-a,c-a).normalized.x<-.9f){found=true;break;}}
    if(!found)throw new Exception("Fixture has no exterior side");
    RoadVolumeMesh.SupportTriangle T(Vector3 x,Vector3 y,Vector3 z,Vector3 d,int side=0)=>new RoadVolumeMesh.SupportTriangle(owner,side,x,y,z,default,default,default,d);
    foreach(float y in new[]{-.5f,.001f,2f})Test(T(a,b,c,shift+new Vector3(0,y,.5f)));
    Test(T(a,a,a,a));
    foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})for(int axis=0;axis<3;axis++){var bad=a;bad[axis]=value;Test(T(bad,b,c,a));}
    Test(T(shift+new Vector3(0,-.9f,0),shift+new Vector3(0,-.9f,1),shift+new Vector3(1,-.9f,0),shift+new Vector3(.5f,-.9f,.5f),-1));
   }
   if(initial==0||inward==0||centroid==0||errors==0)throw new Exception("Missing apex branch/exception coverage");
   File.WriteAllText(Path.Combine(output,"apex-cases.json"),$"{{\"checks\":{checks},\"initial\":{initial},\"inward\":{inward},\"centroid\":{centroid},\"exceptions\":{errors},\"oracle\":\"original flow/global2^-149 integer\"}}\n");oracle.Clear();
   int pairChecks=0,fastPairs=0,intervalChecks=0;
   void Pair(params RoadVolumeMesh.BoundaryScalar[] values){
    bool fast=RoadVolumeMesh.EditorPairCancels(values);int original=ReferenceQuads(values);pairChecks++;
    if(fast){fastPairs++;if(original!=0)throw new Exception("Exact pair shortcut differs from original clipping");
     foreach(float fraction in new[]{.125f,.5f,.875f}){
      float lo=Mathf.Lerp(values[0].from,values[0].to,fraction),hi=Mathf.Lerp(values[0].from,values[0].to,Mathf.Min(1,fraction+.125f));intervalChecks++;
      if(ReferenceQuads(values,lo,hi)!=0)throw new Exception("Exact interval shortcut differs from original clipping");
     }
    }
   }
   RoadVolumeMesh.BoundaryScalar E(float from,float to,float a,float b,bool forward)=>new RoadVolumeMesh.BoundaryScalar(from,to,a,b,forward);
   Pair();Pair(E(0,1,0,0,true));Pair(E(0,1,0,0,true),E(0,1,0,0,false),E(0,1,0,0,true));
   foreach(float from in new[]{-100f,0f,1f})foreach(float length in new[]{-1f,0f,.00001f,.0001f,1f,1000f})foreach(float height in new[]{-100f,0f,100f,float.MaxValue})foreach(float slope in new[]{0f,1f,-1f}){
    var a=E(from,from+length,height,height+slope,true);var b=E(from,from+length,height,height+slope,false);Pair(a,b);Pair(b,a);Pair(a,a);
   }
   float nz=BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),ulp=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(1f)+1);
   var normal=E(0,1,0,1,true);
   foreach(var other in new[]{E(nz,1,0,1,false),E(0,ulp,0,1,false),E(0,1,nz,1,false),E(0,1,0,ulp,false),E(0,1,0,1,true)}){if(RoadVolumeMesh.EditorPairCancels(normal,other))throw new Exception("Inexact/directed pair admitted");Pair(normal,other);}
   foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})for(int field=0;field<4;field++){
    var v=new[]{0f,1f,0f,1f};v[field]=value;var a=E(v[0],v[1],v[2],v[3],true);var b=E(v[0],v[1],v[2],v[3],false);
    if(RoadVolumeMesh.EditorPairCancels(a,b))throw new Exception("Nonfinite pair admitted");Pair(a,b);
   }
   if(fastPairs==0)throw new Exception("Missing exact pair coverage");
   File.WriteAllText(Path.Combine(output,"pair-cases.json"),JsonUtility.ToJson(new PairReport{checks=pairChecks,fastPairs=fastPairs,intervalChecks=intervalChecks}));
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_JUMP_QA_DIR",Path.Combine(output,"graph-jumps"));GraphJumpChecks.Run();
   foreach(string name in new[]{"ValidateFlatClosure","ValidateSlabOracle"})typeof(RoadVolumeChecks).GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
   UnityEngine.Debug.Log("SUPPORT_LOADING_CONTRACTS_OK checks="+checks);
  }
 }
}
