using System.Collections.Generic;
using UnityEngine;
namespace StarRacingPrototype {
 // Modular authored assets. All dimensions are metres; occupied boxes leave true empty portals.
 public static class AuthoredCityArchitecture {
  public struct Part {public string name;public Vector3 center,size;public int finish;public float tile;public Part(string name,Vector3 center,Vector3 size,int finish,float tile){this.name=name;this.center=center;this.size=size;this.finish=finish;this.tile=tile;}}
  public static List<Part> Create(){
   var p=new List<Part>();
   void Box(string name,float x,float y,float z,float w,float h,float d,int finish=0,float tile=36)=>p.Add(new Part(name,new Vector3(x,y,z),new Vector3(w,h,d),finish,tile));
   // Oko: silver frame, dark recessed window wall, warm inner reveals. Aperture 190 x 180m.
   foreach(int side in new[]{-1,1}){
    Box("Око / опора",side*160,390,2050,130,1020,130,0);
    Box("Око / наружное ребро",side*224,390,2050,8,1028,142,1,5);
    Box("Око / золотой откос",side*98,580,2050,6,180,134,3,4);
    for(int floor=0;floor<10;floor++)Box("Око / пояс фасада",side*160,-80+floor*105,2050,134,3,134,1,4);
   }
   Box("Око / нижний корпус",0,182.5f,2050,190,615,130,0);
   Box("Око / верхний корпус",0,785,2050,190,230,130,0);
   Box("Око / нижний откос",0,487,2050,192,6,136,3,4);
   Box("Око / верхний откос",0,673,2050,192,6,136,3,4);
   Box("Око / корона",0,905,2050,460,10,150,1,4);
   for(int i=0;i<8;i++)Box("Око / солнечные панели",-196+i*56,920,2050,38,8,105,4,4);
   // Atrium: warm ceramic decks, dark columns and overhead transverse beams.
   foreach(int side in new[]{-1,1}){
    Box("Атриум / внешний корпус",side*153,345,1555,96,930,330,2);
    Box("Атриум / нижняя галерея",side*116,420,1555,30,14,338,1,4);
    for(int bay=0;bay<5;bay++){
     Box("Атриум / пилон",side*106,510,1420+bay*67,12,180,12,4,4);
     Box("Атриум / световая линия",side*98,560,1420+bay*67,2,34,5,5,4);
     Box("Атриум / оконная ниша",side*104,512,1448+bay*67,3,108,36,6);
     Box("Атриум / кашпо",side*83,429,1420+bay*67,18,10,28,3,8);
     Box("Атриум / сад",side*83,438,1420+bay*67,16,9,25,7,9);
    }
   }
   Box("Атриум / нижняя площадь",0,416,1555,220,16,330,2,8);
   for(int bay=0;bay<5;bay++)Box("Атриум / верхняя балка",0,602,1420+bay*67,230,18,12,1,4);
   Box("Атриум / верхняя терраса",0,690,1555,310,18,330,2,8);
   // Twin towers: different silhouettes and facade finishes make the ascent legible.
   Tower("Башня Север",680,3090,205,900,0,1);
   Tower("Башня Юг",1320,3500,220,1110,6,3);
   void Tower(string name,float x,float z,float w,float height,int finish,int trim){
    Box(name,x,height*.5f-120,z,w,height,w,finish);
    foreach(int side in new[]{-1,1})foreach(int front in new[]{-1,1})
     Box(name+" / угловое ребро",x+side*(w*.5f-2),height*.5f-120,z+front*(w*.5f-2),9,height+8,9,trim,4);
    for(int band=1;band<9;band++)Box(name+" / пояс",x,-120+height*band/9,z,w+12,5,w+12,trim,4);
    Box(name+" / крыша",x,height-115,z,w+24,10,w+24,trim,4);
    Box(name+" / надстройка",x,height-83,z,w*.55f,54,w*.55f,4,8);
   }
   // A high connecting skybridge, above the upper road with generous camera clearance.
   Box("Верхняя перемычка",995,880,3500,430,28,95,2,8);
   Box("Верхняя перемычка / стекло",995,903,3500,430,20,85,6,12);
   // Named slalom buildings have a composition, unlike the distant city filler.
   Box("Слалом / белая башня",170,310,680,120,860,150,2);
   Box("Слалом / синяя башня",-160,300,960,110,840,150,6);
   Box("Слалом / ступенчатый корпус",165,290,1210,150,820,130,0);
   for(int i=0;i<4;i++)Box("Слалом / ступень",165,705+i*22,1210,150-i*26,20,130-i*20,1,4);
   foreach(int side in new[]{-1,1}){
    Box("Слалом / белая башня окна",170+side*34,410,604,25,560,3,6);
    Box("Слалом / белая башня ребро",170+side*58,310,680,5,868,158,1,8);
    Box("Слалом / синяя башня ребро",-160+side*53,300,960,5,848,158,3,8);
   }
   for(int i=0;i<7;i++){
    Box("Слалом / белый пояс",170,10+i*105,680,124,5,158,1,8);
    Box("Слалом / медный пояс",-160,10+i*105,960,118,5,158,3,8);
   }
   Box("Слалом / белая корона",170,748,680,134,14,164,1,8);
   Box("Слалом / сад на крыше",170,760,680,100,10,124,7,9);
   return p;
  }
  public static bool ClearOfLandmarks(Vector3 center,Vector3 extents){
   foreach(var part in Create()){
    var delta=center-part.center;var gap=extents+part.size*.5f+Vector3.one*14;
    if(Mathf.Abs(delta.x)<gap.x&&Mathf.Abs(delta.y)<gap.y&&Mathf.Abs(delta.z)<gap.z)return false;
   }
   return true;
  }
 }
}
