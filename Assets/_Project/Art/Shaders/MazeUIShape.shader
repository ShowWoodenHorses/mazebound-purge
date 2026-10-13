// Thin-line UI shapes (UiShape): a signed distance field per quad, anti-aliased by screen derivatives, so lines stay
// crisp at any DPI and scale. Everything per shape comes in vertex channels (one material for all shapes → batching):
//   uv0.xy — point relative to the shape origin (a sector's point already turned so its sweep is centred on +y),
//   uv0.z  — kind (0 rounded rect / capsule, 1 sector, 2 arrow), uv0.w — outline width (canvas units, inside the edge),
//   uv1    — fill colour, uv2 — outline colour (both sRGB),
//   uv3    — rect: half size xy, corner radius z; arrow: half size xy, tip length z;
//            sector: inner, outer radius, sin/cos of the half sweep (cos -2 = ring).
// Vertex colour is a tint over the result (Graphic.color, button transitions, CanvasGroup alpha).
// Formulas match UiShapeMath (hit tests). Stencil/RectMask2D handling as in UI/Default.
Shader "Maze/UIShape"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0 // fwidth (anti-aliasing): WebGL 2 / GLES 3 have it

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                half4 tint : COLOR;
                float4 point_kind_width : TEXCOORD0;
                half4 fill : TEXCOORD1;
                half4 stroke : TEXCOORD2;
                float4 shape : TEXCOORD3;
                float4 mask : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            half4 ToActiveSpace(half4 c)
            {
                if (!IsGammaSpace()) c.rgb = UIGammaToLinear(c.rgb);
                return c;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 position = UnityObjectToClipPos(v.vertex);
                o.vertex = position;

                float2 pixelSize = position.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                {
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                }

                o.tint = v.color * _Color;
                o.point_kind_width = v.uv0;
                o.fill = ToActiveSpace(v.uv1);
                o.stroke = ToActiveSpace(v.uv2);
                o.shape = v.uv3;
                return o;
            }

            float RoundedRect(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float Arrow(float2 p, float2 halfSize, float tip)
            {
                float box = RoundedRect(p, halfSize, 0.0);
                if (tip <= 0.0) return box;
                float2 normal = normalize(float2(halfSize.y, tip));
                float edge = dot(float2(p.x - (halfSize.x - tip), abs(p.y) - halfSize.y), normal);
                return max(box, edge);
            }

            float Sector(float2 q, float inner, float outer, float2 halfSweep)
            {
                float len = length(q);
                float ring = len - outer;
                if (inner > 0.0) ring = max(ring, inner - len);
                if (halfSweep.y < -1.5) return ring;

                float x = abs(q.x);
                float along = max(x * halfSweep.x + q.y * halfSweep.y, 0.0);
                float toRay = length(float2(x, q.y) - halfSweep * along);
                float side = halfSweep.y * x - halfSweep.x * q.y;
                return max(ring, side > 0.0 ? toRay : -toRay);
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 p = i.point_kind_width.xy;
                float width = i.point_kind_width.w;
                float kind = i.point_kind_width.z;
                float d = kind < 0.5
                    ? RoundedRect(p, i.shape.xy, i.shape.z)
                    : kind < 1.5
                        ? Sector(p, i.shape.x, i.shape.y, i.shape.zw)
                        : Arrow(p, i.shape.xy, i.shape.z);

                // One screen pixel in shape units (from the point, not d: d has kinks at corners and the wedge axis).
                float aa = max(length(fwidth(p)) * 0.7071, 1e-4);
                half inside = saturate(0.5 - d / aa);
                half outline = width > 0.0 ? saturate(0.5 + (d + width) / aa) : 0.0;

                // Outline over fill, premultiplied.
                half strokeA = i.stroke.a * outline;
                half fillA = i.fill.a * (1.0 - strokeA);
                half4 color;
                color.rgb = i.stroke.rgb * strokeA + i.fill.rgb * fillA;
                color.a = strokeA + fillA;
                color *= inside;

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color *= m.x * m.y;
                #endif

                color.rgb *= i.tint.rgb;
                color *= i.tint.a;

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
