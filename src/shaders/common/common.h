#ifndef COMMON_COMMON_H
#define COMMON_COMMON_H

#define CBUF(type, name) cbuffer name { type name; }
#define CBUF_ARRAY(type, name) cbuffer name { type name[2]; }

static float4 _DebugResult;
static bool _Debug;

void debug(float4 value)
{
    _DebugResult = value;
    _Debug = true;
}

void debug(float3 value)
{
    debug(float4(value, 1));
}

void debug(float2 value)
{
    debug(float4(value, 0, 1));
}

void debug(float value)
{
    debug(float4(value.xxx, 1));
}

#endif
