// Health bars and damage numbers (CombatFeedbackView): one dynamic mesh, one draw call. Vertex colour is the colour
// (alpha = fade). uv.z = 1 — a solid quad (bars), 0 — a glyph of the digit atlas (single-channel distance field,
// 0.5 = the edge) with a dark outline. Drawn over everything but the UI, like the noise waves.
Shader "Maze/Overhead"
{
    Properties
    {
        _MainTex ("Digit Atlas (distance field, R)", 2D) = "black" {}
        _OutlineColor ("Outline Color", Color) = (0.08, 0.02, 0.02, 0.9)
        _Outline ("Outline Width (share of the field)", Range(0, 0.45)) = 0.22
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+600" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
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
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _OutlineColor;
            half _Outline;

            struct appdata
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                if (i.uv.z > 0.5)
                    return i.color;

                float d = tex2D(_MainTex, i.uv.xy).r;
                float aa = max(fwidth(d) * 0.75, 0.002);
                half fill = smoothstep(0.5 - aa, 0.5 + aa, d);
                float edge = 0.5 - _Outline;
                half outline = smoothstep(edge - aa, edge + aa, d) * _OutlineColor.a;
                half3 rgb = lerp(_OutlineColor.rgb, i.color.rgb, fill);
                half alpha = lerp(outline, 1, fill) * i.color.a;
                return half4(rgb, alpha);
            }
            ENDCG
        }
    }
}
