Shader "StarRacing/DriftTireMarks" {
 Properties {_MarkTime("Race clock",Float)=0 _RubberTex("Rubber deposition",2D)="white"{}}
 SubShader {
  Tags {"RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off Offset -1,-1
  Pass {
   Tags {"LightMode"="SRPDefaultUnlit"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_RubberTex);SAMPLER(sampler_RubberTex);
   CBUFFER_START(UnityPerMaterial)
    float _MarkTime;
   CBUFFER_END
   struct Input {float4 position:POSITION;float2 uv:TEXCOORD0;float2 age:TEXCOORD1;};
   struct Output {float4 position:SV_POSITION;float2 uv:TEXCOORD0;float alpha:TEXCOORD1;};
   Output vert(Input v) {
    Output o;float remaining=saturate(1-(_MarkTime-v.age.x)/8);
    o.position=remaining>0&&v.age.y>0?TransformObjectToHClip(v.position.xyz):float4(0,0,0,1);
    o.uv=v.uv;o.alpha=v.age.y*remaining*remaining*(3-2*remaining);return o;
   }
   half4 frag(Output i):SV_Target {
    float2 uv=float2(lerp(.14,.86,i.uv.x),i.uv.y);
    // Each sample has zero weight at its own wrap seam, so generated edges never form bars.
    float blend=sin(frac(uv.y)*PI);blend*=blend;
    half4 a=SAMPLE_TEXTURE2D(_RubberTex,sampler_RubberTex,uv);
    half4 b=SAMPLE_TEXTURE2D(_RubberTex,sampler_RubberTex,uv+float2(0,.5));
    float rubber=lerp(b.a*(.3+.7*saturate(b.r*4)),a.a*(.3+.7*saturate(a.r*4)),blend);
    float edge=smoothstep(0,.035,i.uv.x)*(1-smoothstep(.965,1,i.uv.x));
    // Black deposition also darkens the unlit parts of the space-station road.
    return half4(0,0,0,i.alpha*edge*rubber);
   }
   ENDHLSL
  }
 }
}
