using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 public sealed class RaceHud:MonoBehaviour {
  public RaceDirector director;
  public bool EditingTrackSeed {get;private set;}
  string seedText;int themeChoice,railChoice,countChoice=1;bool jumpChoice,handicapChoice;
  bool showFps;float frameSeconds;int frames;string fps="";Vector2 resultsScroll;
  bool showUpdates;Vector2 updateScroll;
  public bool UpdateDialogOpen=>showUpdates;
  static readonly int[] Counts={2,8,16,32,64};
  GUIStyle title,label,large;bool styled;
  public void RefreshTrackSettings(){seedText=null;EditingTrackSeed=false;}
  void Awake(){showFps=PlayerPrefs.GetInt("StarRacing.ShowFps",1)!=0;}
  void Update(){if(showUpdates&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)showUpdates=false;if(Keyboard.current!=null&&Keyboard.current.f3Key.wasPressedThisFrame)SetFps(!showFps);frames++;frameSeconds+=Time.unscaledDeltaTime;if(frameSeconds>.5f){fps="FPS "+Mathf.RoundToInt(frames/frameSeconds);frames=0;frameSeconds=0;}}
  void SetFps(bool value){showFps=value;PlayerPrefs.SetInt("StarRacing.ShowFps",value?1:0);}
  void Style(){if(styled)return;styled=true;GUI.skin.button.fontSize=20;GUI.skin.toggle.fontSize=20;GUI.skin.textField.fontSize=20;title=new GUIStyle(GUI.skin.label){fontSize=24,normal={textColor=Color.white}};label=new GUIStyle(GUI.skin.label){fontSize=18,normal={textColor=Color.white},wordWrap=true};large=new GUIStyle(title){fontSize=56,alignment=TextAnchor.MiddleCenter};}
  void Panel(Rect r){GUI.color=new Color(.035f,.055f,.09f,.96f);GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=Color.white;}
  void OnGUI(){
   if(director==null||director.Session==null)return;Style();GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
   if(Distribution.DesktopUpdater.Instance?.StartupApplying==true){Panel(new Rect(400,350,800,150));GUI.Label(new Rect(450,400,700,60),"Применение обновления…",title);return;}
   if(Distribution.DesktopUpdater.Instance?.Downloading==true)GUI.Label(new Rect(20,860,450,30),"Обновление: "+Distribution.DesktopUpdater.Instance.Progress+"%",label);
   if(showFps)GUI.Label(new Rect(1450,15,140,35),fps,label);
   if(director.InPreparationMenu){DrawMenu();return;}
   for(int i=0;i<2;i++){
    var car=director.Cars[i];var racer=director.Session.Racers[i];float x=i*800;
    Panel(new Rect(x+18,18,355,130));GUI.Label(new Rect(x+30,26,330,32),"Игрок "+(i+1)+" · "+director.Input.DeviceName(i),label);
    GUI.Label(new Rect(x+30,62,330,32),Mathf.RoundToInt(Mathf.Abs(car.Telemetry.speedKmh))+" км/ч · место "+racer.Place+" / "+director.Cars.Length,label);
    GUI.Label(new Rect(x+30,98,330,32),"Нитро "+Mathf.RoundToInt(car.Telemetry.nitro01*100)+"% · трасса "+Mathf.RoundToInt(racer.Progress/director.Session.RaceLength*100)+"%",label);
   }
   float leader=0;foreach(var racer in director.Session.Racers)leader=Mathf.Max(leader,racer.Progress);
   GUI.Label(new Rect(420,24,360,80),System.TimeSpan.FromSeconds(director.Session.Elapsed).ToString(@"mm\:ss")+" · лидер "+Mathf.RoundToInt(leader/director.Session.RaceLength*100)+"%",title);
   GUI.Label(new Rect(550,850,650,35),"ESC — пауза · F5 — повтор · Backspace — в меню",label);
   if(director.Session.Phase==RacePhase.Countdown)GUI.Label(new Rect(600,350,400,120),Mathf.CeilToInt((float)director.Session.Countdown).ToString(),large);
   if(director.Session.Phase==RacePhase.FinishWindow)GUI.Label(new Rect(560,780,600,35),"До результатов: "+director.Session.Remaining.ToString("0.0")+" с",title);
   if(director.Session.Phase==RacePhase.Results){DrawResults();return;}
   if(director.Paused){Panel(new Rect(400,180,800,600));GUI.Label(new Rect(450,215,650,40),"Пауза",title);DrawAudio(450,280);if(GUI.Button(new Rect(850,290,300,55),"Продолжить"))director.StartRace();if(GUI.Button(new Rect(850,365,300,55),"В меню"))director.ExitToMenu();}
  }
  void DrawMenu(){
   if(showUpdates){DrawUpdates();return;}
   Panel(new Rect(120,65,1360,770));GUI.Label(new Rect(170,95,1100,50),"STAR RACING · UNITY",large);
   if(seedText==null){seedText=director.TrackSeed.ToString();themeChoice=director.TrackThemePreference=="random"?0:director.TrackThemePreference=="cloud-city"?1:2;railChoice=director.TrackRailMode=="normal"?0:director.TrackRailMode=="full"?1:2;jumpChoice=director.TrackJumps;handicapChoice=director.Handicap;countChoice=System.Array.IndexOf(Counts,director.EntrantCount);if(countChoice<0)countChoice=1;}
   GUI.Label(new Rect(175,175,700,30),"Два игрока · остальные участники — боты",title);
   GUI.Label(new Rect(175,230,190,32),"Участников",label);countChoice=GUI.SelectionGrid(new Rect(370,225,440,38),countChoice,new[]{"2","8","16","32","64"},5);
   handicapChoice=GUI.Toggle(new Rect(175,285,600,32),handicapChoice,"Фора сильным ботам");
   GUI.Label(new Rect(175,335,120,32),"Seed",label);GUI.SetNextControlName("TrackSeed");seedText=GUI.TextField(new Rect(370,335,440,32),seedText,10);EditingTrackSeed=GUI.GetNameOfFocusedControl()=="TrackSeed";
   themeChoice=GUI.SelectionGrid(new Rect(175,390,635,42),themeChoice,new[]{"Случайная тема","Город в облаках","Станция"},3);
   railChoice=GUI.SelectionGrid(new Rect(175,450,635,42),railChoice,new[]{"Обычные рельсы","Полные рельсы","Без рельсов"},3);
   jumpChoice=GUI.Toggle(new Rect(175,510,600,32),jumpChoice,"Трамплины и пропасти");
   uint seed;GUI.enabled=uint.TryParse(seedText,out seed);
   if(GUI.Button(new Rect(175,565,635,46),"Применить настройки")){director.ConfigureTrack(seed,new[]{"random","cloud-city","space-station"}[themeChoice],new[]{"normal","full","none"}[railChoice],jumpChoice);director.ConfigureRoster(Counts[countChoice],handicapChoice);}
   GUI.enabled=director.Balance!=null;if(GUI.Button(new Rect(175,640,635,64),"Начать гонку · ENTER"))director.StartRace();GUI.enabled=true;
   GUI.Label(new Rect(175,725,635,60),director.Balance==null?director.BalanceError:director.TrackSummary,label);
   DrawAudio(920,225);SetFps(GUI.Toggle(new Rect(920,465,400,30),showFps,"Показывать FPS · F3"));
   GUI.Label(new Rect(920,520,440,220),"Игрок 1: WASD, Space — занос, Shift — нитро.\nИгрок 2: стрелки, Right Alt — занос, Right Shift — нитро.\nГеймпад: RT/RB — газ, A — тормоз, LT/LB — занос, B — нитро.",label);
   if(GUI.Button(new Rect(920,745,400,45),"Новая трасса"))director.NewTrack();
   var updater=Distribution.DesktopUpdater.Instance;
   GUI.Label(new Rect(175,798,600,30),"Версия "+(updater?.InstalledVersion??Application.version),label);
   if(GUI.Button(new Rect(920,798,400,32),updater!=null&&updater.Available?"Доступно обновление · "+updater.NewVersion:"Обновления"))showUpdates=true;
  }
  void DrawUpdates(){
   var updater=Distribution.DesktopUpdater.Instance;Panel(new Rect(340,150,920,610));
   GUI.Label(new Rect(390,185,800,50),"Обновления Star Racing",title);
   GUI.Label(new Rect(390,250,500,35),"Установлена версия "+(updater?.InstalledVersion??Application.version),label);
   GUI.enabled=updater!=null&&!updater.Busy&&!updater.Staged&&updater.CanUpdate;
   if(GUI.Button(new Rect(900,250,290,35),"Проверить"))_=updater.Check();GUI.enabled=true;
   GUI.Label(new Rect(390,305,800,70),updater?.Status??"Обновления недоступны",label);
   if(updater!=null){
    if(updater.Available){
     GUI.Label(new Rect(390,380,800,35),"Новая версия "+updater.NewVersion+" · "+updater.SizeMB.ToString("0")+" МБ",label);
     updateScroll=GUI.BeginScrollView(new Rect(390,425,800,130),updateScroll,new Rect(0,0,760,220));GUI.Label(new Rect(0,0,760,220),updater.Notes,label);GUI.EndScrollView();
    }
    if(updater.Busy)GUI.Label(new Rect(390,565,800,35),updater.Progress>0?"Загружено "+updater.Progress+"%":"Подождите…",label);
    GUI.enabled=!updater.Busy&&updater.CanUpdate;
    if(updater.Staged){if(GUI.Button(new Rect(390,620,480,50),"Перезапустить"))updater.InstallAndRestart();}
    else if(updater.Available){if(GUI.Button(new Rect(390,620,480,50),"Обновить"))_=updater.Download();}
    else GUI.Label(new Rect(390,620,480,50),"Новых обновлений нет",label);
    GUI.enabled=true;
    if(updater.Downloading&&GUI.Button(new Rect(900,565,290,40),"Отменить загрузку"))updater.Cancel();
   }
   if(GUI.Button(new Rect(900,620,290,50),"Назад"))showUpdates=false;
   GUI.Label(new Rect(390,695,800,40),"Скачанный пакет установится после выхода при следующем запуске. Настройки сохраняются.",label);
  }
  void DrawAudio(float x,float y){var audio=director.GetComponent<RaceAudioCoordinator>();if(audio==null)return;GUI.Label(new Rect(x,y,400,35),"Звук",title);bool changed=GUI.changed;GUI.changed=false;bool mute=GUI.Toggle(new Rect(x,y+45,390,30),audio.Muted,"Без звука");float music=Slider(x,y+90,"Музыка",audio.MusicVolume),engines=Slider(x,y+135,"Двигатели",audio.EnginesVolume),effects=Slider(x,y+180,"Эффекты",audio.EffectsVolume);if(GUI.changed)audio.SetMix(mute,music,engines,effects);GUI.changed|=changed;}
  float Slider(float x,float y,string text,float value){GUI.Label(new Rect(x,y,180,28),text,label);return GUI.HorizontalSlider(new Rect(x+180,y+10,210,18),value,0,1);}
  void DrawResults(){Panel(new Rect(360,130,880,650));GUI.Label(new Rect(405,160,790,45),"Результаты",large);resultsScroll=GUI.BeginScrollView(new Rect(405,225,790,400),resultsScroll,new Rect(0,0,750,director.Cars.Length*42));for(int place=1;place<=director.Cars.Length;place++)for(int i=0;i<director.Cars.Length;i++){var r=director.Session.Racers[i];if(r.Place!=place)continue;GUI.Label(new Rect(10,(place-1)*42,730,38),"#"+place+"  "+director.Roster.Entrants[i].Name+"   "+(r.Finished?System.TimeSpan.FromSeconds(r.FinishTime).ToString(@"mm\:ss\.fff"):"DNF · "+Mathf.RoundToInt(r.Progress/director.Session.RaceLength*100)+"%"),label);}GUI.EndScrollView();if(GUI.Button(new Rect(410,675,360,55),"Повторить"))director.Restart();if(GUI.Button(new Rect(820,675,360,55),"В меню"))director.ExitToMenu();}
 }
}
