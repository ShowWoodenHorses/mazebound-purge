// Foliage of the level (leaves, grass cards) — a cheap sibling of Purge/Geometry for the most overdrawn surfaces
// (Progress: «Производительность Леса», 7г). Lambert lit per vertex (ambient + shadowless directional light + Purge
// lighting), no PBR, no normal map: the fragment is one texture fetch, the alpha test and a multiply.
// Per-cell visibility as in Purge/Geometry (PURGE_VISIBILITY, the cell in UV3); no snowdrifts or ground.
// _ALPHATEST_ON clips pixels with albedo alpha below _Cutoff (only cards that need it); _Cull = Off draws both sides.
// Materials are made by Build Biome Decor.
Shader "Purge/Foliage"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Toggle(_ALPHATEST_ON)] _AlphaTest ("Alpha Test", Float) = 1
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        // After the opaque geometry: alpha-tested pixels lose early depth rejection, let walls and floor occlude them.
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull [_Cull]

            CGPROGRAM
            // target 3.5: vertex texture fetch (GLES 3 / WebGL 2).
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile _ PURGE_VISIBILITY
            #pragma shader_feature_local _ALPHATEST_ON
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "PurgeLighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Cutoff;

            #if PURGE_VISIBILITY
            sampler2D _PurgeVisibility;
            float4 _PurgeVisibilitySize; // x = 1 / width, y = 1 / height
            #endif

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 light : TEXCOORD1;
            };

            v2f vert(appdata_full v)
            {
                #if PURGE_VISIBILITY
                float2 cell = floor(v.texcoord3.xy + 0.5);
                float visible = tex2Dlod(_PurgeVisibility, float4((cell + 0.5) * _PurgeVisibilitySize.xy, 0, 0)).r;
                v.vertex.xyz *= step(0.5, visible);
                #endif

                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 normal = UnityObjectToWorldNormal(v.normal);
                o.light = ShadeSH9(half4(normal, 1)) +
                          _LightColor0.rgb * saturate(dot(normal, _WorldSpaceLightPos0.xyz)) +
                          PurgeExtraLightGeometryVertex(worldPos, normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                #if _ALPHATEST_ON
                clip(albedo.a - _Cutoff);
                #endif
                return fixed4(albedo.rgb * i.light, 1);
            }
            ENDCG
        }
    }

    FallBack Off
}
