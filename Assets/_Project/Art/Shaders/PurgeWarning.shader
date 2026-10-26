// Warning of a monster's ability on the floor (ТЗ: опасная способность сначала показывает зону/линию): one flat quad,
// the shape from _Shape — 0 circle (uv 0..1 → radius 0..1), 1 line (u across, v along from the monster), 2 sector
// (centred on the monster, radius 1 = reach, _HalfAngle radians either side of +v), 3 closing circle (a ring runs from
// the rim to the centre with _Progress). A bright edge, a faint inside and a
// fill that grows with _Progress (0 → 1 when it goes off). Drawn over the floor and the crowd, under the UI.
Shader "Purge/Warning"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.12, 0.08, 0.85)
        _Progress ("Progress", Range(0, 1)) = 0
        _Shape ("Shape (0 circle, 1 line, 2 sector, 3 closing)", Float) = 0
        _HalfAngle ("Sector half angle (radians)", Float) = 0.785
        _Edge ("Edge width (share of the size)", Range(0.005, 0.5)) = 0.06
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+400" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        ZWrite Off
        ZTest Always
        Cull Off
        ColorMask RGB
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _Color;
            half _Progress;
            half _Shape;
            half _HalfAngle;
            half _Edge;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Inside 0..1 with a soft rim (aa from screen derivatives); edge = distance to the shape's border.
            half4 frag(v2f i) : SV_Target
            {
                half inside;
                half edge;
                half along; // 0 at the start of the fill, 1 at its end
                if (_Shape > 2.5)
                {
                    // Closing circle (stage 8c): the rim stays, a bright ring runs from it to the centre; what it has passed
                    // over is filled.
                    float2 p = i.uv * 2 - 1;
                    float r = length(p);
                    float aa = fwidth(r) * 1.5;
                    float closing = 1 - _Progress;
                    half inCircle = 1 - smoothstep(1 - aa, 1, r);
                    half ring = 1 - smoothstep(_Edge * 0.6, _Edge, abs(r - closing));
                    half rimEdge = 1 - smoothstep(_Edge * 0.6, _Edge, 1 - r);
                    half pulseRing = 0.85 + 0.15 * sin(_Time.y * 18);
                    half passed = step(closing, r);
                    half alphaRing = inCircle * max(max(rimEdge, ring) * pulseRing, lerp(0.16, 0.42, passed));
                    return half4(_Color.rgb, _Color.a * alphaRing);
                }

                if (_Shape < 0.5)
                {
                    float2 p = i.uv * 2 - 1;
                    float r = length(p);
                    float aa = fwidth(r) * 1.5;
                    inside = 1 - smoothstep(1 - aa, 1, r);
                    edge = 1 - r;
                    along = r;
                }
                else if (_Shape < 1.5)
                {
                    float x = abs(i.uv.x * 2 - 1);
                    float aa = fwidth(x) * 1.5;
                    inside = (1 - smoothstep(1 - aa, 1, x)) * step(0, i.uv.y) * step(i.uv.y, 1);
                    edge = 1 - x;
                    along = i.uv.y;
                }
                else
                {
                    float2 p = i.uv * 2 - 1;
                    float r = length(p);
                    float angle = abs(atan2(p.x, p.y));
                    float aa = fwidth(r) * 1.5;
                    float side = (_HalfAngle - angle) * r; // distance to the side, in radius units
                    float aaSide = fwidth(side) * 1.5 + 1e-4;
                    inside = (1 - smoothstep(1 - aa, 1, r)) * smoothstep(-aaSide, 0, side);
                    edge = min(1 - r, _HalfAngle < 3.1 ? side : 1);
                    along = r;
                }

                half rim = 1 - smoothstep(_Edge * 0.6, _Edge, edge);
                half filled = step(along, _Progress);
                half pulse = 0.85 + 0.15 * sin(_Time.y * 18);
                half alpha = inside * max(rim * pulse, lerp(0.16, 0.42, filled));
                return half4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
