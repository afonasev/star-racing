using System;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed class RaceHud : MonoBehaviour {
  public RaceDirector director;
  Vector2 resultsScroll;
  bool wasPaused,wasResults;
  public bool EditingTrackSeed=>GetComponent<RaceMenu>().EditingSeed;
  public void RefreshTrackSettings(){}
  void OnGUI(){
   if(director==null||GetComponent<RaceMenu>().Loading||!director.Started||director.Session==null)return;
   var menu=GetComponent<RaceMenu>();menu.EnsureSkin();var ui=menu.Skin;
   bool results=director.Session.Phase==RacePhase.Results;
   bool settings=director.Paused&&menu.PauseSettings;
   if(results||settings)ui.Backdrop();var old=GUI.matrix;if(results)CloudlineSkin.Begin();else GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
   if(director.HumanCount==3&&!results&&!settings){CloudlineSkin.Box(new Rect(800,450,800,450),new Color(.91f,.95f,1),0);ui.Brand(new Rect(975,622,520,80),45);}
   if(!results&&!settings&&!director.Paused){for(int seat=0;seat<director.HumanCount;seat++)DrawSeat(ui,seat);DrawProgress(ui);}
   if(results){menu.BeginOverlay("repeat",!wasResults);DrawResults(ui,menu);}
   else if(director.Paused){GUI.matrix=old;CloudlineSkin.Begin();menu.BeginOverlay("resume",!wasPaused);DrawPause(ui,menu);}
   if(results||director.Paused)ui.Footer(!results&&!settings);
   menu.DrawFps();wasPaused=director.Paused;wasResults=results;GUI.matrix=old;
  }
  void DrawSeat(CloudlineSkin ui,int seat){
   var r=RaceHudLayout.View(director.HumanCount,seat);
   var car=director.Cars[seat];var racer=director.Session.Racers[seat];var accent=director.EntrantColor(seat);
   CloudlineSkin.Box(new Rect(r.x,r.y,r.width,2),Color.white,0);CloudlineSkin.Box(new Rect(r.x,r.y,2,r.height),Color.white,0);
   DrawInstrument(ui,RaceHudLayout.Instrument(director.HumanCount,seat),accent,Mathf.Abs(car.Telemetry.speedKmh),car.Telemetry.nitro01);
   var place=RaceHudLayout.Position(director.HumanCount,seat);
   CloudlineSkin.Box(new Rect(place.x,place.y+7,2,26),accent,0);
   ShadowText(ui,new Rect(place.x+12,place.y,48,place.height),racer.Place.ToString("00"),32);
   ShadowText(ui,new Rect(place.x+62,place.y+7,40,place.height-7),"/"+director.Cars.Length,12,false,new Color(.86f,.91f,.96f));
   if(director.Session.Phase==RacePhase.Countdown){
    var box=new Rect(r.center.x-65,r.center.y-74,130,148);ui.Panel(box,.96f);ui.Text(box,Mathf.CeilToInt((float)director.Session.Countdown).ToString(),86,true,CloudlineSkin.Blue,TextAnchor.MiddleCenter);
   }
   if(director.Session.Phase==RacePhase.FinishWindow)ui.Text(new Rect(r.x+120,r.y+132,r.width-240,35),"До результатов: "+director.Session.Remaining.ToString("0.0")+" с",20,true,Color.white,TextAnchor.MiddleCenter);
   if(racer.Finished)ui.Text(new Rect(r.x+100,r.center.y-25,r.width-200,50),"ФИНИШ",40,true,Color.white,TextAnchor.MiddleCenter);
  }
  static void Circle(Vector2 center,float radius,Color color){CloudlineSkin.Box(new Rect(center.x-radius,center.y-radius,radius*2,radius*2),color,radius);}
  static void ShadowText(CloudlineSkin ui,Rect r,string text,int size,bool heavy=false,Color? color=null,TextAnchor align=TextAnchor.MiddleLeft){
   ui.Text(new Rect(r.x+1,r.y+1,r.width,r.height),text,size,heavy,new Color(0,0,0,.9f),align);
   ui.Text(r,text,size,heavy,color??Color.white,align);
  }
  void DrawInstrument(CloudlineSkin ui,Rect r,Color accent,float speed,float nitro){
   ShadowText(ui,new Rect(r.x,r.y,132,70),Mathf.RoundToInt(speed).ToString(),56);
   ShadowText(ui,new Rect(r.x+138,r.y+33,70,24),"КМ/Ч",11,false,new Color(.86f,.91f,.96f));
   var bar=new Rect(r.x+2,r.y+78,166,RaceHudLayout.NitroHeight);
   // Only the energy track has a backing; the instrument itself stays unboxed.
   CloudlineSkin.Box(new Rect(bar.x-1,bar.y-1,bar.width+2,bar.height+2),new Color(.015f,.035f,.06f,.7f),3);
   CloudlineSkin.Box(bar,new Color(.75f,.83f,.91f,.35f),2);
   float fill=bar.width*Mathf.Clamp01(nitro);if(fill>0)CloudlineSkin.Box(new Rect(bar.x,bar.y,fill,bar.height),accent,Mathf.Min(2,fill/2));
   ShadowText(ui,new Rect(r.x+182,r.y+69,32,24),"N₂",12);
  }
  void DrawProgress(CloudlineSkin ui){
   var line=RaceHudLayout.ProgressLine;
   // Rasterize each edge in physical pixels; separately scaled IMGUI rectangles
   // can round their origins/heights differently and erase the lower border.
   var canvas=GUI.matrix;var pixels=RaceHudLayout.ProgressPixels(canvas);int border=RaceHudLayout.ProgressBorderPixels(canvas);
   GUI.matrix=Matrix4x4.identity;
   try{
    var dark=new Color(0,0,0,.65f);
    CloudlineSkin.Box(new Rect(pixels.x-border,pixels.y-border,pixels.width+2*border,border),dark,0);
    CloudlineSkin.Box(new Rect(pixels.x-border,pixels.yMax,pixels.width+2*border,border),dark,0);
    CloudlineSkin.Box(new Rect(pixels.x-border,pixels.y,border,pixels.height),dark,0);
    CloudlineSkin.Box(new Rect(pixels.xMax,pixels.y,border,pixels.height),dark,0);
    CloudlineSkin.Box(pixels,new Color(.9f,.96f,1,.72f),0);
   }finally{GUI.matrix=canvas;}
   for(int tick=0;tick<=10;tick++)CloudlineSkin.Box(new Rect(line.x+line.width*tick/10,line.center.y-3.5f,1,7),new Color(1,1,1,.3f),0);
   CloudlineSkin.Box(new Rect(line.x-12,line.center.y-5.5f,2,11),Color.white,0);
   DrawFinishFlag(new Vector2(line.xMax+16,line.center.y));
   // Stable entrant colors match the actual cars. Local seats remain legible over the pack.
   for(int pass=0;pass<2;pass++)for(int i=0;i<director.Session.Racers.Length;i++){
    int seat=director.Roster.Entrants[i].HumanSeat;bool human=seat>=0;if(human!=(pass==1))continue;
    var point=RaceHudLayout.Marker(director.Session.Racers[i].Progress,director.Session.RaceLength);
    if(human){
     // Draw on top of every AI dot: opaque bright center, white rim and dark outer edge.
     Circle(point,9,new Color(.015f,.035f,.06f,.85f));Circle(point,7.5f,Color.white);
     Circle(point,6,Color.Lerp(director.EntrantColor(i),Color.white,.28f));
     ShadowText(ui,new Rect(point.x-10,point.y-29,20,20),(seat+1).ToString(),12,true,Color.white,TextAnchor.MiddleCenter);
    }else Circle(point,1.4f,CloudlineSkin.Alpha(Color.Lerp(director.EntrantColor(i),new Color(.6f,.68f,.75f),.7f),.28f));
   }
  }
  static void DrawFinishFlag(Vector2 end){
   var dark=new Color(.015f,.035f,.06f,.95f);float x=end.x,y=end.y-22;
   CloudlineSkin.Box(new Rect(x-1,y-1,3,29),dark,0);CloudlineSkin.Box(new Rect(x,y,1,27),Color.white,0);
   CloudlineSkin.Box(new Rect(x+1,y-1,22,17),dark,0);
   for(int row=0;row<3;row++)for(int col=0;col<4;col++)CloudlineSkin.Box(new Rect(x+2+col*5,y+row*5,5,5),(row+col)%2==0?Color.white:dark,0);
  }
  void DrawPause(CloudlineSkin ui,RaceMenu menu){
   if(menu.PauseSettings){menu.DrawPauseSettings();return;}
   CloudlineSkin.Box(new Rect(0,0,1600,900),new Color(.03f,.075f,.17f,.45f),0);ui.Panel(new Rect(490,174,620,552),1);
   ui.Text(new Rect(534,200,532,76),"Пауза",45,true);
   menu.OverlayButton("resume",new Rect(534,300,532,70),"Продолжить",director.StartRace,true,!director.Input.MissingDevice);
   menu.OverlayButton("settings",new Rect(534,388,532,70),"Настройки",menu.OpenPauseSettings);
   menu.OverlayButton("restart",new Rect(534,476,532,70),"Повторить",menu.RepeatRace,false,!director.Input.MissingDevice);
   menu.OverlayButton("menu",new Rect(534,564,532,70),"В меню",()=>director.ExitToMenu());
   if(!director.Input.MissingDevice)ui.Text(new Rect(534,650,532,56),"Баланс: "+(director.RaceBalanceSnapshot?.Label??"Default"),17,false,CloudlineSkin.Muted,TextAnchor.MiddleLeft,true);
   if(director.Input.MissingDevice)ui.Text(new Rect(534,650,532,56),"Подключите устройство или измените состав в меню.",17,false,CloudlineSkin.Muted,TextAnchor.MiddleLeft,true);
  }
  void DrawResults(CloudlineSkin ui,RaceMenu menu){
   menu.RegisterResultsScroll(delta=>resultsScroll.y=Mathf.Clamp(resultsScroll.y+delta*162,0,Mathf.Max(0,director.Cars.Length*54-466)));
   ui.Panel(new Rect(286,65,1028,770),.97f);ui.Text(new Rect(332,94,870,80),"Результаты",50,true);
   ui.Text(new Rect(338,196,130,32),"МЕСТО",15,true,CloudlineSkin.Muted);ui.Text(new Rect(482,196,475,32),"УЧАСТНИК",15,true,CloudlineSkin.Muted);ui.Text(new Rect(1020,196,240,32),"ВРЕМЯ",15,true,CloudlineSkin.Muted,TextAnchor.MiddleRight);
   resultsScroll=CloudlineSkin.BeginScrollView(new Rect(328,242,950,466),resultsScroll,new Rect(0,0,922,director.Cars.Length*54));
   for(int place=1;place<=director.Cars.Length;place++)for(int i=0;i<director.Cars.Length;i++){
    var racer=director.Session.Racers[i];if(racer.Place!=place)continue;int human=director.Roster.Entrants[i].HumanSeat;float y=(place-1)*54;
    if(human>=0)CloudlineSkin.Box(new Rect(0,y,915,49),CloudlineSkin.Alpha(CloudlineSkin.PlayerColors[human],.13f),7);
    var color=human>=0?CloudlineSkin.PlayerColors[human]:CloudlineSkin.Ink;
    ui.Text(new Rect(18,y,90,49),place.ToString("00"),26,true,color);
    string name=human>=0?director.AppliedConfig.PlayerName(human):director.Roster.Entrants[i].Name;
    ui.Text(new Rect(154,y,480,49),name,23,human>=0,color);
    string time=racer.Finished?TimeSpan.FromSeconds(racer.FinishTime).ToString(@"mm\:ss\.fff"):"DNF · "+Mathf.RoundToInt(racer.Progress/director.Session.RaceLength*100)+"%";
    ui.Text(new Rect(650,y,245,49),time,22,human>=0,color,TextAnchor.MiddleRight);
   }
   CloudlineSkin.EndScrollView();ui.Text(new Rect(338,710,920,24),"Список: выберите и нажмите ← / → · колесо мыши",14,menu.ResultsScrollFocused,menu.ResultsScrollFocused?CloudlineSkin.Blue:CloudlineSkin.Muted);menu.OverlayButton("repeat",new Rect(332,741,440,64),"Повторить",menu.RepeatRace,true,!director.Input.MissingDevice);
   menu.OverlayButton("menu",new Rect(810,741,456,64),"В меню",()=>director.ExitToMenu());
  }
 }
}
