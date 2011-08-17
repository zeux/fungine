#ifndef COMMON_H
#define COMMON_H

static const float kGamma = 2.2;

float3 degamma(float3 v)
{
    return pow(saturate(v), kGamma);
}

float4 degamma(float4 v)
{
    return float4(degamma(v.rgb), v.a);
}

float3 gamma(float3 v)
{
    return pow(saturate(v), 1 / kGamma);
}

float4 gamma(float4 v)
{
    return float4(gamma(v.rgb), v.a);
}

#endif
