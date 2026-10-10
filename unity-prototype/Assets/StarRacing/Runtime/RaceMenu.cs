using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarRacingPrototype {
 public enum RaceMenuScreen { Main, LocalSetup, Settings }
 public sealed class RaceMenu : MonoBehaviour {
  public RaceDirector director;
  public LocalRaceConfig Selected {get;private set;}
  public RaceMenuScreen ScreenState {get;private set;}
  public bool EditingSeed {get;private set;}
  public string Error {get;private set;}="";
  public CloudlineSkin Skin {get;private set;}
  readonly List<Control> controls=new List<Control>();
  sealed class Control {public string id;public Action action;public Action<int> adjust;}
  string focus="local",seedText,popupTitle,popupSearch="",focusRequest,popupReturnFocus="back";
  string[] popupOptions;Action<int> popupSelect;Vector2 popupScroll;
  int nameEditor=-1;bool latinNames;string editingName;
  bool collect=true,showFps;float elapsed;int frames,fps;float nextStick;
  Gamepad activatingPad;
  bool gamepadNavigation,applicationFocused=true;
  public DisplaySettings Display {get;private set;}
  Vector2Int[] displayOptions;Vector2Int nativeSize;
  void ApplyDisplay(){Display.Save();Display.Apply(nativeSize);}
  void RefreshDisplay(){nativeSize=DisplaySettings.NativeSize();displayOptions=DisplaySettings.Options(UnityEngine.Screen.resolutions,nativeSize);Display.Validate(displayOptions);}
  public bool PopupOpen=>popupOptions!=null;
  public bool EditingName=>nameEditor>=0||GUI.GetNameOfFocusedControl().StartsWith("PlayerName");
  public bool ShowFps=>showFps;
  public bool PauseSettings {get;private set;}
  int pauseBackFrame=-1,controlPage;
  public void OpenPauseSettings(){RefreshDisplay();PauseSettings=true;focus="settings-back";controls.Clear();focusRequest="";}
  public void ClosePauseSettings(){ClosePopup();PauseSettings=false;focus="settings";controls.Clear();focusRequest="";pauseBackFrame=Time.frameCount;}
  public bool ConsumePauseSettingsBack(bool pressed){if(!PauseSettings||!pressed)return false;if(PopupOpen){ClosePopup();pauseBackFrame=Time.frameCount;}else ClosePauseSettings();return true;}
  public void ResetPauseSettings(){PauseSettings=false;controls.Clear();focusRequest="";}

  void Awake(){Selected=LocalRaceConfig.Load();SessionControllers.SeedNames(Selected);seedText=Selected.seed;showFps=PlayerPrefs.GetInt("StarRacing.ShowFps",1)!=0;Display=DisplaySettings.Load();RefreshDisplay();Display.Apply(nativeSize);}
  public void EnsureSkin(){if(Skin==null)Skin=new CloudlineSkin();}
  public void Open(RaceMenuScreen screen,Gamepad source=null){nameEditor=-1;ResetPauseSettings();ScreenState=screen;if(screen==RaceMenuScreen.Settings)RefreshDisplay();if(screen==RaceMenuScreen.LocalSetup){if(source!=null&&source.added)SelectFirstDevice(SessionControllers.Slot(source));RollSeed();}Error="";ClosePopup();focus=screen==RaceMenuScreen.Main?"local":"back";controls.Clear();EditingSeed=false;focusRequest="";}
  public void SelectFirstDevice(int device){
   if(Selected.humans==0){JoinDevice(device);return;}
   int existing=-1;for(int i=0;i<Selected.humans;i++)if(Selected.devices[i]==device)existing=i;
   if(existing==0)return;
   if(existing>0){
    (Selected.devices[0],Selected.devices[existing])=(Selected.devices[existing],Selected.devices[0]);
    (Selected.names[0],Selected.names[existing])=(Selected.names[existing],Selected.names[0]);
    Selected.Save();
   }else AssignDevice(0,device);
  }
  public void RollSeed(){Selected.RandomizeSeed();seedText=Selected.seed;Selected.Save();}
  public bool JoinDevice(int device){
   for(int i=0;i<Selected.humans;i++)if(Selected.devices[i]==device)return false;
   if(Selected.humans==4){Error="Все четыре места заняты";return false;}
   int seat=Selected.humans++;Selected.devices[seat]=device;Selected.names[seat]=SessionControllers.Name(device,"Игрок "+(seat+1));Selected.Save();Error="";return true;
  }
  public void RemoveSeat(int seat){
   if(seat<0||seat>=Selected.humans)return;SessionControllers.Remember(Selected.devices[seat],Selected.names[seat]);
   for(int i=seat;i<Selected.humans-1;i++){Selected.devices[i]=Selected.devices[i+1];Selected.names[i]=Selected.names[i+1];}
   Selected.humans--;Selected.Save();focus="add";focusRequest="";
  }
  public void RenameSeat(int seat,string value){EnsureSkin();Selected.names[seat]=Skin.FitName(value);SessionControllers.Remember(Selected.devices[seat],Selected.names[seat]);Selected.Save();}
  void AssignDevice(int seat,int device){
   SessionControllers.Remember(Selected.devices[seat],Selected.names[seat]);
   for(int i=0;i<Selected.humans;i++)if(i!=seat&&Selected.devices[i]==device){Error="Это устройство уже назначено игроку";return;}
   Selected.devices[seat]=device;Selected.names[seat]=SessionControllers.Name(device,Selected.PlayerName(seat));Selected.Save();Error="";
  }
  void ChooseDevice(int seat){
   var devices=SessionControllers.AvailableDevices();var free=new List<int>();foreach(int device in devices){bool used=false;for(int i=0;i<Selected.humans;i++)if(i!=seat&&Selected.devices[i]==device)used=true;if(!used)free.Add(device);}
   if(free.Count==0){Error="Подключите геймпад и нажмите Y";return;}
   var labels=new string[free.Count];for(int i=0;i<labels.Length;i++)labels[i]=Skin.Ellipsis(LocalInputRouter.Label(free[i]),560,23);
   ShowPopup(seat<0?"Добавить игрока":"Устройство игрока",labels,0,choice=>{if(seat<0)JoinDevice(free[choice]);else AssignDevice(seat,free[choice]);});
  }
  void BeginNameEditor(int seat){nameEditor=seat;editingName=Selected.names[seat];focus="name-key-0";focusRequest="";controls.Clear();}
  void CloseNameEditor(){if(nameEditor>=0){RenameSeat(nameEditor,editingName);focus="name-"+nameEditor;}nameEditor=-1;focusRequest="";controls.Clear();}
  void Change(Action change){change();Selected.Save();Error="";}
  public bool CanStartSelected=>director!=null&&director.Balance!=null&&uint.TryParse(seedText,out _)&&LocalInputRouter.CanBind(Selected,out _);
  bool TryStartShortcut(Gamepad pad){
   if(ScreenState!=RaceMenuScreen.LocalSetup||director==null||director.Started||PopupOpen||nameEditor>=0||EditingSeed||EditingName||!CanStartSelected||pad==null||!pad.added||!pad.buttonWest.wasPressedThisFrame)return false;
   StartSelected();return true;
  }
  public bool Loading {get;private set;}
  public void StartSelected(){
   if(Loading)return;
   if(!uint.TryParse(seedText,out uint seed)){Error="Seed: целое число от 0 до 4294967295";return;}
   Selected.seed=seed.ToString();Selected.Save();
   EditingSeed=false;ClosePopup();controls.Clear();
   if(!Application.isPlaying){if(!director.TryStartSelected(Selected,out var error))Error=error;return;}
   Loading=true;StartCoroutine(PrepareSelected(Selected.Copy()));
  }
  public void RepeatRace(){
   if(Loading)return;
   if(!Application.isPlaying){director.Restart();return;}
   Loading=true;StartCoroutine(PrepareSelected(null));
  }
  IEnumerator PrepareSelected(LocalRaceConfig config){
   var loading=gameObject.AddComponent<RaceLoading>();loading.Status="Готовим гонку";
   yield return RaceLoading.Present();
   try{if(config==null)director.Restart();else if(!director.TryStartSelected(config,out var error))Error=error;}
   finally{loading.enabled=false;Destroy(loading);Loading=false;}
  }
  void Update(){
   if(Loading)return;
   if(pauseBackFrame==Time.frameCount)return;
   frames++;elapsed+=Time.unscaledDeltaTime;if(elapsed>=.5f){fps=Mathf.RoundToInt(frames/elapsed);elapsed=0;frames=0;}
   if(Keyboard.current!=null&&Keyboard.current.f3Key.wasPressedThisFrame){showFps=!showFps;PlayerPrefs.SetInt("StarRacing.ShowFps",showFps?1:0);PlayerPrefs.Save();}
   if(director==null||(director.Started&&!director.Paused&&director.Session.Phase!=RacePhase.Results))return;
   if(ScreenState==RaceMenuScreen.LocalSetup&&!director.Started&&!PopupOpen&&nameEditor<0&&!EditingSeed&&!EditingName){
    foreach(var pad in SessionControllers.Snapshot())if(TryStartShortcut(pad))return;
    foreach(var pad in SessionControllers.Snapshot())if(pad!=null&&pad.buttonNorth.wasPressedThisFrame)JoinDevice(SessionControllers.Slot(pad));
   }
   var k=Keyboard.current;Gamepad p=null;
   foreach(var pad in Gamepad.all)if(pad.buttonSouth.wasPressedThisFrame||pad.buttonEast.wasPressedThisFrame||pad.dpad.up.wasPressedThisFrame||pad.dpad.down.wasPressedThisFrame||pad.dpad.left.wasPressedThisFrame||pad.dpad.right.wasPressedThisFrame){p=pad;break;}
   if(p==null)foreach(var pad in Gamepad.all)if(pad.leftStick.ReadValue().sqrMagnitude>.25f){p=pad;break;}
   if(nameEditor>=0&&k!=null&&k.escapeKey.wasPressedThisFrame){CloseNameEditor();return;}
   if(nameEditor<0&&EditingName){if(k!=null&&(k.enterKey.wasPressedThisFrame||k.escapeKey.wasPressedThisFrame||k.tabKey.wasPressedThisFrame)){focusRequest="";Move(1);}return;}
   bool back=(k!=null&&k.escapeKey.wasPressedThisFrame)||(p!=null&&p.buttonEast.wasPressedThisFrame);
   if(back&&director.Started){if(PauseSettings)return;if(p!=null&&p.buttonEast.wasPressedThisFrame&&director.Paused)director.StartRace();return;}
   if(back){if(nameEditor>=0){CloseNameEditor();return;}if(PopupOpen)ClosePopup();else if(EditingSeed){EditingSeed=false;focusRequest="";}else Open(RaceMenuScreen.Main);return;}
   if(EditingSeed){if(k!=null&&(k.enterKey.wasPressedThisFrame||k.tabKey.wasPressedThisFrame)){EditingSeed=false;focusRequest="";Move(1);}return;}
   if(nameEditor>=0&&GUI.GetNameOfFocusedControl()=="NameEditorInput"){if(k!=null&&k.enterKey.wasPressedThisFrame)CloseNameEditor();return;}
   bool searchFocused=PopupOpen&&popupOptions.Length>7&&GUIFocusIsSearch;
   if(searchFocused)return;
   int move=0,adjust=0;
   if(k!=null){if(k.downArrowKey.wasPressedThisFrame||k.tabKey.wasPressedThisFrame)move=k.shiftKey.isPressed?-1:1;else if(k.upArrowKey.wasPressedThisFrame)move=-1;
    if(k.leftArrowKey.wasPressedThisFrame)adjust=-1;else if(k.rightArrowKey.wasPressedThisFrame)adjust=1;}
   if(p!=null){if(p.dpad.down.wasPressedThisFrame)move=1;else if(p.dpad.up.wasPressedThisFrame)move=-1;
    if(p.dpad.left.wasPressedThisFrame)adjust=-1;else if(p.dpad.right.wasPressedThisFrame)adjust=1;
    float stick=p.leftStick.y.ReadValue();if(Mathf.Abs(stick)>.6f&&Time.unscaledTime>=nextStick){move=stick>0?-1:1;nextStick=Time.unscaledTime+.2f;}if(Mathf.Abs(stick)<.3f)nextStick=0;}
   if(nameEditor>=0&&focus.StartsWith("name-key-")&&int.TryParse(focus.Substring(9),out int key)&&(move!=0||adjust!=0)){
    int length=latinNames?36:43,next=key+move*11+adjust;
    focus=next<0?"name-language":next>=length?(key%11<4?"name-space":key%11<8?"name-delete":"name-done"):"name-key-"+next;move=0;adjust=0;
   }
   if(move!=0)Move(move);
   var current=controls.Find(c=>c.id==focus);
   if(adjust!=0){if(current?.adjust!=null)current.adjust(adjust);else Move(adjust);current=controls.Find(c=>c.id==focus);}
   if((k!=null&&(k.enterKey.wasPressedThisFrame||k.spaceKey.wasPressedThisFrame))||(p!=null&&p.buttonSouth.wasPressedThisFrame)){
    activatingPad=p!=null&&p.buttonSouth.wasPressedThisFrame?p:null;
    try{current?.action?.Invoke();}finally{activatingPad=null;}
   }
  }
  // Run after menu/race transitions, including Update paths that return early.
  void LateUpdate(){
   foreach(var pad in Gamepad.all){
    bool active=pad.leftStick.ReadValue().sqrMagnitude>.25f||pad.rightStick.ReadValue().sqrMagnitude>.25f;
    foreach(var control in pad.allControls)if(control is UnityEngine.InputSystem.Controls.ButtonControl button&&button.wasPressedThisFrame){active=true;break;}
    if(active){gamepadNavigation=true;break;}
   }
   var mouse=Mouse.current;
   if(mouse!=null&&(mouse.delta.ReadValue().sqrMagnitude>.01f||mouse.scroll.ReadValue().sqrMagnitude>.01f||mouse.leftButton.wasPressedThisFrame||mouse.rightButton.wasPressedThisFrame||mouse.middleButton.wasPressedThisFrame))gamepadNavigation=false;
   if(Keyboard.current!=null&&Keyboard.current.anyKey.wasPressedThisFrame)gamepadNavigation=false;
   bool racing=director!=null&&director.Started&&!director.Paused&&director.Session!=null&&director.Session.Phase!=RacePhase.Results;
   Cursor.visible=!applicationFocused||(!racing&&!gamepadNavigation);
  }
  void OnApplicationFocus(bool focused){applicationFocused=focused;if(!focused)Cursor.visible=true;}
  void OnDisable(){Cursor.visible=true;}
  bool GUIFocusIsSearch;
  void Move(int direction){if(controls.Count==0)return;int index=controls.FindIndex(c=>c.id==focus);focus=controls[(index+direction+controls.Count)%controls.Count].id;if(PopupOpen)popupScroll.y=Mathf.Max(0,(controls.FindIndex(c=>c.id==focus)-1)*56-112);}
  void Register(string id,Action action,Action<int> adjust=null){if(collect)controls.Add(new Control{id=id,action=action,adjust=adjust});}
  void Button(string id,Rect r,string text,Action action,bool primary=false,bool enabled=true,int size=23,Action<int> adjust=null){
   if(enabled)Register(id,action,adjust);
   if(Skin.Button(r,text,primary,collect&&focus==id,enabled,size)&&collect){focus=id;action();}
  }
  void Toggle(string id,Rect r,string text,bool value,Action action){
   Skin.Text(new Rect(r.x,r.y,r.width-95,r.height),text,21,true);Skin.Toggle(new Rect(r.xMax-70,r.y+8,64,32),value);
   Register(id,action);if(collect&&focus==id)CloudlineSkin.Box(new Rect(r.x,r.yMax-2,r.width,2),CloudlineSkin.Blue,0);
   if(GUI.Button(r,GUIContent.none,GUIStyle.none)&&collect){focus=id;action();}
  }
  void Dropdown(string id,Rect r,string label,string[] values,int selected,Action<int> choose){
   Skin.Text(new Rect(r.x,r.y,245,r.height),label,21,true);
   Button(id,new Rect(r.x+245,r.y,r.width-245,r.height),values[selected]+"   ▾",()=>ShowPopup(label,values,selected,choose),false,true,18);
  }
  void ShowPopup(string title,string[] values,int selected,Action<int> choose){popupReturnFocus=focus;popupTitle=title;popupOptions=values;popupSelect=choose;popupSearch="";popupScroll=Vector2.zero;focus="option-"+selected;controls.Clear();EditingSeed=false;focusRequest="";}
  void ClosePopup(){string returnFocus=PopupOpen?popupReturnFocus:"back";popupOptions=null;popupSelect=null;popupSearch="";GUIFocusIsSearch=false;controls.Clear();focus=returnFocus;focusRequest="";}
  void OnGUI(){
   if(director==null||director.Started||Loading)return;EnsureSkin();Skin.Backdrop();var old=CloudlineSkin.Begin();
   controls.Clear();collect=!PopupOpen&&nameEditor<0;GUI.enabled=collect;
   if(ScreenState==RaceMenuScreen.Main)DrawMain();else if(ScreenState==RaceMenuScreen.LocalSetup)DrawSetup();else DrawSettings();
   GUI.enabled=true;collect=true;
   if(PopupOpen)DrawPopup();else if(nameEditor>=0)DrawNameEditor();
   if(focusRequest!=null){GUI.FocusControl(focusRequest);focusRequest=null;}
   EditingSeed=GUI.GetNameOfFocusedControl()=="TrackSeed";GUIFocusIsSearch=GUI.GetNameOfFocusedControl()=="ThemeSearch";
   Skin.Footer();DrawFps();
   GUI.matrix=old;
  }
  public void DrawFps(){if(showFps)Skin.Text(new Rect(1450,870,110,24),fps+" FPS",14,false,CloudlineSkin.Muted,TextAnchor.MiddleRight);}
  void DrawMain(){
   Skin.Brand(new Rect(68,78,800,112),76);
   Skin.Panel(new Rect(56,245,690,590),.88f);
   Button("local",new Rect(80,269,642,86),"Игра на одном экране   →",()=>Open(RaceMenuScreen.LocalSetup,activatingPad),true, true,28);
   DisabledMenuButton("network",new Rect(80,371,642,90),"Сетевая игра");
   Button("settings",new Rect(80,477,642,80),"Настройки",()=>Open(RaceMenuScreen.Settings),false,true,26);
   var updater=Distribution.DesktopUpdater.Instance;
   string updateLabel=updater==null?"Обновления доступны в установленной игре":updater.Staged?"Обновление готово · перезапустите игру":updater.Available?"Обновить до "+updater.NewVersion:"Проверить обновления";
   Button("updates",new Rect(80,573,642,72),updateLabel,()=>{if(updater==null)return;if(updater.Staged)updater.InstallAndRestart();else if(updater.Available)_=updater.Download();else _=updater.Check();},false,updater==null||(!updater.Busy&&updater.CanUpdate),22);
   DisabledMenuButton("lab",new Rect(80,661,642,64),"Лаборатория геймдизайна");
   CloudlineSkin.Box(new Rect(80,707,642,1),new Color(.74f,.8f,.89f),0);
   Button("quit",new Rect(80,738,642,72),"Выход",()=>{Selected.Save();Application.Quit();},false,true,26);
  }
  void DisabledMenuButton(string id,Rect rect,string title){
   Button(id,rect,"",()=>{},false,false);
   Skin.Text(new Rect(rect.x+20,rect.y+8,rect.width-40,40),title,25,true,CloudlineSkin.Muted,TextAnchor.MiddleCenter);
   Skin.Text(new Rect(rect.x+20,rect.y+49,rect.width-40,30),"Пока недоступна",16,false,CloudlineSkin.Muted,TextAnchor.MiddleCenter);
  }
  void Header(string title){Button("back",new Rect(56,36,240,54),"‹  Главное меню",()=>Open(RaceMenuScreen.Main),false,true,21);Skin.Brand(new Rect(1170,34,374,60),33);Skin.Text(new Rect(56,111,1430,75),title,48,true);}
  void DrawSetup(){
   Header("Игра на одном экране");
   Skin.Text(new Rect(1040,134,505,34),"Y на геймпаде — присоединиться",18,false,CloudlineSkin.Muted,TextAnchor.MiddleRight);
   for(int i=0;i<4;i++){
    float x=56+i*378;bool active=i<Selected.humans;var accent=CloudlineSkin.PlayerColors[i];Skin.Panel(new Rect(x,212,360,112),active?.93f:.66f);
    if(!active){if(i==Selected.humans)Button("add",new Rect(x+18,244,324,54),"+  Добавить игрока",()=>ChooseDevice(-1),false,true,20);continue;}
    int seat=i;CloudlineSkin.Box(new Rect(x+8,226,5,83),accent,2);
    Skin.Text(new Rect(x+20,231,35,42),(i+1).ToString(),28,true,accent);
    Button("remove-"+seat,new Rect(x+316,219,34,34),"×",()=>RemoveSeat(seat),false,true,24);
    Register("name-"+seat,()=>BeginNameEditor(seat));
    string playerName=Skin.Field(new Rect(x+66,226,241,39),Selected.names[seat],"PlayerName"+seat,LocalRaceConfig.MaxNameLength,collect&&focus=="name-"+seat);
    if(playerName!=Selected.names[seat])RenameSeat(seat,playerName);
    string deviceName=Skin.Ellipsis(LocalInputRouter.Label(Selected.devices[seat]),266,16);
    Button("device-"+seat,new Rect(x+57,276,288,36),deviceName+" ▾",()=>ChooseDevice(seat),false,true,16);
   }
   Skin.Panel(new Rect(56,352,704,448));
   Skin.Text(new Rect(84,371,470,32),"Участников всего",22,true);
   for(int i=0;i<LocalRaceConfig.ParticipantCounts.Length;i++){int count=LocalRaceConfig.ParticipantCounts[i];Button("count-"+count,new Rect(84+i*161,411,149,46),count.ToString(),()=>Change(()=>Selected.entrants=count),Selected.entrants==count);}
   Toggle("handicap",new Rect(84,475,648,46),"Высокая сложность ИИ",Selected.handicap,()=>Change(()=>Selected.handicap=!Selected.handicap));
   Dropdown("theme",new Rect(84,534,648,46),"Тема трассы",LocalRaceConfig.ThemeNames,Array.IndexOf(LocalRaceConfig.ThemeIds,Selected.theme),index=>Change(()=>Selected.theme=LocalRaceConfig.ThemeIds[index]));
   Dropdown("rails",new Rect(84,593,648,46),"Ограждения",LocalRaceConfig.RailNames,Array.IndexOf(LocalRaceConfig.RailIds,Selected.rails),index=>Change(()=>Selected.rails=LocalRaceConfig.RailIds[index]));
   Toggle("jumps",new Rect(84,652,648,46),"Трамплины и пропасти",Selected.jumps,()=>Change(()=>Selected.jumps=!Selected.jumps));
   Skin.Text(new Rect(84,723,220,44),"Seed",21,true);
   Register("seed",()=>{focusRequest="TrackSeed";EditingSeed=true;});
   string value=Skin.Field(new Rect(330,723,344,44),seedText,"TrackSeed",10,collect&&focus=="seed");
   if(value!=seedText){seedText=value;if(uint.TryParse(value,out var parsed))Change(()=>Selected.seed=parsed.ToString());}
   Button("seed-dice",new Rect(684,723,48,44),"",RollSeed);
   foreach(var point in new[]{new Vector2(696,734),new Vector2(718,734),new Vector2(707,745),new Vector2(696,756),new Vector2(718,756)})CloudlineSkin.Box(new Rect(point.x-2,point.y-2,4,4),CloudlineSkin.Ink,2);
   var artRect=new Rect(792,352,752,448);Skin.Panel(artRect,1);
   var preview=Selected.theme=="random"?Skin.RandomPreview:Selected.theme=="cloud-city"?Skin.CloudPreview:Skin.SpacePreview;
   if(preview!=null)GUI.DrawTexture(new Rect(800,360,736,432),preview,ScaleMode.ScaleAndCrop);
   string validation=uint.TryParse(seedText,out _)?Error:"Seed: целое число от 0 до 4294967295";
   if(validation.Length==0&&!LocalInputRouter.CanBind(Selected,out var deviceError))validation=deviceError;
   if(director.Balance==null)validation=director.BalanceError;
   if(validation.Length>0)Skin.Text(new Rect(60,805,940,48),validation,18,true,new Color(.67f,.12f,.14f));
   Button("start",new Rect(1078,817,466,60),"Старт!",StartSelected,true,CanStartSelected,27);
   var shortcutColor=CanStartSelected?Color.white:CloudlineSkin.Muted;
   CloudlineSkin.Box(new Rect(1100,830,34,34),CloudlineSkin.Alpha(shortcutColor,.22f),17);
   Skin.Text(new Rect(1100,830,34,34),"X",23,true,shortcutColor,TextAnchor.MiddleCenter);
  }
  void DrawSettings(){Header("Настройки");DrawSettingsContent();}
  public void DrawPauseSettings(){
   collect=!PopupOpen;GUI.enabled=collect;
   Button("settings-back",new Rect(56,36,300,54),"‹  К меню паузы",ClosePauseSettings,false,true,21);
   Skin.Text(new Rect(56,111,1430,75),"Настройки",48,true);DrawSettingsContent();
   GUI.enabled=true;collect=true;if(PopupOpen)DrawPopup();GUIFocusIsSearch=GUI.GetNameOfFocusedControl()=="ThemeSearch";
  }
  void DrawSettingsContent(){
   Skin.Panel(new Rect(56,223,698,634),1);Skin.Panel(new Rect(784,223,760,576),1);
   Skin.Text(new Rect(88,242,500,40),"Экран",28,true);
   Toggle("fullscreen",new Rect(88,288,634,44),"Полный экран",Display.fullscreen,()=>{Display.fullscreen=!Display.fullscreen;ApplyDisplay();});
   var labels=new string[displayOptions.Length];labels[0]="Авто ("+nativeSize.x+" × "+nativeSize.y+")";for(int i=1;i<labels.Length;i++)labels[i]=displayOptions[i].x+" × "+displayOptions[i].y;
   Dropdown("resolution",new Rect(88,341,634,44),"Разрешение",labels,Mathf.Max(0,Display.Selected(displayOptions)),index=>{Display.width=displayOptions[index].x;Display.height=displayOptions[index].y;ApplyDisplay();});
   Skin.Text(new Rect(88,404,500,40),"Звук",28,true);DrawAudio(new Rect(88,454,634,256));
   Toggle("fps",new Rect(88,733,634,44),"Показывать FPS",showFps,()=>{showFps=!showFps;PlayerPrefs.SetInt("StarRacing.ShowFps",showFps?1:0);PlayerPrefs.Save();});
   Toggle("nitro-blur",new Rect(88,789,634,44),"Размытие на нитро",NitroBlurSettings.Enabled,()=>NitroBlurSettings.Enabled=!NitroBlurSettings.Enabled);
   var guide=controlPage==0?Skin.KeyboardGuide:Skin.GamepadGuide;
   if(guide!=null)GUI.DrawTexture(new Rect(792,231,744,560),guide,ScaleMode.ScaleToFit);
   Button("controls-prev",new Rect(1040,814,60,48),"‹",()=>controlPage=1-controlPage,false,true,30);
   Skin.Text(new Rect(1114,814,100,48),(controlPage+1)+" / 2",19,true,CloudlineSkin.Muted,TextAnchor.MiddleCenter);
   Button("controls-next",new Rect(1228,814,60,48),"›",()=>controlPage=1-controlPage,false,true,30);
  }
  public bool ResultsScrollFocused=>focus=="results-list";
  public void RegisterResultsScroll(Action<int> scroll){Register("results-list",()=>{},scroll);}
  public void BeginOverlay(string initialFocus,bool reset){EnsureSkin();controls.Clear();collect=true;if(reset)focus=initialFocus;}
  public void OverlayButton(string id,Rect r,string text,Action action,bool primary=false,bool enabled=true){Button(id,r,text,action,primary,enabled);}
  public void DrawAudio(Rect r){
   var audio=director.GetComponent<RaceAudioCoordinator>();if(audio==null)return;
   Toggle("mute",new Rect(r.x,r.y,r.width,40),"Без звука",audio.Muted,()=>audio.SetMix(!audio.Muted,audio.MusicVolume,audio.EnginesVolume,audio.EffectsVolume));
   AudioSlider("music",r.x,r.y+48,r.width,"Музыка",audio.MusicVolume,v=>audio.SetMix(audio.Muted,v,audio.EnginesVolume,audio.EffectsVolume));
   AudioSlider("engine",r.x,r.y+118,r.width,"Двигатели",audio.EnginesVolume,v=>audio.SetMix(audio.Muted,audio.MusicVolume,v,audio.EffectsVolume));
   AudioSlider("effects",r.x,r.y+188,r.width,"Эффекты",audio.EffectsVolume,v=>audio.SetMix(audio.Muted,audio.MusicVolume,audio.EnginesVolume,v));
  }
  void AudioSlider(string id,float x,float y,float width,string name,float value,Action<float> set){
   Skin.Text(new Rect(x,y,width-80,30),name,22,true);Skin.Text(new Rect(x+width-80,y,80,30),Mathf.RoundToInt(value*100)+"%",19,false,CloudlineSkin.Muted,TextAnchor.MiddleRight);
   var track=new Rect(x,y+43,width,10);CloudlineSkin.Box(track,new Color(.81f,.85f,.91f),5);CloudlineSkin.Box(new Rect(x,y+43,width*value,10),CloudlineSkin.Blue,5);
   CloudlineSkin.Box(new Rect(x+width*value-9,y+39,18,18),CloudlineSkin.Blue,9);
   Register(id,()=>{},delta=>set(Mathf.Clamp01(value+delta*.05f)));
   if(focus==id&&collect)CloudlineSkin.Box(new Rect(x,y+65,width,2),CloudlineSkin.Blue,0);
   var hit=new Rect(x,y+31,width,36);var e=Event.current;if(collect&&(e.type==EventType.MouseDown||e.type==EventType.MouseDrag)&&hit.Contains(e.mousePosition)){focus=id;set(Mathf.Clamp01((e.mousePosition.x-x)/width));e.Use();}
  }
  void DrawNameEditor(){
   CloudlineSkin.Box(new Rect(0,0,1600,900),new Color(.035f,.08f,.18f,.35f),0);Skin.Panel(new Rect(350,170,900,560));
   Skin.Text(new Rect(390,195,590,50),"Имя игрока · до 10 символов",28,true);
   editingName=Skin.FitName(Skin.Field(new Rect(390,260,560,48),editingName,"NameEditorInput",LocalRaceConfig.MaxNameLength));
   Button("name-language",new Rect(980,260,220,48),latinNames?"EN → РУ":"РУ → EN",()=>{latinNames=!latinNames;focusRequest="";});
   string alphabet=latinNames?"ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789":"АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ0123456789";
   for(int i=0;i<alphabet.Length;i++){char letter=alphabet[i];Button("name-key-"+i,new Rect(390+(i%11)*74,336+(i/11)*60,66,50),letter.ToString(),()=>{editingName=Skin.FitName(editingName+letter);focusRequest="";});}
   Button("name-space",new Rect(390,602,250,56),"Пробел",()=>editingName=Skin.FitName(editingName+" "));
   Button("name-delete",new Rect(662,602,250,56),"⌫",()=>{if(editingName.Length>0)editingName=editingName.Substring(0,editingName.Length-1);});
   Button("name-done",new Rect(934,602,266,56),"Готово",CloseNameEditor,true);
  }
  void DrawPopup(){
   if(popupOptions==null)return;
   float searchHeight=popupOptions.Length>7?54:0;
   float listHeight=Mathf.Min(368,popupOptions.Length*64+16);
   var panel=new Rect(400,(900-(126+searchHeight+listHeight))/2,800,126+searchHeight+listHeight);
   if(Event.current.type==EventType.MouseDown&&!panel.Contains(Event.current.mousePosition)){ClosePopup();Event.current.Use();return;}
   CloudlineSkin.Box(new Rect(0,0,1600,900),new Color(.035f,.08f,.18f,.44f),0);
   Skin.Panel(panel,1);Skin.Text(new Rect(panel.x+36,panel.y+20,650,56),popupTitle,30,true);
   Button("close",new Rect(panel.xMax-86,panel.y+23,50,50),"×",ClosePopup);
   if(popupOptions==null)return;
   float top=panel.y+94;
   if(searchHeight>0){popupSearch=Skin.Field(new Rect(panel.x+36,top,728,42),popupSearch,"ThemeSearch",80);top+=54;}
   var options=popupOptions;var matches=new List<int>();for(int i=0;i<options.Length;i++)if(options[i].IndexOf(popupSearch,StringComparison.OrdinalIgnoreCase)>=0)matches.Add(i);
   popupScroll=CloudlineSkin.BeginScrollView(new Rect(panel.x+28,top,744,listHeight),popupScroll,new Rect(0,0,722,Mathf.Max(listHeight,matches.Count*64+16)));
   for(int i=0;i<matches.Count;i++){int choice=matches[i];Button("option-"+choice,new Rect(8,8+i*64,706,52),options[choice],()=>{var select=popupSelect;ClosePopup();select(choice);},false,true,23);}
   CloudlineSkin.EndScrollView();
  }
 }
}
