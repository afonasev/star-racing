Shader "StarRacing/CloudDeck" {
 Properties { _CloudTex("Cloud sea",2D)="white" {} _CloudY("Cloud height",Float)=-200 }
 SubShader {
  Tags { "RenderType"="Opaque" "Queue"="Geometry" }
  Cull Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "CloudSampling.cginc"
   struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; };
   v2f vert(float4 vertex:POSITION) { v2f o; o.pos=UnityObjectToClipPos(vertex); o.world=mul(unity_ObjectToWorld,vertex).xyz; return o; }
   float4 frag(v2f i):SV_Target { return float4(CloudColor(i.world,_WorldSpaceCameraPos),1); }
   ENDCG
  }
 }
}
