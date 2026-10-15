using System;
using UnityEngine;
namespace StarRacingPrototype {
 [Serializable] public sealed class NativeCourseReplay {
  public int schema=1,revision,attempt;public uint requestedSeed,actualSeed;public bool fallback,jumps;
  public string theme,rails,family,geometryIdentity,fullIdentity;
  public bool Matches(LocalRaceConfig config,string resolvedTheme)=>revision==config.generatorRevision&&requestedSeed==config.Seed&&theme==resolvedTheme&&rails==config.rails&&jumps==config.jumps;
  public void Verify(NativeTrackCourse course,TrackBuilder track){
   if(schema!=1||revision!=course.GeneratorRevision||requestedSeed!=course.RequestedSeed||actualSeed!=course.ActualSeed||fallback!=course.UsedFallback||attempt!=course.Attempt||theme!=course.Settings.theme||rails!=course.Settings.rails||jumps!=course.Settings.jumps||family!=course.Family||geometryIdentity!=course.GeometryIdentity||fullIdentity!=course.ReplayIdentity(track))throw new ArgumentException("Saved track content does not match this generator revision");
  }
  public static NativeCourseReplay Capture(NativeTrackCourse course,TrackBuilder track)=>new NativeCourseReplay{revision=course.GeneratorRevision,attempt=course.Attempt,requestedSeed=course.RequestedSeed,actualSeed=course.ActualSeed,fallback=course.UsedFallback,jumps=course.Settings.jumps,theme=course.Settings.theme,rails=course.Settings.rails,family=course.Family,geometryIdentity=course.GeometryIdentity,fullIdentity=course.ReplayIdentity(track)};
 }
}
