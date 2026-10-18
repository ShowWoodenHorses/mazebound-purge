// Unlit glow of mesh particles (spheres, rings of pack effects): a dim inner colour plus a bright rim where the surface
// turns away from the camera. Replaces Polygon Arsenal's lit rim shaders (no lamps in levels — they went dark).
// Vertex colour from the particle system tints and fades it. Blend is a material setting: additive (SrcAlpha One) or
// alpha (SrcAlpha OneMinusSrcAlpha). Drawn after the fog: effects are shown only in cells the player sees.
Shader "Purge/ParticleRim"
{
    Properties
    {
        [HDR] _InnerColor ("Inner Color", Color) = (1, 1, 1, 1)
        _InnerScale ("Inner Brightness", Range(0, 1)) = 0.35
        [HDR] _RimColor ("Rim Color", Color) = (1, 0.8, 0.5, 1)
        _RimWidth ("Rim Width (power)", Range(0.2, 20)) = 3
        _RimGlow ("Rim Glow", Range(0, 9)) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1 // One (additive)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Back
        ColorMask RGB // keep the frame's alpha (screenshots, render textures)
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _InnerColor;
            half _InnerScale;
            half4 _RimColor;
            half _RimWidth;
            half _RimGlow;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                half4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half4 color : COLOR;
                half rim : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 normal = normalize(UnityObjectToWorldNormal(v.normal));
                float3 view = normalize(UnityWorldSpaceViewDir(mul(unity_ObjectToWorld, v.vertex).xyz));
                o.rim = pow(1.0 - saturate(dot(view, normal)), _RimWidth);
                o.color = v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half3 rgb = _InnerColor.rgb * _InnerScale + _RimColor.rgb * (_RimGlow * i.rim);
                return half4(rgb * i.color.rgb, saturate(_InnerColor.a + i.rim) * i.color.a);
            }
            ENDCG
        }
    }
}
