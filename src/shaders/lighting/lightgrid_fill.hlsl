//# compute
#include <auto_LightGrid.h>

RWBuffer<uint> lightGridBufferUA;
cbuffer lightGrid { LightGrid lightGrid; }

[numthreads(1, 1, 1)]
void main(uint3 tid: SV_DispatchThreadID)
{
    int gridOffset = tid.y * lightGrid.stride + tid.x * lightGrid.tileSize;

    if (tid.x % 2 == 0)
        lightGridBufferUA[gridOffset++] = 1;

    if (tid.x % 3 == 0)
        lightGridBufferUA[gridOffset++] = 2;

    lightGridBufferUA[gridOffset] = 0;
}
