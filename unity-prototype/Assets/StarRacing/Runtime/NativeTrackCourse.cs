using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace StarRacingPrototype {
 // A native generated course can carry one legacy route or one independent
 // graph. Replay content is finalized against the actual built gate layout.
 public sealed class NativeTrackCourse {
  public const int Schema=1,Revision=12;
  public readonly Procedural.Definition Single;
  public readonly Procedural.GraphCourseDefinition Independent;
  public readonly Procedural.GraphCourseSettings Settings;
  public readonly Procedural.IndependentGraph Graph;
  public readonly uint RequestedSeed,ActualSeed;
  public readonly int GeneratorRevision,Attempt;
  public readonly bool UsedFallback;
  public readonly string Family,GeometryIdentity;
  readonly string singleContent;
  public bool IsIndependent=>Independent!=null;
  public float Start=>IsIndependent?(float)Procedural.GraphNativeCatalog.GridStart:30f;
  public float Finish=>IsIndependent?(float)(Graph.CommonLength-160):(float)Single.totalLength+30;
  internal NativeTrackCourse(Procedural.Definition single,Procedural.GraphCourseDefinition independent,Procedural.GraphCourseSettings settings) {
   if((single==null)==(independent==null))throw new ArgumentException("Exactly one course representation required");
   Single=single;Independent=independent;Settings=settings;singleContent=single==null?null:JsonUtility.ToJson(single);
   Graph=independent?.graph??Procedural.IndependentGraph.SingleRoad(single);
   RequestedSeed=independent?.requestedSeed??single.requestedSeed;ActualSeed=independent?.actualSeed??single.seed;UsedFallback=independent?.usedFallback??single.usedFallback;
   GeneratorRevision=independent!=null?Revision:single.version;Family=independent!=null?Procedural.GraphCourseDefinition.Family:single.family??"legacy";
   Attempt=independent?.attempt??SingleAttempt(single);
   using(var buffer=new MemoryStream()) {
    using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true)){writer.Write(Schema);writer.Write(GeneratorRevision);writer.Write(Family);writer.Write(RequestedSeed);writer.Write(ActualSeed);writer.Write(UsedFallback);writer.Write(Attempt);writer.Write(settings.theme);writer.Write(settings.rails);writer.Write(settings.jumps);writer.Write(Graph.Identity);writer.Write(independent?.Identity??singleContent);}
    GeometryIdentity=Digest(buffer.ToArray());
   }
  }
  static int SingleAttempt(Procedural.Definition d) {
   uint basis=d.usedFallback?(d.jumpModeEnabled||d.theme!="space-station"?0x51a7faceu:0x51a7faceu^0x5a710a1eu):(d.theme=="space-station"?d.requestedSeed^0x5a710a1eu:d.requestedSeed);
   for(int attempt=0;attempt<(d.usedFallback?384:32);attempt++)if(unchecked(basis+(uint)attempt*0x9e3779b9u)==d.seed)return attempt;
   throw new ArgumentException("Single course seed is outside its bounded generation history");
  }
  public string ReplayIdentity(TrackBuilder track) {
   if(track==null)throw new ArgumentNullException(nameof(track));
   if(Single!=null&&JsonUtility.ToJson(Single)!=singleContent)throw new ArgumentException("Native single-route content changed after generation");
   using(var buffer=new MemoryStream()) {
    using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true)) {
     writer.Write(Schema);writer.Write(GeometryIdentity);writer.Write(Start);writer.Write(Finish);
     if(IsIndependent) {
      if(track.GraphSurface==null||track.GraphSurface.Graph.Identity!=Graph.Identity)throw new ArgumentException("Built graph content mismatch");
      writer.Write(new GraphRaceCourse(track.GraphSurface,Start,Finish).ReplayIdentity(Independent));
     } else {
      if(track.Route==null||JsonUtility.ToJson(track.Route.Definition)!=singleContent)throw new ArgumentException("Built single-route content mismatch");
      WriteFrame(writer,track.Route.Evaluate(Start));int count=(int)Math.Ceiling((Finish-Start)/50);writer.Write(count);
      for(int i=1;i<=count;i++)WriteFrame(writer,track.Route.Evaluate(Mathf.Min(Start+i*50,Finish)));
     }
    }
    return Digest(buffer.ToArray());
   }
  }
  static void WriteFrame(BinaryWriter writer,TrackFrame f){writer.Write(f.distance);writer.Write(f.position.x);writer.Write(f.position.y);writer.Write(f.position.z);writer.Write(f.tangent.x);writer.Write(f.tangent.y);writer.Write(f.tangent.z);writer.Write(f.normal.x);writer.Write(f.normal.y);writer.Write(f.normal.z);writer.Write(f.right.x);writer.Write(f.right.y);writer.Write(f.right.z);writer.Write(f.halfWidth);}
  static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
 }
 public static class NativeTrackGenerator {
  public static readonly string[] Families={"speed","technical","vertical","path-choice"};
  public static string FamilyFor(uint seed,string theme)=>Families[new Procedural.SeedRandom(seed^(theme=="space-station"?0x5a710a1eu:0u)).Int(0,3)];
  public static NativeTrackCourse Generate(uint seed,Procedural.GraphCourseSettings settings,int revision=12,bool fallback=false) {
   if(settings==null)throw new ArgumentNullException(nameof(settings));
   if(revision==10||revision==11) {
    var old=fallback?Procedural.Generator.GenerateFallback(seed,settings.rails,settings.theme,settings.jumps,true,revision):Procedural.Generator.Generate(seed,settings.rails,settings.theme,settings.jumps,true,revision);
    return new NativeTrackCourse(old,null,settings);
   }
   if(revision!=12)throw new ArgumentException("Unsupported native revision");
   string family=FamilyFor(seed,settings.theme);
   if(family!="path-choice")return new NativeTrackCourse(Procedural.Generator.GenerateNative12(seed,settings.rails,settings.theme,settings.jumps,family,fallback),null,settings);
   var graph=Procedural.GraphCourseGenerator.SelectProfile(seed,settings,fallback,actual=>Procedural.GraphNativeCatalog.Candidate(actual,settings),actual=>Procedural.GraphNativeCatalog.ProfileBase+Procedural.GraphNativeCatalog.ProfileFor(actual));
   return new NativeTrackCourse(null,graph,settings);
  }
 }
}
