cbuffer c0: register(cb0)
{
    float4x4 view_projection;
    float4x4 view_projection_inv;
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
    float3 spot_position;
    float spot_outer;
    float3 spot_direction;
    float spot_inner;
    float3 spot_color;
    float spot_radius;
}

SamplerState default_sampler;

Texture2D<float4> albedo_buffer: register(t0);
Texture2D<float4> specular_buffer: register(t1);
Texture2D<float4> normal_buffer: register(t2);
Texture2D<float4> depth_buffer: register(t3);

struct PS_IN
{
	float4 pos: SV_POSITION;
    float2 uv: TEXCOORD;
};

PS_IN vs_main(uint id: SV_VertexID)
{
    // form a full-screen triangle
    float2 pos = float2(id == 1 ? 2 : 0, id == 2 ? 2 : 0);

    PS_IN O;
    O.pos = float4(pos.x * 2 - 1, 1 - pos.y * 2, 0, 1);
    O.uv = pos.xy;

    return O;
}

float4 gamma(float4 color)
{
    return float4(pow(saturate(color.rgb), 2.2), color.a);
}

float4 ps_main(PS_IN I): SV_Target
{
    float3 albedo = gamma(albedo_buffer.Sample(default_sampler, I.uv)).rgb;
    float4 spec = gamma(specular_buffer.Sample(default_sampler, I.uv));
    float roughness = spec.a;
    float3 normal = normal_buffer.Sample(default_sampler, I.uv).rgb * 2 - 1;
    float depth = depth_buffer.Sample(default_sampler, I.uv).r;

    float4 pos_ws_h = mul(view_projection_inv, float4(I.uv * float2(2, -2) + float2(-1, 1), depth, 1));
    float3 pos_ws = pos_ws_h.xyz / pos_ws_h.w;

    float3 light_un = spot_position - pos_ws;
    float3 light = normalize(light_un);
    float atten_dist = saturate(1 - length(light_un) / spot_radius);
    float atten_cone = pow(saturate((dot(-light, spot_direction) - spot_outer) / (spot_inner - spot_outer)), 4);

    float diffuse = saturate(dot(normal, light)) * atten_dist * atten_cone;

    float3 view = normalize(view_position - pos_ws);
    float3 hvec = normalize(light + view);

    float cosnh = saturate(dot(hvec, normal));

    // Normalized Blinn-Phong
    float specpower = pow(2, roughness * 10);
    float3 specular = spec.rgb * pow(cosnh, specpower) * ((specpower + 8) / 8);

	return float4(spot_color * (albedo.rgb * diffuse + specular * diffuse), 1);
}
