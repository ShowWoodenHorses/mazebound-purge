// Animated fog of war over hidden cells (visual only: visibility and AI do not depend on it).
// One plane at floor level covers the level; for each pixel the fog amount comes from the global _MazeFog mask
// (one texel per cell plus a 1-cell hidden border, bilinear, values fade in and out on the CPU), softened by a ring
// of extra taps. Drawn over everything (no depth test): the soft edge also covers the walls standing at the border
// of the visible area, so the border dissolves into the fog instead of being cut. Movement: two layers of a small
// tiling noise texture scrolling in different directions. No keywords, one pass, no depth texture.
Shader "Maze/Fog"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex ("Noise (tiling, R)", 2D) = "gray" {}
        _Color ("Fog Color", Color) = (0.10, 0.11, 0.14, 1)
        _WispColor ("Wisp Color", Color) = (0.26, 0.28, 0.34, 1)
        _Density ("Density", Range(0, 1)) = 0.96
        _WispStrength ("Wisp Strength", Range(0, 1)) = 0.7
        _NoiseScale ("Noise Scale (layer 1, layer 2)", Vector) = (0.07, 0.12, 0, 0)
        _Flow1 ("Flow Layer 1 (cells/s, xy)", Vector) = (0.35, 0.12, 0, 0)
        _Flow2 ("Flow Layer 2 (cells/s, xy)", Vector) = (-0.2, 0.28, 0, 0)
        _EdgeSoftness ("Edge Softness", Range(0.05, 1)) = 0.6
        _EdgeOffset ("Edge Offset (+ toward hidden, - into visible)", Range(-0.5, 0.5)) = 0.05
        _BlurRadius ("Edge Blur Radius (cells)", Range(0, 1.5)) = 0.55
        _FadeSeconds ("Fade Time (s)", Range(0, 2)) = 0.35
        _PlaneHeight ("Plane Height (m)", Range(0, 2)) = 0.01
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _NoiseTex;
            fixed4 _Color;
            fixed4 _WispColor;
            half _Density;
            half _WispStrength;
            float4 _NoiseScale;
            float4 _Flow1;
            float4 _Flow2;
            half _EdgeSoftness;
            half _EdgeOffset;
            float _BlurRadius;

            sampler2D _MazeFog;
            float4 _MazeFogSize; // x = 1 / texture width, y = 1 / texture height (grid + 2-cell border)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 cell : TEXCOORD0; // world XZ = grid coordinates (cell (x, y) is centred at (x, y))
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.cell = mul(unity_ObjectToWorld, v.vertex).xz;
                return o;
            }

            // Visibility (0 hidden .. 1 visible), bilinear between cell centres; the border texel is the outer cell.
            half Visible(float2 cell)
            {
                return tex2D(_MazeFog, (cell + 1.5) * _MazeFogSize.xy).r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Nine taps (centre + ring of 8): a cheap blur, so the edge is a wide soft gradient, not steps along cells.
                float r = _BlurRadius;
                float d = r * 0.7071;
                half visible = Visible(i.cell) * 0.2
                    + (Visible(i.cell + float2(r, 0)) + Visible(i.cell + float2(-r, 0))
                     + Visible(i.cell + float2(0, r)) + Visible(i.cell + float2(0, -r))
                     + Visible(i.cell + float2(d, d)) + Visible(i.cell + float2(-d, d))
                     + Visible(i.cell + float2(d, -d)) + Visible(i.cell + float2(-d, -d))) * 0.1;

                // 0.5 is the border between a visible and a hidden cell; push the fog toward the hidden side.
                half edge = 0.5 - _EdgeOffset;
                half fog = 1 - smoothstep(edge - _EdgeSoftness * 0.5, edge + _EdgeSoftness * 0.5, visible);

                float t = _Time.y;
                half n1 = tex2D(_NoiseTex, i.cell * _NoiseScale.x + _Flow1.xy * t * _NoiseScale.x).r;
                half n2 = tex2D(_NoiseTex, i.cell * _NoiseScale.y + _Flow2.xy * t * _NoiseScale.y).r;
                // Soft wisps where both layers are bright: they drift and change shape as the layers slide past.
                half wisps = smoothstep(0.5, 0.95, (n1 + n2) * 0.5);

                fixed3 color = lerp(_Color.rgb, _WispColor.rgb, wisps * _WispStrength);
                half alpha = fog * _Density * lerp(0.88, 1.0, n2);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
