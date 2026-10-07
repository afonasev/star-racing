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
   if(director==null||!director.Started||director.Session==null)return;
   var menu=GetComponent<RaceMenu>();menu.EnsureSkin();var ui=menu.Skin;
   bool results=director.Session.Phase==RacePhase.Results;
   bool settings=director.Paused&&menu.PauseSettings;
   if(results||settings)ui.Backdrop();var old=GUI.matrix;if(results)CloudlineSkin.Begin();else GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
   if(director.HumanCount==3&&!results&&!settings){CloudlineSkin.Box(new Rect(800,450,800,450),new Color(.91f,.95f,1),0);ui.Brand(new Rect(975,622,520,80),45);}
   if(!results&&!settings)for(int seat=0;seat<director.HumanCount;seat++)DrawSeat(ui,seat);
   if(results){menu.BeginOverlay("repeat",!wasResults);DrawResults(ui,menu);}
   else if(director.Paused){GUI.matrix=old;CloudlineSkin.Begin();menu.BeginOverlay("resume",!wasPaused);DrawPause(ui,menu);}
   var updater=StarRacingPrototype.Distribution.DesktopUpdater.Instance;
   if(updater!=null&&updater.Downloading)ui.Text(new Rect(1170,864,370,24),"Загрузка обновления · "+updater.Progress+"%",14,false,CloudlineSkin.Muted,TextAnchor.MiddleRight);
   menu.DrawFps();wasPaused=director.Paused;wasResults=results;GUI.matrix=old;
  }
  void DrawSeat(CloudlineSkin ui,int seat){
   var view=RaceViewports.For(director.HumanCount,seat);var r=new Rect(view.x*1600,(1-view.yMax)*900,view.width*1600,view.height*900);
   var car=director.Cars[seat];var racer=director.Session.Racers[seat];var accent=CloudlineSkin.PlayerColors[seat];
   float margin=22;bool compact=director.HumanCount>2;
   CloudlineSkin.Box(new Rect(r.x,r.y,r.width,2),Color.white,0);CloudlineSkin.Box(new Rect(r.x,r.y,2,r.height),Color.white,0);
   ui.Panel(new Rect(r.x+margin,r.y+18,230,98),.93f);
   CloudlineSkin.Box(new Rect(r.x+margin,r.y+18,5,98),accent,2);
   ui.Text(new Rect(r.x+margin+18,r.y+24,196,28),director.AppliedConfig.PlayerName(seat),17,true,accent);
   ui.Text(new Rect(r.x+margin+18,r.y+48,200,58),racer.Place+" / "+director.Cars.Length,34,true);
   ui.Panel(new Rect(r.xMax-188,r.y+18,166,92),.91f);
   ui.Text(new Rect(r.xMax-176,r.y+24,142,27),"ВРЕМЯ ГОНКИ",12,false,CloudlineSkin.Muted,TextAnchor.MiddleCenter);
   ui.Text(new Rect(r.xMax-176,r.y+43,142,32),TimeSpan.FromSeconds(director.Session.Elapsed).ToString(@"mm\:ss"),24,true,null,TextAnchor.MiddleCenter);
   float leader=0;foreach(var standing in director.Session.Racers)leader=Mathf.Max(leader,standing.Progress/director.Session.RaceLength);
   ui.Text(new Rect(r.xMax-176,r.y+76,142,24),"Лидер "+Mathf.RoundToInt(leader*100)+"%",14,false,CloudlineSkin.Muted,TextAnchor.MiddleCenter);
   float y=r.yMax-(compact?95:126);
   ui.Panel(new Rect(r.x+margin,y,260,compact?76:96),.93f);
   ui.Text(new Rect(r.x+margin+14,y,120,64),Mathf.RoundToInt(Mathf.Abs(car.Telemetry.speedKmh)).ToString(),compact?40:49,true);
   ui.Text(new Rect(r.x+margin+132,y+25,92,27),"км/ч",17,false,CloudlineSkin.Muted);
   ui.Text(new Rect(r.x+margin+14,y+55,62,21),"НИТРО",12,true,accent);
   CloudlineSkin.Box(new Rect(r.x+margin+80,y+62,156,8),new Color(.77f,.82f,.9f),4);
   CloudlineSkin.Box(new Rect(r.x+margin+80,y+62,156*car.Telemetry.nitro01,8),accent,4);
   float progress=Mathf.Clamp01(racer.Progress/director.Session.RaceLength);
   ui.Panel(new Rect(r.xMax-260,r.yMax-73,238,52),.93f);
   ui.Text(new Rect(r.xMax-246,r.yMax-68,211,26),"Трасса   "+Mathf.RoundToInt(progress*100)+"%",16,true);
   CloudlineSkin.Box(new Rect(r.xMax-246,r.yMax-36,208,5),new Color(.8f,.85f,.91f),2);
   CloudlineSkin.Box(new Rect(r.xMax-246,r.yMax-36,208*progress,5),accent,2);
   if(director.Session.Phase==RacePhase.Countdown){
    var box=new Rect(r.center.x-65,r.center.y-74,130,148);ui.Panel(box,.96f);ui.Text(box,Mathf.CeilToInt((float)director.Session.Countdown).ToString(),86,true,CloudlineSkin.Blue,TextAnchor.MiddleCenter);
   }
   if(director.Session.Phase==RacePhase.FinishWindow)ui.Text(new Rect(r.x+270,r.y+90,r.width-300,35),"До результатов: "+director.Session.Remaining.ToString("0.0")+" с",20,true,Color.white,TextAnchor.MiddleCenter);
   if(racer.Finished)ui.Text(new Rect(r.x+100,r.center.y-25,r.width-200,50),"ФИНИШ",40,true,Color.white,TextAnchor.MiddleCenter);
  }
  void DrawPause(CloudlineSkin ui,RaceMenu menu){
   if(menu.PauseSettings){menu.DrawPauseSettings();return;}
   CloudlineSkin.Box(new Rect(0,0,1600,900),new Color(.03f,.075f,.17f,.45f),0);ui.Panel(new Rect(490,174,620,552),1);
   ui.Text(new Rect(534,200,532,76),"Пауза",45,true);
   menu.OverlayButton("resume",new Rect(534,300,532,70),"Продолжить",director.StartRace,true,!director.Input.MissingDevice);
   menu.OverlayButton("settings",new Rect(534,388,532,70),"Настройки",menu.OpenPauseSettings);
   menu.OverlayButton("restart",new Rect(534,476,532,70),"Повторить",()=>director.Restart(),false,!director.Input.MissingDevice);
   menu.OverlayButton("menu",new Rect(534,564,532,70),"В меню",()=>director.ExitToMenu());
   if(director.Input.MissingDevice)ui.Text(new Rect(534,650,532,56),"Подключите устройство или измените состав в меню.",17,false,CloudlineSkin.Muted,TextAnchor.MiddleLeft,true);
  }
  void DrawResults(CloudlineSkin ui,RaceMenu menu){
   menu.RegisterResultsScroll(delta=>resultsScroll.y=Mathf.Clamp(resultsScroll.y+delta*162,0,Mathf.Max(0,director.Cars.Length*54-466)));
   ui.Panel(new Rect(286,65,1028,770),.97f);ui.Text(new Rect(332,94,870,80),"Результаты",50,true);
   ui.Text(new Rect(338,196,130,32),"МЕСТО",15,true,CloudlineSkin.Muted);ui.Text(new Rect(482,196,475,32),"УЧАСТНИК",15,true,CloudlineSkin.Muted);ui.Text(new Rect(1020,196,240,32),"ВРЕМЯ",15,true,CloudlineSkin.Muted,TextAnchor.MiddleRight);
   resultsScroll=GUI.BeginScrollView(new Rect(328,242,950,466),resultsScroll,new Rect(0,0,922,director.Cars.Length*54));
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
   GUI.EndScrollView();ui.Text(new Rect(338,710,920,24),"Список: выберите и нажмите ← / → · колесо мыши",14,menu.ResultsScrollFocused,menu.ResultsScrollFocused?CloudlineSkin.Blue:CloudlineSkin.Muted);menu.OverlayButton("repeat",new Rect(332,741,440,64),"Повторить",()=>director.Restart(),true,!director.Input.MissingDevice);
   menu.OverlayButton("menu",new Rect(810,741,456,64),"В меню",()=>director.ExitToMenu());
  }
 }
}
