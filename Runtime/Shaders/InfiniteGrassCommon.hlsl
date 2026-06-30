#ifndef INFINITE_GRASS_INCLUDED
#define INFINITE_GRASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

half3 ApplySingleDirectLight(Light light, half3 N, half3 V, half3 albedo, half mask, half positionY)
{
    half3 H = normalize(light.direction + V);

    half directDiffuse = dot(N, light.direction) * 0.5 + 0.5;

    float directSpecular = saturate(dot(N, H));
    directSpecular *= directSpecular;
    directSpecular *= directSpecular;
    directSpecular *= directSpecular;
    directSpecular *= directSpecular;

    directSpecular *= positionY * 0.12;

    half3 lighting = light.color * (light.shadowAttenuation * light.distanceAttenuation);
    half3 result = (albedo * directDiffuse + directSpecular * (1 - mask)) * lighting;

    return result; 
}

uint murmurHash3(float input)
{
    uint h = abs(input);
    h ^= h >> 16;
    h *= 0x85ebca6b;
    h ^= h >> 13;
    h *= 0xc2b2ae3d;
    h ^= h >> 16;
    return h;
}

float random(float input)
{
    return murmurHash3(input) / 4294967295.0;
}

float srandom(float input)
{
    return murmurHash3(input) / 4294967295.0 * 2 - 1;
}

float Remap(float In, float2 InMinMax, float2 OutMinMax)
{
    return OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
}

float3 CalculateLighting(float3 albedo, float3 positionWS, float3 N, float3 V, float mask, float positionY)
{
    half3 result = SampleSH(N) * albedo;
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
    result += ApplySingleDirectLight(mainLight, N, V, albedo, mask, positionY);
    
    int additionalLightsCount = GetAdditionalLightsCount();
    
    LIGHT_LOOP_BEGIN(additionalLightsCount)
    Light light = GetAdditionalLight(lightIndex, positionWS);
    result += ApplySingleDirectLight(light, N, V, albedo, mask, positionY);
    LIGHT_LOOP_END
    
    return result;
}

#endif