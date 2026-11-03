Shader "StarRacing/OrbitalStone" {
 Properties {
  _BaseColor("Stone tint",Color)=(.42,.36,.29,1)
  _BaseMap("Fractures",2D)="white" {}
  _BumpMap("Micro relief",2D)="bump" {}
  _TileMeters("Texture repeat in metres",Float)=8
  _Cull("Cull",Float)=0
 }
 SubShader {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
  Pass {
   Tags {"LightMode"="UniversalForward"}
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _TileMeters;
   CBUFFER_END
   struct A{float4 p:POSITION;float3 n:NORMAL;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float fog:TEXCOORD2;};
   V vert(A i){V o;o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.n);o.fog=ComputeFogFactor(o.p.z);return o;}
   half4 frag(V i,FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target {
    float3 n=normalize(i.normal)*IS_FRONT_VFACE(facing,1,-1),w=pow(abs(n),4);w/=max(w.x+w.y+w.z,.001);
    float3 p=i.world/max(_TileMeters,.1),s=step(0,n)*2-1;
    float2 x=float2(p.y,p.z*s.x),y=float2(p.z,p.x*s.y),z=float2(p.x,p.y*s.z);
    float3 a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,x).rgb*w.x+SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,y).rgb*w.y+SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,z).rgb*w.z;
    float3 bx=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,x)),by=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,y)),bz=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,z));
    float3 detail=float3(0,bx.x,bx.y*s.x)*w.x+float3(by.y*s.y,0,by.x)*w.y+float3(bz.x,bz.y*s.z,0)*w.z;
    n=normalize(n+(detail-n*dot(n,detail))*.7);
    // Broad geological variation is independent of microtexture and object scale.
    float strata=.86+.09*sin(i.world.y*.035+i.world.x*.008)+.05*sin(i.world.z*.021);
    a*= _BaseColor.rgb*2.4*strata;
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
    float3 lit=SampleSH(n)*.8+float3(.018,.023,.035)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation;
    #if defined(_ADDITIONAL_LIGHTS)
    uint count=GetAdditionalLightsCount();
    for(uint j=0;j<count;j++){Light l=GetAdditionalLight(j,i.world);lit+=l.color*saturate(dot(n,l.direction))*l.distanceAttenuation*l.shadowAttenuation;}
    #endif
    return half4(MixFog(a*lit,i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
