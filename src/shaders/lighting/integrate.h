#ifndef LIGHTING_INTEGRATE_H
#define LIGHTING_INTEGRATE_H

#include <lighting/shadowmap.h>

#include <auto_LightData.h>
#include <auto_LightGrid.h>

Buffer<uint> lightGridBuffer;
CBUF(LightGrid, lightGrid);
CBUF_ARRAY(LightData, lightData);

struct LightInput
{
    float3 direction;
    float3 color;
};

float getLightShadow(LightData light, float3 position)
{
    float4 p = mul(light.shadowData.transform, float4(position, 1));
    p.xyz /= p.w;
    p.xy = saturate(p.xy * float2(0.5, -0.5) + 0.5);
    p.xy = p.xy * light.shadowData.atlasScale + light.shadowData.atlasOffset;

    return sampleShadowFiltered(p.xy, p.z - 1e-6);
}

LightInput getLightInput(LightData light, float3 position)
{
    float3 lightUn = light.position - position;
    float3 L = normalize(lightUn);

    float attenDist = saturate(1 - length(lightUn) / light.radius);
    float attenCone = pow(saturate((dot(-L, light.direction) - light.outerAngle) / (light.innerAngle - light.outerAngle)), 4);

    float atten = (light.type == LIGHTTYPE_DIRECTIONAL ? 1 : attenDist) * (light.type == LIGHTTYPE_SPOT ? attenCone : 1);
    
    float shadow = getLightShadow(light, position);

    LightInput result;
    result.direction = light.type == LIGHTTYPE_DIRECTIONAL ? -light.direction : L;
    result.color = light.color.rgb * (light.intensity * atten * shadow);

    return result;
}

#define integrateBRDF(hpos, position, brdf) { \
    int gridOffset = (int)(hpos.y / LIGHTGRID_CELLSIZE) * lightGrid.stride + (int)(hpos.x / LIGHTGRID_CELLSIZE) * lightGrid.tileSize; \
    \
    for (int lightIter = 0; lightIter < lightGrid.tileSize; ++lightIter) { \
        int lightIndex = lightGridBuffer[gridOffset + lightIter]; \
        if (lightIndex == 0) break; \
        \
        LightData light = lightData[lightIndex - 1]; \
        LightInput L = getLightInput(light, position); \
        \
        brdf \
    } }

#endif
