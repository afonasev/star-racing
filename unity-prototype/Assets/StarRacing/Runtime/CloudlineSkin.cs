using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype {
 public sealed class CloudlineSkin {
  public static readonly Color Ink=new Color(.035f,.085f,.19f),Muted=new Color(.37f,.44f,.56f),Blue=new Color(.08f,.35f,.98f);
  public static readonly Color[] PlayerColors={new Color(.1f,.4f,1),new Color(.94f,.36f,.12f),new Color(0,.62f,.69f),new Color(.48f,.23f,.9f)};
  public readonly Texture2D Background,CloudPreview,SpacePreview,RandomPreview,KeyboardGuide,GamepadGuide;
  readonly GUIStyle normal,bold,field;
  public CloudlineSkin(){
   Background=Resources.Load<Texture2D>("UI/CloudlineBackground");
   RandomPreview=Resources.Load<Texture2D>("UI/ThemeRandom");CloudPreview=Resources.Load<Texture2D>("UI/ThemeCloudCity");SpacePreview=Resources.Load<Texture2D>("UI/ThemeSpaceStation");KeyboardGuide=Resources.Load<Texture2D>("UI/ControlsKeyboard");GamepadGuide=Resources.Load<Texture2D>("UI/ControlsGamepad");
   var regular=Resources.Load<Font>("UI/NotoSans-Regular")??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
   var heavy=Resources.Load<Font>("UI/NotoSans-Bold")??regular;
   normal=new GUIStyle{font=regular,wordWrap=false,richText=false};bold=new GUIStyle(normal){font=heavy};
   field=new GUIStyle(GUIStyle.none){font=regular,fontSize=23,alignment=TextAnchor.MiddleLeft,richText=false,clipping=TextClipping.Clip,padding=new RectOffset(14,14,0,0),border=new RectOffset(0,0,0,0)};
   foreach(var state in new[]{field.normal,field.hover,field.active,field.focused,field.onNormal,field.onHover,field.onActive,field.onFocused}){state.background=null;state.textColor=Ink;}

  }
  public static void Box(Rect rect,Color color,float radius=12){GUI.DrawTexture(rect,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,color,0,radius);}
  public static Color Alpha(Color color,float alpha){color.a=alpha;return color;}
  public void Panel(Rect r,float opacity=.94f){Box(new Rect(r.x,r.y+5,r.width,r.height),new Color(.12f,.23f,.42f,.08f));Box(r,new Color(1,1,1,opacity));}
  public void Text(Rect r,string value,int size=22,bool heavy=false,Color? color=null,TextAnchor align=TextAnchor.MiddleLeft,bool wrap=false){
   var style=heavy?bold:normal;style.fontSize=size;style.normal.textColor=color??Ink;style.alignment=align;style.wordWrap=wrap;
   if(scrollTexts.Count>0){
    if(Event.current.type==EventType.Repaint)scrollTexts.Peek().labels.Add(new ScrollLabel{skin=this,rect=r,value=value,size=size,heavy=heavy,color=color??Ink,align=align,wrap=wrap,fontStyle=style.fontStyle});
    GUI.Label(r,GUIContent.none,style);return;
   }
   // Rasterize glyphs at their displayed size instead of enlarging a low-resolution atlas.
   var matrix=GUI.matrix;float scale=TextScale(matrix);
   style.fontSize=Mathf.Max(1,Mathf.RoundToInt(size*scale));
   try{GUI.Label(BeginRasterText(r,matrix,scale),value,style);}
   finally{GUI.matrix=matrix;style.fontSize=size;}

  }
  public static float TextScale(Matrix4x4 matrix)=>Mathf.Max(1,matrix.MultiplyVector(Vector3.up).magnitude);
  public static Rect ScaledRect(Rect r,float scale)=>new Rect(r.x*scale,r.y*scale,r.width*scale,r.height*scale);
  struct ScrollLabel {public CloudlineSkin skin;public Rect rect;public string value;public int size;public bool heavy,wrap;public Color color;public TextAnchor align;public FontStyle fontStyle;}
  sealed class ScrollText {public Rect viewport,content;public Vector2 scroll;public Matrix4x4 matrix;public readonly List<ScrollLabel> labels=new List<ScrollLabel>();}
  static readonly Stack<ScrollText> scrollTexts=new Stack<ScrollText>();
  static readonly List<ScrollText> scrollTextPool=new List<ScrollText>();
  static readonly GUIContent scrollContent=new GUIContent();
  public static Vector2 BeginScrollView(Rect viewport,Vector2 scroll,Rect content){
   int depth=scrollTexts.Count;if(depth==scrollTextPool.Count)scrollTextPool.Add(new ScrollText());
   var context=scrollTextPool[depth];context.viewport=viewport;context.content=content;context.matrix=GUI.matrix;context.labels.Clear();
   context.scroll=GUI.BeginScrollView(viewport,scroll,content);scrollTexts.Push(context);return context.scroll;
  }
  public static void EndScrollView(){
   GUI.EndScrollView();var context=scrollTexts.Pop();if(Event.current.type!=EventType.Repaint)return;
   // Establish a physical-size clip BEFORE drawing enlarged glyphs. Changing
   // GUI.matrix inside the original IMGUI clip would shrink its bounds.
   var matrix=GUI.matrix;float scale=TextScale(context.matrix);
   GUI.matrix=context.matrix*Matrix4x4.Scale(new Vector3(1/scale,1/scale,1));
   var viewport=ScaledRect(context.viewport,scale);if(context.content.height>context.viewport.height)viewport.width-=GUI.skin.verticalScrollbar.fixedWidth*scale;
   GUI.BeginClip(viewport);
   try{foreach(var label in context.labels){
    var rect=label.rect;rect.position-=context.scroll+context.content.position;
    var style=label.heavy?label.skin.bold:label.skin.normal;style.fontSize=Mathf.Max(1,Mathf.RoundToInt(label.size*scale));style.fontStyle=label.fontStyle;style.normal.textColor=label.color;style.alignment=label.align;style.wordWrap=label.wrap;
    scrollContent.text=label.value;style.Draw(ScaledRect(rect,scale),scrollContent,false,false,false,false);style.fontSize=label.size;
   }}finally{GUI.EndClip();GUI.matrix=matrix;context.labels.Clear();}
  }
  static Rect BeginRasterText(Rect r,Matrix4x4 matrix,float scale){
   GUI.matrix=matrix*Matrix4x4.Scale(new Vector3(1/scale,1/scale,1));return ScaledRect(r,scale);
  }
  public bool Button(Rect r,string value,bool primary=false,bool focused=false,bool enabled=true,int size=23){
   bool hover=enabled&&r.Contains(Event.current.mousePosition);var color=primary?Blue:new Color(1,1,1,.88f);
   if(!enabled)color=new Color(.88f,.9f,.93f,.85f);else if(hover&&!primary)color=new Color(.9f,.94f,1);
   if(focused&&enabled)Box(new Rect(r.x-3,r.y-3,r.width+6,r.height+6),primary?Ink:Blue,14);
   Box(r,color);Text(r,value,size,true,!enabled?Muted:primary?Color.white:Ink,TextAnchor.MiddleCenter);
   bool old=GUI.enabled;GUI.enabled=old&&enabled;bool clicked=GUI.Button(r,GUIContent.none,GUIStyle.none);GUI.enabled=old;return clicked;
  }
  public float NameWidth(string value){bold.fontSize=17;return bold.CalcSize(new GUIContent(value)).x;}
  public string FitName(string value){value=LocalRaceConfig.CleanName(value);while(value.Length>0&&(NameWidth(value)>190||field.CalcSize(new GUIContent(value)).x>239))value=value.Substring(0,value.Length-1);return value;}
  public string Ellipsis(string value,float width,int size=16){bold.fontSize=size;if(bold.CalcSize(new GUIContent(value)).x<=width)return value;while(value.Length>0&&bold.CalcSize(new GUIContent(value+"…")).x>width)value=value.Substring(0,value.Length-1);return value+"…";}
  public string Field(Rect r,string value,string name,int maxLength=10,bool navigationFocus=false){
   bool editing=GUI.GetNameOfFocusedControl()==name;
   bool hover=GUI.enabled&&r.Contains(Event.current.mousePosition);
   if(editing)Box(new Rect(r.x-4,r.y-4,r.width+8,r.height+8),Alpha(Blue,.12f),12);
   Box(r,editing||navigationFocus?Blue:hover?new Color(.57f,.69f,.86f):new Color(.81f,.86f,.93f),8);
   float inset=editing||navigationFocus?2:1;
   Box(new Rect(r.x+inset,r.y+inset,r.width-2*inset,r.height-2*inset),editing?Color.white:new Color(.96f,.975f,.995f),7);
   var settings=GUI.skin.settings;Color oldCursor=settings.cursorColor,oldSelection=settings.selectionColor;settings.cursorColor=Blue;settings.selectionColor=Alpha(Blue,.26f);
   var matrix=GUI.matrix;float scale=TextScale(matrix);int padding=Mathf.RoundToInt(14*scale);
   field.fontSize=Mathf.RoundToInt(23*scale);field.padding.left=field.padding.right=padding;
   try{var rasterRect=BeginRasterText(r,matrix,scale);GUI.SetNextControlName(name);return GUI.TextField(rasterRect,value,maxLength,field);}
   finally{GUI.matrix=matrix;field.fontSize=23;field.padding.left=field.padding.right=14;settings.cursorColor=oldCursor;settings.selectionColor=oldSelection;}
  }
  public void Toggle(Rect r,bool enabled){Box(r,enabled?Blue:new Color(.78f,.81f,.86f),r.height/2);float d=r.height-8;Box(new Rect(enabled?r.xMax-d-4:r.x+4,r.y+4,d,d),Color.white,d/2);}
  public void Brand(Rect r,int size){bold.fontStyle=FontStyle.Italic;Text(r,"STAR",size,true,Ink);float width=bold.CalcSize(new GUIContent("STAR ")).x;Text(new Rect(r.x+width,r.y,r.width-width,r.height),"RACING",size,true,Blue);bold.fontStyle=FontStyle.Normal;}
  public void CenteredBrand(Rect r,int size){
   bold.fontSize=size;bold.fontStyle=FontStyle.Italic;
   float width=bold.CalcSize(new GUIContent("STAR RACING")).x;bold.fontStyle=FontStyle.Normal;
   Brand(new Rect(r.center.x-width/2,r.y,width,r.height),size);
  }
  public static Matrix4x4 Begin(){
   float scale=Mathf.Min(Screen.width/1600f,Screen.height/900f);var old=GUI.matrix;
   GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1600*scale)/2,(Screen.height-900*scale)/2,0),Quaternion.identity,Vector3.one*scale);return old;
  }
  public const string FooterText="© 2026 Evgeniy Afonasev · afonasev.tech · Made with Codex";
  public void Footer(bool dark=false){Text(new Rect(56,864,1330,28),FooterText,15,false,dark?new Color(.9f,.94f,1):Muted,TextAnchor.MiddleLeft);}
  public void Backdrop(){if(Background!=null)GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Background,ScaleMode.ScaleAndCrop);else Box(new Rect(0,0,Screen.width,Screen.height),new Color(.88f,.93f,1),0);}
 }
}
