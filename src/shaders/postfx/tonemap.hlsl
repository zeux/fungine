SamplerState default_sampler;

Texture2D<float4> color_map: register(t0);

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
    float4 color = color_map.Sample(default_sampler, I.uv);
    float3 srgb = pow(saturate(color.rgb), 1 / 2.2);

    // store luma in alpha for fxaa
    return float4(srgb, dot(srgb, float3(0.299, 0.587, 0.114)));
}
