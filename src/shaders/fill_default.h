#ifndef FILL_DEFAULT_H
#define FILL_DEFAULT_H

#include <auto_Camera.h>
#include <auto_Material.h>
#include <auto_MeshCompressionInfo.h>

cbuffer camera { Camera camera; };
cbuffer meshCompressionInfo { MeshCompressionInfo meshCompressionInfo; };
cbuffer material { Material material; };

cbuffer mesh
{
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
    uint4 boneIndices: BONEINDICES;
    float4 boneWeights: BONEWEIGHTS;
	float2 uv0: TEXCOORD0;

#if !DEPTH_ONLY
    float3 normal: NORMAL;
    float4 tangent: TANGENT;
#endif
};

struct PS_IN
{
	float4 pos: SV_POSITION;

	float2 uv0: TEXCOORD0;

#if !DEPTH_ONLY
    float3 tangent: TANGENT;
    float3 bitangent: BITANGENT;
    float3 normal: NORMAL;
#endif
};

struct PS_OUT
{
#if !DEPTH_ONLY
    float4 albedo: SV_Target0;
    float4 specular: SV_Target1;
    float4 normal: SV_Target2;
#endif
};

PS_IN vsMain(uint instance: SV_InstanceId, VS_IN I)
{
	PS_IN O;
	
    I.pos.xyz = I.pos.xyz * meshCompressionInfo.posScale + meshCompressionInfo.posOffset;
    I.pos.w = 1;

    float3x4 transform = 0;

    [unroll] for (int i = 0; i < 4; ++i)
    {
        transform += bones[I.boneIndices[i]] * I.boneWeights[i];
    }

    float3 posLs = mul(transform, I.pos);
    float3 posWs = mul(offsets[instance], float4(posLs, 1));

	O.pos = mul(camera.viewProjection, float4(posWs, 1));
    O.uv0 = I.uv0 * meshCompressionInfo.uvScale + meshCompressionInfo.uvOffset;

#if !DEPTH_ONLY
    O.normal = normalize(mul((float3x3)offsets[instance], mul((float3x3)transform, I.normal * 2 - 1)));
    O.tangent = normalize(mul((float3x3)offsets[instance], mul((float3x3)transform, I.tangent.xyz * 2 - 1)));
    O.bitangent = cross(O.normal, O.tangent) * (I.tangent.w * 2 - 1);
#endif
	
	return O;
}

float3 sampleNormal(Texture2D<float2> map, float2 uv)
{
    float2 xy = map.Sample(defaultSampler, uv) * 2 - 1;
    xy *= 1 - material.smoothness;

    return float3(xy, sqrt(1 - dot(xy, xy)));
}

PS_OUT psMain(PS_IN I)
{
    float4 albedo = albedoMap.Sample(defaultSampler, I.uv0);

    if (albedo.a < 0.5) discard;

    PS_OUT O;

#if !DEPTH_ONLY
    float3 normalTs = sampleNormal(normalMap, I.uv0);
    float3 normal = normalize(normalTs.x * I.tangent + normalTs.y * I.bitangent + normalTs.z * I.normal);

    float3 spec = specularMap.Sample(defaultSampler, I.uv0);

    O.albedo = albedo;
    O.specular = float4(spec, material.roughness);
    O.normal = float4(normal * 0.5 + 0.5, 0);
#endif

	return O;
}

#endif
