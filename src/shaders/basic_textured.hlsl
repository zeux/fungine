cbuffer c0: register(cb0)
{
    float4x4 view_projection;
    float3 view_position;
    float roughness;
    float3 position_offset;
    float smoothness;
    float3 position_scale;
    float2 texcoord_offset;
    float2 texcoord_scale;
    float3x4 bones[2];
}

cbuffer c1: register(cb1)
{
    float3x4 offsets[2];
}

SamplerState default_sampler;

Texture2D<float4> albedo_map: register(t0);
Texture2D<float2> normal_map: register(t1);
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
	
    I.pos.xyz = I.pos.xyz * position_scale + position_offset;
    I.pos.w = 1;

    float3x4 transform = 0;

    [unroll] for (int i = 0; i < 4; ++i)
    {
        transform += bones[I.bone_indices[i]] * I.bone_weights[i];
    }

    float3 pos_ls = mul(transform, I.pos);
    float3 pos_ws = mul(offsets[instance], float4(pos_ls, 1));

	O.pos = mul(view_projection, float4(pos_ws, 1));

    O.pos_ws = pos_ws;
    O.normal = normalize(mul((float3x3)offsets[instance], mul((float3x3)transform, I.normal * 2 - 1)));
    O.tangent = normalize(mul((float3x3)offsets[instance], mul((float3x3)transform, I.tangent.xyz * 2 - 1)));
    O.bitangent = cross(O.normal, O.tangent) * (I.tangent.w * 2 - 1);
    O.uv0 = I.uv0 * texcoord_scale + texcoord_offset;
	
	return O;
}

static const float GAMMA = 2.2;

float3 degamma(float3 v)
{
    return pow(saturate(v), GAMMA);
}

float4 degamma(float4 v)
{
    return float4(degamma(v.xyz), v.w);
}

float3 gamma(float3 v)
{
    return pow(saturate(v), 1 / GAMMA);
}

float4 gamma(float4 v)
{
    return float4(gamma(v.xyz), v.w);
}

float3 sample_normal(Texture2D<float2> map, float2 uv)
{
    float2 xy = map.Sample(default_sampler, uv) * 2 - 1;
    xy *= 1 - smoothness;

    return float3(xy, sqrt(1 - dot(xy, xy)));
}

float4 ps_main(PS_IN I): SV_Target
{
    float3 normal_ts = sample_normal(normal_map, I.uv0);
    float3 normal = normalize(normal_ts.x * I.tangent + normal_ts.y * I.bitangent + normal_ts.z * I.normal);

    float4 albedo = degamma(albedo_map.Sample(default_sampler, I.uv0));

    if (albedo.a < 0.5) discard;

    float3 spec = degamma(specular_map.Sample(default_sampler, I.uv0));

    float3 light = normalize(float3(0, 1, 0.2));
    float diffuse = saturate(dot(normal, light));

    float3 view = normalize(view_position - I.pos_ws);
    float3 hvec = normalize(light + view);

    float cosnh = saturate(dot(hvec, normal));

    // Normalized Blinn-Phong
    float specpower = pow(2, roughness * 10);
    float3 specular = spec * pow(cosnh, specpower) * ((specpower + 8) / 8);

    float3 ambient = 0.1;

	return float4(gamma(albedo.rgb * (ambient + diffuse) + specular * diffuse), albedo.a);
}
