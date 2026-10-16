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
    case "south":return playstation?"×":nintendo?"B":xbox?"A":"Нижняя";
    case "east":return playstation?"○":nintendo?"A":xbox?"B":"Правая";
    case "north":return playstation?"△":nintendo?"X":xbox?"Y":"Верх";
    case "west":return playstation?"□":nintendo?"Y":xbox?"X":"Левая";
    default:throw new ArgumentException("Unknown pad position");
   }
  }
  string PadGlyph(string position)=>PadGlyph(navigationPad==null?"":navigationPad.layout+" "+navigationPad.displayName,position);
  string ButtonHint(string id){
   if(!gamepadNavigation||id.StartsWith("lab-group-"))return "";
   if(id=="reset-all")return "   ["+PadGlyph("north")+"]";
   if(id=="back"||id=="settings-back"||id=="help-back"||id=="close"||id=="reset-cancel"||id=="display-revert"||id=="lab-dialog-cancel")return "   ["+PadGlyph("east")+"]";
   return id==focus?"   ["+PadGlyph("south")+"]":"";
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
