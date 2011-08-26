#include <common/gbuffer.h>

#include <auto_Camera.h>
#include <auto_SpotLight.h>

cbuffer camera { Camera camera; };
cbuffer light { SpotLight light; };

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

    float4 pos_ws_h = mul(camera.view_projection_inverse, float4(I.uv * float2(2, -2) + float2(-1, 1), S.depth, 1));
    float3 pos_ws = pos_ws_h.xyz / pos_ws_h.w;

    float3 light_un = light.position - pos_ws;
    float3 L = normalize(light_un);
    float atten_dist = saturate(1 - length(light_un) / light.radius);
    float atten_cone = pow(saturate((dot(-L, light.direction) - light.outer_angle) / (light.inner_angle - light.outer_angle)), 4);

    float diffuse = saturate(dot(S.normal, L)) * atten_dist * atten_cone;

    float3 view = normalize(camera.eye_position - pos_ws);
    float3 hvec = normalize(L + view);

    float cosnh = saturate(dot(hvec, S.normal));

    // Normalized Blinn-Phong
    float specpower = pow(2, S.roughness * 10);
    float3 specular = S.specular * pow(cosnh, specpower) * ((specpower + 8) / 8);

	return float4(light.color * (S.albedo * diffuse + specular * diffuse), 1);
}
