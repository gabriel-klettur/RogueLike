// Solid-color sprite shader used by BuildingSilhouetteOutline's offset copies.
// Replaces the sprite's RGB with _Color while keeping the sprite's own alpha, so
// a stack of offset copies renders as a constant-colour SILHOUETTE instead of a
// tinted copy of the art. The building's own Footprint/Canopy draw over the
// copies' centres, leaving only the offset fringe — the yellow highlight edge.
//
// ATLAS-SAFE by construction: it samples only the sprite's own alpha (the UVs the
// SpriteRenderer supplies), never neighbour texels, so it cannot bleed into
// adjacent sprites packed on the same atlas page.
//
// Follows the URP sprite-shader conventions of SpriteAdditive.shader /
// SpriteHDRTint.shader (CanUseSpriteAtlas, RenderPipeline=UniversalPipeline,
// SrcAlpha/OneMinusSrcAlpha so a per-layer alpha fade actually fades).
Shader "Valkur/SpriteSolidColor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                OUT.color = IN.color;
                OUT.uv    = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                // Solid colour, tinted by SpriteRenderer.color. Alpha combines
                // texture, material and renderer values.
                return half4(_Color.rgb * IN.color.rgb, a * _Color.a * IN.color.a);
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
