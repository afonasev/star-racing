Shader "StarRacing/NitroEdgeBlur" {
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" }
  Pass {
   Name "NitroEdgeBlur"
   ZWrite Off ZTest Always Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma target 3.5
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

   float _Strength;

   float4 Frag(Varyings input) : SV_Target {
    float2 uv=input.texcoord;
    float4 original=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv);
    if(all(uv>=float2(0.2,0.2))&&all(uv<=float2(0.8,0.8)))return original;

    float2 fromCenter=uv-float2(0.5,0.5);
    float radial=saturate(length(fromCenter)*2.0);
    float2 towardCenter=-normalize(fromCenter+1e-6);
    float2 displacement=towardCenter*(0.035*_Strength*radial);
    float4 streak=0;
    [unroll] for(int i=0;i<8;i++) {
     float t=(i+1.0)/8.0;
     streak+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,clamp(uv+displacement*t,0.5*_BlitTexture_TexelSize.xy,1-0.5*_BlitTexture_TexelSize.xy));
    }
    streak*=0.125;

    float2 outside=max(float2(0.2,0.2)-uv,uv-float2(0.8,0.8));
    float edgeMask=smoothstep(0.0,0.055,max(outside.x,outside.y));
    // The chase car sits below the center, especially in small split views.
    if(uv.y<0.2)edgeMask*=smoothstep(0.12,0.20,abs(uv.x-0.5));
    return lerp(original,streak,edgeMask*_Strength);
   }
   ENDHLSL
  }
 }
}
