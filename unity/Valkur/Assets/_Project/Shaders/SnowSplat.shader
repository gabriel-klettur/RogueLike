// The two operations that maintain the world-space snow accumulation buffer
// (Gameplay/World/Weather/SnowSplatMap.cs). Never drawn to the screen — both passes only ever
// render into a single-channel offscreen RenderTexture.
//
// Pass 0 SPLAT      one soft additive disc per landed flake, drawn as a mesh of quads in
//                   WORLD space through a projection the C# side supplies.
// Pass 1 SCROLLFADE the whole buffer, resampled at an offset (so the map can follow the
//                   camera without the drift sliding across the world) and multiplied down
//                   (so lying snow melts). One pass rather than two: they run on the same
//                   cadence and a second full-buffer blit would be pure bandwidth.
Shader "Valkur/SnowSplat"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Splat"
            Blend One One          // accumulate: overlapping flakes deepen the drift

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // Model is identity and the view-projection is an ortho matrix over the map's
                // world rect, so object space IS world space here.
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv         = v.uv;
                o.color      = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // A radial falloff rather than a hard disc: hard discs tile into a visible
                // pattern of circles as they overlap, soft ones integrate into a drift.
                float2 d = i.uv * 2.0 - 1.0;
                float  a = saturate(1.0 - length(d));
                a = pow(a, 1.6);
                return half4(a * i.color.r, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ScrollFade"
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // xy = UV offset to resample at (the camera's move, in map units)
            // z  = melt multiplier for this step
            float4 _SnowScrollFade;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv         = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv + _SnowScrollFade.xy;

                // Ground scrolled in from outside the old buffer has no history, so it comes
                // in bare. Clamping instead would smear the last column of the old drift
                // across the newly revealed world as a stripe.
                float2 inside = step(0.0, uv) * step(uv, 1.0);
                float  v      = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv)).r;

                return half4(v * inside.x * inside.y * _SnowScrollFade.z, 0, 0, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
