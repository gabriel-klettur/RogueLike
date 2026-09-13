// The sun shadow of a sprite: the SAME sprite, drawn a second time as a dark silhouette
// sheared and squashed onto the ground from its feet. The shear and the squash are GLOBALS
// (one sun for the whole world, written once per frame by SunShadowState); the only thing
// per renderer is where its feet are in its own object space, because every sprite here
// has its own pivot and its own trim.
//
// Vertex maths, in object space, with h the height above the foot line:
//   x' = x + h * skew        (tips the silhouette sideways: west at dawn, east at dusk)
//   y' = foot + h * squash   (lays it down: short at noon, long at the horizon)
// so the foot line never moves and the shadow stays attached to whatever cast it.
//
// The alpha is the sprite's own alpha through a soft step, so an anti-aliased edge does not
// become a grey fringe on a shadow that should be one flat tone.
Shader "Valkur/SpriteShadowProjected"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FootY ("Foot line in object space", Float) = 0
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
                float _FootY;
            CBUFFER_END

            // x = skew, y = squash, z = alpha, w = unused. Written by SunShadowState.
            float4 _ValkurSunShadow;
            // The shadow's colour. Not black: a shadow on a lit ground is the ambient without
            // the sun in it, which leans blue.
            float4 _ValkurSunShadowColor;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 p = IN.positionOS;
                float  h = p.y - _FootY;
                p.x += h * _ValkurSunShadow.x;
                p.y  = _FootY + h * _ValkurSunShadow.y;
                OUT.positionHCS = TransformObjectToHClip(p);
                OUT.color       = IN.color;
                OUT.uv          = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                a = smoothstep(0.12, 0.55, a);
                a *= IN.color.a * _ValkurSunShadow.z * _ValkurSunShadowColor.a;
                return half4(_ValkurSunShadowColor.rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
