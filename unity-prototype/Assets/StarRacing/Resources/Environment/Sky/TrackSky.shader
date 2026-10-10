Shader "StarRacing/TrackSky" {
 Properties { _CloudTex("Cloud sea", 2D) = "white" {} _PlanetTex("Planet", 2D) = "black" {} _Space("Space", Float)=0 _CloudY("Cloud height", Float)=-200 }
 SubShader {
  Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
  Cull Off ZWrite Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #include "UnityCG.cginc"
   #include "CloudSampling.cginc"
   sampler2D _PlanetTex;
   float _Space, _PlanetScale;
   float3 _PlanetDirection, _PlanetRight, _PlanetUp, _SunDirection;
   struct appdata { float4 vertex : POSITION; };
   struct v2f { float4 pos : SV_POSITION; float3 ray : TEXCOORD0; };
   v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.ray=mul((float3x3)unity_ObjectToWorld,v.vertex.xyz); return o; }
   float hash(float3 p) { return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453); }
   float stars(float3 d) {
    float3 p=d*430; float3 cell=floor(p); float3 f=frac(p)-.5;
    float h=hash(cell); float radius=length(f);
    return smoothstep(.19,.02,radius)*step(.992,h)*(.3+hash(cell+7));
   }
   float4 frag(v2f i) : SV_Target {
    float3 d=normalize(i.ray);
    float st=stars(d);
    if(_Space>.5) {
     float band=exp(-pow(dot(d,normalize(float3(.25,.87,.43)))*5.5,2));
     float dust=(sin(d.x*28+sin(d.z*19))*sin(d.y*37-d.z*11)*.5+.5);
     float3 col=float3(.0015,.0025,.007)+band*dust*float3(.009,.012,.025)+st*float3(.62,.74,1);
     float facing=dot(d,_PlanetDirection);
     float2 uv=float2(dot(d,_PlanetRight),dot(d,_PlanetUp))/max(facing,.001)/_PlanetScale*.5+.5;
     if(facing>0 && all(uv>=0) && all(uv<=1)) { float4 planet=tex2D(_PlanetTex,uv); col=lerp(col,planet.rgb*1.35,planet.a); }
     return float4(col,1);
    }
    float h=saturate(d.y);
    float3 col=lerp(float3(.70,.81,.91),float3(.055,.26,.55),sqrt(h));
    col=lerp(col,float3(.007,.028,.10),smoothstep(.55,1,h)*.85);
    col+=st*smoothstep(.55,1,h)*.11;
    float sun=dot(d,normalize(_SunDirection));
    col+=pow(saturate(sun),96)*float3(.15,.13,.09);
    col+=smoothstep(.9997,.9999,sun)*float3(2.8,2.5,1.9);
    if(d.y<-.001 && _WorldSpaceCameraPos.y>_CloudY) {
     float t=(_CloudY-_WorldSpaceCameraPos.y)/min(d.y,-.001);
     float3 worldPoint=_WorldSpaceCameraPos+d*t;
     col=CloudColor(worldPoint,_WorldSpaceCameraPos);
    }
    return float4(col,1);
   }
   ENDCG
  }
 }
}
