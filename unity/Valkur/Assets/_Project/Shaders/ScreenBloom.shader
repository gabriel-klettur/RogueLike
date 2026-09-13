// The bloom behind ScreenGradeFeature: four passes over a half-resolution pyramid, the
// classic dual-Kawase shape, composited BEFORE the grade so the vignette still closes over it.
//
// Why it exists at all: the project renders into an HDR buffer (UniversalRP.asset, HDR on) and
// every additive VFX in it — torches, spell cores, the XP orb, the weapon-swap flare — writes
// energy above 1.0 that the framebuffer then clamps away. Nothing blossomed. A threshold at
// exactly 1.0 means only what is already brighter than white feeds the pyramid, so no texel
// of pixel art can cross it and a stone wall never glows; the additive layers do.
//
// Colour space: linear in, linear out. The 2D renderer encodes in FinalBlitPass.
//
// Cost budget: 0.5 ms at 1600x800 (the grade measured 0.215). The pyramid is at HALF res and
// below, so the composite is the only full-resolution pass and it is a single texture read
// over the one the grade already pays for.
Shader "Hidden/Valkur/ScreenBloom"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

        // x = threshold (linear), y = soft knee, z = intensity, w = unused
        float4 _BloomParams;
        // rgb = tint multiplied into the composite, a unused
        float4 _BloomTint;
        // xy = 1 / source size for the pass being drawn. Set per blit by the pass.
        float4 _BloomTexel;

        TEXTURE2D_X(_BloomTex);

        half3 SampleSrc(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
        }

        // 13-tap-free variant: a 4-corner box at half-texel offsets plus the centre, which is
        // what makes a Kawase chain both cheap and free of the square-kernel look.
        half3 DownsampleBox(float2 uv, float2 texel)
        {
            float2 o = texel * 0.5;
            half3 c  = SampleSrc(uv) * 4.0;
            c += SampleSrc(uv + float2(-o.x, -o.y));
            c += SampleSrc(uv + float2( o.x, -o.y));
            c += SampleSrc(uv + float2(-o.x,  o.y));
            c += SampleSrc(uv + float2( o.x,  o.y));
            return c * (1.0 / 8.0);
        }

        // Tent filter: four diagonals at half weight, four edges at full, centre at double.
        half3 UpsampleTent(float2 uv, float2 texel)
        {
            float2 o = texel;
            half3 c  = SampleSrc(uv) * 4.0;
            c += SampleSrc(uv + float2(-o.x, 0)) * 2.0;
            c += SampleSrc(uv + float2( o.x, 0)) * 2.0;
            c += SampleSrc(uv + float2(0, -o.y)) * 2.0;
            c += SampleSrc(uv + float2(0,  o.y)) * 2.0;
            c += SampleSrc(uv + float2(-o.x, -o.y));
            c += SampleSrc(uv + float2( o.x, -o.y));
            c += SampleSrc(uv + float2(-o.x,  o.y));
            c += SampleSrc(uv + float2( o.x,  o.y));
            return c * (1.0 / 16.0);
        }

        // URP's own soft-threshold curve, so the knee behaves like the one artists know.
        half3 Prefilter(half3 c)
        {
            half brightness = Max3(c.r, c.g, c.b);
            half knee       = _BloomParams.y;
            half soft       = brightness - _BloomParams.x + knee;
            soft            = clamp(soft, 0.0, 2.0 * knee);
            soft            = soft * soft / (4.0 * knee + 1e-4);
            half contrib    = max(soft, brightness - _BloomParams.x);
            contrib        /= max(brightness, 1e-4);
            return c * contrib;
        }
        ENDHLSL

        // 0 — Prefilter: threshold the camera colour into the first (half-res) mip.
        Pass
        {
            Name "BloomPrefilter"
            Blend Off
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragPrefilter
            half4 FragPrefilter (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 c = DownsampleBox(input.texcoord, _BloomTexel.xy);
                // FP16 target: a NaN from an over-bright particle would spread through the
                // whole pyramid and come back as a black square on screen.
                c = min(c, 65000.0);
                return half4(Prefilter(max(c, 0.0)), 1.0);
            }
            ENDHLSL
        }

        // 1 — Downsample one mip.
        Pass
        {
            Name "BloomDownsample"
            Blend Off
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragDown
            half4 FragDown (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(DownsampleBox(input.texcoord, _BloomTexel.xy), 1.0);
            }
            ENDHLSL
        }

        // 2 — Upsample one mip ADDITIVELY onto the one above it. The blend is what makes each
        // level of the pyramid contribute its own radius, which is the whole "scatter" look.
        Pass
        {
            Name "BloomUpsample"
            Blend One One
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragUp
            half4 FragUp (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(UpsampleTent(input.texcoord, _BloomTexel.xy), 1.0);
            }
            ENDHLSL
        }

        // 3 — Composite: camera colour plus the finished pyramid, tinted and scaled.
        Pass
        {
            Name "BloomComposite"
            Blend Off
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragComposite
            half4 FragComposite (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 c     = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp,  uv).rgb;
                half3 bloom = SAMPLE_TEXTURE2D_X(_BloomTex,    sampler_LinearClamp, uv).rgb;
                c += bloom * _BloomParams.z * _BloomTint.rgb;
                return half4(max(c, 0.0), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
