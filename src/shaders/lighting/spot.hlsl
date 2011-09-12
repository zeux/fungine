#include <common/gbuffer.h>

#include <auto_Camera.h>
#include <auto_SpotLight.h>

cbuffer camera { Camera camera; };
cbuffer light { SpotLight light; };
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

float SampleShadow(float2 uv, float depth)
{
    float result = 0;
    
    [unroll] for (int x = -1; x <= 1; ++x)
        [unroll] for (int y = -1; y <= 1; ++y)
        {
            result += shadowMap.SampleCmpLevelZero(shadowSampler, uv, depth, int2(x, y));
        }

    return result / 9;
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
    float shadow = SampleShadow(posLs.xy * float2(0.5, -0.5) + 0.5,  posLs.z - 1e-5);

	return float4(shadow * light.color * (S.albedo * diffuse + specular * diffuse), 1);
}
