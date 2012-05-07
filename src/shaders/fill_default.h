#ifndef FILL_DEFAULT_H
#define FILL_DEFAULT_H

#include <common/common.h>
#include <common/gamma.h>

#include <auto_Camera.h>
#include <auto_Material.h>
#include <auto_MeshCompressionInfo.h>
#include <auto_LightData.h>
#include <auto_LightGrid.h>

CBUF(Camera, camera);
CBUF(MeshCompressionInfo, meshCompressionInfo);
CBUF(Material, material);

Buffer<uint> lightGridBuffer;
CBUF(LightGrid, lightGrid);
CBUF_ARRAY(LightData, lightData);

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
    float3 position: POSITION;

    float3 tangent: TANGENT;
    float3 bitangent: BITANGENT;
    float3 normal: NORMAL;
#endif
};

struct PS_OUT
{
#if !DEPTH_ONLY
    float4 color: SV_Target0;
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
    O.position = posWs;
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
    float4 albedo = degamma(albedoMap.Sample(defaultSampler, I.uv0));

    if (albedo.a < 0.5) discard;

    PS_OUT O;

#if !DEPTH_ONLY
    float3 normalTs = sampleNormal(normalMap, I.uv0);
    float3 normal = normalize(normalTs.x * I.tangent + normalTs.y * I.bitangent + normalTs.z * I.normal);

    float3 spec = degamma(specularMap.Sample(defaultSampler, I.uv0));

    float3 view = normalize(camera.eyePosition - I.position);

    int gridOffset = (int)(I.pos.y / LIGHTGRID_CELLSIZE) * lightGrid.stride + (int)(I.pos.x / LIGHTGRID_CELLSIZE) * lightGrid.tileSize;

    float3 diffuse = 0, specular = 0;

    for (int i = 0; i < lightGrid.tileSize; ++i)
    {
        int index = lightGridBuffer[gridOffset + i];
        if (index == 0) break;

        LightData light = lightData[index - 1];

        float3 lightUn = light.position - I.position;
        float3 L = normalize(lightUn);
        float attenDist = saturate(1 - length(lightUn) / light.radius);
        float attenCone = pow(saturate((dot(-L, light.direction) - light.outerAngle) / (light.innerAngle - light.outerAngle)), 4);

        float diff = saturate(dot(normal, L)) * attenDist * (light.type == LIGHTTYPE_SPOT ? attenCone : 1);

        float3 hvec = normalize(L + view);

        float cosnh = saturate(dot(hvec, normal));

        // Normalized Blinn-Phong
        float specpower = pow(2, material.roughness * 10);

        float3 lightcolor = light.color.rgb * light.intensity;

        diffuse += lightcolor * diff;
        specular += lightcolor * diff * pow(cosnh, specpower) * ((specpower + 8) / 8);
    }

    O.color = float4(albedo.rgb * diffuse + spec * specular * diffuse, albedo.a);
#endif

	return O;
}

#endif
