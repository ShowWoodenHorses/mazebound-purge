// Static level geometry (floor, walls) of the Purge. Standard lighting (ambient + shadowless directional light;
// no realtime shadows, no per-pixel additional lights) plus Purge lighting (PurgeLighting.cginc: lantern) and
// per-cell visibility (ТЗ §54, §105): every vertex carries its grid cell in UV3; when PURGE_VISIBILITY is on,
// vertices of cells hidden in the global _PurgeVisibility mask collapse to a point, so a combined chunk mesh can
// hide single cells. Without the keyword (editor preview) everything is visible.
// Snowdrifts (PURGE_SNOWDRIFT, SnowdriftMask): the global _PurgeDrift mask (texel per cell, bilinear) lifts the colour of
// low up-facing surfaces towards the drift snow; noise makes the edge uneven, a band outside it gets a light shadow.
// One texture fetch; the mask is continuous, so neighbouring drift cells have no seams.
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
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        // target 3.5: vertex texture fetch (GLES 3 / WebGL 2).
        #pragma surface surf Standard vertex:vert noforwardadd nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.5
        #pragma multi_compile _ PURGE_VISIBILITY
        #pragma multi_compile _ PURGE_SNOWDRIFT
        #include "PurgeLighting.cginc"

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half4 _EmissionColor;

        #if PURGE_VISIBILITY
        sampler2D _PurgeVisibility;
        float4 _PurgeVisibilitySize; // x = 1 / width, y = 1 / height
        #endif

        #if PURGE_SNOWDRIFT
        sampler2D _PurgeDrift;
        float4 _PurgeDriftSize;   // x = 1 / width, y = 1 / height
        half4 _PurgeDriftColor;   // linear
        float4 _PurgeDriftParams; // x = edge softness, y = edge noise, z = noise scale (per cell), w = foot shadow

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
            #if PURGE_VISIBILITY
            float2 cell = floor(v.texcoord3.xy + 0.5);
            float visible = tex2Dlod(_PurgeVisibility, float4((cell + 0.5) * _PurgeVisibilitySize.xy, 0, 0)).r;
            v.vertex.xyz *= step(0.5, visible);
            #endif
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            #if PURGE_SNOWDRIFT
            albedo.rgb = PurgeSnowdrift(albedo.rgb, IN.worldPos, normalize(IN.purgeNormal));
            #endif
            o.Albedo = albedo.rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = _EmissionColor.rgb + albedo.rgb * PurgeExtraLightGeometry(IN.worldPos, normalize(IN.purgeNormal));
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
