// Sprite-Unlit shader that multiplies the texture by an HDR color set via
// a MaterialPropertyBlock. This bypasses the SpriteRenderer.color vertex-color
// route, whose Color32 clamping crushes HDR tints (e.g. 2.5) down to 1.0
// before reaching the fragment, leaving every "boosted" tint looking just
// like the un-boosted version. Used by Valkur to push monster variants
// (yellow, cyan, magenta...) past the multiplicative ceiling that makes
// brown sprites read as olive instead of vibrant.
Shader "Valkur/SpriteHDRTint"
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
                // Vertex color carries SpriteRenderer.color (clamped to [0,1] via
                // Color32). We keep using it for alpha fades / dim effects, but
                // saturated-color tinting flows through the HDR _Color below.
                OUT.color = IN.color;
                OUT.uv    = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                // Multiply texture by per-renderer color (vertex, alpha-friendly)
                // and by the material's HDR _Color (constant, NOT vertex-clamped).
                half4 c = tex * IN.color * _Color;
                return c;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
