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

SamplerState gbuf_sampler: register(s0);

Texture2D gbuf_albedo: register(t0);
Texture2D gbuf_specular: register(t1);
Texture2D gbuf_normal: register(t2);
Texture2D gbuf_depth: register(t3);

Surface gbufSampleSurface(float2 uv)
{
    Surface O;

    O.albedo = degamma(gbuf_albedo.Sample(gbuf_sampler, uv)).rgb;
    O.normal = gbuf_normal.Sample(gbuf_sampler, uv).xyz * 2 - 1;

    float4 spec = gbuf_specular.Sample(gbuf_sampler, uv);
    O.specular = degamma(spec.rgb);
    O.roughness = spec.a;

    O.depth = gbuf_depth.Sample(gbuf_sampler, uv).r;

    return O;
}

#endif
