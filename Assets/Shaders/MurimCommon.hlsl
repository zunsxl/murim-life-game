#ifndef MURIM_COMMON_INCLUDED
#define MURIM_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

#define MURIM_NORTH_UNCUT_Z 90.0
#define MURIM_TILE_SCALE 8.0
#define MURIM_SPRITE_DEPTH_BIAS 0.7

float _MurimCutY;
float _MurimCutYFrom;
float _MurimCutFade;
float _MurimAmbientTime;
float4 _MurimGridSize;

TEXTURE2D(_MurimHeightTex);
SAMPLER(sampler_MurimHeightTex);

TEXTURE2D(_MurimSurfaceTex0);
SAMPLER(sampler_MurimSurfaceTex0);

TEXTURE2D(_MurimSurfaceTex1);
SAMPLER(sampler_MurimSurfaceTex1);

float3 _LightDirection;
float3 _LightPosition;

half MurimEvaluateCutCoverage(float3 positionWS)
{
    if (positionWS.z >= MURIM_NORTH_UNCUT_Z)
    {
        return 1.0h;
    }

    float fade = saturate(_MurimCutFade);
    if (fade >= 0.9999)
    {
        clip(_MurimCutY - positionWS.y);
        return 1.0h;
    }

    float cutMin = min(_MurimCutY, _MurimCutYFrom);
    float cutMax = max(_MurimCutY, _MurimCutYFrom);
    clip(cutMax - positionWS.y);

    if (positionWS.y <= cutMin)
    {
        return 1.0h;
    }

    half cov = (_MurimCutY < _MurimCutYFrom) ? half(1.0 - fade) : half(fade);
    clip(cov - 0.001h);
    return cov;
}

void MurimApplyCutawayShadow(float3 positionWS)
{
    half cov = MurimEvaluateCutCoverage(positionWS);
    clip(cov - 0.5h);
}

float4 MurimShadowClipPos(float3 positionWS, float3 normalWS)
{
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirWS = _LightDirection;
#endif
    float4 posCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));
    return ApplyShadowClamping(posCS);
}

half3 MurimLambertLit(half3 albedo, float3 positionWS, half3 normalWS)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    half NdotL = saturate(dot(normalWS, mainLight.direction));
    half3 direct = mainLight.color * (NdotL * mainLight.shadowAttenuation);
    half3 ambient = unity_AmbientSky.rgb;
    return albedo * (direct + ambient);
}

half3 MurimFixedLightLit(half3 albedo, float3 positionWS)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    // Matches flat-ground NdotL under the fixed 50-deg directional daylight so painted sprites match terrain brightness.
    half fixedNdotL = 0.766h;
    half3 direct = mainLight.color * (fixedNdotL * mainLight.shadowAttenuation);
    half3 ambient = unity_AmbientSky.rgb;
    return albedo * (direct + ambient);
}

float MurimSmoothTriangleWave(float phaseInput)
{
    float f = frac(phaseInput);
    float tri = 1.0 - abs(f * 2.0 - 1.0);
    return tri * tri * (3.0 - 2.0 * tri) * 2.0 - 1.0;
}

#endif
