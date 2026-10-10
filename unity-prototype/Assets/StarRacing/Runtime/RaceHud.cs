using System;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed class RaceHud : MonoBehaviour {
  public RaceDirector director;
  Vector2 resultsScroll;Texture2D instrumentBackdrop;
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
   if(!results&&!settings){for(int seat=0;seat<director.HumanCount;seat++)DrawSeat(ui,seat);DrawProgress(ui);}
   if(results){menu.BeginOverlay("repeat",!wasResults);DrawResults(ui,menu);}
   else if(director.Paused){GUI.matrix=old;CloudlineSkin.Begin();menu.BeginOverlay("resume",!wasPaused);DrawPause(ui,menu);}
   if(!results&&!settings){var hudMatrix=GUI.matrix;GUI.matrix=hudMatrix*Matrix4x4.Translate(new Vector3(0,-124,0));menu.DrawFps();GUI.matrix=hudMatrix;}else menu.DrawFps();wasPaused=director.Paused;wasResults=results;GUI.matrix=old;
  }
  void DrawSeat(CloudlineSkin ui,int seat){
   var r=RaceHudLayout.View(director.HumanCount,seat);
   var car=director.Cars[seat];var racer=director.Session.Racers[seat];var accent=director.EntrantColor(seat);
   CloudlineSkin.Box(new Rect(r.x,r.y,r.width,2),Color.white,0);CloudlineSkin.Box(new Rect(r.x,r.y,2,r.height),Color.white,0);
   DrawInstrument(ui,RaceHudLayout.Instrument(director.HumanCount,seat),accent,Mathf.Abs(car.Telemetry.speedKmh),car.Telemetry.nitro01,RaceHudLayout.Right(director.HumanCount,seat));
   var place=RaceHudLayout.Position(director.HumanCount,seat);
   CloudlineSkin.Box(place,new Color(.027f,.078f,.137f,.66f),4);
   Circle(new Vector2(place.x+8,place.center.y),2,accent);
   // Separate sizes keep the place dominant while retaining the full roster count.
   ui.Text(new Rect(place.x+14,place.y,25,place.height),racer.Place.ToString(),18,true,Color.white,TextAnchor.MiddleRight);
   ui.Text(new Rect(place.x+40,place.y+2,25,place.height-2),"/"+director.Cars.Length,8,false,new Color(.8f,.87f,.94f));
   if(director.Session.Phase==RacePhase.Countdown){
    var box=new Rect(r.center.x-65,r.center.y-74,130,148);ui.Panel(box,.96f);ui.Text(box,Mathf.CeilToInt((float)director.Session.Countdown).ToString(),86,true,CloudlineSkin.Blue,TextAnchor.MiddleCenter);
   }
   if(director.Session.Phase==RacePhase.FinishWindow)ui.Text(new Rect(r.x+120,r.y+132,r.width-240,35),"До результатов: "+director.Session.Remaining.ToString("0.0")+" с",20,true,Color.white,TextAnchor.MiddleCenter);
   if(racer.Finished)ui.Text(new Rect(r.x+100,r.center.y-25,r.width-200,50),"ФИНИШ",40,true,Color.white,TextAnchor.MiddleCenter);
  }
  static void Circle(Vector2 center,float radius,Color color){CloudlineSkin.Box(new Rect(center.x-radius,center.y-radius,radius*2,radius*2),color,radius);}
  void DrawInstrument(CloudlineSkin ui,Rect r,Color accent,float speed,float nitro,bool right){
   // The darkest edge faces the screen border; both sides fade inward.
   if(instrumentBackdrop==null){
    instrumentBackdrop=new Texture2D(64,1,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave};
    for(int i=0;i<64;i++){float inward=i/63f;instrumentBackdrop.SetPixel(i,0,new Color(.027f,.078f,.137f,RaceHudLayout.InstrumentOpacity*(1-inward*inward)));}
    instrumentBackdrop.Apply(false,true);
   }
   GUI.DrawTextureWithTexCoords(r,instrumentBackdrop,right?new Rect(1,0,-1,1):new Rect(0,0,1,1),true);
   CloudlineSkin.Box(new Rect(right?r.xMax-1:r.x,r.y,1,r.height),accent,0);
   ui.Text(new Rect(r.x+9,r.y+3,54,34),Mathf.RoundToInt(speed).ToString(),28,true,Color.white);
   ui.Text(new Rect(r.x+63,r.y+13,32,16),"КМ/Ч",6,false,new Color(.82f,.89f,.96f));
   var bar=new Rect(r.x+9,r.yMax-12,68,2);
   CloudlineSkin.Box(bar,new Color(.7f,.78f,.87f,.3f),0);
   float fill=bar.width*Mathf.Clamp01(nitro);if(fill>0)CloudlineSkin.Box(new Rect(bar.x,bar.y,fill,bar.height),accent,0);
   ui.Text(new Rect(r.x+85,r.yMax-18,16,14),"N₂",6,false,new Color(.82f,.89f,.96f));
  }
  void OnDestroy(){if(instrumentBackdrop!=null)Destroy(instrumentBackdrop);}
  void DrawProgress(CloudlineSkin ui){
   var line=RaceHudLayout.ProgressLine;
   CloudlineSkin.Box(new Rect(line.x,line.y+1,line.width,1),new Color(0,0,0,.35f),0);
   CloudlineSkin.Box(line,new Color(.9f,.96f,1,.45f),0);
   for(int tick=0;tick<=10;tick++)CloudlineSkin.Box(new Rect(line.x+line.width*tick/10,line.y-1,1,3),new Color(1,1,1,.3f),0);
   ui.Text(new Rect(line.x-68,line.y-8,56,16),"СТАРТ",6,true,Color.white,TextAnchor.MiddleCenter);
   ui.Text(new Rect(line.xMax+12,line.y-8,56,16),"ФИНИШ",6,true,Color.white,TextAnchor.MiddleCenter);
   // Stable entrant colors match the actual cars. Local seats remain legible over the pack.
   for(int pass=0;pass<2;pass++)for(int i=0;i<director.Session.Racers.Length;i++){
    int seat=director.Roster.Entrants[i].HumanSeat;bool human=seat>=0;if(human!=(pass==1))continue;
    var point=RaceHudLayout.Marker(director.Session.Racers[i].Progress,director.Session.RaceLength);
    Circle(point,human?7:1.8f,CloudlineSkin.Alpha(Color.white,human?1:.25f));Circle(point,human?6:1.3f,CloudlineSkin.Alpha(director.EntrantColor(i),human?1:.4f));
    if(human)ui.Text(new Rect(point.x-6,point.y-7,12,14),(seat+1).ToString(),7,true,Color.white,TextAnchor.MiddleCenter);
   }
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
