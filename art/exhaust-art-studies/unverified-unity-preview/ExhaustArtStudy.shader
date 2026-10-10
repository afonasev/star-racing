Shader "StarRacing/Editor/ExhaustArtStudy"
{
 Properties {
  _MainTex("Study atlas",2D)="white"{}
  _Cell("Atlas cell",Range(0,1))=0
  _Phase("Study frame",Float)=.38
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
   struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
   TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    half _Cell;
    float _Phase;
   CBUFFER_END
   Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;return o;}
   half4 Frag(Varyings i):SV_Target {
    // UV Y=0 is the nozzle, same as the production mesh. Atlas has roots at bottom.
    float2 uv=i.uv;
    uv.x+=sin(uv.y*21-_Phase*8)*.012*uv.y;
    uv.y+=sin(uv.y*31+uv.x*13-_Phase*6)*.008*uv.y*(1-uv.y);
    uv=saturate(uv);
    uv.x=(uv.x+_Cell)*.5;
    half4 flame=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);
    return half4(flame.rgb*1.7,flame.a*.82);
   }
   ENDHLSL
  }
 }
}
