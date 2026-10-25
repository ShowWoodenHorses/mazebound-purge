// Static level geometry (floor, walls) of the Purge. Standard lighting (ambient + shadowless directional light;
// no realtime shadows, no per-pixel additional lights) plus Purge lighting (PurgeLighting.cginc: lantern) and
// per-cell visibility (ТЗ §54, §105): every vertex carries its grid cell in UV3; when PURGE_VISIBILITY is on,
// vertices of cells hidden in the global _PurgeVisibility mask collapse to a point, so a combined chunk mesh can
// hide single cells. Without the keyword (editor preview) everything is visible.
// Snowdrifts (PURGE_SNOWDRIFT, SnowdriftMask): the global _PurgeDrift mask (texel per cell, bilinear) lifts the colour of
// low up-facing surfaces towards the drift snow; noise makes the edge uneven, a band outside it gets a light shadow.
// One texture fetch; the mask is continuous, so neighbouring drift cells have no seams.
// Ground (PURGE_GROUND, GroundMask; exclusive with snowdrifts): the global _PurgeGround mask (texel per cell, bilinear)
// blends the low up-facing floor towards the theme's ground texture (_PurgeGroundTex, world space) with a noisy edge.
// Foliage (leaves, tall grass cards): _ALPHATEST_ON clips pixels with albedo alpha below _Cutoff; _Cull = Off draws
// both sides of the cards (materials made by Build Decor from the vegetation shaders of the packs; leaves of the biome
// props are on the cheaper Purge/Foliage).
// Two sub-shaders: LOD 200 — Standard (normal map, specular, reflections); LOD 150 — Lambert without the normal map,
// for phones (GeometryShader.ApplyQuality sets the shader's maximumLOD; Progress: «Производительность Леса», 7г).
Shader "Purge/Geometry"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.2
        [Gamma] _Metallic ("Metallic", Range(0, 1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        [Toggle(_ALPHATEST_ON)] _AlphaTest ("Alpha Test", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    CGINCLUDE
        #include "PurgeLighting.cginc"

        sampler2D _MainTex;
        fixed4 _Color;
        half4 _EmissionColor;
        half _Cutoff;

        #if PURGE_VISIBILITY
        sampler2D _PurgeVisibility;
        float4 _PurgeVisibilitySize; // x = 1 / width, y = 1 / height
        #endif

        #if PURGE_SNOWDRIFT || PURGE_GROUND
        float PurgeDriftHash(float2 p)
        {
            float3 p3 = frac(p.xyx * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float PurgeDriftNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3 - 2 * f);
            return lerp(lerp(PurgeDriftHash(i), PurgeDriftHash(i + float2(1, 0)), u.x),
                        lerp(PurgeDriftHash(i + float2(0, 1)), PurgeDriftHash(i + float2(1, 1)), u.x), u.y);
        }
        #endif

        #if PURGE_GROUND
        sampler2D _PurgeGround;
        sampler2D _PurgeGroundTex;
        float4 _PurgeGroundSize;   // x = 1 / width, y = 1 / height, z = 1 / tile cells
        float4 _PurgeGroundParams; // x = edge softness, y = edge noise, z = noise scale (per cell)

        // Ground over the floor: grid cell (x, y) is world (x, 0, y); the floor top is at y = 0.
        half3 PurgeGroundBlend(half3 albedo, float3 worldPos, float3 normal)
        {
            half surface = saturate((normal.y - 0.6) * 4) * saturate((0.06 - worldPos.y) * 20);
            float mask = tex2D(_PurgeGround, (worldPos.xz + 0.5) * _PurgeGroundSize.xy).r;
            float2 p = worldPos.xz * _PurgeGroundParams.z;
            float noise = 0.65 * PurgeDriftNoise(p + 41.7) + 0.35 * PurgeDriftNoise(p * 2.3 + 5.3);
            float edge = mask + (noise - 0.5) * _PurgeGroundParams.y;
            float soft = _PurgeGroundParams.x;
            half cover = smoothstep(0.5 - soft, 0.5 + soft, edge) * surface;
            half3 ground = tex2D(_PurgeGroundTex, (worldPos.xz + 0.5) * _PurgeGroundSize.z).rgb;
            return lerp(albedo, ground, cover);
        }
        #endif

        #if PURGE_SNOWDRIFT
        sampler2D _PurgeDrift;
        float4 _PurgeDriftSize;   // x = 1 / width, y = 1 / height
        half4 _PurgeDriftColor;   // linear
        float4 _PurgeDriftParams; // x = edge softness, y = edge noise, z = noise scale (per cell), w = foot shadow

        // Drift snow over the floor: grid cell (x, y) is world (x, 0, y), its texel centre is at (x + 0.5) / width.
        half3 PurgeSnowdrift(half3 albedo, float3 worldPos, float3 normal)
        {
            half surface = saturate((normal.y - 0.4) * 3) * saturate((0.35 - worldPos.y) * 8);
            float mask = tex2D(_PurgeDrift, (worldPos.xz + 0.5) * _PurgeDriftSize.xy).r;
            float2 p = worldPos.xz * _PurgeDriftParams.z;
            float noise = 0.65 * PurgeDriftNoise(p) + 0.35 * PurgeDriftNoise(p * 2.3 + 17.1);
            float edge = mask + (noise - 0.5) * _PurgeDriftParams.y;
            float soft = _PurgeDriftParams.x;
            half cover = smoothstep(0.5 - soft, 0.5 + soft, edge) * surface;
            half foot = smoothstep(0.5 - soft - 0.2, 0.5 - soft, edge) * (1 - cover) * surface * _PurgeDriftParams.w;
            half lum = dot(albedo, half3(0.2126, 0.7152, 0.0722));
            half3 drift = saturate(_PurgeDriftColor.rgb * (0.72 + 0.55 * lum));
            return lerp(albedo, drift, cover) * (1 - foot);
        }
        #endif

        // Hides the vertices of the cells hidden by visibility (the cell in UV3).
        void PurgeCollapseHidden(inout appdata_full v)
        {
            #if PURGE_VISIBILITY
            float2 cell = floor(v.texcoord3.xy + 0.5);
            float visible = tex2Dlod(_PurgeVisibility, float4((cell + 0.5) * _PurgeVisibilitySize.xy, 0, 0)).r;
            v.vertex.xyz *= step(0.5, visible);
            #endif
        }

        // Albedo with the alpha test, snowdrifts and ground.
        fixed3 PurgeGeometryAlbedo(float2 uv, float3 worldPos, float3 normal)
        {
            fixed4 albedo = tex2D(_MainTex, uv) * _Color;
            #if _ALPHATEST_ON
            clip(albedo.a - _Cutoff);
            #endif
            #if PURGE_SNOWDRIFT
            albedo.rgb = PurgeSnowdrift(albedo.rgb, worldPos, normal);
            #elif PURGE_GROUND
            albedo.rgb = PurgeGroundBlend(albedo.rgb, worldPos, normal);
            #endif
            return albedo.rgb;
        }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        Cull [_Cull]

        CGPROGRAM
        // target 3.5: vertex texture fetch (GLES 3 / WebGL 2).
        #pragma surface surf Standard vertex:vert noforwardadd nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.5
        #pragma multi_compile _ PURGE_VISIBILITY
        #pragma multi_compile _ PURGE_SNOWDRIFT PURGE_GROUND
        #pragma shader_feature_local _ALPHATEST_ON

        sampler2D _BumpMap;
        half _Glossiness;
        half _Metallic;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
            float3 worldPos;
            float3 purgeNormal; // world space, from the vertex (the normal map does not matter for the extra light)
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.purgeNormal = UnityObjectToWorldNormal(v.normal);
            PurgeCollapseHidden(v);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 normal = normalize(IN.purgeNormal);
            fixed3 albedo = PurgeGeometryAlbedo(IN.uv_MainTex, IN.worldPos, normal);
            o.Albedo = albedo;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = _EmissionColor.rgb + albedo * PurgeExtraLightGeometry(IN.worldPos, normal);
            o.Alpha = 1;
        }
        ENDCG
    }

    // Phones: no normal map, specular or reflections.
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 150
        Cull [_Cull]

        CGPROGRAM
        // target 3.5: vertex texture fetch (GLES 3 / WebGL 2).
        #pragma surface surf Lambert vertex:vert noforwardadd nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.5
        #pragma multi_compile _ PURGE_VISIBILITY
        #pragma multi_compile _ PURGE_SNOWDRIFT PURGE_GROUND
        #pragma shader_feature_local _ALPHATEST_ON

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 purgeNormal; // world space
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.purgeNormal = UnityObjectToWorldNormal(v.normal);
            PurgeCollapseHidden(v);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 normal = normalize(IN.purgeNormal);
            fixed3 albedo = PurgeGeometryAlbedo(IN.uv_MainTex, IN.worldPos, normal);
            o.Albedo = albedo;
            o.Emission = _EmissionColor.rgb + albedo * PurgeExtraLightGeometry(IN.worldPos, normal);
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
