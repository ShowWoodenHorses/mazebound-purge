// Shared lit shader of everything "alive" in the level: characters, weapons, doors, keys, pickups. Same property
// names as Standard (a material switched from Standard keeps its values), Standard lighting from ambient and the
// shadowless directional light, plus Purge lighting (PurgeLighting.cginc: lantern, light of the level's sources).
// No realtime shadows, no per-pixel additional lights. Opaque only. _HitFlash (set from code) flashes the model when hit;
// _Frost (set from code, per model) whitens it with the theme's frost.
Shader "Purge/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.3
        [Gamma] _Metallic ("Metallic", Range(0, 1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        // Set from code (MaterialPropertyBlock) for a short flash when hit: albedo and emission go to this colour.
        [HideInInspector] _HitFlash ("Hit Flash (alpha = amount)", Color) = (1, 1, 1, 0)
        // Set from code (MaterialPropertyBlock) per model: frost of the theme's weather, 0..1. Colour and shape are
        // globals (_PurgeFrostColor, _PurgeFrostParams), set by LevelLighting.
        [HideInInspector] _Frost ("Frost", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard vertex:vert noforwardadd nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0
        #include "PurgeLighting.cginc"

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _EmissionMap;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half4 _EmissionColor;
        fixed4 _HitFlash;
        half _Frost;
        half4 _PurgeFrostColor;  // rgb — frost colour (linear in a linear project)
        half4 _PurgeFrostParams; // x — sharpness (power of the up-facing share), y — rim share

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 purgeNormal;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.purgeNormal = UnityObjectToWorldNormal(v.normal);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            // Frost: the surfaces facing up and the edges whiten.
            half frost = 0;
            if (_Frost > 0)
            {
                float3 normal = normalize(IN.purgeNormal);
                half up = pow(saturate(normal.y), _PurgeFrostParams.x);
                half facing = saturate(dot(normal, normalize(_WorldSpaceCameraPos - IN.worldPos)));
                half rim = (1 - facing) * (1 - facing) * _PurgeFrostParams.y;
                frost = saturate(up + rim) * _Frost;
                albedo.rgb = lerp(albedo.rgb, _PurgeFrostColor.rgb, frost);
            }

            albedo.rgb = lerp(albedo.rgb, _HitFlash.rgb, _HitFlash.a);
            o.Albedo = albedo.rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Metallic = _Metallic;
            o.Smoothness = lerp(_Glossiness, 0.45, frost);
            o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb +
                         albedo.rgb * PurgeExtraLight(IN.worldPos, normalize(IN.purgeNormal)) +
                         _HitFlash.rgb * _HitFlash.a;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
