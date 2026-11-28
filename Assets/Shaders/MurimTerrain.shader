Shader "Murim/Terrain"
{
    Properties
    {
        _GroundMeadow ("Ground Meadow", 2D) = "white" {}
        _GroundEarth ("Ground Earth", 2D) = "white" {}
        _GroundGranite ("Ground Granite", 2D) = "white" {}
        _GroundDamp ("Ground Damp", 2D) = "white" {}
        _SlopeEarth ("Slope Earth", 2D) = "white" {}
        _SlopeRock ("Slope Rock", 2D) = "white" {}
        _CliffFace ("Cliff Face", 2D) = "white" {}
        _WetRock ("Wet Rock", 2D) = "white" {}
        _BlendMeadowEarth ("Blend Meadow Earth", 2D) = "gray" {}
        _BlendEarthGranite ("Blend Earth Granite", 2D) = "gray" {}
        _BlendMeadowGranite ("Blend Meadow Granite", 2D) = "gray" {}
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
            AlphaToMask On
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Assets/Shaders/MurimCommon.hlsl"

            TEXTURE2D(_GroundMeadow);      SAMPLER(sampler_GroundMeadow);
            TEXTURE2D(_GroundEarth);       SAMPLER(sampler_GroundEarth);
            TEXTURE2D(_GroundGranite);     SAMPLER(sampler_GroundGranite);
            TEXTURE2D(_GroundDamp);        SAMPLER(sampler_GroundDamp);
            TEXTURE2D(_SlopeEarth);        SAMPLER(sampler_SlopeEarth);
            TEXTURE2D(_SlopeRock);         SAMPLER(sampler_SlopeRock);
            TEXTURE2D(_CliffFace);         SAMPLER(sampler_CliffFace);
            TEXTURE2D(_WetRock);           SAMPLER(sampler_WetRock);
            TEXTURE2D(_BlendMeadowEarth);  SAMPLER(sampler_BlendMeadowEarth);
            TEXTURE2D(_BlendEarthGranite); SAMPLER(sampler_BlendEarthGranite);
            TEXTURE2D(_BlendMeadowGranite);SAMPLER(sampler_BlendMeadowGranite);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half coverage = MurimEvaluateCutCoverage(input.positionWS);

                float2 gridSize = max(_MurimGridSize.xy, float2(1.0, 1.0));
                float2 uvSurf = input.positionWS.xz / gridSize;
                float2 uvGround = input.positionWS.xz / MURIM_TILE_SCALE;

                half4 w0 = SAMPLE_TEXTURE2D(_MurimSurfaceTex0, sampler_MurimSurfaceTex0, uvSurf);
                half4 w1 = SAMPLE_TEXTURE2D(_MurimSurfaceTex1, sampler_MurimSurfaceTex1, uvSurf);

                half wM = w0.r;
                half wE = w0.g;
                half wG = w0.b;
                half wD = w0.a;
                half wSE = w1.r;
                half wSR = w1.g;
                half wK = w1.b;
                half wC = w1.a;

                half maskME = SAMPLE_TEXTURE2D(_BlendMeadowEarth, sampler_BlendMeadowEarth, uvGround).r;
                half maskEG = SAMPLE_TEXTURE2D(_BlendEarthGranite, sampler_BlendEarthGranite, uvGround).r;
                half maskMG = SAMPLE_TEXTURE2D(_BlendMeadowGranite, sampler_BlendMeadowGranite, uvGround).r;

                half dME = (maskME - 0.5h) * 2.0h * min(wM, wE);
                half dEG = (maskEG - 0.5h) * 2.0h * min(wE, wG);
                half dMG = (maskMG - 0.5h) * 2.0h * min(wM, wG);

                wM = max(0.0h, wM + dME + dMG);
                wE = max(0.0h, wE - dME + dEG);
                wG = max(0.0h, wG - dEG - dMG);

                float3 geomN = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                if (geomN.y < 0.0)
                {
                    geomN = -geomN;
                }

                bool steepRock = geomN.y < 0.6;
                float2 horizN = length(geomN.xz) > 1e-4 ? normalize(geomN.xz) : float2(0.0, -1.0);
                float2 horizT = float2(horizN.y, -horizN.x);
                float2 uvWall = float2(dot(input.positionWS.xz, horizT), input.positionWS.y) / MURIM_TILE_SCALE;

                half3 cGranite = SAMPLE_TEXTURE2D(_GroundGranite, sampler_GroundGranite, uvGround).rgb;
                half3 cMeadow  = SAMPLE_TEXTURE2D(_GroundMeadow,  sampler_GroundMeadow,  uvGround).rgb;
                half3 cEarth   = SAMPLE_TEXTURE2D(_GroundEarth,   sampler_GroundEarth,   uvGround).rgb;
                half3 cDamp    = SAMPLE_TEXTURE2D(_GroundDamp,    sampler_GroundDamp,    uvGround).rgb;
                half3 cSlopeE  = SAMPLE_TEXTURE2D(_SlopeEarth,    sampler_SlopeEarth,    uvGround).rgb;
                half3 cSlopeR  = SAMPLE_TEXTURE2D(_SlopeRock,     sampler_SlopeRock,     uvGround).rgb;
                half3 cCliff   = steepRock ? SAMPLE_TEXTURE2D(_CliffFace, sampler_CliffFace, uvWall).rgb : cGranite;
                half3 cWet     = steepRock ? SAMPLE_TEXTURE2D(_WetRock,   sampler_WetRock,   uvWall).rgb : cGranite;

                half totalW = max(wM + wE + wG + wD + wSE + wSR + wK + wC, 1e-4h);
                half3 albedo = (cMeadow * wM + cEarth * wE + cGranite * wG + cDamp * wD
                    + cSlopeE * wSE + cSlopeR * wSR + cWet * wK + cCliff * wC) / totalW;

                half3 litN = steepRock ? half3(geomN) : normalize(input.normalWS);
                half3 color = MurimLambertLit(albedo, input.positionWS, litN);
                return half4(color, coverage);
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Assets/Shaders/MurimCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = MurimShadowClipPos(output.positionWS, normalWS);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                MurimApplyCutawayShadow(input.positionWS);
                return 0;
            }
            ENDHLSL
        }
    }
}
