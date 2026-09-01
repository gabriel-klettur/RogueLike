// Lit twin of Valkur/SpriteHDRTint.
//
// Same contract — an HDR _Color set through a MaterialPropertyBlock (so a 2.5x tint is not
// crushed to 1.0 by SpriteRenderer.color's Color32 route) plus a _FlashAmount hit flash —
// but it participates in URP 2D lighting, so entities darken with the day/night ambient and
// are lit by placed torches instead of floating at noon brightness over a night-blue world.
//
// The flash is applied AFTER lighting on purpose. It is readability feedback, not a surface
// property: a hit landed in a pitch-black cave still has to be visible.
//
// Requires a Global Light2D whose sorting-layer mask covers the renderer's layer, or the
// sprite renders black. Valkur.Core.Rendering.WorldSpriteMaterials owns that decision.
Shader "Valkur/SpriteHDRTintLit"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        [HDR] _Color ("HDR Tint", Color) = (1,1,1,1)
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        // Snow accumulation role: 0 none, 1 cap (silhouette edge), 2 blanket (ground).
        // See ValkurSnow.hlsl; the amount itself is a global, not a material property.
        _SnowRole ("Snow Role", Float) = 0

        // Legacy sprite properties, so a material using this shader can fall back gracefully.
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex CombinedShapeLightVertex
            #pragma fragment CombinedShapeLightFragment

            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                half2  lightingUV : TEXCOORD1;
                // Unconditional, where it used to be gated behind DEBUG_DISPLAY: the snow
                // accumulation buffer is indexed by world position, so this is now needed on
                // every frame of every build, not only when the debug views are compiled in.
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            half4  _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;
            half4  _RendererColor;
            float4 _FlashColor;
            float  _FlashAmount;
            float  _SnowRole;

            #include "ValkurSnow.hlsl"

            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif

            Varyings CombinedShapeLightVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                #ifdef UNITY_INSTANCING_ENABLED
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteFlip);
                #endif
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);

                // Vertex colour carries SpriteRenderer.color (Color32-clamped, kept for alpha
                // fades); _Color carries the un-clamped HDR tint.
                o.color = v.color * _Color * _RendererColor;
                #ifdef UNITY_INSTANCING_ENABLED
                o.color *= unity_SpriteColor;
                #endif
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 CombinedShapeLightFragment(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // Snow goes on the ALBEDO, BEFORE lighting — unlike the flash below. It is a
                // surface, so midnight snow has to be midnight-dark; a post-lighting white
                // would leave a snowed roof glowing at noon values over a night-blue world.
                tex.rgb = ValkurApplySnow(tex.rgb, tex.a, i.uv, i.positionWS.xy,
                                          TEXTURE2D_ARGS(_MainTex, sampler_MainTex),
                                          _MainTex_TexelSize);

                const half4 main = i.color * tex;
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);

                SurfaceData2D surfaceData;
                InputData2D   inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(i.uv, i.lightingUV, inputData);

                half4 lit = CombinedShapeLightShared(surfaceData, inputData);

                // Applied after lighting: a hit flash is feedback the player must see even in
                // the dark. Alpha is untouched so the silhouette stays exact.
                lit.rgb = lerp(lit.rgb, _FlashColor.rgb * lit.a, _FlashAmount);
                return lit;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 tangent    : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                half4  color       : COLOR;
                float2 uv          : TEXCOORD0;
                half3  normalWS    : TEXCOORD1;
                half3  tangentWS   : TEXCOORD2;
                half3  bitangentWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            half4 _NormalMap_ST;

            Varyings NormalsRenderingVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                #ifdef UNITY_INSTANCING_ENABLED
                attributes.positionOS = UnityFlipSprite(attributes.positionOS, unity_SpriteFlip);
                #endif
                o.positionCS  = TransformObjectToHClip(attributes.positionOS);
                o.uv          = TRANSFORM_TEX(attributes.uv, _NormalMap);
                o.color       = attributes.color;
                o.normalWS    = -GetViewForwardDir();
                o.tangentWS   = TransformObjectToWorldDir(attributes.tangent.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * attributes.tangent.w;
                #ifdef UNITY_INSTANCING_ENABLED
                o.color *= unity_SpriteColor;
                #endif
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

            half4 NormalsRenderingFragment(Varyings i) : SV_Target
            {
                const half4 mainTex  = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv));
                return NormalsRenderingShared(mainTex, normalTS, i.tangentWS.xyz, i.bitangentWS.xyz, i.normalWS.xyz);
            }
            ENDHLSL
        }
    }

    FallBack "Valkur/SpriteHDRTint"
}
