// Maze lighting added on top of Unity's ambient and shadowless directional light (Progress: stage 5).
// Globals are set by LevelLighting and LevelLightMap (Presentation); all zero outside a level (editor preview)
// = no extra light.
#ifndef MAZE_LIGHTING_INCLUDED
#define MAZE_LIGHTING_INCLUDED

float4 _MazeLanternPosition; // xyz = world position, w = 1 / radius
float4 _MazeLanternColor;    // rgb = color * intensity

sampler2D _MazeLightMap;     // LightField: rgb = light / max, a = flickering share
float4 _MazeLightMapSize;    // xy = 1 / level size in cells, z = max light (0 = no light map)
float4 _MazeLightCeiling;    // x = height where the sources' light starts to fade, y = 1 / fade length,
                             // z = share left above it (all zero = no fading)

// Soft light of the lantern around the player: smooth falloff to zero at the radius, wrapped Lambert so the far
// sides of walls and characters are not pitch black.
float3 MazeLantern(float3 worldPos, float3 worldNormal)
{
    float3 toLight = _MazeLanternPosition.xyz - worldPos;
    float distance = length(toLight);
    float falloff = saturate(1 - distance * _MazeLanternPosition.w);
    falloff *= falloff;
    float wrap = saturate(dot(worldNormal, toLight / max(distance, 1e-4)) * 0.6 + 0.4);
    return _MazeLanternColor.rgb * (falloff * wrap);
}

// Torch-like flicker, 0.7..1, drifting smoothly over the level so neighbouring lights do not pulse in sync.
float MazeFlicker(float2 gridPos)
{
    float phase = gridPos.x * 0.73 + gridPos.y * 1.31;
    float t = _Time.y;
    return 0.85 + 0.15 * (sin(t * 9.1 + phase * 6.0) * 0.6 + sin(t * 23.7 + phase * 3.1) * 0.4);
}

// Light of the level's sources. The sample point is pushed a little along the normal, so the face of a wall reads
// the light of the floor in front of it, and a top reads its own cell.
float3 MazeGridLight(float3 worldPos, float3 worldNormal)
{
    float2 gridPos = worldPos.xz + worldNormal.xz * 0.15;
    float4 light = tex2D(_MazeLightMap, (gridPos + 0.5) * _MazeLightMapSize.xy);
    return light.rgb * _MazeLightMapSize.z * lerp(1, MazeFlicker(gridPos), light.a);
}

// Light of the sources keeps low, like a torch shining downwards: full on the floor, fading up the walls, only a
// small share on wall tops (the most visible part of a wall from the top-down camera).
float MazeLightCeiling(float height)
{
    return 1 - (1 - _MazeLightCeiling.z) * saturate((height - _MazeLightCeiling.x) * _MazeLightCeiling.y);
}

// Everything Maze adds; multiplied by albedo and added as emission. Characters and objects (Maze/Lit).
float3 MazeExtraLight(float3 worldPos, float3 worldNormal)
{
    return MazeLantern(worldPos, worldNormal) + MazeGridLight(worldPos, worldNormal);
}

// Same for level geometry (Maze/Geometry): the sources' light fades with height (MazeLightCeiling).
float3 MazeExtraLightGeometry(float3 worldPos, float3 worldNormal)
{
    return MazeLantern(worldPos, worldNormal) +
           MazeGridLight(worldPos, worldNormal) * MazeLightCeiling(worldPos.y);
}

#endif
