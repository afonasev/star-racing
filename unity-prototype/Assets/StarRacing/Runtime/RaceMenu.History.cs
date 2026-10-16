using System;
using System.Collections.Generic;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed partial class RaceMenu {
  int historySelected;Vector2 historyVersionsScroll,historyBodyScroll;
  string historyLanguage="ru";
  public void OpenHistory(){Open(RaceMenuScreen.History);historySelected=0;historyBodyScroll=Vector2.zero;_=Distribution.DesktopUpdater.Instance?.LoadHistory();}
  void DrawHistory(){
   Header("Что нового");var updater=Distribution.DesktopUpdater.Instance;
   Skin.Panel(new Rect(56,204,300,600),1);Skin.Panel(new Rect(380,204,1164,600),1);
   var entries=updater?.HistoryEntries??new Distribution.ReleaseHistoryEntry[0];
   Button("history-ru",new Rect(1322,718,88,42),"РУ",()=>{historyLanguage="ru";historyBodyScroll=Vector2.zero;},historyLanguage=="ru",true,17);
   Button("history-en",new Rect(1422,718,88,42),"EN",()=>{historyLanguage="en";historyBodyScroll=Vector2.zero;},historyLanguage=="en",true,17);
   if(entries.Length==0){Skin.Text(new Rect(410,240,1100,140),updater?.HistoryStatus??"История недоступна",22,false,null,TextAnchor.UpperLeft,true);return;}
   historySelected=Mathf.Clamp(historySelected,0,entries.Length-1);
   var viewport=new Rect(80,232,252,448);historyVersionsScroll=CloudlineSkin.BeginScrollView(viewport,historyVersionsScroll,new Rect(0,0,228,Mathf.Max(448,entries.Length*92)));
   for(int i=0;i<entries.Length;i++){
    int selected=i;var entry=entries[i];bool installed=entry.Version==updater.InstalledVersion;
    var rect=new Rect(0,i*92,228,installed?80:66);
    // Whole button is one focus target; installed label stays inside its lower line.
    if(Skin.Button(rect,"",i==historySelected,collect&&focus=="history-version-"+i,true,18)&&collect){historySelected=i;historyBodyScroll=Vector2.zero;focus="history-version-"+i;}
    Skin.Text(new Rect(8,rect.y+8,212,28),Skin.Ellipsis(entry.Version,212,Mathf.RoundToInt(21*Preferences.textScale)),21,true,i==historySelected?Color.white:(Color?)null,TextAnchor.MiddleCenter);
    Skin.Text(new Rect(8,rect.y+37,212,22),installed?"Установлена":entry.Date,15,false,i==historySelected?Color.white:CloudlineSkin.Muted,TextAnchor.MiddleCenter);
    Rect visible=new Rect(viewport.x,viewport.y+rect.y-historyVersionsScroll.y,rect.width,rect.height);
    if(visible.y>=viewport.y&&visible.yMax<=viewport.yMax)Register("history-version-"+i,()=>{historySelected=selected;historyBodyScroll=Vector2.zero;},null,visible);
   }
   CloudlineSkin.EndScrollView();
   Button("history-versions-up",new Rect(80,695,115,36),"↑ Выше",()=>historyVersionsScroll.y=Mathf.Max(0,historyVersionsScroll.y-180),false,true,16);
   Button("history-versions-down",new Rect(205,695,127,36),"Ниже ↓",()=>historyVersionsScroll.y+=180,false,true,16);
   var active=entries[historySelected];Skin.Text(new Rect(408,232,770,45),Skin.Ellipsis("Версия "+active.Version,770,Mathf.RoundToInt(28*Preferences.textScale)),28,true);
   Skin.Text(new Rect(1200,232,312,45),active.Date,18,false,CloudlineSkin.Muted,TextAnchor.MiddleRight);
   string text=active.Localized(historyLanguage);var lines=new List<(string text,float y,float height,bool heading)>();float y=0;
   foreach(string raw in text.Replace("\r","").Split('\n')){
    if(raw.Trim().Length==0){y+=18;continue;}
    bool heading=raw.StartsWith("### ",StringComparison.Ordinal);string line=heading?raw.Substring(4):raw.StartsWith("- ",StringComparison.Ordinal)?"• "+raw.Substring(2):raw;
    float height=Skin.TextHeight(line,heading?25:21,1060)+10;lines.Add((line,y,height,heading));y+=height;
   }
   historyBodyScroll=CloudlineSkin.BeginScrollView(new Rect(408,295,1106,390),historyBodyScroll,new Rect(0,0,1070,Mathf.Max(390,y+16)));
   foreach(var line in lines)Skin.Text(new Rect(0,line.y,1060,line.height),line.text,line.heading?25:21,line.heading,null,TextAnchor.UpperLeft,true);
   CloudlineSkin.EndScrollView();
   Button("history-body-up",new Rect(408,718,150,42),"↑ Выше",()=>historyBodyScroll.y=Mathf.Max(0,historyBodyScroll.y-220),false,true,17);
   Button("history-body-down",new Rect(578,718,150,42),"Ниже ↓",()=>historyBodyScroll.y+=220,false,true,17);
   if(!string.IsNullOrEmpty(updater.HistoryStatus))Skin.Text(new Rect(760,718,530,52),updater.HistoryStatus,16,false,CloudlineSkin.Muted,TextAnchor.MiddleRight,true);
  }
 }
}
