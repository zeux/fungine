#include <common/common.h>

#include <auto_LightGrid.h>

SamplerState defaultSampler;

Buffer<uint> lightGridBuffer;
CBUF(LightGrid, lightGrid);

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
    if ((uint)I.pos.x % LIGHTGRID_CELLSIZE == 0)
        return float4(0.f.xxx, 1.0);

    if ((uint)I.pos.y % LIGHTGRID_CELLSIZE == 0)
        return float4(0.f.xxx, 1.0);

    int gridOffset = (int)(I.pos.y / LIGHTGRID_CELLSIZE) * lightGrid.stride + (int)(I.pos.x / LIGHTGRID_CELLSIZE) * lightGrid.tileSize;

    int count = 0;

    for (int i = 0; i < lightGrid.tileSize; ++i)
    {
        int index = lightGridBuffer[gridOffset + i];
        if (index == 0) break;
        count++;
    }

    return float4((count / 4.f).xxx, 1.0);
}
