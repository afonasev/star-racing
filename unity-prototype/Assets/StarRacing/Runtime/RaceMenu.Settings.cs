using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarRacingPrototype {
 public sealed partial class RaceMenu {
  static readonly string[] SettingsSections={"Экран","Графика","Звук","Управление","Интерфейс"};
  DisplaySettings displayBefore;
  double displayDeadline;
  string settingsReturnFocus="settings-back",dialogReturnFocus;
  bool resetDialog,controlsHelp;
  int settingsOpenedFrame=-1,dialogOpenedFrame=-1;
  public bool DisplayConfirmationPending=>displayBefore!=null;
  public bool SettingsModalOpen=>DisplayConfirmationPending||resetDialog||controlsHelp;
  bool InSettings=>PauseSettings||(!director.Started&&ScreenState==RaceMenuScreen.Settings);
  public UserSettings Preferences=>UserSettingsStore.Current;
  void SavePreferences(){UserSettingsStore.Save();if(director!=null){director.ApplyUserSettings();director.GetComponent<RaceAudioCoordinator>()?.RefreshPreferences();}}
  static DisplaySettings CopyDisplay(DisplaySettings source)=>new DisplaySettings{fullscreen=source.fullscreen,vSync=source.vSync,width=source.width,height=source.height};
  void ApplyDisplay(){
   // Display is a separate candidate: the persistent snapshot stays confirmed.
   if(displayBefore==null){displayBefore=CopyDisplay(Preferences.display);dialogReturnFocus=focus;dialogOpenedFrame=Time.frameCount;}
   displayDeadline=Time.realtimeSinceStartupAsDouble+15;Display.Apply(nativeSize);focus="display-revert";controls.Clear();
  }
  public void ConfirmDisplay(){
   if(displayBefore==null)return;Preferences.display=CopyDisplay(Display);SavePreferences();displayBefore=null;focus=dialogReturnFocus;controls.Clear();
  }
  public void RevertDisplay(){
   if(displayBefore==null)return;Display=displayBefore;displayBefore=null;Display.Apply(nativeSize);focus=dialogReturnFocus;controls.Clear();
  }
  void TickSettings(){if(DisplayConfirmationPending&&(Time.realtimeSinceStartupAsDouble>=displayDeadline||!applicationFocused))RevertDisplay();}
  bool CloseSettingsLayer(){
   if(DisplayConfirmationPending){RevertDisplay();return true;}
   if(resetDialog){resetDialog=false;focus=dialogReturnFocus;controls.Clear();return true;}
   if(controlsHelp){controlsHelp=false;focus=settingsReturnFocus;controls.Clear();return true;}
   return false;
  }
  void OpenReset(){if(!InSettings||PopupOpen||SettingsModalOpen||EditingSeed||EditingName)return;dialogReturnFocus=focus;resetDialog=true;dialogOpenedFrame=Time.frameCount;focus="reset-cancel";controls.Clear();}
  public void ResetAllPreferences(){
   var old=CopyDisplay(Display);var defaults=new UserSettings();
   bool risky=old.fullscreen!=defaults.display.fullscreen||old.width!=defaults.display.width||old.height!=defaults.display.height;
   UserSettingsStore.Replace(defaults);NitroBlurSettings.NotifyChange();Display=CopyDisplay(defaults.display);showFps=defaults.showFps;resetDialog=false;
   if(risky){Preferences.display=old;ApplyDisplay();}else{Display.Apply(nativeSize);focus="reset-all";}
   SavePreferences();controls.Clear();
  }
  void OpenControlsHelp(){settingsReturnFocus=focus;controlsHelp=true;controlPage=gamepadNavigation?1:0;focus="help-back";controls.Clear();}
  void SettingsSection(int page){settingsPage=page;focus="settings-section-"+page;controls.Clear();}
  void DrawSettingsContent(){
   if(controlsHelp)return;
   Skin.Panel(new Rect(56,223,270,554),1);Skin.Panel(new Rect(350,223,1194,554),1);
   for(int i=0;i<SettingsSections.Length;i++){int page=i;Button("settings-section-"+i,new Rect(76,245+i*66,230,52),SettingsSections[i],()=>SettingsSection(page),settingsPage==i,true,21);}
   if(settingsPage==0)DrawScreenPreferences();else if(settingsPage==1)DrawGraphicsPreferences();else if(settingsPage==2)DrawAudio(new Rect(386,314,654,340));else if(settingsPage==3)DrawInputPreferences();else DrawInterfacePreferences();
   Button("reset-all",new Rect(56,802,480,54),"Сбросить все настройки",OpenReset,false,true,21);
   Button("controls-help",new Rect(1110,802,434,54),"Справка об управлении →",OpenControlsHelp,false,true,21);
   if(!string.IsNullOrEmpty(UserSettingsStore.LastError))Skin.Text(new Rect(386,699,1118,60),UserSettingsStore.LastError,17,false,CloudlineSkin.Ink,TextAnchor.MiddleLeft,true);
  }
  void DrawScreenPreferences(){
   Skin.Text(new Rect(386,249,1000,44),"Экран",28,true);
   Toggle("fullscreen",new Rect(386,314,820,44),"Полный экран",Display.fullscreen,()=>{Display=CopyDisplay(Display);Display.fullscreen=!Display.fullscreen;ApplyDisplay();});
   var labels=new string[displayOptions.Length];labels[0]="Авто ("+nativeSize.x+" × "+nativeSize.y+")";for(int i=1;i<labels.Length;i++)labels[i]=displayOptions[i].x+" × "+displayOptions[i].y;
   Dropdown("resolution",new Rect(386,381,1058,44),"Разрешение",labels,Mathf.Max(0,Display.Selected(displayOptions)),index=>{Display=CopyDisplay(Display);Display.width=displayOptions[index].x;Display.height=displayOptions[index].y;ApplyDisplay();});
   Toggle("vsync",new Rect(386,448,820,44),"Вертикальная синхронизация",Display.vSync,()=>{Display.vSync=!Display.vSync;Preferences.display=CopyDisplay(Display);Display.Apply(nativeSize);SavePreferences();});
   var limits=UserSettings.FrameLimits;var names=new string[limits.Length];for(int i=0;i<names.Length;i++)names[i]=limits[i]==0?"Без ограничения":limits[i]+" FPS";
   Dropdown("fps-limit",new Rect(386,515,1058,44),"Лимит FPS",names,Array.IndexOf(limits,Preferences.frameLimit),i=>{Preferences.frameLimit=limits[i];SavePreferences();});
   Skin.Text(new Rect(386,589,1000,70),"При включённой вертикальной синхронизации частота ограничена экраном. Лимит FPS используется при отключённом VSync.",18,false,CloudlineSkin.Muted,TextAnchor.MiddleLeft,true);
  }
  int GraphicsProfile()=>Preferences.graphics.contactShadows&&Preferences.graphics.shadows&&Preferences.graphics.bloom&&Preferences.graphics.mist&&Preferences.nitroBlur?2:!Preferences.graphics.contactShadows&&!Preferences.graphics.shadows&&!Preferences.graphics.bloom&&!Preferences.graphics.mist&&!Preferences.nitroBlur?0:Preferences.graphics.shadows&&Preferences.graphics.bloom&&!Preferences.graphics.contactShadows&&!Preferences.graphics.mist&&!Preferences.nitroBlur?1:3;
  void DrawGraphicsPreferences(){
   Skin.Text(new Rect(386,249,1000,44),"Графика",28,true);
   var profiles=new[]{"Минимальное","Среднее","Высокое"};int profile=GraphicsProfile();
   Skin.Text(new Rect(386,310,245,44),"Качество эффектов",21,true);
   Button("graphics-quality",new Rect(631,310,813,44),(profile==3?"Пользовательское":profiles[profile])+"   ▾",()=>ShowPopup("Качество эффектов",profiles,Mathf.Min(profile,2),i=>{
    var g=Preferences.graphics;g.contactShadows=i==2;g.shadows=i>0;g.bloom=i>0;g.mist=i==2;NitroBlurSettings.Enabled=i==2;SavePreferences();
   }),false,true,18);
   Dropdown("antialiasing",new Rect(386,369,1058,44),"Сглаживание",new[]{"MSAA ×"+(director==null?2:director.DefaultMsaaSamples),"Выключено","FXAA","SMAA"},Preferences.antiAliasing+1,i=>{Preferences.antiAliasing=i-1;SavePreferences();});
   var graphics=Preferences.graphics;
   Toggle("contact-shadows",new Rect(386,434,500,44),"Контактные тени",graphics.contactShadows,()=>{graphics.contactShadows=!graphics.contactShadows;SavePreferences();});
   Toggle("shadows",new Rect(970,434,500,44),"Тени",graphics.shadows,()=>{graphics.shadows=!graphics.shadows;SavePreferences();});
   Toggle("bloom",new Rect(386,498,500,44),"Свечение",graphics.bloom,()=>{graphics.bloom=!graphics.bloom;SavePreferences();});
   Toggle("mist",new Rect(970,498,500,44),"Облачная дымка",graphics.mist,()=>{graphics.mist=!graphics.mist;SavePreferences();});
   Toggle("nitro-blur",new Rect(386,562,500,44),"Размытие на нитро",Preferences.nitroBlur,()=>{NitroBlurSettings.Enabled=!Preferences.nitroBlur;SavePreferences();});
   Skin.Text(new Rect(386,634,1050,44),"Профиль управляет тенями и эффектами. Сглаживание выбирается отдельно.",17,false,CloudlineSkin.Muted);
  }
  public void DrawAudio(Rect r){
   Skin.Text(new Rect(r.x,249,1000,44),"Громкость",28,true);
   AudioSlider("master",r.x,r.y,r.width,"Общая",Preferences.master,v=>{Preferences.master=v;SavePreferences();});
   AudioSlider("music",r.x,r.y+80,r.width,"Музыка",Preferences.music,v=>{Preferences.music=v;SavePreferences();});
   AudioSlider("engine",r.x,r.y+160,r.width,"Двигатели",Preferences.engines,v=>{Preferences.engines=v;SavePreferences();});
   AudioSlider("effects",r.x,r.y+240,r.width,"Эффекты",Preferences.effects,v=>{Preferences.effects=v;SavePreferences();});
   Skin.Text(new Rect(1100,314,390,160),"Общие для устройства\n\nИзменения действуют сразу для всех локальных участников.",19,false,CloudlineSkin.Muted,TextAnchor.UpperLeft,true);
   Skin.Text(new Rect(r.x,r.y+330,1000,34),"0% общей громкости отключает весь игровой звук.",17,false,CloudlineSkin.Muted);
  }
  void DrawInputPreferences(){
   Skin.Text(new Rect(386,249,1000,44),"Управление",28,true);
   PreferenceSlider("stick-sensitivity",386,321,680,"Чувствительность стика",Preferences.stickSensitivity,.5f,2,.05f,"×",v=>Preferences.stickSensitivity=v);
   PreferenceSlider("stick-deadzone",386,431,680,"Мёртвая зона стика",Preferences.stickDeadzone,0,.5f,.01f,"%",v=>Preferences.stickDeadzone=v,true);
   Skin.Text(new Rect(386,556,1050,90),"Общие настройки руления для всех локальных геймпадов. D-pad сохраняет дискретное управление; игровые возможности человека и AI не меняются.",18,false,CloudlineSkin.Muted,TextAnchor.MiddleLeft,true);
  }
  void DrawInterfacePreferences(){
   Skin.Text(new Rect(386,249,1000,44),"Интерфейс",28,true);
   Dropdown("text-scale",new Rect(386,314,1058,44),"Масштаб текста",new[]{"90%","100%","110%"},Preferences.textScale<.95f?0:Preferences.textScale>1.05f?2:1,i=>{Preferences.textScale=.9f+i*.1f;SavePreferences();});
   Toggle("high-contrast",new Rect(386,391,820,44),"Повышенный контраст",Preferences.highContrast,()=>{Preferences.highContrast=!Preferences.highContrast;SavePreferences();});
   Toggle("fps",new Rect(386,468,820,44),"Показывать FPS",Preferences.showFps,()=>{Preferences.showFps=!Preferences.showFps;showFps=Preferences.showFps;SavePreferences();});
  }
  void PreferenceSlider(string id,float x,float y,float width,string label,float value,float min,float max,float step,string unit,Action<float> assign,bool percent=false){
   Skin.Text(new Rect(x,y,width-150,34),label,22,true);Skin.Text(new Rect(x+width-150,y,150,34),(percent?Mathf.RoundToInt(value*100).ToString():value.ToString("0.00"))+unit,20,true,CloudlineSkin.Muted,TextAnchor.MiddleRight);
   float normalized=(value-min)/(max-min);CloudlineSkin.Box(new Rect(x,y+48,width,9),new Color(.81f,.85f,.91f),5);CloudlineSkin.Box(new Rect(x,y+48,width*normalized,9),CloudlineSkin.Blue,5);CloudlineSkin.Box(new Rect(x+width*normalized-8,y+43,16,19),CloudlineSkin.Blue,8);
   Action<float> set=v=>{assign(Mathf.Clamp(Mathf.Round(v/step)*step,min,max));SavePreferences();};
   Register(id,()=>{},delta=>set(value+delta*step),new Rect(x,y,width,70));
   if(focus==id&&collect)CloudlineSkin.Box(new Rect(x,y+72,width,2),CloudlineSkin.Blue,0);
   var hit=new Rect(x,y+34,width,38);var e=Event.current;if(collect&&(e.type==EventType.MouseDown||e.type==EventType.MouseDrag)&&hit.Contains(e.mousePosition)){focus=id;set(Mathf.Lerp(min,max,Mathf.Clamp01((e.mousePosition.x-x)/width)));e.Use();}
  }
  void DrawSettingsLayer(){
   if(!SettingsModalOpen)return;controls.Clear();collect=true;GUI.enabled=true;
   if(controlsHelp){
    Skin.Backdrop();Button("help-back",new Rect(56,36,390,54),"‹ К настройкам",()=>CloseSettingsLayer(),false,true,21);Skin.Brand(new Rect(1170,34,374,60),33);
    Skin.Text(new Rect(56,111,1430,75),"Управление в гонке",48,true);
    Button("help-keyboard",new Rect(56,219,420,52),"Клавиатура и мышь",()=>controlPage=0,controlPage==0,true,21);Button("help-gamepad",new Rect(498,219,300,52),"Геймпад",()=>controlPage=1,controlPage==1,true,21);
    Skin.Panel(new Rect(56,295,1060,544),1);Skin.Panel(new Rect(1140,295,404,544),1);
    var guide=controlPage==0?Skin.KeyboardGuide:Skin.GamepadGuide;
    if(guide!=null)GUI.DrawTexture(new Rect(82,312,1008,510),guide,ScaleMode.ScaleToFit);
    else Skin.Text(new Rect(90,400,970,80),"Не удалось загрузить справку. Вернитесь в настройки.",23,true);
    Skin.Text(new Rect(1174,326,338,64),controlPage==0?"Два игрока на клавиатуре":"Управление автомобилем",25,true,null,TextAnchor.MiddleLeft,true);
    Skin.Text(new Rect(1174,423,338,340),controlPage==0?"Первый игрок — WASD.\n\nВторой игрок — стрелки.\n\nМышь не участвует в рулении.":"На рисунке — Xbox.\n\nНа других контроллерах: нижняя лицевая кнопка — тормоз, правая — нитро.\n\nОстальные действия соответствуют расположению органов управления.",20,false,CloudlineSkin.Muted,TextAnchor.UpperLeft,true);
    return;
   }
   CloudlineSkin.Box(new Rect(0,0,1600,900),new Color(.035f,.08f,.18f,.45f),0);
   var r=new Rect(410,280,780,350);Skin.Panel(r,1);
   if(DisplayConfirmationPending){
    Skin.Text(new Rect(450,310,700,56),"Сохранить настройки экрана?",29,true);
    Skin.Text(new Rect(450,383,700,72),"Прежний режим вернётся через "+Mathf.Max(0,Mathf.CeilToInt((float)(displayDeadline-Time.realtimeSinceStartupAsDouble)))+" секунд без подтверждения.",21,false,CloudlineSkin.Muted,TextAnchor.MiddleLeft,true);
    Button("display-revert",new Rect(450,535,370,54),"Вернуть прежний режим",RevertDisplay,false,true,20);
    Button("display-confirm",new Rect(844,535,306,54),"Сохранить",ConfirmDisplay,true,true,22);
   }else{
    Skin.Text(new Rect(450,310,700,56),"Сбросить все настройки?",30,true);
    Skin.Text(new Rect(450,383,700,110),"Все предпочтения вернутся к значениям по умолчанию. Рекорды, история, лабораторные профили и согласие на статистику сохранятся.",20,false,CloudlineSkin.Muted,TextAnchor.MiddleLeft,true);
    Button("reset-cancel",new Rect(610,535,230,54),"Отмена",()=>CloseSettingsLayer(),false,true,21);
    Button("reset-confirm",new Rect(864,535,286,54),"Сбросить все",ResetAllPreferences,true,true,22);
   }
  }
 }
}
