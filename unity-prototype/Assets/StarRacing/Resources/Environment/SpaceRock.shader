Shader "StarRacing/SpaceRock" {
 Properties {
  _BaseColor("Stone", Color) = (.42,.43,.44,1)
  _BaseMap("Fractured stone", 2D) = "white" {}
  _BumpMap("Stone micro relief", 2D) = "bump" {}
 }
 SubShader {
  Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
  Pass {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionOS:TEXCOORD0; float3 normalOS:TEXCOORD1; float fog:TEXCOORD2; };
   Varyings vert(Attributes v) {
    Varyings o;o.positionOS=v.positionOS.xyz;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.normalOS=v.normalOS;o.fog=ComputeFogFactor(o.positionCS.z);return o;
   }
   half4 frag(Varyings i):SV_Target {
    float3 p=i.positionOS*2.6;
    float3 n=normalize(i.normalOS), axisSign=step(0,n)*2-1;
    float3 weights=pow(abs(n),4);weights/=max(dot(weights,1),.0001);
    // Each projection uses a signed, right handed basis. Texture derivatives select stable mip levels.
    float2 ux=float2(p.y,p.z*axisSign.x), uy=float2(p.z,p.x*axisSign.y), uz=float2(p.x,p.y*axisSign.z);
    float3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,ux).rgb*weights.x+
     SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uy).rgb*weights.y+SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uz).rgb*weights.z;
    float3 bx=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,ux));
    float3 by=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uy));
    float3 bz=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uz));
    float3 detail=float3(0,bx.x,bx.y*axisSign.x)*weights.x+
     float3(by.y*axisSign.y,0,by.x)*weights.y+float3(bz.x,bz.y*axisSign.z,0)*weights.z;
    n=TransformObjectToWorldNormal(normalize(n+(detail-n*dot(detail,n))*.55));
    // Preserve the approved stone palette and existing space lighting.
    albedo*= _BaseColor.rgb*2.2;
    Light light=GetMainLight();float diffuse=saturate(dot(n,light.direction));
    float3 ambient=SampleSH(n)*.8+float3(.035,.043,.06);
    float3 color=albedo*(ambient+light.color*(diffuse*.94+.045));
    color=MixFog(color,i.fog);return half4(color,1);
   }
   ENDHLSL
  }
 }
}
