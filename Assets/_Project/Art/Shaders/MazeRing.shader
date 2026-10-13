// Noise wave: a soft ring at the edge of one flat quad (uv 0..1 → radius 0..1) with a faint fill inside.
// _Thickness is the ring width as a share of the radius (set from code per ring, so the width in cells stays the
// same while the ring grows). Drawn over everything but the UI: sounds pass walls, so nothing hides a wave.
Shader "Maze/Ring"
{
    Properties
    {
        _Color ("Color", Color) = (0.75, 0.75, 0.78, 0.35)
        _Thickness ("Ring Width (share of radius)", Range(0.005, 1)) = 0.05
        _Fill ("Fill (share of the ring's alpha)", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+500" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
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
            half _Thickness;
            half _Fill;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 p : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.p = v.uv * 2 - 1;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float r = length(i.p);
                float aa = fwidth(r) * 1.5;
                half outer = 1 - smoothstep(1 - aa, 1, r);
                half ring = outer * smoothstep(1 - _Thickness - aa, 1 - _Thickness, r);
                half fill = outer * _Fill * r * r; // stronger towards the ring
                return half4(_Color.rgb, _Color.a * max(ring, fill));
            }
            ENDCG
        }
    }
}
