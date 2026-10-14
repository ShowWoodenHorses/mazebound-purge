// Burning cells (stress test stub of cell zones): quads only over burning cells (a dynamic mesh, not an object per
// cell); their shape comes from the fire mask (_PurgeFireMask, R8 W×H, bilinear: neighbouring cells merge into one
// patch; the edge sits at the cell border, broken by noise) and two scrolling value noises. Alpha-blended, after
// the fog: drawn only where the player sees (the mesh holds only visible cells). Cartoon style: bright yellow core, orange-red edge.
Shader "Purge/FireCells"
{
    Properties
    {
        _Core ("Core", Color) = (1, 0.86, 0.35, 1)
        _Edge ("Edge", Color) = (0.95, 0.3, 0.06, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _NoiseScale ("Noise Scale", Float) = 2.6
        _Speed ("Speed", Float) = 1.6
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        ColorMask RGB
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _Core;
            half4 _Edge;
            half _Opacity;
            float _NoiseScale;
            float _Speed;
            sampler2D _PurgeFireMask;
            float4 _PurgeFireMaskSize; // xy = 1 / level size in cells

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 grid : TEXCOORD0;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.grid = world.xz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half mask = tex2D(_PurgeFireMask, (i.grid + 0.5) * _PurgeFireMaskSize.xy).r;
                float t = _Time.y * _Speed;
                float2 p = i.grid * _NoiseScale;
                half n = Noise(p + float2(0, -t)) * 0.6 + Noise(p * 2.1 + float2(t * 0.7, -t * 1.3)) * 0.4;
                // Mask is 0.5 at the border of a burning cell next to a cold one: the edge goes there.
                half edge = saturate((mask - 0.5) * 2.5 + (n - 0.5) * 0.8);
                // Inside the patch the heat flickers with the noise (tongues of yellow over orange).
                half heat = edge * saturate(n * 1.4 - 0.1);
                half3 color = lerp(_Edge.rgb, _Core.rgb, heat);
                return half4(color, saturate(edge * 3) * _Opacity * (0.7 + 0.3 * n));
            }
            ENDCG
        }
    }
}
