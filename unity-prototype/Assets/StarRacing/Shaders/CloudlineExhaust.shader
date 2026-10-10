Shader "StarRacing/CloudlineExhaust"
{
 Properties {
  _FlameAtlas("Natural flame: gas / nitro",2D)="white"{}
  _Intensity("Flame intensity",Range(0,4))=1.7
  _Nitro("Nitro blend",Range(0,1))=0
  [HideInInspector] _FlowPhase("Flow phase",Float)=0
  [HideInInspector] _JetPhase("Jet phase",Float)=0
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
   TEXTURE2D(_FlameAtlas);SAMPLER(sampler_FlameAtlas);
   CBUFFER_START(UnityPerMaterial)
    float4 _FlameAtlas_ST;
    half _Intensity;
    half _Nitro;
    float _FlowPhase;
    float _JetPhase;
   CBUFFER_END
   Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;return o;}
   half4 Frag(Varyings i):SV_Target {
    float2 uv=i.uv;
    float phase=_FlowPhase+_JetPhase;
    // Advection bends the drawn wisps without scrolling the nozzle or flame tip.
    float anchored=uv.y*(1-uv.y);
    uv.x+=(sin(uv.y*21-phase)+.5*sin(uv.y*37-phase*2+uv.x*7))*.07*anchored;
    uv.y+=sin(uv.y*31+uv.x*13-phase*2)*.055*anchored;
    uv=saturate(uv);
    // Guard band keeps mip/bilinear samples inside their atlas cell.
    float x=lerp(.002,.498,uv.x);
    half4 gas=SAMPLE_TEXTURE2D(_FlameAtlas,sampler_FlameAtlas,float2(x,uv.y));
    half4 nitro=SAMPLE_TEXTURE2D(_FlameAtlas,sampler_FlameAtlas,float2(x+.5,uv.y));
    half4 flame=lerp(gas,nitro,saturate(_Nitro));
    return half4(flame.rgb*_Intensity,flame.a*.82);
   }
   ENDHLSL
  }
 }
}
