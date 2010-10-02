struct VS_IN
{
	float4 pos: POSITION;
	float4 col: COLOR;
};

struct PS_IN
{
	float4 pos: SV_POSITION;
	float4 col: COLOR;
};

PS_IN vs_main(VS_IN I)
{
	PS_IN O;
	
	O.pos = I.pos;
	O.col = I.col;
	
	return O;
}

float4 ps_main(PS_IN I): SV_Target
{
	return I.col;
}
