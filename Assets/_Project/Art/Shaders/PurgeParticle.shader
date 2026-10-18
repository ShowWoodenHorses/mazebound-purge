// Unlit effects: particles (vertex colour from the particle system), tracers. Texture × tint × vertex colour.
// Blend is a material setting: additive (flashes, sparks, tracers — SrcAlpha One) or alpha (dust, stars —
// SrcAlpha OneMinusSrcAlpha). Drawn after the fog: effects are shown only in cells the player sees.
// _ClipPlane (stage E3; set per renderer by a property block): fragments where dot(xyz, world position) > w are cut —
// a stream ends hard at the wall it hits. The default (0, 0, 0, 1) cuts nothing.
Shader "Purge/Particle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1 // One (additive)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 4 // LessEqual; Always = over models
        [HideInInspector] _ClipPlane ("Clip Plane", Vector) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        ZWrite Off
        Cull Off
        ColorMask RGB // keep the frame's alpha (screenshots, render textures)
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            float4 _ClipPlane;

            struct appdata
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float clip : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.clip = _ClipPlane.w - dot(_ClipPlane.xyz, mul(unity_ObjectToWorld, v.vertex).xyz);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                clip(i.clip);
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }
}
