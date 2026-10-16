using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 public sealed partial class RaceMenu {
  public LabLibrary Laboratory=>DesignLabRuntime.Current;
  string labQuery="",labGroup=LabParameters.Groups[0],labDialog,labText="",labField,labMessage="",labValidation="",labFingerprint="";
  Vector2 labScroll,labDiffScroll,labDialogScroll;Action labPending;bool labBypass,labTextEditing;int labDialogFrame;
  struct LabDialogReturn {public string Kind,Focus;public Vector2 Scroll;}
  readonly Stack<LabDialogReturn> labDialogParents=new Stack<LabDialogReturn>();string labReturnFocus="lab-profile";
  bool LabModal=>ScreenState==RaceMenuScreen.Lab&&labDialog!=null;
  public bool LabDirty=>Laboratory.Draft?.Dirty??false;
  public string LabDialog=>labDialog;
  void LabTry(Action action){try{labMessage="";labFingerprint="";action();}catch(Exception e){labMessage=LabLibrary.Describe(e);}}
  void LabGuard(Action action){if(labDialog=="dirty")return;if(!LabDirty){LabTry(action);return;}labPending=action;LabShow("dirty");}
  void LabShow(string name){labMessage="";if(labDialog!=null)labDialogParents.Push(new LabDialogReturn{Kind=labDialog,Focus=focus,Scroll=labDialogScroll});else labReturnFocus=focus;labDialog=name;labDialogFrame=Time.frameCount;labDialogScroll=Vector2.zero;controls.Clear();focus="lab-dialog-cancel";focusRequest="";labTextEditing=false;}
  void LabClose(){labPending=null;if(labDialogParents.Count>0){var parent=labDialogParents.Pop();labDialog=parent.Kind;focus=parent.Focus;labDialogScroll=parent.Scroll;}else{labDialog=null;focus=labReturnFocus;}controls.Clear();focusRequest="";labTextEditing=false;}
  void LabCloseAll(){labDialogParents.Clear();LabClose();}
  void LabLeave(RaceMenuScreen screen,Gamepad source){LabGuard(()=>{labBypass=true;try{Open(screen,source);}finally{labBypass=false;}});}
  public void GuardExit(Action action){if(!LabDirty){action();return;}ScreenState=RaceMenuScreen.Lab;LabGuard(action);}
  bool LabQuit(){if(!LabDirty||labBypass)return true;ScreenState=RaceMenuScreen.Lab;LabGuard(()=>{labBypass=true;Application.Quit();});return false;}
  void OnDestroy(){CancelLoading();Application.wantsToQuit-=LabQuit;}
  bool LabInput(Keyboard keyboard,Gamepad pad){
   if(ScreenState!=RaceMenuScreen.Lab)return false;
   bool back=(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)||(pad!=null&&pad.buttonEast.wasPressedThisFrame);
   if(LabModal&&back){LabClose();return true;}
   if(LabModal&&Time.frameCount<=labDialogFrame)return true;
   if(labTextEditing&&pad!=null){focusRequest="";labTextEditing=false;return false;}
   if(labTextEditing){if(keyboard!=null&&(keyboard.escapeKey.wasPressedThisFrame||keyboard.enterKey.wasPressedThisFrame||keyboard.tabKey.wasPressedThisFrame)){focusRequest="";labTextEditing=false;}return true;}
   return false;
  }
  void ValidateLab(){
   var draft=Laboratory.Draft;if(draft==null){labValidation="Выберите корректную сохранённую версию.";return;}
   string fingerprint=string.Join("|",LabParameters.All.Select(p=>draft.Inputs[p.Key]));if(fingerprint==labFingerprint)return;labFingerprint=fingerprint;
   try{draft.Build();labValidation="";}catch(Exception e){labValidation=e.Message;}
  }
  void LabStep(LabParameter p,int direction){var draft=Laboratory.Draft;if(!p.TryValue(draft.Inputs[p.Key],out var value)){labMessage=p.Name+": исправьте ввод перед изменением шага";return;}decimal next=value+direction*p.Step;if(!p.TryValue(p.Format(next),out _)){labMessage=p.Name+": граница диапазона "+p.Min+"–"+p.Max;return;}draft.Inputs[p.Key]=p.Format(next);labMessage="";}
  void LabEdit(LabParameter p){labField=p.Key;LabShow("number");focus="lab-number-input";}
  public static string LabSidebarLabel(string group,int changes)=>group+(changes>0?" · "+changes:"");
  void DrawLab(){
   Header("Лаборатория геймдизайна");ValidateLab();var lab=Laboratory;var selected=lab.Selected;
   Button("lab-profile",new Rect(56,193,310,52),Skin.Ellipsis(selected?.ProfileName??"Выберите профиль",260-Skin.LabelWidth(ButtonHint("lab-profile"),20),Mathf.RoundToInt(20*UserSettingsStore.Current.textScale))+" ▾",()=>LabShow("profiles"),false,true,20);
   Button("lab-version",new Rect(382,193,216,52),selected==null?"Версия ▾":"Версия "+selected.Version+" ▾",()=>LabShow("versions"),false,selected!=null,19);
   Skin.Text(new Rect(620,193,320,52),lab.Author?"Автор · файлы проекта":selected!=null&&lab.Editable(selected.ProfileId)?"Свой профиль":"Из поставки · защищён",17,false,CloudlineSkin.Muted);
   Button("lab-copy",new Rect(1006,193,252,52),"Копировать версию",()=>LabGuard(()=>{labText="Мой эксперимент";LabShow("copy");}),false,selected!=null,18);
   Button("lab-default",new Rect(1274,193,270,52),"На релизный Default",()=>LabGuard(()=>{lab.Reset();labScroll=Vector2.zero;}),false,true,18);
   Skin.Panel(new Rect(56,265,270,487),1);Skin.Panel(new Rect(350,265,810,487),1);Skin.Panel(new Rect(1184,265,360,487),1);
   Skin.Text(new Rect(76,281,230,28),"Поиск по всем группам",16,true);
   string query=Skin.Field(new Rect(76,317,230,40),labQuery,"LabSearch",60,focus=="lab-search");if(query!=labQuery){labQuery=query;labScroll=Vector2.zero;}
   Register("lab-search",()=>{if(gamepadNavigation||activatingPad!=null){labText=labQuery;LabShow("search");}else{focusRequest="LabSearch";labTextEditing=true;}},null,new Rect(76,317,230,40));
   if(!string.IsNullOrEmpty(labQuery))Button("lab-search-clear",new Rect(76,365,230,36),"Очистить поиск",()=>{labQuery="";labScroll=Vector2.zero;},false,true,17);
   for(int i=0;i<LabParameters.Groups.Length;i++){string group=LabParameters.Groups[i];int count=lab.Draft?.Changes.Count(p=>p.Group==group)??0;Button("lab-group-"+i,new Rect(76,414+i*51,230,42),LabSidebarLabel(group,count),()=>{labGroup=group;labQuery="";labScroll=Vector2.zero;},labGroup==group&&labQuery=="",true,18);if(gamepadNavigation&&focus=="lab-group-"+i)Skin.Text(new Rect(307,426+i*51,18,18),PadGlyph("south")=="Нижняя"?"●":PadGlyph("south"),12,true,CloudlineSkin.Blue,TextAnchor.MiddleCenter);}
   Skin.Text(new Rect(76,634,230,85),"23 действующих поля\nГруппы и поиск сохраняют черновик",16,false,CloudlineSkin.Muted,TextAnchor.UpperLeft,true);
   var fields=(string.IsNullOrWhiteSpace(labQuery)?LabParameters.All.Where(p=>p.Group==labGroup):LabParameters.Search(labQuery)).ToArray();
   Skin.Text(new Rect(374,280,762,38),string.IsNullOrWhiteSpace(labQuery)?labGroup:"Поиск · найдено "+fields.Length,25,true);
   var viewport=new Rect(374,332,762,349);labScroll=CloudlineSkin.BeginScrollView(viewport,labScroll,new Rect(0,0,735,Mathf.Max(349,fields.Length*128)));
   if(fields.Length==0)Skin.Text(new Rect(8,20,710,60),"Ничего не найдено. Измените или очистите поиск.",21,false,CloudlineSkin.Muted,TextAnchor.UpperLeft,true);
   for(int i=0;i<fields.Length;i++)DrawLabField(fields[i],i*128,viewport,labScroll);
   CloudlineSkin.EndScrollView();
   Button("lab-fields-up",new Rect(374,697,180,38),"↑ Выше",()=>labScroll.y=Mathf.Max(0,labScroll.y-256),false,true,17);
   Button("lab-fields-down",new Rect(566,697,180,38),"Ниже ↓",()=>labScroll.y+=256,false,true,17);
   Skin.Text(new Rect(1206,284,316,35),"Черновик · "+(lab.Draft?.Changes.Count()??0),22,true);
   Skin.Text(new Rect(1206,326,316,50),selected==null?"Нет выбранной базы":"База: версия "+selected.Version,18,false,CloudlineSkin.Muted,TextAnchor.UpperLeft,true);
   DrawLabDiff(new Rect(1206,380,316,210),false);
   Skin.Text(new Rect(1206,603,316,90),lab.Active==null?"Следующая гонка:\nнет активной версии":"Следующая гонка:\n"+Skin.Ellipsis(lab.Active.ProfileName,300,Mathf.RoundToInt(17*UserSettingsStore.Current.textScale))+"\nВерсия "+lab.Active.Version,17,true,null,TextAnchor.UpperLeft,true);
   Button("lab-diff-up",new Rect(1206,696,144,38),"↑ Выше",()=>labDiffScroll.y=Mathf.Max(0,labDiffScroll.y-150),false,true,17);
   Button("lab-diff-down",new Rect(1360,696,162,38),"Ниже ↓",()=>labDiffScroll.y+=150,false,true,17);
   Button("lab-test",new Rect(56,792,270,52),"Подготовить гонку →",()=>LabLeave(RaceMenuScreen.LocalSetup,null),true,lab.Active!=null,18);
   Button("lab-manage",new Rect(350,792,235,52),"Профиль и история",()=>LabShow("manage"),false,selected!=null,18);
   Button("lab-discard",new Rect(954,792,268,52),"Отменить черновик",()=>LabGuard(()=>lab.Discard()),false,LabDirty,18);
   Button("lab-save",new Rect(1238,792,306,52),"Сохранить новую версию",()=>LabTry(()=>{lab.Save();labMessage="Версия сохранена: "+lab.Destination+". Git commit/push выполняются отдельно.";}),true,selected!=null&&lab.Editable(selected.ProfileId)&&LabDirty&&labValidation=="",18);
   string message=!string.IsNullOrEmpty(labMessage)?labMessage:lab.Warning!=""?lab.Warning:lab.CatalogError!=""?lab.CatalogError:labValidation!=""?labValidation:selected!=null&&!lab.Editable(selected.ProfileId)?"Профиль поставки защищён. Создайте копию для редактирования.":"Изменения применятся после сохранения к следующей гонке.";
   Skin.Text(new Rect(56,755,1488,34),message,15,false,CloudlineSkin.Ink,TextAnchor.MiddleLeft,true);
  }
  void LabScrollButton(string id,Rect r,string text,Action action,Action<int> adjust,Rect viewport,Vector2 scroll,int size=18){
   Button(id,r,text,action,false,true,size,adjust);if(collect&&controls.Count>0){var c=controls[controls.Count-1];c.area.position+=viewport.position-scroll;}
  }
  void DrawLabField(LabParameter p,float y,Rect viewport,Vector2 scroll){
   var draft=Laboratory.Draft;if(draft==null)return;bool editable=Laboratory.Selected!=null&&Laboratory.Editable(Laboratory.Selected.ProfileId);
   Skin.Text(new Rect(0,y,480,34),p.Name,21,true);Skin.Text(new Rect(0,y+38,485,45),p.Description+(labQuery!=""?" · "+p.Group:""),15,false,CloudlineSkin.Muted,TextAnchor.UpperLeft,true);
   Skin.Text(new Rect(0,y+86,720,30),p.Key+" · "+p.Min+"–"+p.Max+" · шаг "+p.Step,14,false,CloudlineSkin.Muted);
   string id="lab-field-"+p.Key;
   if(editable){
    string value=Skin.Field(new Rect(500,y,142,40),draft.Inputs[p.Key],"LabValue-"+p.Key,24,focus==id);if(value!=draft.Inputs[p.Key])draft.Inputs[p.Key]=value;
    Register(id,()=>LabEdit(p),d=>LabStep(p,d),new Rect(viewport.x+500,viewport.y+y-scroll.y,142,40));
    LabScrollButton(id+"-minus",new Rect(500,y+52,62,38),"−",()=>LabStep(p,-1),null,viewport,scroll);
    LabScrollButton(id+"-plus",new Rect(580,y+52,62,38),"+",()=>LabStep(p,1),null,viewport,scroll);
   }else Skin.Text(new Rect(500,y,142,40),draft.Inputs[p.Key],22,true);
   Skin.Text(new Rect(650,y,80,40),p.Unit,17,false,CloudlineSkin.Muted);
   if(!p.TryValue(draft.Inputs[p.Key],out _))Skin.Text(new Rect(500,y+96,230,26),"Исправьте значение",14,true,new Color(.65f,.18f,.13f));
  }
  void DrawLabDiff(Rect area,bool dialog){
   var changes=Laboratory.Draft?.Changes.ToArray()??new LabParameter[0];Vector2 scroll=dialog?labDialogScroll:labDiffScroll;
   scroll=CloudlineSkin.BeginScrollView(area,scroll,new Rect(0,0,area.width-24,Mathf.Max(area.height,changes.Length*94)));
   for(int i=0;i<changes.Length;i++){var p=changes[i];Skin.Text(new Rect(0,i*94,area.width-28,50),p.Group+" / "+p.Name,16,true,null,TextAnchor.UpperLeft,true);Skin.Text(new Rect(0,i*94+54,area.width-28,34),Laboratory.Draft.OldValue(p)+" → "+Laboratory.Draft.Inputs[p.Key]+" "+p.Unit,17,false,CloudlineSkin.Muted);}
   if(changes.Length==0)Skin.Text(new Rect(0,0,area.width-24,60),"Нет несохранённых изменений",17,false,CloudlineSkin.Muted,TextAnchor.UpperLeft,true);
   CloudlineSkin.EndScrollView();if(dialog)labDialogScroll=scroll;else labDiffScroll=scroll;
  }
  void DrawLabDialog(){
   controls.Clear();GUI.enabled=true;collect=true;CloudlineSkin.Box(new Rect(0,0,1600,900),new Color(.035f,.08f,.18f,.45f),0);Skin.Panel(new Rect(300,225,1000,520),1);
   string title=labDialog=="dirty"?"Сбросить несохранённые изменения?":labDialog=="profiles"?"Профили":labDialog=="versions"?"Сохранённые версии":labDialog=="copy"?"Копировать конкретную версию":labDialog=="rename"?"Переименовать профиль":labDialog=="delete"?"Удалить профиль и всю его историю?":labDialog=="search"?"Поиск по всем группам":labDialog=="number"?LabParameters.All.Single(p=>p.Key==labField).Name:"Профиль и история";
   Skin.Text(new Rect(334,247,932,52),title,29,true);var lab=Laboratory;var selected=lab.Selected;
   if(labDialog=="dirty"){
    Skin.Text(new Rect(334,311,932,44),"Эти изменения сбросятся. Сохранённые версии останутся в истории.",19,false,CloudlineSkin.Muted);
    DrawLabDiff(new Rect(334,369,932,230),true);
    Button("lab-dirty-up",new Rect(334,613,142,38),"↑ Выше",()=>labDialogScroll.y=Mathf.Max(0,labDialogScroll.y-150),false,true,16);Button("lab-dirty-down",new Rect(490,613,150,38),"Ниже ↓",()=>labDialogScroll.y+=150,false,true,16);
    Button("lab-dialog-cancel",new Rect(688,677,310,46),"Продолжить редактирование",LabClose,false,true,17);
    Button("lab-dirty-confirm",new Rect(1014,677,252,46),"Сбросить и продолжить",()=>{var action=labPending;lab.Discard();LabClose();LabTry(action);},true,true,17);return;
   }
   if(labDialog=="profiles"||labDialog=="versions"){
    var list=labDialog=="profiles"?lab.Profiles.Select(p=>(p.id,p.versions.Max(v=>v.number),p.name+" · "+(lab.Editable(p.id)?"свой/автор":"из поставки")+" · "+p.id.Substring(0,8))).ToArray():lab.Profiles.Single(p=>p.id==selected.ProfileId).versions.OrderByDescending(v=>v.number).Select(v=>(selected.ProfileId,v.number,"Версия "+v.number+" · "+(DateTimeOffset.TryParse(v.createdUtc,out var date)?date.ToLocalTime().ToString("dd.MM.yyyy"):"дата неизвестна")+(lab.Marked(selected.ProfileId,v.number)?" · релизная":""))).ToArray();
    var viewport=new Rect(334,315,932,250);labDialogScroll=CloudlineSkin.BeginScrollView(viewport,labDialogScroll,new Rect(0,0,905,Mathf.Max(250,list.Length*62)));
    for(int i=0;i<list.Length;i++){var choice=list[i];LabScrollButton("lab-choice-"+i,new Rect(0,i*62,900,50),choice.Item3,()=>LabGuard(()=>{lab.Select(choice.Item1,choice.Item2);LabCloseAll();labScroll=Vector2.zero;}),null,viewport,labDialogScroll,20);}CloudlineSkin.EndScrollView();
    Button("lab-list-up",new Rect(334,580,150,38),"↑ Выше",()=>labDialogScroll.y=Mathf.Max(0,labDialogScroll.y-200),false,true,16);Button("lab-list-down",new Rect(500,580,150,38),"Ниже ↓",()=>labDialogScroll.y+=200,false,true,16);
   }else if(labDialog=="copy"||labDialog=="rename"||labDialog=="search"){
    Skin.Text(new Rect(334,315,932,60),labDialog=="search"?"Группа, название, описание или ключ":"Источник: "+selected.Label,19,true,null,TextAnchor.UpperLeft,true);labText=Skin.Field(new Rect(334,381,932,52),labText,"LabName",40,focus=="lab-name");Register("lab-name",()=>{focusRequest="LabName";labTextEditing=true;},null,new Rect(334,381,932,52));
    // The established on-screen keyboard also works with a pad; its limit is specific to this profile dialog.
    DrawLabNameKeys(new Rect(334,450,932,150));
    Button("lab-name-save",new Rect(970,677,296,46),labDialog=="search"?"Найти":labDialog=="copy"?"Создать копию":"Переименовать",()=>LabTry(()=>{if(labDialog=="search"){labQuery=labText;labScroll=Vector2.zero;}else if(labDialog=="copy")lab.Copy(selected.ProfileId,selected.Version,labText);else lab.Rename(selected.ProfileId,labText);if(labDialog=="copy")LabCloseAll();else LabClose();}),true,true,19);
   }else if(labDialog=="number"){
    var p=LabParameters.All.Single(x=>x.Key==labField);Skin.Text(new Rect(334,310,932,42),p.Min+"–"+p.Max+" "+p.Unit+" · шаг "+p.Step,20,false,CloudlineSkin.Muted);
    lab.Draft.Inputs[p.Key]=Skin.Field(new Rect(334,366,932,50),lab.Draft.Inputs[p.Key],"LabNumber",24,focus=="lab-number-input");Register("lab-number-input",()=>{focusRequest="LabNumber";labTextEditing=true;},null,new Rect(334,366,932,50));
    string[] keys={"1","2","3","4","5","6","7","8","9","0",",","−","⌫","Очистить"};for(int i=0;i<keys.Length;i++){string key=keys[i];Button("lab-key-"+i,new Rect(334+i%7*133,444+i/7*61,120,48),key,()=>{string v=lab.Draft.Inputs[p.Key];lab.Draft.Inputs[p.Key]=key=="⌫"?(v.Length==0?v:v.Substring(0,v.Length-1)):key=="Очистить"?"":key=="−"?(v.StartsWith("-")?v.Substring(1):"-"+v):v.Length<23?v+key:v;},false,true,20);}
    Button("lab-number-done",new Rect(970,677,296,46),"Готово",()=>{if(p.TryValue(lab.Draft.Inputs[p.Key],out _))LabClose();else labMessage="Исправьте значение: "+p.Min+"–"+p.Max+", шаг "+p.Step;},true,true,20);
   }else if(labDialog=="delete"){
    Skin.Text(new Rect(334,322,932,146),"Будут удалены профиль «"+selected.ProfileName+"» и все его версии. Отменить действие нельзя. Если профиль активен, следующая гонка вернётся к релизному Default.",23,false,CloudlineSkin.Ink,TextAnchor.UpperLeft,true);
    Button("lab-delete-confirm",new Rect(970,677,296,46),"Удалить профиль",()=>LabTry(()=>{lab.Delete(selected.ProfileId);LabCloseAll();}),true,lab.CanDelete(selected.ProfileId),20);
   }else{
    Skin.Text(new Rect(334,315,932,60),selected.Label+" · "+(lab.Editable(selected.ProfileId)?"редактирование доступно":"профиль поставки защищён"),19,true,null,TextAnchor.UpperLeft,true);
    Button("lab-rename",new Rect(334,382,440,48),"Переименовать",()=>{labText=selected.ProfileName;LabShow("rename");},false,lab.Editable(selected.ProfileId),20);
    Button("lab-delete",new Rect(796,382,470,48),"Удалить свой профиль",()=>LabGuard(()=>LabShow("delete")),false,lab.CanDelete(selected.ProfileId),20);
    Button("lab-release-mark",new Rect(334,453,932,48),lab.Marked(selected.ProfileId,selected.Version)?"✓ Включена в релиз — снять отметку":"Включить выбранную версию в релиз",()=>LabTry(()=>lab.Mark(selected.ProfileId,selected.Version,!lab.Marked(selected.ProfileId,selected.Version))),false,lab.Author,20);
    Skin.Text(new Rect(334,522,932,95),lab.Author?"Отметка меняет будущий состав пакета. Текущая активная версия не меняется. Файлы проекта: "+lab.Destination+". Git commit, сборка и публикация выполняются отдельно.":"В установленной игре релизные отметки и профили поставки защищены. Можно скопировать любую сохранённую версию в свой профиль.",19,false,CloudlineSkin.Muted,TextAnchor.UpperLeft,true);
   }
   Skin.Text(new Rect(334,620,932,44),labMessage,16,false,CloudlineSkin.Ink,TextAnchor.MiddleLeft,true);
   Button("lab-dialog-cancel",new Rect(334,677,280,46),"Назад",LabClose,false,true,20);
  }
  void DrawLabNameKeys(Rect area){
   string alphabet=latinNames?"ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789":"АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ0123456789";float width=area.width/15;
   for(int i=0;i<alphabet.Length;i++){char key=alphabet[i];Button("lab-letter-"+i,new Rect(area.x+i%15*width,area.y+i/15*42,width-5,36),key.ToString(),()=>{if(labText.Length<40)labText+=key;},false,true,17);}
   Button("lab-name-language",new Rect(area.x,area.y+132,170,36),latinNames?"АБВ":"ABC",()=>latinNames=!latinNames,false,true,17);Button("lab-name-space",new Rect(area.x+186,area.y+132,300,36),"Пробел",()=>{if(labText.Length<40)labText+=" ";},false,true,17);Button("lab-name-delete",new Rect(area.x+502,area.y+132,170,36),"⌫",()=>{if(labText.Length>0)labText=labText.Substring(0,labText.Length-1);},false,true,17);
  }
 }
}
