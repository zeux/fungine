#define FXAA_PC 1
#define FXAA_HLSL_5 1
#define FXAA_GREEN_AS_LUMA 1
#define FXAA_QUALITY__PRESET 12
#include "fxaa3.h"

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
    float fxaaSubpix = 0.75;
    float fxaaEdgeThreshold = 0.166;
    float fxaaEdgeThresholdMin = 0.0833;

    float2 fxaaFrame;
    colorMap.GetDimensions(fxaaFrame.x, fxaaFrame.y);

    FxaaTex tex = { defaultSampler, colorMap };

    return FxaaPixelShader(I.uv, 0, tex, tex, tex, 1 / fxaaFrame, 0, 0, 0, fxaaSubpix, fxaaEdgeThreshold, fxaaEdgeThresholdMin, 0, 0, 0, 0);
}
