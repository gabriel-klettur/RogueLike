// Sprite-Unlit shader with PREMULTIPLIED-ADDITIVE blending (SrcAlpha One) and an
// HDR tint, for spell VFX quads that have to ADD light rather than replace the
// pixel under them.
//
// Why this exists: `Universal Render Pipeline/2D/Sprite-Unlit-Default` — the shader
// behind ElementalSprites.SharedUnlitMaterial — declares no `_SrcBlend`/`_DstBlend`
// properties at all, so every `SetInt("_SrcBlend", One)` against it is a SILENT
// no-op and the surface stays fixed alpha (BeamMaterialCache.cs records the same
// measurement). A "glow" on alpha blend cannot exceed its own colour: over pale
// ground it is a net luminance LOSS. There was no additive path for a SpriteRenderer
// in the project — ParticleMaterialCache's additive material is built on
// URP/Particles/Unlit and is for ParticleSystemRenderers, not sprite quads.
//
// Blend is SrcAlpha/One, NOT One/One, to match ParticleMaterialCache exactly: alpha
// still modulates brightness, so a layer can fade out and a dissipation ramp works.
// Under One/One the blend ignores alpha and only the RGB could dim.
//
// `_Color` is [HDR] so a core can be pushed past 1.0 and genuinely blow out. That is
// the whole point of a hot centre — the vertex-colour route clamps through Color32
// and crushes any tint above 1.0 back to 1.0 before the fragment sees it.
Shader "Valkur/SpriteAdditive"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR] _Color ("HDR Tint", Color) = (1,1,1,1)
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
        Blend SrcAlpha One

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
                // Vertex colour carries SpriteRenderer.color, which is where a
                // per-layer fade lives. Kept for both RGB and alpha; the material's
                // HDR _Color is the un-clamped multiplier on top.
                OUT.color = IN.color;
                OUT.uv    = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * IN.color * _Color;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
