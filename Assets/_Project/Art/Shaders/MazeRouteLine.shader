// Route hint line: a flat ribbon on the floor. uv.x = distance from the line start (cells), uv.y = -1..1 across the
// strip, uv2.x = distance left to the target (cells). The outline and the fill are one strip (no overdraw at the
// edges); pulses flow towards the target (a pattern of the distance left, so it does not slide while the player walks).
// Drawn after the fog and the vision zones; walls in front still hide it.
Shader "Maze/RouteLine"
{
    Properties
    {
        _Color ("Color", Color) = (0.62, 0.86, 1, 1)
        _OutlineColor ("Outline Color", Color) = (0.04, 0.06, 0.08, 0.9)
        _Brightness ("Brightness", Range(0, 2)) = 0.85
        _Opacity ("Opacity", Range(0, 1)) = 0.6
        _HalfWidth ("Half Width (cells)", Float) = 0.09
        _Outline ("Outline (cells)", Float) = 0.03
        _StartFade ("Start Fade (cells)", Float) = 0.6
        _Pulse ("Pulse", Range(0, 1)) = 0.35
        _PulseSpacing ("Pulse Spacing (cells)", Float) = 1.6
        _PulseSpeed ("Pulse Speed (cells/s)", Float) = 1.2
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        ZWrite Off
        ZTest LEqual
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
            half4 _OutlineColor;
            half _Brightness;
            half _Opacity;
            float _HalfWidth;
            float _Outline;
            float _StartFade;
            half _Pulse;
            float _PulseSpacing;
            float _PulseSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float left : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.left = v.uv2.x;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float across = abs(i.uv.y) * _HalfWidth;
                float edge = _HalfWidth - across; // cells to the outer edge
                float aa = max(fwidth(across), 1e-5);
                half outer = saturate(edge / aa);
                half fill = _Outline > 0 ? saturate((edge - _Outline) / aa) : 1;

                // Pulses: a short bright band every _PulseSpacing, moving towards the target.
                float phase = frac((i.left + _Time.y * _PulseSpeed) / max(_PulseSpacing, 0.05));
                half band = smoothstep(0.0, 0.12, phase) * (1 - smoothstep(0.12, 0.45, phase));
                half3 lineColor = _Color.rgb * _Brightness * (1 + _Pulse * band);

                half3 color = lerp(_OutlineColor.rgb, lineColor, fill);
                half alpha = lerp(_OutlineColor.a, _Color.a, fill) * outer * _Opacity;
                alpha *= saturate(i.uv.x / max(_StartFade, 1e-3));
                return half4(color, alpha);
            }
            ENDCG
        }
    }
}
