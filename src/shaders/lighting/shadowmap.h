#ifndef LIGHTING_SHADOWMAP_H
#define LIGHTING_SHADOWMAP_H

Texture2D<float> shadowMap;
SamplerComparisonState shadowSampler;

float sampleShadow(float2 uv, float depth, int2 offset = 0)
{
    return shadowMap.SampleCmpLevelZero(shadowSampler, uv, depth, offset);
}

float2 getShadowMapSize()
{
    float2 result;
    shadowMap.GetDimensions(result.x, result.y);

    return result;
}

float sampleShadowPCF(float2 uv, float depth, int size)
{
    int radius = size / 2;
    int area = (radius * 2 + 1) * (radius * 2 + 1);

    float result = 0;
    
    [unroll] for (int x = -radius; x <= radius; ++x)
        [unroll] for (int y = -radius; y <= radius; ++y)
        {
            result += sampleShadow(uv, depth, int2(x, y));
        }

    return result / area;
}

float sampleShadowPoisson(float2 uv, float depth, int size)
{
    const float2 offsets[13] =
    {
        {0, 0},
        {0.200887527842703, -0.805816066868008}, {0.169759583602972, 0.787268282932537}, {-0.639597778815228, -0.370236979450183},
        {0.629148098269191, 0.367398185340504}, {-0.456211403483755, 0.542374725109481}, {-0.411828977295713, -0.484566120009967},
        {0.106871055317844, 0.59918690478536}, {-0.117388437710382, -0.518626519610864}, {0.436862373204161, -0.182937652849816},
        {0.206632546759667, 0.343668469981935}, {-0.330409446933952, 0.175773621457362}, {-0.1, -0.16089955706328},
    };

    float2 scale = 1 / getShadowMapSize();

    float result = 0;

    [unroll] for (int i = 0; i < size; ++i)
    {
        result += sampleShadow(uv + offsets[i] * scale, depth);
    }

    return result / size;
}

float sampleShadowFXAA(float2 uv, float depth)
{
    float2 scale = 1 / getShadowMapSize();

    float shadowM = sampleShadow(uv, depth);
    float shadowNW = sampleShadow(uv, depth, int2(-1, -1));
    float shadowNE = sampleShadow(uv, depth, int2(+1, -1));
    float shadowSW = sampleShadow(uv, depth, int2(-1, +1));
    float shadowSE = sampleShadow(uv, depth, int2(+1, +1));

    float shadowDiag1 = shadowSW - shadowNE;
    float shadowDiag2 = shadowSE - shadowNW;

    float2 dir = float2(shadowDiag1 + shadowDiag2, shadowDiag1 - shadowDiag2);

    if (dot(dir, dir) < 0.01) return shadowM;

    float2 offset = normalize(dir) * scale;

    float shadowD1 = sampleShadow(uv - offset, depth);
    float shadowD2 = sampleShadow(uv + offset, depth);

    return shadowM * 0.5 + shadowD1 * 0.25 + shadowD2 * 0.25;
}

float sampleShadowFiltered(float2 uv, float depth)
{
    return sampleShadowPoisson(uv, depth, 8);
}

#endif
