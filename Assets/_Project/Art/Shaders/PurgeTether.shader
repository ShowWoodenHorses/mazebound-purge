// The chain or vine of a monster's hook (stage 8b, MonsterHookView): a LineRenderer whose texture tiles along it.
// Unlit (a line has no normals): the texture × _Color × _Light (how bright it reads in the scene), the alpha test cuts
// the gaps between the links; both sides. Materials are made by Build Monsters (Art/Monster/Hook).
Shader "Purge/Tether"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (alpha = shape)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Light ("Brightness", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Cutoff;
            half _Light;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * i.color;
                clip(albedo.a - _Cutoff);
                return fixed4(albedo.rgb * _Light, 1);
            }
            ENDCG
        }
    }

    FallBack Off
}
