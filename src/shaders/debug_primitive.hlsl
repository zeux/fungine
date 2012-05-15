#include <common/common.h>

#include <auto_Camera.h>

CBUF(Camera, camera);

struct VS_IN
{
	float3 pos: POSITION;
    float4 color: COLOR;
};

struct PS_IN
{
	float4 pos: SV_POSITION;
	float4 color: COLOR;
};

PS_IN vsMain(VS_IN I)
{
	PS_IN O;
	
	O.pos = mul(camera.viewProjection, float4(I.pos, 1));
    O.color = I.color;
	
	return O;
}

float4 psMain(PS_IN I): SV_Target
{
    return I.color;
}
