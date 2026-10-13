// Melee swing: a cartoon "swoosh" along the attack sector, drawn on one flat quad (uv 0..1 → -1..1, +v = forward,
// +u = right, radius 1 = the weapon's reach). Angles are degrees from forward, positive to the right.
// The head of the stroke is at _Head and moves in the direction _Dir (+1 / -1); behind it a trail of _Trail degrees
// fades and thins out. Everything outside ±_HalfArc is cut. Animated from code (SwingArc, MaterialPropertyBlock).
Shader "Maze/SwingArc"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 0.85)
        _Inner ("Inner Radius (thickest part)", Range(0, 0.95)) = 0.7
        _HalfArc ("Half Arc", Float) = 50
        _Head ("Head Angle", Float) = 0
        _Trail ("Trail Length", Float) = 70
        _Dir ("Direction", Float) = 1
        _Alpha ("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        ZWrite Off
        Cull Off
        ColorMask RGB // keep the frame's alpha (screenshots, render textures)
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _Color;
            half _Inner;
            float _HalfArc;
            float _Head;
            float _Trail;
            float _Dir;
            half _Alpha;

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
                float angle = degrees(atan2(i.p.x, i.p.y));

                float behind = (_Head - angle) * _Dir;                   // degrees behind the head
                half trail = saturate(1 - behind / max(_Trail, 1)) * smoothstep(-6, 0, behind);
                half arc = 1 - smoothstep(_HalfArc - 6, _HalfArc, abs(angle));

                half width = lerp(0.06, 1 - _Inner, trail);               // thick at the head, thin at the tail
                half inner = 1 - width;
                half band = smoothstep(inner - 0.05, inner, r) * (1 - smoothstep(0.95, 1, r));
                half rim = smoothstep(inner, 1, r);                       // brighter towards the outer edge

                half alpha = _Color.a * _Alpha * trail * arc * band;
                return half4(_Color.rgb * (0.7 + 0.3 * rim), alpha);
            }
            ENDCG
        }
    }
}
