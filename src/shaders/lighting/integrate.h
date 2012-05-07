#ifndef LIGHTING_INTEGRATE_H
#define LIGHTING_INTEGRATE_H

#include <auto_LightData.h>
#include <auto_LightGrid.h>

Buffer<uint> lightGridBuffer;
CBUF(LightGrid, lightGrid);
CBUF_ARRAY(LightData, lightData);

struct LightInput
{
    float3 direction;
    float3 color;
    float attenuation;
};

LightInput getLightInput(LightData light, float3 position)
{
    float3 lightUn = light.position - position;
    float3 L = normalize(lightUn);

    float attenDist = saturate(1 - length(lightUn) / light.radius);
    float attenCone = pow(saturate((dot(-L, light.direction) - light.outerAngle) / (light.innerAngle - light.outerAngle)), 4);

    float atten = attenDist * (light.type == LIGHTTYPE_SPOT ? attenCone : 1);

    LightInput result;
    result.direction = L;
    result.color = light.color.rgb * light.intensity;
    result.attenuation = atten;

    return result;
}

#define IntegrateBRDF(hpos, position, brdf) { \
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
