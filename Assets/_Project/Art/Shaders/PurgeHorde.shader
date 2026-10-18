// Mass monsters (HordeVisualDefinition): GPU instancing + animation baked into textures, no Animator, no skinning
// on the CPU. Two animation paths, switched by the global keyword PURGE_HORDE_VAT (stress test compares them):
//  - VAT: the vertex reads its model-space position for the frame (one fetch; texture per character);
//  - bones (default): the vertex is skinned in the shader by its two strongest bones read from the bone matrix
//    texture (six fetches; texture shared by every character on the skeleton).
// PURGE_HORDE_LERP blends two frames (twice the fetches) instead of snapping to the nearest one.
// Normals are flat, from screen derivatives (Synty models are flat-shaded; the mesh is welded without normals).
// Lighting as Purge/Lit: ambient (SH) + shadowless directional light + Purge light (lantern, light map); no
// shadows. Per instance: _HordeFrame = (frame A, frame B, blend, hit flash), _HordeGlow = champion's glow (rgb, strength):
// a tint plus a bright rim where the surface turns away from the camera.
Shader "Purge/Horde"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        [NoScaleOffset] _VatTex ("Vertex Animation (positions)", 2D) = "black" {}
        [NoScaleOffset] _BoneTex ("Bone Animation (3x4 matrices)", 2D) = "black" {}
        _FlashColor ("Hit Flash Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nolightprobe nolodfade
            #pragma multi_compile_fog
            #pragma multi_compile _ PURGE_HORDE_VAT
            #pragma multi_compile _ PURGE_HORDE_LERP
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "PurgeLighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _FlashColor;
            sampler2D _VatTex;
            float4 _VatTex_TexelSize;
            sampler2D _BoneTex;
            float4 _BoneTex_TexelSize;

            UNITY_INSTANCING_BUFFER_START(HordeProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HordeFrame)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HordeGlow)
            UNITY_INSTANCING_BUFFER_END(HordeProps)

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 column : TEXCOORD1;
                float4 bones : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            #if defined(PURGE_HORDE_VAT)
            float3 VatPosition(float column, float frame)
            {
                float2 uv = float2((column + 0.5) * _VatTex_TexelSize.x, (frame + 0.5) * _VatTex_TexelSize.y);
                return tex2Dlod(_VatTex, float4(uv, 0, 0)).xyz;
            }
            #else
            float3 BoneTransform(float bone, float frame, float3 position)
            {
                float v = (frame + 0.5) * _BoneTex_TexelSize.y;
                float u = (bone * 3 + 0.5) * _BoneTex_TexelSize.x;
                float4 row0 = tex2Dlod(_BoneTex, float4(u, v, 0, 0));
                float4 row1 = tex2Dlod(_BoneTex, float4(u + _BoneTex_TexelSize.x, v, 0, 0));
                float4 row2 = tex2Dlod(_BoneTex, float4(u + 2 * _BoneTex_TexelSize.x, v, 0, 0));
                float4 p = float4(position, 1);
                return float3(dot(row0, p), dot(row1, p), dot(row2, p));
            }

            float3 SkinnedPosition(float4 bones, float frame, float3 position)
            {
                float3 result = BoneTransform(bones.x, frame, position) * bones.z;
                if (bones.w > 0)
                    result += BoneTransform(bones.y, frame, position) * bones.w;
                return result;
            }
            #endif

            float3 AnimatedPosition(appdata v, float4 frame)
            {
                #if defined(PURGE_HORDE_VAT)
                    float3 a = VatPosition(v.column.x, frame.x);
                    #if defined(PURGE_HORDE_LERP)
                        a = lerp(a, VatPosition(v.column.x, frame.y), frame.z);
                    #endif
                #else
                    float3 a = SkinnedPosition(v.bones, frame.x, v.vertex.xyz);
                    #if defined(PURGE_HORDE_LERP)
                        a = lerp(a, SkinnedPosition(v.bones, frame.y, v.vertex.xyz), frame.z);
                    #endif
                #endif
                return a;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float4 frame = UNITY_ACCESS_INSTANCED_PROP(HordeProps, _HordeFrame);
                float3 local = AnimatedPosition(v, frame);
                float4 world = mul(unity_ObjectToWorld, float4(local, 1));
                o.worldPos = world.xyz;
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float flash = UNITY_ACCESS_INSTANCED_PROP(HordeProps, _HordeFrame).w;

                // Flat normal of the triangle; faces drawn always face the camera, which fixes the sign on every API.
                float3 normal = normalize(cross(ddy(i.worldPos), ddx(i.worldPos)));
                if (dot(normal, _WorldSpaceCameraPos - i.worldPos) < 0)
                    normal = -normal;

                fixed3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                albedo = lerp(albedo, _FlashColor.rgb, flash);
                half3 light = ShadeSH9(float4(normal, 1)) + _LightColor0.rgb * saturate(dot(normal, _WorldSpaceLightPos0.xyz));
                fixed4 color = fixed4(albedo * (light + PurgeExtraLight(i.worldPos, normal)) + _FlashColor.rgb * flash, 1);
                float4 glow = UNITY_ACCESS_INSTANCED_PROP(HordeProps, _HordeGlow);
                if (glow.a > 0)
                {
                    half rim = 1 - saturate(dot(normal, normalize(_WorldSpaceCameraPos - i.worldPos)));
                    color.rgb += glow.rgb * (glow.a * (0.3 + 1.6 * rim * rim));
                }
                UNITY_APPLY_FOG(i.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
}
