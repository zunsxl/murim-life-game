Shader "Murim/Cap"
{
    Properties
    {
        _GroundEarth ("Ground Earth", 2D) = "white" {}
        _CliffFace ("Cliff Face", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+1"
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

            TEXTURE2D(_GroundEarth); SAMPLER(sampler_GroundEarth);
            TEXTURE2D(_CliffFace);   SAMPLER(sampler_CliffFace);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  isFromCap  : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                bool useFrom = input.uv2.x > 0.5;
                posWS.y = useFrom ? _MurimCutYFrom : _MurimCutY;

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.isFromCap = input.uv2.x;
                return output;
            }

            float EvaluateCoreTerrainHeight(float2 xz)
            {
                float2 gridSize = max(_MurimGridSize.xy, float2(1.0, 1.0));
                float2 vertSize = max(_MurimGridSize.zw, float2(2.0, 2.0));

                float maxCellX = gridSize.x - 1.0;
                float maxCellZ = gridSize.y - 1.0;
                float cxF = clamp(xz.x, 0.0, gridSize.x);
                float czF = clamp(xz.y, 0.0, gridSize.y);

                float cx = min(floor(cxF), maxCellX);
                float cz = min(floor(czF), maxCellZ);
                float u = cxF - cx;
                float v = czF - cz;

                float h00 = SAMPLE_TEXTURE2D_LOD(_MurimHeightTex, sampler_MurimHeightTex, float2((cx + 0.5) / vertSize.x, (cz + 0.5) / vertSize.y), 0).r;
                float h11 = SAMPLE_TEXTURE2D_LOD(_MurimHeightTex, sampler_MurimHeightTex, float2((cx + 1.5) / vertSize.x, (cz + 1.5) / vertSize.y), 0).r;

                if (u >= v)
                {
                    float h10 = SAMPLE_TEXTURE2D_LOD(_MurimHeightTex, sampler_MurimHeightTex, float2((cx + 1.5) / vertSize.x, (cz + 0.5) / vertSize.y), 0).r;
                    return h00 + u * (h10 - h00) + v * (h11 - h10);
                }

                float h01 = SAMPLE_TEXTURE2D_LOD(_MurimHeightTex, sampler_MurimHeightTex, float2((cx + 0.5) / vertSize.x, (cz + 1.5) / vertSize.y), 0).r;
                return h00 + u * (h11 - h01) + v * (h01 - h00);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.positionWS.z >= MURIM_NORTH_UNCUT_Z)
                {
                    discard;
                }

                float fade = saturate(_MurimCutFade);
                bool useFrom = input.isFromCap > 0.5;
                if (useFrom && fade >= 0.9999)
                {
                    discard;
                }

                half coverage = 1.0h;
                if (fade < 0.9999)
                {
                    coverage = useFrom ? half(1.0 - fade) : half(fade);
                    clip(coverage - 0.001h);
                }

                float cutY = useFrom ? _MurimCutYFrom : _MurimCutY;
                float terrainH = EvaluateCoreTerrainHeight(input.positionWS.xz);
                clip(terrainH - cutY - 1e-4);

                float2 gridSize = max(_MurimGridSize.xy, float2(1.0, 1.0));
                float2 uvSurf = input.positionWS.xz / gridSize;
                float2 uvTile = input.positionWS.xz / MURIM_TILE_SCALE;

                half4 w0 = SAMPLE_TEXTURE2D(_MurimSurfaceTex0, sampler_MurimSurfaceTex0, uvSurf);
                half4 w1 = SAMPLE_TEXTURE2D(_MurimSurfaceTex1, sampler_MurimSurfaceTex1, uvSurf);

                half earthW = w0.r + w0.g + w0.a + w1.r;
                half rockW = w0.b + w1.g + w1.b + w1.a;
                half rockRatio = rockW / max(earthW + rockW, 1e-4h);

                half3 cEarth = SAMPLE_TEXTURE2D(_GroundEarth, sampler_GroundEarth, uvTile).rgb;
                half3 cCliff = SAMPLE_TEXTURE2D(_CliffFace, sampler_CliffFace, uvTile).rgb;
                half3 albedo = lerp(cEarth, cCliff, rockRatio);

                half3 color = MurimLambertLit(albedo, input.positionWS, half3(0.0h, 1.0h, 0.0h));
                return half4(color, coverage);
            }
            ENDHLSL
        }
    }
}
