using System.Collections;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed class RaceLoading : MonoBehaviour {
  public string Status="Загружаем меню";
  CloudlineSkin skin;
  // Wait across a rendered frame before doing synchronous Unity construction.
  public static IEnumerator Present(){yield return null;yield return null;}
  void OnGUI(){
   skin=skin??new CloudlineSkin();int depth=GUI.depth;GUI.depth=-1000;
   skin.Backdrop();var old=CloudlineSkin.Begin();
   CloudlineSkin.Box(new Rect(0,0,1600,900),new Color(1,1,1,.32f),0);
   skin.CenteredBrand(new Rect(440,340,720,100),64);
   skin.Text(new Rect(440,462,720,40),Status,21,false,CloudlineSkin.Muted,TextAnchor.MiddleCenter);
   var bar=new Rect(620,530,360,3);CloudlineSkin.Box(bar,CloudlineSkin.Alpha(CloudlineSkin.Blue,.14f),2);
   float phase=Mathf.PingPong(Time.realtimeSinceStartup*.55f,1);
   CloudlineSkin.Box(new Rect(bar.x+phase*(bar.width-72),bar.y,72,3),CloudlineSkin.Blue,2);
   skin.Footer();GUI.matrix=old;GUI.depth=depth;
  }
 }
}
