using UnityEngine;
namespace StarRacingPrototype {
 // One virtual canvas overlays all cameras; no viewport space is reserved for the rail.
 public static class RaceHudLayout {
  public const float Width=1600,Height=900,InstrumentOpacity=.88f;
  public static readonly Rect ProgressArea=new Rect(320,872,960,18);
  public static readonly Rect ProgressLine=new Rect(388,879,824,1);
  public static Rect View(int humans,int seat){var v=RaceViewports.For(humans,seat);return new Rect(v.x*Width,(1-v.yMax)*Height,v.width*Width,v.height*Height);}
  public static bool Right(int humans,int seat)=>humans>1&&(seat%2)==1;
  public static bool Top(int humans,int seat)=>humans>2&&seat<2;
  public static Rect Instrument(int humans,int seat){var v=View(humans,seat);return new Rect(Right(humans,seat)?v.xMax-124:v.x+16,Top(humans,seat)?v.y+16:v.yMax-72,108,54);}
  public static Rect Position(int humans,int seat){var v=View(humans,seat);return new Rect(Right(humans,seat)?v.x+14:v.xMax-80,v.y+12,66,26);}
  public static float Progress01(float progress,float length)=>length>0&&float.IsFinite(progress)?Mathf.Clamp01(progress/length):0;
  public static Vector2 Marker(float progress,float length)=>new Vector2(ProgressLine.x+Progress01(progress,length)*ProgressLine.width,ProgressLine.center.y);
 }
}
