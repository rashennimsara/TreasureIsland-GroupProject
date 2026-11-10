Shader "Island/Waterfall Flow URP"
{
 Properties {
  _Speed("Flow Speed",Range(0,10))=1.2
  _Foam("Foam",Range(0,1))=0.3
  _WaterColor("Water Color",Color)=(0.04,0.48,0.62,1)
 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
  Pass {
   Tags { "LightMode"="UniversalForwardOnly" }
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
    float _Speed;float _Foam;float4 _WaterColor;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
   V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.uv=i.uv;return o;}
   half4 frag(V i):SV_Target {
    float v=i.uv.y-_Time.y*_Speed;
    float bands=sin(v*7.0+sin(i.uv.x*22.0));
    float veins=sin(i.uv.x*38.0+sin(v*3.0))*sin(v*11.0+i.uv.x*8.0);
    float foam=smoothstep(0.35,0.95,veins);
    float3 color=lerp(_WaterColor.rgb,float3(0.88,0.98,1.0),saturate(0.10+0.08*bands+foam*_Foam));
    return half4(color,1);
   }
   ENDHLSL
  }
 }
 Fallback Off
}