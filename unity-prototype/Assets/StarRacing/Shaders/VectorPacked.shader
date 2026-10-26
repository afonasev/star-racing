Shader "StarRacing/VectorPacked"
{
 Properties { _BaseColor("Player paint",Color)=(1,1,1,1) [HideInInspector] _BaseMap("Unused shadow fallback",2D)="white"{} [HideInInspector] _Cutoff("Cutoff",Float)=0 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   Cull Back ZWrite On
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #pragma multi_compile_fog
   #define _CLEARCOAT 1
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   UNITY_INSTANCING_BUFFER_START(VectorPerInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
   UNITY_INSTANCING_BUFFER_END(VectorPerInstance)
   struct Attributes {
    float4 positionOS:POSITION; float3 normalOS:NORMAL;
    float2 finish:TEXCOORD0; float2 colorRG:TEXCOORD1; float2 colorBEmission:TEXCOORD2;
    float2 paintCoat:TEXCOORD3; float2 coatSmoothness:TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
   };
   struct Varyings {
    float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1;
    float2 finish:TEXCOORD2; float4 colorEmission:TEXCOORD3; float3 paintCoat:TEXCOORD4;
    half fog:TEXCOORD5; half3 vertexLighting:TEXCOORD6;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
   };
   Varyings Vert(Attributes v) {
    Varyings o=(Varyings)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=TransformObjectToWorldNormal(v.normalOS);
    o.finish=v.finish; o.colorEmission=float4(v.colorRG,v.colorBEmission); o.paintCoat=float3(v.paintCoat,v.coatSmoothness.x);
    o.fog=ComputeFogFactor(p.positionCS.z); o.vertexLighting=VertexLighting(p.positionWS,o.normalWS); return o;
   }
   half4 Frag(Varyings i):SV_Target {
    UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    InputData d=(InputData)0; d.positionWS=i.positionWS; d.normalWS=NormalizeNormalPerPixel(i.normalWS);
    d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS); d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
    d.bakedGI=SampleSH(d.normalWS); d.vertexLighting=i.vertexLighting; d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask=half4(1,1,1,1);
    SurfaceData s=(SurfaceData)0;
    s.albedo=lerp(i.colorEmission.rgb,UNITY_ACCESS_INSTANCED_PROP(VectorPerInstance,_BaseColor).rgb,i.paintCoat.x);
    s.metallic=i.finish.x; s.smoothness=i.finish.y; s.specular=half3(.04,.04,.04); s.normalTS=half3(0,0,1); s.occlusion=1; s.alpha=1;
    s.emission=i.colorEmission.rgb*i.colorEmission.w; s.clearCoatMask=i.paintCoat.y; s.clearCoatSmoothness=i.paintCoat.z;
    half4 color=UniversalFragmentPBR(d,s); color.rgb=MixFog(color.rgb,i.fog); return color;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
  UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
 FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
