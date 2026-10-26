using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarRacingPrototype {
 public sealed partial class RaceMenu {
  Gamepad navigationPad;
  public static string PadGlyph(string family,string position){
   family=(family??"").ToLowerInvariant();
   bool playstation=family.Contains("dualshock")||family.Contains("dualsense")||family.Contains("playstation");
   bool nintendo=family.Contains("switch")||family.Contains("nintendo");
   bool xbox=family.Contains("xinput")||family.Contains("xbox");
   switch(position){
    case "south":return playstation?"×":nintendo?"B":xbox?"A":"●";
    case "east":return playstation?"○":nintendo?"A":xbox?"B":"●";
    case "north":return playstation?"△":nintendo?"X":xbox?"Y":"●";
    case "west":return playstation?"□":nintendo?"Y":xbox?"X":"●";
    default:throw new ArgumentException("Unknown pad position");
   }
  }
  public static string ShortcutPosition(string id){
   if(id=="reset-all")return "north";
   if(id=="start")return "west";
   if(id=="back"||id=="settings-back"||id=="help-back"||id=="close"||id=="reset-cancel"||id=="display-revert"||id=="lab-dialog-cancel")return "east";
   return "";
  }
  string ButtonHint(string id)=>gamepadNavigation?ShortcutPosition(id):"";
  public static Color PadHintColor(string family,string position){
   string glyph=PadGlyph(family,position);
   switch(glyph){
    case "A":case "△":return new Color(.19f,.59f,.36f);
    case "B":case "○":return new Color(.84f,.29f,.38f);
    case "X":case "×":return new Color(.15f,.4f,.75f);
    case "Y":return new Color(.74f,.51f,.09f);
    case "□":return new Color(.69f,.32f,.65f);
    default:return position=="north"?new Color(.74f,.51f,.09f):position=="east"?new Color(.84f,.29f,.38f):position=="south"?new Color(.19f,.59f,.36f):new Color(.15f,.4f,.75f);
   }
  }
  void DrawPadHint(Rect r,string position,bool enabled=true){
   string family=navigationPad==null?"":navigationPad.layout+" "+navigationPad.displayName;
   string glyph=PadGlyph(family,position);
   // IMGUI texture tint is linear; keep the approved badge palette in display colors.
   Color color=(enabled?PadHintColor(family,position):CloudlineSkin.Muted).linear;
   float radius=r.width/2;
   CloudlineSkin.Box(new Rect(r.x,r.y+2,r.width,r.height),new Color(.12f,.23f,.42f,.16f),radius);
   CloudlineSkin.Box(r,color,radius);
   CloudlineSkin.Box(new Rect(r.x+2,r.y+2,r.width-4,r.height-4),new Color(1,1,1,.5f),radius-2);
   CloudlineSkin.Box(new Rect(r.x+3,r.y+3,r.width-6,r.height-6),color,radius-3);
   if(glyph!="●")Skin.Text(r,glyph,Mathf.RoundToInt(r.height*.6f),true,Color.white,TextAnchor.MiddleCenter);
   else {
    // Unknown controllers show physical face-button positions, never invented letters.
    for(int i=0;i<4;i++){
     string at=i==0?"north":i==1?"east":i==2?"south":"west";
     Vector2 offset=at=="north"?Vector2.up*-1:at=="south"?Vector2.up:at=="east"?Vector2.right:Vector2.left;
     float d=r.width*.18f;Vector2 centre=r.center+offset*r.width*.23f;
     CloudlineSkin.Box(new Rect(centre.x-d/2,centre.y-d/2,d,d),new Color(1,1,1,at==position?1:.28f),d/2);
    }
   }
  }
  void MoveSpatial(Vector2 direction){
   var current=controls.Find(c=>c.id==focus);
   if(current==null||current.area.width<=0){Move(direction.x+direction.y<0?-1:1);return;}
   Control best=null;float score=float.PositiveInfinity;
   foreach(var candidate in controls){
    if(candidate==current||candidate.area.width<=0)continue;
    var delta=candidate.area.center-current.area.center;float along=Vector2.Dot(delta,direction);
    if(along<1)continue;float across=Mathf.Abs(delta.x*direction.y-delta.y*direction.x);
    float value=along+across*3+across*across/Mathf.Max(1,along);
    if(value<score){best=candidate;score=value;}
   }
   if(best!=null){focus=best.id;if(ScreenState==RaceMenuScreen.Lab){if(best.id.StartsWith("lab-field-")){if(best.area.y<332)labScroll.y=Mathf.Max(0,labScroll.y+best.area.y-332);else if(best.area.yMax>681)labScroll.y+=best.area.yMax-681;}else if(best.id.StartsWith("lab-choice-")){if(best.area.y<315)labDialogScroll.y=Mathf.Max(0,labDialogScroll.y+best.area.y-315);else if(best.area.yMax>565)labDialogScroll.y+=best.area.yMax-565;}}if(PopupOpen)popupScroll.y=Mathf.Max(0,best.area.y-112);}
  }
 }
}
