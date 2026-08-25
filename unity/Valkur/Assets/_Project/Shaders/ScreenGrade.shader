// One full-screen pass that does what a Multiply Light2D structurally cannot: desaturate the
// night, recontrast it, and close the screen edges in — plus the ordered dither that keeps a
// dim, low-contrast frame from banding.
//
// It replaces the day/night vignette, which used to be a 64x64 RGBA sprite stretched across the
// whole screen. That sprite was itself a banding source: 64 texels of radial gradient blown up
// to 1080p, quantised to 8 bits before it ever reached the frame.
//
// Colour space: the input is LINEAR. The 2D renderer leaves encoding to FinalBlitPass
// (Renderer2D sets enableColorEncoding false), so nothing here converts to sRGB.
//
// Operation order deliberately mirrors URP's own UberPost / LutBuilderLdr so the look stays
// portable if this is ever folded into a Volume: vignette, contrast in LogC, lift/gamma/gain,
// saturation, dither.
Shader "Hidden/Valkur/ScreenGrade"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            Name "ScreenGrade"

            HLSLPROGRAM
            #pragma vertex   Vert          // from Blit.hlsl — the draw is DrawProcedural with no
            #pragma fragment Frag          // mesh, so a hand-written vertex stage renders nothing.

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Pulls in core Color.hlsl (LinearToLogC / Luminance) and Blit.hlsl (_BlitTexture,
            // Vert, Varyings, the global samplers) in one include.
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

            // x = saturation multiplier, y = contrast multiplier,
            // z = vignette intensity,    w = vignette smoothness
            float4 _GradeParams;
            // rgb = vignette tint, a = dither strength in 1/255 units
            float4 _VignetteColor;
            // rgb = lift, a unused
            float4 _GradeLift;
            // rgb = inverse gamma, a unused
            float4 _GradeGamma;
            // rgb = gain, a unused
            float4 _GradeGain;

            // ACEScc mid-grey, the pivot URP contrasts around (core Color.hlsl uses the same
            // constant); spelled out so this file does not depend on the name being exported.
            #define VALKUR_ACESCC_MIDGRAY 0.4135884

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half3  c  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;

                // ── Vignette ────────────────────────────────────────────────────────────
                // Left elliptical on purpose: the sprite it replaces was a circle stretched to a
                // 2:1 viewport, so correcting for aspect here would visibly change the framing
                // players are used to.
                float2 d = abs(uv - 0.5) * _GradeParams.z;
                float  v = pow(saturate(1.0 - dot(d, d)), _GradeParams.w);
                c *= lerp(_VignetteColor.rgb, half3(1.0, 1.0, 1.0), v);

                // ── Contrast, in LogC around ACEScc mid-grey ────────────────────────────
                // Contrasting in linear crushes the shadows of a night frame into pure black;
                // doing it in log space keeps the toe, which is the whole point at 0.3 ambient.
                float3 logc = LinearToLogC(max(c, 0.0));
                logc = (logc - VALKUR_ACESCC_MIDGRAY) * _GradeParams.y + VALKUR_ACESCC_MIDGRAY;
                c = max(LogCToLinear(logc), 0.0);

                // ── Lift / gamma / gain ─────────────────────────────────────────────────
                c = c * _GradeGain.rgb + _GradeLift.rgb;
                c = sign(c) * pow(abs(c), _GradeGamma.rgb);

                // ── Saturation ──────────────────────────────────────────────────────────
                half luma = GetLuminance(c);
                c = luma + _GradeParams.x * (c - luma);

                // ── Ordered dither ──────────────────────────────────────────────────────
                // A 4x4 Bayer threshold, amplitude under one 8-bit step. Night is where a
                // gradient has the fewest code values to land on, so this is where banding
                // shows; the noise costs nothing and breaks the contours.
                float2 p = floor(uv * _ScreenParams.xy);
                float  bayer = frac(dot(float2(p.x, p.y), float2(0.75487766, 0.56984029)));
                c += (bayer - 0.5) * _VignetteColor.a;

                // FP16 target: a negative here poisons whatever samples it next.
                return half4(max(c, 0.0), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
