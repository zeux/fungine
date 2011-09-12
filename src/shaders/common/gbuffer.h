// vim: ft=fx
#ifndef GBUFFER_H
#define GBUFFER_H

#include "gamma.h"

struct Surface
{
    float3 albedo;
    float3 normal;
    float3 specular;
    float roughness;
    float depth;
};

SamplerState gbufSampler;

Texture2D gbufAlbedo;
Texture2D gbufSpecular;
Texture2D gbufNormal;
Texture2D gbufDepth;

Surface gbufSampleSurface(float2 uv)
{
    Surface O;

    O.albedo = degamma(gbufAlbedo.Sample(gbufSampler, uv)).rgb;
    O.normal = gbufNormal.Sample(gbufSampler, uv).xyz * 2 - 1;

    float4 spec = gbufSpecular.Sample(gbufSampler, uv);
    O.specular = degamma(spec.rgb);
    O.roughness = spec.a;

    O.depth = gbufDepth.Sample(gbufSampler, uv).r;

    return O;
}

#endif
