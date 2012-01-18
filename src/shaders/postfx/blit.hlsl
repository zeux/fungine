SamplerState defaultSampler;

Texture2D<float4> colorMap;

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

float4 psMain(PS_IN I): SV_Target
{
    float3 color = colorMap.Sample(defaultSampler, I.uv).rgb;

    return float4(color, 1);
}
