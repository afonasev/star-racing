using System;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class AuthoredCourses {
  public static bool Known(string id,int revision)=>(id==AuthoredCloudCityCourse.Id&&revision==AuthoredCloudCityCourse.Revision)||(id==AuthoredOrbitalRiftCourse.Id&&revision==AuthoredOrbitalRiftCourse.Revision);
  public static string Theme(string id)=>id==AuthoredCloudCityCourse.Id?"cloud-city":id==AuthoredOrbitalRiftCourse.Id?"space-station":throw new ArgumentException("Unknown authored course");
  public static string Title(string id)=>id==AuthoredCloudCityCourse.Id?AuthoredCloudCityCourse.Title:id==AuthoredOrbitalRiftCourse.Id?AuthoredOrbitalRiftCourse.Title:throw new ArgumentException("Unknown authored course");
  public static int Presentation(string id)=>id==AuthoredCloudCityCourse.Id?AuthoredCloudCityCourse.PresentationRevision:id==AuthoredOrbitalRiftCourse.Id?AuthoredOrbitalRiftCourse.PresentationRevision:throw new ArgumentException("Unknown authored course");
  public static bool Matches(string id,int revision,Definition d)=>Known(id,revision)&&(id==AuthoredCloudCityCourse.Id?AuthoredCloudCityCourse.Is(d):AuthoredOrbitalRiftCourse.Is(d));
 }
}
