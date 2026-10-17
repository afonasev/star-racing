Shader "StarRacing/SpaceRock" {
 Properties { _BaseColor("Stone", Color) = (.42,.43,.44,1) }
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
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float fog:TEXCOORD2; };
   float hash(float3 p) { p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z); }
   float noise(float3 p) {
    float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),lerp(hash(i+float3(0,1,1)),hash(i+1),f.x),f.y),f.z);
   }
   Varyings vert(Attributes v) {
    Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o;
   }
   half4 frag(Varyings i):SV_Target {
    float3 p=i.positionWS;
    float broad=noise(p*.055),grain=noise(p*.8),fine=noise(p*3.1);
    float seam=smoothstep(.43,.51,noise(p*.22))*(1-smoothstep(.51,.56,noise(p*.22)));
    float3 albedo=_BaseColor.rgb*(.50+broad*.95)*(.65+grain*.5+fine*.15)*(1-seam*.2);
    float3 n=normalize(i.normalWS);
    // Band-limited world-space granular normal; screen derivative attenuates the finest layer at distance.
    float attenuation=saturate(1-length(fwidth(p))*.22);
    float3 bump=float3(noise(p*.7+17),noise(p*.7+41),noise(p*.7+83))-.5;
    n=normalize(n+(bump-n*dot(bump,n))*.85*attenuation);
    Light light=GetMainLight();float diffuse=saturate(dot(n,light.direction));
    float3 ambient=SampleSH(n)*.8+float3(.035,.043,.06);
    float3 color=albedo*(ambient+light.color*(diffuse*.94+.045));
    color=MixFog(color,i.fog);return half4(color,1);
   }
   ENDHLSL
  }
 }
}
