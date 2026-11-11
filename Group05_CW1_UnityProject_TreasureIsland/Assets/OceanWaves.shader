Shader "Island/Ocean Waves URP"
{
    Properties
    {
        _WaterDeep("Deep Water Color", Color) = (0.025, 0.20, 0.36, 1)
        _WaterLight("Wave Color", Color) = (0.055, 0.46, 0.58, 1)
        _FoamColor("Ripple Highlight", Color) = (0.58, 0.85, 0.88, 1)
        _WaveSpeed("Wave Speed", Range(0, 5)) = 1.2
        _WaveLength("Wave Length", Range(2, 100)) = 18
        _WaveStrength("Wave Strength", Range(0, 2)) = 0.65
        _FoamAmount("Ripple Highlights", Range(0, 1)) = 0.25
        _SunGlint("Sun Glint", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "OceanWaves"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterDeep;
                float4 _WaterLight;
                float4 _FoamColor;
                float _WaveSpeed;
                float _WaveLength;
                float _WaveStrength;
                float _FoamAmount;
                float _SunGlint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // World coordinates keep the wave size stable even on a very
                // large Ocean plane. No texture, dense mesh or script is needed.
                float2 p = input.positionWS.xz;
                float k = 6.2831853 / max(_WaveLength, 0.01);
                float time = _Time.y * _WaveSpeed;
                const float2 directionA = float2(0.85, 0.5268);
                const float2 directionB = float2(-0.4, 0.9165);
                const float2 directionC = float2(0.9798, -0.2);

                float a = dot(p, directionA) * k - time;
                float b = dot(p, directionB) * k * 1.37 - time * 1.19;
                float c = dot(p, directionC) * k * 2.31 - time * 1.53;
                float wave = 0.5 * sin(a) + 0.3 * sin(b) + 0.2 * sin(c);
                float2 slope = _WaveStrength * (
                    0.5 * cos(a) * directionA
                    + 0.411 * cos(b) * directionB
                    + 0.462 * cos(c) * directionC);
                half3 normalWS = normalize(float3(-slope.x, 1, -slope.y));
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light sun = GetMainLight();
                half diffuse = saturate(dot(normalWS, sun.direction));
                half3 color = lerp(_WaterDeep.rgb, _WaterLight.rgb, saturate(0.5 + wave * 0.5));
                color *= 0.55h + 0.45h * diffuse * sun.color;

                half3 halfway = SafeNormalize(viewDir + sun.direction);
                float glint = pow(saturate(dot(normalWS, halfway)), 96.0) * _SunGlint;
                color += sun.color * glint;
                float highlights = smoothstep(0.62, 0.95, wave) * _FoamAmount;
                color = lerp(color, _FoamColor.rgb, highlights);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 4.0);
                color = lerp(color, _WaterLight.rgb, fresnel * 0.35);

                // Evaluate the standard fog equations here to stay compatible
                // with URP versions that moved their fog helper functions.
                float eyeDepth = max(0.0, -TransformWorldToView(input.positionWS).z);
                float visibility = 1.0;
                #if defined(FOG_LINEAR)
                    visibility = saturate(eyeDepth * unity_FogParams.z + unity_FogParams.w);
                #elif defined(FOG_EXP)
                    visibility = saturate(exp2(-eyeDepth * unity_FogParams.y));
                #elif defined(FOG_EXP2)
                    float density = eyeDepth * unity_FogParams.x;
                    visibility = saturate(exp2(-density * density));
                #endif
                color = lerp(unity_FogColor.rgb, color, visibility);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
