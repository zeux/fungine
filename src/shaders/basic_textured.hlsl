cbuffer c0: register(cb0)
{
    float4x4 view_projection: register(c0);
    float4x4 bones[2]: register(c4);
}

cbuffer c1: register(cb1)
{
    float4x4 offsets[2];
}

SamplerState default_sampler;

Texture2D<float4> albedo_map: register(t0);
Texture2D<float3> normal_map: register(t1);
Texture2D<float3> specular_map: register(t2);

struct VS_IN
{
	float4 pos: POSITION;
    float3 normal: NORMAL;
    float4 tangent: TANGENT;
	float2 uv0: TEXCOORD0;
    uint4 bone_indices: BONEINDICES;
    float4 bone_weights: BONEWEIGHTS;
};

struct PS_IN
{
	float4 pos: SV_POSITION;

    float3 pos_ws: WORLDPOS;
    float3 tangent: TANGENT;
    float3 bitangent: BITANGENT;
    float3 normal: NORMAL;
	float2 uv0: TEXCOORD0;
};

PS_IN vs_main(VS_IN I, uint instance: SV_InstanceId)
{
	PS_IN O;
	
    float4 pos_ls = 0;

    [unroll] for (int i = 0; i < 4; ++i)
    {
        pos_ls += mul(I.pos, bones[I.bone_indices[i]]) * I.bone_weights[i];
    }

    float4 pos_ws = mul(pos_ls, offsets[instance]);

	O.pos = mul(pos_ws, view_projection);

    O.pos_ws = pos_ws.xyz;
    O.normal = I.normal;
    O.tangent = I.tangent.xyz;
    O.bitangent = cross(O.normal, O.tangent) * I.tangent.w;
    O.uv0 = I.uv0;
	
	return O;
}

float4 ps_main(PS_IN I): SV_Target
{
    float3 normal_ts = normal_map.Sample(default_sampler, I.uv0) * 2 - 1;
    float3 normal = normalize(normal_ts.x * I.tangent + normal_ts.y * I.bitangent + normal_ts.z * I.normal);

    float4 albedo = albedo_map.Sample(default_sampler, I.uv0);

    if (albedo.a < 0.5) discard;

    float3 spec = specular_map.Sample(default_sampler, I.uv0);

    float3 light = normalize(float3(0, 1, 1));
    float diffuse = saturate(dot(normal, light) * 0.5 + 0.5);

    float3 view = normalize(float3(0, 20, 35) - I.pos_ws);
    float3 reflected = reflect(-view, normal);

    float3 specular = spec * pow(saturate(dot(reflected, light)), 20);

	return albedo_map.Sample(default_sampler, I.uv0) * diffuse + float4(specular, 0);
}
