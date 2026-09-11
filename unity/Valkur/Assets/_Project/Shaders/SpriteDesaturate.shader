// Sprite/Tilemap shader that outputs the texture (times the renderer's own tint) in pure
// Rec.601 luminance — every pixel becomes (lum, lum, lum, alpha) regardless of source hue, so
// vivid yellows / reds / cyans actually go gray on the screen. Used by
// SpiritWorldGrayscale to drain the world while the player is in spirit form;
// the regular Sprites/Default + Tilemap.color route is multiplicative and
// can't desaturate (it only tints), so we swap the renderer's sharedMaterial
// to this for the duration of the death sequence and restore on revive.
Shader "Valkur/SpriteDesaturate"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
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
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                // Rec.601 luminance — close enough to perceived brightness and
                // matches the "saturation -100" output URP's ColorAdjustments
                // produces, so the look matches the previous global volume.
                // The VERTEX colour is in the product. It used not to be, and two things went
                // wrong at once: the world bars over a spirit's head are white generated art
                // whose whole colour arrives as SpriteRenderer.color, so they drew as white
                // slabs with a white outline; and the Dark roster's black-tinted twins, whose
                // tint also lives on the renderer, drained to pale grey ghosts.
                half3 rgb = tex.rgb * IN.color.rgb;
                half lum = rgb.r * 0.299h + rgb.g * 0.587h + rgb.b * 0.114h;
                // Honor renderer alpha (vertex color) for fades, and the
                // texture's own alpha for cutouts.
                half a = tex.a * IN.color.a;
                return half4(lum, lum, lum, a);
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
