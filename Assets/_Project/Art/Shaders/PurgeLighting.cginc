// Purge lighting added on top of Unity's ambient and shadowless directional light (Progress: stage 5).
// Globals are set by LevelLighting and LevelLightMap (Presentation); all zero outside a level (editor preview)
// = no extra light.
#ifndef PURGE_LIGHTING_INCLUDED
#define PURGE_LIGHTING_INCLUDED

float4 _PurgeLanternPosition; // xyz = world position, w = 1 / radius
float4 _PurgeLanternColor;    // rgb = color * intensity
float4 _PurgeShotLightPosition; // flash of the player's shot (WeaponFeelView): xyz = world position, w = 1 / radius
float4 _PurgeShotLightColor;    // rgb = color * intensity, fading to zero in a few frames

sampler2D _PurgeLightMap;     // LightField: rgb = light / max, a = flickering share
float4 _PurgeLightMapSize;    // xy = 1 / level size in cells, z = max light (0 = no light map)
float4 _PurgeLightCeiling;    // x = height where the sources' light starts to fade, y = 1 / fade length,
                             // z = share left above it (all zero = no fading)

// Soft light of the lantern around the player: smooth falloff to zero at the radius, wrapped Lambert so the far
// sides of walls and characters are not pitch black.
float3 PurgeLantern(float3 worldPos, float3 worldNormal)
{
    float3 toLight = _PurgeLanternPosition.xyz - worldPos;
    float distance = length(toLight);
    float falloff = saturate(1 - distance * _PurgeLanternPosition.w);
    falloff *= falloff;
    float wrap = saturate(dot(worldNormal, toLight / max(distance, 1e-4)) * 0.6 + 0.4);
    return _PurgeLanternColor.rgb * (falloff * wrap);
}

// The flash of a shot at the muzzle: like the lantern, only for a moment (zero colour = nothing).
float3 PurgeShotLight(float3 worldPos, float3 worldNormal)
{
    float3 toLight = _PurgeShotLightPosition.xyz - worldPos;
    float distance = length(toLight);
    float falloff = saturate(1 - distance * _PurgeShotLightPosition.w);
    falloff *= falloff;
    float wrap = saturate(dot(worldNormal, toLight / max(distance, 1e-4)) * 0.6 + 0.4);
    return _PurgeShotLightColor.rgb * (falloff * wrap);
}

// Torch-like flicker, 0.7..1, drifting smoothly over the level so neighbouring lights do not pulse in sync.
float PurgeFlicker(float2 gridPos)
{
    float phase = gridPos.x * 0.73 + gridPos.y * 1.31;
    float t = _Time.y;
    return 0.85 + 0.15 * (sin(t * 9.1 + phase * 6.0) * 0.6 + sin(t * 23.7 + phase * 3.1) * 0.4);
}

// Light of the level's sources. The sample point is pushed a little along the normal, so the face of a wall reads
// the light of the floor in front of it, and a top reads its own cell.
float3 PurgeGridLight(float3 worldPos, float3 worldNormal)
{
    float2 gridPos = worldPos.xz + worldNormal.xz * 0.15;
    float4 light = tex2D(_PurgeLightMap, (gridPos + 0.5) * _PurgeLightMapSize.xy);
    return light.rgb * _PurgeLightMapSize.z * lerp(1, PurgeFlicker(gridPos), light.a);
}

// Same in a vertex program (Purge/Foliage lights per vertex): an explicit mip level.
float3 PurgeGridLightVertex(float3 worldPos, float3 worldNormal)
{
    float2 gridPos = worldPos.xz + worldNormal.xz * 0.15;
    float4 light = tex2Dlod(_PurgeLightMap, float4((gridPos + 0.5) * _PurgeLightMapSize.xy, 0, 0));
    return light.rgb * _PurgeLightMapSize.z * lerp(1, PurgeFlicker(gridPos), light.a);
}

// Light of the sources keeps low, like a torch shining downwards: full on the floor, fading up the walls, only a
// small share on wall tops (the most visible part of a wall from the top-down camera).
float PurgeLightCeiling(float height)
{
    return 1 - (1 - _PurgeLightCeiling.z) * saturate((height - _PurgeLightCeiling.x) * _PurgeLightCeiling.y);
}

// Everything Purge adds; multiplied by albedo and added as emission. Characters and objects (Purge/Lit).
float3 PurgeExtraLight(float3 worldPos, float3 worldNormal)
{
    return PurgeLantern(worldPos, worldNormal) + PurgeShotLight(worldPos, worldNormal) + PurgeGridLight(worldPos, worldNormal);
}

// Same for level geometry (Purge/Geometry): the sources' light fades with height (PurgeLightCeiling).
float3 PurgeExtraLightGeometry(float3 worldPos, float3 worldNormal)
{
    return PurgeLantern(worldPos, worldNormal) + PurgeShotLight(worldPos, worldNormal) +
           PurgeGridLight(worldPos, worldNormal) * PurgeLightCeiling(worldPos.y);
}

// Same in a vertex program (Purge/Foliage).
float3 PurgeExtraLightGeometryVertex(float3 worldPos, float3 worldNormal)
{
    return PurgeLantern(worldPos, worldNormal) + PurgeShotLight(worldPos, worldNormal) +
           PurgeGridLightVertex(worldPos, worldNormal) * PurgeLightCeiling(worldPos.y);
}

#endif
