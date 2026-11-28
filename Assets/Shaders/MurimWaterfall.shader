Shader "Murim/Waterfall"
{
    Properties
    {
        _FallRibbon ("Fall Ribbon", 2D) = "white" {}
        _WaterfallSpeed ("Waterfall Speed", Float) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Assets/Shaders/MurimCommon.hlsl"

            TEXTURE2D(_FallRibbon); SAMPLER(sampler_FallRibbon);
            float _WaterfallSpeed;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv0        : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float2 uv0        : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
                output.uv0 = input.uv0;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half cutCoverage = MurimEvaluateCutCoverage(input.positionWS);

                float vScale = 3.1622777 / MURIM_TILE_SCALE;
                float vScroll = _MurimAmbientTime * (_WaterfallSpeed / MURIM_TILE_SCALE);
                float2 uv = float2(input.uv0.x, input.uv0.y * vScale + vScroll);

                half4 tex = SAMPLE_TEXTURE2D(_FallRibbon, sampler_FallRibbon, uv);
                half3 color = MurimLambertLit(tex.rgb, input.positionWS, normalize(input.normalWS));
                return half4(color, tex.a * cutCoverage);
            }
            ENDHLSL
        }
    }
}
