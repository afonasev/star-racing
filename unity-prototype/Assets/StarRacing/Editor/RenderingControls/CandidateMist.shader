Shader "Hidden/StarRacing/Control/CandidateMist" {
 Properties { _CloudTex("Cloud sea",2D)="white" {} _CloudY("Cloud height",Float)=-200 }
 SubShader {
  Tags { "RenderType"="Transparent" "Queue"="Transparent" }
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #include "UnityCG.cginc"
   #include "CloudSampling.cginc"
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
   struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; };
   v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz; o.uv=v.uv; return o; }
   float4 frag(v2f i):SV_Target {
    float edge=1-smoothstep(.18,.5,length(i.uv-.5));
    // Derivatives are taken before divergent early returns, retaining texture LOD.
    float2 dx=ddx(i.world.xz),dy=ddy(i.world.xz);
    [branch] if(edge==0)return 0;
    float n=tex2Dgrad(_CloudTex,i.world.xz/180,dx/180,dy/180).r;
    float alpha=edge*smoothstep(.18,.68,n)*.58;
    [branch] if(alpha==0)return 0;
    return float4(CloudColorGrad(i.world,_WorldSpaceCameraPos,dx/950,dy/950),alpha);
   }
   ENDCG
  }
 }
}
