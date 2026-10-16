using System.Collections;
using UnityEngine;
namespace StarRacingPrototype {
 // The wordmark remains still; an occasional masked sheen and a clear moving
 // segment communicate loading without inventing a percentage or using scaled time.
 public sealed class RaceLoading : MonoBehaviour {
  public string Status="Загружаем меню";
  CloudlineSkin skin;Texture2D wash,sweep,wordmark,sheen;float started;
  const int SheenWidth=256,SheenHeight=85;
  readonly byte[] mask=new byte[SheenWidth*SheenHeight];
  readonly Color32[] sheenPixels=new Color32[SheenWidth*SheenHeight];
  public float AnimationTime=>Time.realtimeSinceStartup-started;
  public int RenderedFrames {get;private set;}
  public float SweepPosition=>(AnimationTime/2.7f%1)*846-180;
  public static readonly Rect WordmarkRect=new Rect(18,278,882,294),BarRect=new Rect(112,658,666,10);
  public static IEnumerator Present(){yield return null;yield return null;yield return null;}
  void Awake(){
   started=Time.realtimeSinceStartup;skin=new CloudlineSkin(true);
   wordmark=Resources.Load<Texture2D>("UI/LoadingWordmark");wash=Strip(true);sweep=Strip(false);
   if(wordmark!=null){
    sheen=new Texture2D(SheenWidth,SheenHeight,TextureFormat.RGBA32,false){name="Loading wordmark sheen",wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
    for(int y=0;y<SheenHeight;y++)for(int x=0;x<SheenWidth;x++)mask[y*SheenWidth+x]=(byte)(wordmark.GetPixelBilinear((x+.5f)/SheenWidth,(y+.5f)/SheenHeight).a*255);
   }
  }
  static Texture2D Strip(bool background){
   var texture=new Texture2D(256,1,TextureFormat.RGBA32,false){name=background?"Loading wash":"Loading light",wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
   var pixels=new Color[256];for(int i=0;i<pixels.Length;i++){
    float x=i/255f;pixels[i]=background?new Color(.96f,.98f,1,.3f*(1-Mathf.SmoothStep(0,1,x))):new Color(.02f,.3f,1,Mathf.SmoothStep(0,1,x));
   }
   texture.SetPixels(pixels);texture.Apply(false,true);return texture;
  }
  void Update(){
   if(sheen==null)return;
   float phase=AnimationTime%4.8f,position=phase/1.7f*1.5f-.25f;
   for(int y=0;y<SheenHeight;y++)for(int x=0;x<SheenWidth;x++){
    float distance=Mathf.Abs((x+.5f)/SheenWidth+(y/(float)SheenHeight-.5f)*.12f-position);
    float alpha=phase<1.7f?Mathf.SmoothStep(0,1,1-distance/.055f)*.40f:0;
    int at=y*SheenWidth+x;sheenPixels[at]=new Color32(192,241,255,(byte)(mask[at]*alpha));
   }
   sheen.SetPixels32(sheenPixels);sheen.Apply(false,false);
  }
  void OnGUI(){
   if(Event.current.type!=EventType.Repaint)return;
   RenderedFrames++;int depth=GUI.depth;var color=GUI.color;var matrix=GUI.matrix;GUI.depth=-1000;
   try {
    skin.Backdrop();GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),wash);CloudlineSkin.Begin();
    float enter=Mathf.SmoothStep(0,1,AnimationTime/.4f);GUI.color=new Color(1,1,1,enter);
    var logo=WordmarkRect;logo.y+=(1-enter)*10;
    if(wordmark!=null){GUI.DrawTexture(logo,wordmark);if(sheen!=null)GUI.DrawTexture(logo,sheen);}
    else skin.CenteredBrand(logo,74);
    GUI.color=color;
    CloudlineSkin.Box(BarRect,new Color(.52f,.67f,.89f,.7f).linear,5);
    GUI.BeginGroup(BarRect);GUI.DrawTexture(new Rect(SweepPosition,0,180,10),sweep);CloudlineSkin.Box(new Rect(SweepPosition+174,0,10,10),new Color(.08f,.55f,1).linear,5);GUI.EndGroup();
    skin.Text(new Rect(112,695,666,46),Status,26,false,CloudlineSkin.Ink.linear,TextAnchor.MiddleCenter);
    skin.Footer();
   }finally{GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;}
  }
  void OnDestroy(){if(wash!=null)Destroy(wash);if(sweep!=null)Destroy(sweep);if(sheen!=null)Destroy(sheen);}
 }
}
