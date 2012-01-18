#include <auto_Camera.h>

SamplerState defaultSampler;

Texture2D<float4> colorMap;

cbuffer blitUnpackDepth { bool blitUnpackDepth; }
cbuffer camera { Camera camera; };

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

    if (blitUnpackDepth)
    {
        float depth = color.x;
        float4x4 proj = camera.projection;

        float zn = -proj._34 / proj._33;
        float zf = (proj._33 * zn) / (proj._33 - 1);

        float z = proj._34 / (depth * proj._43 - proj._33);

        color = sqrt(saturate((z - zn) / (zf - zn)));
    }

    return float4(color, 1);
}
