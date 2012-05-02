#include <common/gbuffer.h>

#include <auto_Camera.h>
#include <auto_LightData.h>

cbuffer camera { Camera camera; };
cbuffer light { LightData light; };
cbuffer lightCamera { Camera lightCamera; };

Texture2D<float> shadowMap;
SamplerComparisonState shadowSampler;

struct PS_IN
{
	float4 pos: SV_POSITION;
    float2 uv: TEXCOORD;
};

PS_IN vsMain(uint id: SV_VertexID)
{
    // form a full-screen triangle
    float2 pos = float2(id == 1 ? 2 : 0, id == 2 ? 2 : 0);

    PS_IN O;
    O.pos = float4(pos.x * 2 - 1, 1 - pos.y * 2, 0, 1);
    O.uv = pos.xy;

    return O;
}

float SampleShadow(float2 uv, float depth, int2 offset = 0)
{
    return shadowMap.SampleCmpLevelZero(shadowSampler, uv, depth, offset);
}

float2 GetShadowMapSize()
{
    float2 result;
    shadowMap.GetDimensions(result.x, result.y);

    return result;
}

float SampleShadowPCF(float2 uv, float depth, int size)
{
    int radius = size / 2;
    int area = (radius * 2 + 1) * (radius * 2 + 1);

    float result = 0;
    
    [unroll] for (int x = -radius; x <= radius; ++x)
        [unroll] for (int y = -radius; y <= radius; ++y)
        {
            result += SampleShadow(uv, depth, int2(x, y));
        }

    return result / area;
}

float SampleShadowPoisson(float2 uv, float depth, int size)
{
    const float2 offsets[13] =
    {
        {0, 0},
        {0.200887527842703, -0.805816066868008}, {0.169759583602972, 0.787268282932537}, {-0.639597778815228, -0.370236979450183},
        {0.629148098269191, 0.367398185340504}, {-0.456211403483755, 0.542374725109481}, {-0.411828977295713, -0.484566120009967},
        {0.106871055317844, 0.59918690478536}, {-0.117388437710382, -0.518626519610864}, {0.436862373204161, -0.182937652849816},
        {0.206632546759667, 0.343668469981935}, {-0.330409446933952, 0.175773621457362}, {-0.1, -0.16089955706328},
    };

    float2 scale = 1 / GetShadowMapSize();

    float result = 0;

    [unroll] for (int i = 0; i < size; ++i)
    {
        result += SampleShadow(uv + offsets[i] * scale, depth);
    }

    return result / size;
}

float SampleShadowFXAA(float2 uv, float depth)
{
    float2 scale = 1 / GetShadowMapSize();

    float shadowM = SampleShadow(uv, depth);
    float shadowNW = SampleShadow(uv, depth, int2(-1, -1));
    float shadowNE = SampleShadow(uv, depth, int2(+1, -1));
    float shadowSW = SampleShadow(uv, depth, int2(-1, +1));
    float shadowSE = SampleShadow(uv, depth, int2(+1, +1));

    float shadowDiag1 = shadowSW - shadowNE;
    float shadowDiag2 = shadowSE - shadowNW;

    float2 dir = float2(shadowDiag1 + shadowDiag2, shadowDiag1 - shadowDiag2);

    if (dot(dir, dir) < 0.01) return shadowM;

    float2 offset = normalize(dir) * scale;

    float shadowD1 = SampleShadow(uv - offset, depth);
    float shadowD2 = SampleShadow(uv + offset, depth);

    return shadowM * 0.5 + shadowD1 * 0.25 + shadowD2 * 0.25;
}

float SampleShadowFiltered(float2 uv, float depth)
{
    return SampleShadowFXAA(uv, depth);
}

float4 psMain(PS_IN I): SV_Target
{
    Surface S = gbufSampleSurface(I.uv);

    float4 posWsH = mul(camera.viewProjectionInverse, float4(I.uv * float2(2, -2) + float2(-1, 1), S.depth, 1));
    float3 posWs = posWsH.xyz / posWsH.w;

    float3 lightUn = light.position - posWs;
    float3 L = normalize(lightUn);
    float attenDist = saturate(1 - length(lightUn) / light.radius);
    float attenCone = pow(saturate((dot(-L, light.direction) - light.outerAngle) / (light.innerAngle - light.outerAngle)), 4);

    float diffuse = saturate(dot(S.normal, L)) * attenDist * attenCone;

    float3 view = normalize(camera.eyePosition - posWs);
    float3 hvec = normalize(L + view);

    float cosnh = saturate(dot(hvec, S.normal));

    // Normalized Blinn-Phong
    float specpower = pow(2, S.roughness * 10);
    float3 specular = S.specular * pow(cosnh, specpower) * ((specpower + 8) / 8);

    // Shadow
    float4 posLsH = mul(lightCamera.viewProjection, float4(posWs, 1));
    float3 posLs = posLsH.xyz / posLsH.w;
    float shadow = SampleShadowFiltered(posLs.xy * float2(0.5, -0.5) + 0.5,  posLs.z - 1e-5);

	return float4(shadow * light.color.rgb * light.intensity * (S.albedo * diffuse + specular * diffuse), 1);
}
