//# compute
#include <common/common.h>

#include <auto_LightGrid.h>
#include <auto_LightCullData.h>
#include <auto_Camera.h>

// 0 - use usual frustum planes
// 1 - use capsule approximation for frustum
#define CULL_METHOD 1

RWBuffer<uint> lightGridBufferUA;
Texture2D<float> depthBuffer;

CBUF(LightGrid, lightGrid);
CBUF_ARRAY(LightCullData, lightCullData);
CBUF(int, lightCount);
CBUF(Camera, camera);

float4x4 getTileFrustum(int x, int y, float2 zrange)
{
    float4x4 baseFrustum = camera.viewProjection;

    float width, height;
    depthBuffer.GetDimensions(width, height);

    float zscale = 1 / (zrange.y - zrange.x + 1e-10);

    float4x4 crop = float4x4(
        width/LIGHTGRID_CELLSIZE, 0, 0, -x,
        0, height/LIGHTGRID_CELLSIZE, 0, -y,
        0, 0, zscale, -zrange.x * zscale,
        0, 0, 0, 1);

    return mul(float4x4(2, 0, 0, -1, 0, -2, 0, +1, 0, 0, 1, 0, 0, 0, 0, 1), mul(crop, mul(float4x4(0.5, 0, 0, 0.5, 0, -0.5, 0, 0.5, 0, 0, 1, 0, 0, 0, 0, 1), baseFrustum)));
}

float4 normalizePlane(float4 plane)
{
    return plane / length(plane.xyz);
}

void getFrustumPlanes(float4x4 frustum, out float4 planes[6])
{
    planes[0] = normalizePlane(frustum[3] - frustum[0]);
    planes[1] = normalizePlane(frustum[3] + frustum[0]);
    planes[2] = normalizePlane(frustum[3] - frustum[1]);
    planes[3] = normalizePlane(frustum[3] + frustum[1]);
    planes[4] = normalizePlane(frustum[2]);
    planes[5] = normalizePlane(frustum[3] - frustum[2]);
}

bool isSphereVisible(float4 planes[6], float3 center, float radius)
{
    [unroll]
    for (int i = 0; i < 6; ++i)
        [flatten]
        if (dot(planes[i], float4(center, 1)) < -radius)
            return false;

    return true;
}

float getViewSpaceZ(float depth)
{
    // depth = (z * _33 + _34) / z
    return camera.projection._34 / (depth - camera.projection._33);
}

float3 getWorldPosition(float x, float y, float z)
{
    float4 r = mul(camera.viewProjectionInverse, float4(x * 2 - 1, 1 - 2 * y, z, 1));
    return r.xyz / r.w;
}

// Cone axis is defined by origin + t * direction where t in trange
// Cone points are defined by radius - distance between point and axispoint(t) should be <= radius * t
// I.e. radius is the section radius at t=1
struct Cone
{
    float3 origin;
    float3 direction;
    float2 trange;
    float radius;
};

float2 getConeTU(float3 p, Cone cone)
{
    float t = dot(p - cone.origin, cone.direction);
    float3 ap = cone.origin + t * cone.direction;

    float u = length(ap - p);

    return float2(t, u / t);
}

// return x, where, if d = distance from point to cone, then
// if d > 0, then x <= d
// if d < 0 (point inside cone) then x < 0
float distancePointToConeConservative(float3 p, Cone cone)
{
    // project point on the cone axis and clamp to valid range
    float t = clamp(dot(p - cone.origin, cone.direction), cone.trange.x, cone.trange.y);
    float3 ap = cone.origin + t * cone.direction;

    // get distance to the cone axis and cone section radius
    float ad = length(ap - p);

    // get distance to the cone surface (assuming infinite cone)
    // note that cone.radius = sin(cone angle / 2)
    float d = (ad - t * cone.radius) * sqrt(1 - cone.radius * cone.radius);

    return d;
}

groupshared uint2 gsZRange;
groupshared uint3 gsConeRange;
groupshared uint gsLightCount;

[numthreads(LIGHTGRID_CELLSIZE, LIGHTGRID_CELLSIZE, 1)]
void main(
    uint groupIndex: SV_GroupIndex,
    uint2 groupId: SV_GroupID,
    uint2 dispatchThreadId: SV_DispatchThreadID)
{
    // initialize group shared variables
    if (groupIndex == 0)
    {
        gsZRange = uint2(0x7f7fffff, 0); // FLT_MAX, 0
        gsConeRange = uint3(0x7f7fffff, 0, 0); // FLT_MAX, 0, 0
        gsLightCount = 0;
    }

    GroupMemoryBarrierWithGroupSync();

    uint width, height;
    depthBuffer.GetDimensions(width, height);

    // compute cone
    Cone cone;
    cone.origin = mul(camera.viewInverse, float4(0, 0, 0, 1));
    cone.direction =
        normalize(
            getWorldPosition(
                (groupId.x + 0.5) * LIGHTGRID_CELLSIZE / (float)width,
                (groupId.y + 0.5) * LIGHTGRID_CELLSIZE / (float)height,
                1) - cone.origin);

    // compute z range
    if (dispatchThreadId.x < width && dispatchThreadId.y < height)
    {
        float depth = depthBuffer[dispatchThreadId];

    #if CULL_METHOD == 0
        float z = depth;

        InterlockedMin(gsZRange.x, asuint(z));
        InterlockedMax(gsZRange.y, asuint(z));
    #elif CULL_METHOD == 1
        float3 pos = getWorldPosition((dispatchThreadId.x + 0.5) / (float)width, (dispatchThreadId.y + 0.5) / (float)height, depth);
        float2 tu = getConeTU(pos, cone);
        
        InterlockedMin(gsConeRange.x, asuint(tu.x));
        InterlockedMax(gsConeRange.y, asuint(tu.x));
        InterlockedMax(gsConeRange.z, asuint(tu.y));
    #else
        #error Unknown cull method
    #endif
    }

    GroupMemoryBarrierWithGroupSync();

#if CULL_METHOD == 0
    // compute frustum
    float2 zrange = asfloat(gsZRange);

    float4x4 frustum = getTileFrustum(groupId.x, groupId.y, zrange);

    float4 planes[6];
    getFrustumPlanes(frustum, planes);
#elif CULL_METHOD == 1
    // finalize cone construction
    cone.trange = asfloat(gsConeRange.xy);
    cone.radius = asfloat(gsConeRange.z);
#else
    #error Unknown cull method
#endif

    // cull lights
    int gridOffset = groupId.y * lightGrid.stride + groupId.x * lightGrid.tileSize;
    int gridLimit = lightGrid.tileSize - 1;

    for (int i = groupIndex; i < lightCount; i += LIGHTGRID_CELLSIZE * LIGHTGRID_CELLSIZE)
    {
        LightCullData light = lightCullData[i];

        // +1 to change bytecode so that AMD driver works with cbuffer [2]
        if (light.type + 1 == LIGHTTYPE_DIRECTIONAL + 1 ||
    #if CULL_METHOD == 1
        distancePointToConeConservative(light.position, cone) < light.radius
    #else
        isSphereVisible(planes, light.position, light.radius)
    #endif
        )
        {
            uint idx;
            InterlockedAdd(gsLightCount, 1, idx);
            lightGridBufferUA[gridOffset + min(idx, gridLimit)] = i + 1;
        }
    }

    GroupMemoryBarrierWithGroupSync();

    lightGridBufferUA[gridOffset + min(gsLightCount, gridLimit)] = 0;
}
