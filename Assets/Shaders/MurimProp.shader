Shader "Murim/Prop"
{
    Properties
    {
        _PropAtlas ("Prop Atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            AlphaToMask On
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Assets/Shaders/MurimCommon.hlsl"

            TEXTURE2D(_PropAtlas); SAMPLER(sampler_PropAtlas);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
                float4 uv2        : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0        : TEXCOORD0;
                float3 clipPosWS  : TEXCOORD1;
                float4 swayInfo   : TEXCOORD2;
                float4 pineInfo   : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);

                float amplitude = input.uv1.x;
                float period = max(input.uv1.y, 0.001);
                float phase = input.uv1.z;
                float normH = input.uv1.w;
                float cutoff = input.uv2.x;

                float wave = MurimSmoothTriangleWave(_MurimAmbientTime / period + phase);
                float dx = amplitude * wave;
                if (cutoff <= 0.01 && amplitude > 0.0)
                {
                    posWS.x += dx * saturate(normH);
                }

                output.clipPosWS = posWS;
                float3 viewDir = normalize(GetWorldSpaceViewDir(posWS));
                float3 biasedWS = posWS + viewDir * MURIM_SPRITE_DEPTH_BIAS;

                output.positionCS = TransformWorldToHClip(biasedWS);
                output.uv0 = input.uv0;
                output.swayInfo = float4(dx, normH, cutoff, input.uv2.y);
                output.pineInfo = float4(input.uv2.z, input.uv2.w, 0.0, 0.0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half cutCoverage = MurimEvaluateCutCoverage(input.clipPosWS);

                float2 uv = input.uv0;
                float cutoff = input.swayInfo.z;
                if (cutoff > 0.01)
                {
                    float normH = input.swayInfo.y;
                    float weight = saturate((normH - cutoff) / (1.0 - cutoff));
                    uv.x -= input.swayInfo.x * weight * input.swayInfo.w;
                    if (uv.x < input.pineInfo.x || uv.x > input.pineInfo.y)
                    {
                        discard;
                    }
                }

                half4 tex = SAMPLE_TEXTURE2D(_PropAtlas, sampler_PropAtlas, uv);
                clip(tex.a - 0.5h);

                half3 color = MurimFixedLightLit(tex.rgb, input.clipPosWS);
                return half4(color, cutCoverage);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Assets/Shaders/MurimCommon.hlsl"

            TEXTURE2D(_PropAtlas); SAMPLER(sampler_PropAtlas);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
                float4 uv2        : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0        : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 swayInfo   : TEXCOORD2;
                float4 pineInfo   : TEXCOORD3;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                float amplitude = input.uv1.x;
                float period = max(input.uv1.y, 0.001);
                float phase = input.uv1.z;
                float normH = input.uv1.w;
                float cutoff = input.uv2.x;

                float wave = MurimSmoothTriangleWave(_MurimAmbientTime / period + phase);
                float dx = amplitude * wave;
                if (cutoff <= 0.01 && amplitude > 0.0)
                {
                    posWS.x += dx * saturate(normH);
                }

                output.positionWS = posWS;
                output.positionCS = MurimShadowClipPos(posWS, normalWS);
                output.uv0 = input.uv0;
                output.swayInfo = float4(dx, normH, cutoff, input.uv2.y);
                output.pineInfo = float4(input.uv2.z, input.uv2.w, 0.0, 0.0);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                MurimApplyCutawayShadow(input.positionWS);

                float2 uv = input.uv0;
                float cutoff = input.swayInfo.z;
                if (cutoff > 0.01)
                {
                    float normH = input.swayInfo.y;
                    float weight = saturate((normH - cutoff) / (1.0 - cutoff));
                    uv.x -= input.swayInfo.x * weight * input.swayInfo.w;
                    if (uv.x < input.pineInfo.x || uv.x > input.pineInfo.y)
                    {
                        discard;
                    }
                }

                half alpha = SAMPLE_TEXTURE2D(_PropAtlas, sampler_PropAtlas, uv).a;
                clip(alpha - 0.5h);
                return 0;
            }
            ENDHLSL
        }
    }
}
