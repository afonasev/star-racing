using System;
using UnityEngine;
namespace StarRacingPrototype {
 [Serializable] public sealed class NativeCourseReplay {
  public int schema=1,revision,attempt;public uint requestedSeed,actualSeed;public bool fallback,jumps;
  public string theme,rails,family,geometryIdentity,fullIdentity;
  public string courseId;public int contentRevision;
  public bool IsEmpty=>schema<=1&&revision==0&&attempt==0&&requestedSeed==0&&actualSeed==0&&!fallback&&!jumps&&contentRevision==0&&string.IsNullOrEmpty(courseId)&&string.IsNullOrEmpty(theme)&&string.IsNullOrEmpty(rails)&&string.IsNullOrEmpty(family)&&string.IsNullOrEmpty(geometryIdentity)&&string.IsNullOrEmpty(fullIdentity);
  public bool Matches(LocalRaceConfig config,string resolvedTheme)=>config.IsAuthored?schema==2&&courseId==config.courseId&&contentRevision==config.courseRevision&&theme==resolvedTheme&&rails==config.rails&&jumps==config.jumps:schema==1&&string.IsNullOrEmpty(courseId)&&revision==config.generatorRevision&&requestedSeed==config.Seed&&theme==resolvedTheme&&rails==config.rails&&jumps==config.jumps;
  public void Verify(NativeTrackCourse course,TrackBuilder track){
   if(schema!=(course.IsAuthored?2:1)||(courseId??"")!=(course.CourseId??"")||contentRevision!=course.ContentRevision||revision!=course.GeneratorRevision||requestedSeed!=course.RequestedSeed||actualSeed!=course.ActualSeed||fallback!=course.UsedFallback||attempt!=course.Attempt||theme!=course.Settings.theme||rails!=course.Settings.rails||jumps!=course.Settings.jumps||family!=course.Family||geometryIdentity!=course.GeometryIdentity||fullIdentity!=course.ReplayIdentity(track))throw new ArgumentException("Saved track content does not match this course revision");
  }
  public static NativeCourseReplay Capture(NativeTrackCourse course,TrackBuilder track)=>new NativeCourseReplay{schema=course.IsAuthored?2:1,courseId=course.CourseId,contentRevision=course.ContentRevision,revision=course.GeneratorRevision,attempt=course.Attempt,requestedSeed=course.RequestedSeed,actualSeed=course.ActualSeed,fallback=course.UsedFallback,jumps=course.Settings.jumps,theme=course.Settings.theme,rails=course.Settings.rails,family=course.Family,geometryIdentity=course.GeometryIdentity,fullIdentity=course.ReplayIdentity(track)};
 }
}
