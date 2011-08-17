#include "../common/gbuffer.h"

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

float4 ps_main(PS_IN I): SV_Target
{
    Surface S = gbufSampleSurface(I.uv);

    float4 pos_ws_h = mul(view_projection_inv, float4(I.uv * float2(2, -2) + float2(-1, 1), S.depth, 1));
    float3 pos_ws = pos_ws_h.xyz / pos_ws_h.w;

    float3 light = normalize(float3(0, 1, 0.2));
    float diffuse = saturate(dot(S.normal, light));

    float3 view = normalize(view_position - pos_ws);
    float3 hvec = normalize(light + view);

    float cosnh = saturate(dot(hvec, S.normal));

    // Normalized Blinn-Phong
    float specpower = pow(2, S.roughness * 10);
    float3 specular = S.specular * pow(cosnh, specpower) * ((specpower + 8) / 8);

    float3 ambient = 0.1;

	return float4(S.albedo * (ambient + diffuse) + specular * diffuse, 1);
}
