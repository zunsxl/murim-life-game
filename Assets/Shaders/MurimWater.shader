Shader "Murim/Water"
{
    Properties
    {
        _RiverFlow ("River Flow", 2D) = "white" {}
        _RiverRiffle ("River Riffle", 2D) = "black" {}
        _RiverShore ("River Shore", 2D) = "black" {}
        _BlendDampRiver ("Blend Damp River", 2D) = "gray" {}
        _DecalTex ("Decal Texture", 2D) = "white" {}
        _RiverFlowSpeed ("River Flow Speed", Float) = 1.2
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _AlphaToMask ("AlphaToMask", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            AlphaToMask [_AlphaToMask]
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _WATER_DECAL
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Assets/Shaders/MurimCommon.hlsl"

            #define FOAM_ANGULAR_FREQ 5.23598776

            TEXTURE2D(_RiverFlow);     SAMPLER(sampler_RiverFlow);
            TEXTURE2D(_RiverRiffle);   SAMPLER(sampler_RiverRiffle);
            TEXTURE2D(_RiverShore);    SAMPLER(sampler_RiverShore);
            TEXTURE2D(_BlendDampRiver);SAMPLER(sampler_BlendDampRiver);
            TEXTURE2D(_DecalTex);      SAMPLER(sampler_DecalTex);

            float _RiverFlowSpeed;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv0        : TEXCOORD1;
                float4 shoreEdges : TEXCOORD2;
                float4 diagShore  : TEXCOORD3;
                float  isRiffle   : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);

#if defined(_WATER_DECAL)
                float2 centreXZ = input.uv1.xy;
                float isFoam = input.uv1.z;
                if (isFoam > 0.5)
                {
                    float scale = 1.0 + 0.05 * sin(_MurimAmbientTime * FOAM_ANGULAR_FREQ);
                    posWS.xz = centreXZ + (posWS.xz - centreXZ) * scale;
                }
#endif

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.uv0 = input.uv0;
                output.shoreEdges = input.uv1;
                output.diagShore = input.color;
                output.isRiffle = input.uv2.x;
                return output;
            }

            float ComputeShoreDistance(float2 cellUv, float4 cardinal, float4 diag)
            {
                float dW = cardinal.x > 0.5 ? cellUv.x : 2.0;
                float dE = cardinal.y > 0.5 ? (1.0 - cellUv.x) : 2.0;
                float dS = cardinal.z > 0.5 ? cellUv.y : 2.0;
                float dN = cardinal.w > 0.5 ? (1.0 - cellUv.y) : 2.0;

                float dSW = diag.x > 0.5 ? length(cellUv) : 2.0;
                float dSE = diag.y > 0.5 ? length(float2(1.0 - cellUv.x, cellUv.y)) : 2.0;
                float dNW = diag.z > 0.5 ? length(float2(cellUv.x, 1.0 - cellUv.y)) : 2.0;
                float dNE = diag.w > 0.5 ? length(float2(1.0 - cellUv.x, 1.0 - cellUv.y)) : 2.0;

                return min(min(min(dW, dE), min(dS, dN)), min(min(dSW, dSE), min(dNW, dNE)));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half cutCoverage = MurimEvaluateCutCoverage(input.positionWS);

#if defined(_WATER_DECAL)
                half4 decal = SAMPLE_TEXTURE2D(_DecalTex, sampler_DecalTex, input.uv0);
                half3 decalLit = MurimLambertLit(decal.rgb, input.positionWS, half3(0.0h, 1.0h, 0.0h));
                return half4(decalLit, decal.a * cutCoverage);
#else
                float scrollU = _MurimAmbientTime * (_RiverFlowSpeed / MURIM_TILE_SCALE);
                float2 uvBase = input.positionWS.xz / MURIM_TILE_SCALE;

                half3 albedo = SAMPLE_TEXTURE2D(_RiverFlow, sampler_RiverFlow, uvBase + float2(scrollU, 0.0)).rgb;

                if (input.isRiffle > 0.5)
                {
                    half4 riffle = SAMPLE_TEXTURE2D(_RiverRiffle, sampler_RiverRiffle, uvBase + float2(scrollU * 2.0, 0.0));
                    albedo = lerp(albedo, riffle.rgb, riffle.a);
                }

                float shoreDist = ComputeShoreDistance(input.uv0, input.shoreEdges, input.diagShore);
                if (shoreDist < 1.0)
                {
                    half mask = SAMPLE_TEXTURE2D(_BlendDampRiver, sampler_BlendDampRiver, uvBase).r;
                    float uShore = (input.positionWS.x + input.positionWS.z) / MURIM_TILE_SCALE;
                    float vShore = saturate(shoreDist + (mask - 0.5) * 0.45);
                    half4 shore = SAMPLE_TEXTURE2D(_RiverShore, sampler_RiverShore, float2(uShore, vShore));
                    half shoreAlpha = shore.a * half(1.0 - smoothstep(0.65, 1.0, shoreDist));
                    albedo = lerp(albedo, shore.rgb, shoreAlpha);
                }

                half3 color = MurimLambertLit(albedo, input.positionWS, half3(0.0h, 1.0h, 0.0h));
                return half4(color, cutCoverage);
#endif
            }
            ENDHLSL
        }
    }
}
