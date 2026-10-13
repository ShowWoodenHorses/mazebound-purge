// Soft round shadow under a character (instead of realtime shadows): a quad on the floor darkened towards its
// centre, no texture. Instancing-friendly (all blob shadows share one material and mesh).
Shader "Maze/BlobShadow"
{
    Properties
    {
        _Color ("Color (alpha = darkness at the centre)", Color) = (0.02, 0.03, 0.06, 0.6)
        _Softness ("Softness", Range(0.05, 1)) = 0.7
    }

    SubShader
    {
        // Right after opaque geometry, before the fog (Transparent) covers it.
        Tags { "Queue" = "Geometry+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2 - 1;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half d = length(i.uv);
                half shade = 1 - smoothstep(1 - _Softness, 1, d);
                return fixed4(_Color.rgb, _Color.a * shade);
            }
            ENDCG
        }
    }
}
