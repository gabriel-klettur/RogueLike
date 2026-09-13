// Cloud shadows: one camera-following quad that MULTIPLIES the world by a slow field of
// value noise sampled by WORLD position. World position, not screen position — clouds pinned
// to the glass would slide with the camera and read as dirt on the lens; clouds pinned to
// the ground are something between the player and the sun.
//
// The field is three octaves of value noise, the first two carrying the shape and the third
// only roughening the edge, thresholded by a coverage with a wide soft band: a cloud's
// shadow has no hard edge at ground level, and the softness is most of what separates it
// from a shadow cast by a thing.
//
// Multiply blend, unlit, on a sorting layer above every world layer and under UI_World, so
// it darkens the ground, the buildings and the creatures alike — a cloud's shadow falls on
// the character too — and never a health bar.
Shader "Valkur/CloudShadow"
{
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend DstColor Zero

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 positionWS  : TEXCOORD0;
            };

            // x = noise scale (cycles per world unit), y = coverage threshold,
            // z = edge softness, w = strength (0 = no darkening)
            float4 _CloudParams;
            // xy = scroll offset in noise space, accumulated on the CPU from the wind.
            float4 _CloudOffset;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS).xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 q = IN.positionWS * _CloudParams.x + _CloudOffset.xy;
                float n = ValueNoise(q) * 0.60
                        + ValueNoise(q * 2.17 + 19.1) * 0.28
                        + ValueNoise(q * 4.91 + 7.3)  * 0.12;
                float cloud = smoothstep(_CloudParams.y - _CloudParams.z,
                                         _CloudParams.y + _CloudParams.z, n);
                float dark = 1.0 - cloud * _CloudParams.w;
                return half4(dark, dark, dark, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
