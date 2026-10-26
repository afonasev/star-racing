using UnityEngine;
namespace StarRacingPrototype {
 // One virtual canvas overlays all cameras; no viewport space is reserved for the rail.
 public static class RaceHudLayout {
  public const float Width=1600,Height=900,NitroHeight=8;
  public static readonly Rect Fps=new Rect(1456,12,130,24);
  public static readonly Rect ProgressArea=new Rect(320,850,960,44);
  public static readonly Rect ProgressLine=new Rect(388,878,824,3);
  public static Rect ProgressPixels(Matrix4x4 canvas){
   var min=canvas.MultiplyPoint3x4(ProgressLine.min);var max=canvas.MultiplyPoint3x4(ProgressLine.max);
   return Rect.MinMaxRect(Mathf.Round(min.x),Mathf.Round(min.y),Mathf.Round(max.x),Mathf.Round(max.y));
  }
  public static int ProgressBorderPixels(Matrix4x4 canvas)=>Mathf.Max(1,Mathf.RoundToInt(canvas.MultiplyVector(Vector3.up).magnitude));
  public static Rect View(int humans,int seat){var v=RaceViewports.For(humans,seat);return new Rect(v.x*Width,(1-v.yMax)*Height,v.width*Width,v.height*Height);}
  public static bool Right(int humans,int seat)=>humans>1&&(seat%2)==1;
  public static bool Top(int humans,int seat)=>humans>2&&seat<2;
  public static Rect Instrument(int humans,int seat){var v=View(humans,seat);return new Rect(Right(humans,seat)?v.xMax-236:v.x+20,v.yMax-120,216,96);}
  public static Rect Position(int humans,int seat){var v=View(humans,seat);return new Rect(humans==1||Right(humans,seat)?v.x+14:v.xMax-118,v.y+16,104,40);}
  public static float Progress01(float progress,float length)=>length>0&&float.IsFinite(progress)?Mathf.Clamp01(progress/length):0;
  public static Vector2 Marker(float progress,float length)=>new Vector2(ProgressLine.x+Progress01(progress,length)*ProgressLine.width,ProgressLine.center.y);
 }
}
