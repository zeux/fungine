#include <auto_Camera.h>

cbuffer camera { Camera camera; };

cbuffer mesh
{
    float roughness;
    float3 positionOffset;
    float smoothness;
    float3 positionScale;
    float2 texcoordOffset;
    float2 texcoordScale;
    float3x4 bones[2];
}

cbuffer transforms
{
    float3x4 offsets[2];
}

SamplerState defaultSampler;

Texture2D<float4> albedoMap;
Texture2D<float2> normalMap;
Texture2D<float3> specularMap;

struct VS_IN
{
	float4 pos: POSITION;
    float3 normal: NORMAL;
    float4 tangent: TANGENT;
	float2 uv0: TEXCOORD0;
    uint4 boneIndices: BONEINDICES;
    float4 boneWeights: BONEWEIGHTS;
};

struct PS_IN
{
	float4 pos: SV_POSITION;

    float3 tangent: TANGENT;
    float3 bitangent: BITANGENT;
    float3 normal: NORMAL;
	float2 uv0: TEXCOORD0;
};

struct PS_OUT
{
    float4 albedo: SV_Target0;
    float4 specular: SV_Target1;
    float4 normal: SV_Target2;
};

PS_IN vsMain(VS_IN I, uint instance: SV_InstanceId)
{
	PS_IN O;
	
    I.pos.xyz = I.pos.xyz * positionScale + positionOffset;
    I.pos.w = 1;

    float3x4 transform = 0;

    [unroll] for (int i = 0; i < 4; ++i)
    {
        transform += bones[I.boneIndices[i]] * I.boneWeights[i];
    }

    float3 posLs = mul(transform, I.pos);
    float3 posWs = mul(offsets[instance], float4(posLs, 1));

	O.pos = mul(camera.viewProjection, float4(posWs, 1));
    O.normal = normalize(mul((float3x3)offsets[instance], mul((float3x3)transform, I.normal * 2 - 1)));
    O.tangent = normalize(mul((float3x3)offsets[instance], mul((float3x3)transform, I.tangent.xyz * 2 - 1)));
    O.bitangent = cross(O.normal, O.tangent) * (I.tangent.w * 2 - 1);
    O.uv0 = I.uv0 * texcoordScale + texcoordOffset;
	
	return O;
}

float3 sampleNormal(Texture2D<float2> map, float2 uv)
{
    float2 xy = map.Sample(defaultSampler, uv) * 2 - 1;
    xy *= 1 - smoothness;

    return float3(xy, sqrt(1 - dot(xy, xy)));
}

PS_OUT psMain(PS_IN I)
{
    float3 normalTs = sampleNormal(normalMap, I.uv0);
    float3 normal = normalize(normalTs.x * I.tangent + normalTs.y * I.bitangent + normalTs.z * I.normal);

    float4 albedo = albedoMap.Sample(defaultSampler, I.uv0);

    if (albedo.a < 0.5) discard;

    float3 spec = specularMap.Sample(defaultSampler, I.uv0);

    PS_OUT O;
    O.albedo = albedo;
    O.specular = float4(spec, roughness);
    O.normal = float4(normal * 0.5 + 0.5, 0);

	return O;
}
