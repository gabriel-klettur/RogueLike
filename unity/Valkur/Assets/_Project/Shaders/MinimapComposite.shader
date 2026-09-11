// The minimap and the world map, composed in one pass.
//
// Every layer the map shows is computed here from three inputs: the baked terrain atlas
// (MinimapWorldBaker), the explored mask (MinimapFogMap) and a handful of view parameters.
// The graphic that uses it is a single quad whose UV runs 0..1 across its rect; the shader
// turns that into a WORLD position and samples everything in world space, so the same
// material draws a circular minimap and a rectangular full map with different numbers.
//
// Layers, bottom to top:
//   terrain  -> edge ink -> saturation/contrast -> remembered-vs-seen -> grid -> day/night
//   -> weather -> fog (drifting cloud ink) + frontier glow -> sonar ring -> vignette + rim
//
// No Mask component is used by the minimap: the circular edge is antialiased HERE, because
// a stencil mask gives a hard, aliased circle and forces a material copy per graphic.
Shader "Valkur/UI/MinimapComposite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Terrain atlas", 2D) = "black" {}
        _FogTex ("Explored mask", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _AtlasRect ("Atlas world rect (x0,y0,w,h)", Vector) = (0,0,1,1)
        _FogRect ("Fog world rect (x0,y0,w,h)", Vector) = (0,0,1,1)
        _View ("View (cx,cy,halfW,halfH)", Vector) = (0,0,24,24)
        _Shape ("Shape (circle, edgeSoft, vignette, rimShade)", Vector) = (1,0.012,0.45,0.35)
        _Player ("Player (x,y,visionR,hasPlayer)", Vector) = (0,0,14,1)
        _Look ("Look (sat, contrast, bright, edgeInk)", Vector) = (1.1,1.08,0.92,0.45)
        _Look2 ("Look2 (remBright, remSat, gridStr, gridSpacing)", Vector) = (0.62,0.55,0.05,10)
        _Tint ("Day/night tint (rgb, amount)", Color) = (1,1,1,0)
        _Weather ("Weather (desat, whiten, time, unused)", Vector) = (0,0,0,0)
        _FogLight ("Fog ink light", Color) = (0.085,0.095,0.135,1)
        _FogDark ("Fog ink dark", Color) = (0.025,0.03,0.05,1)
        _Frontier ("Frontier glow", Color) = (0.98,0.78,0.40,0.55)
        _Cloud ("Cloud (strength, scale, speed, unused)", Vector) = (0.65,0.09,0.35,0)
        _Void ("Void colour", Color) = (0.035,0.04,0.06,1)
        _Fog2 ("Fog2 (echo, hatchStrength, hatchSpacing, unused)", Vector) = (0.07,0.3,1.6,0)
        _Ping ("Sonar (radius, strength, width, unused)", Vector) = (0,0,1.2,0)
        _PingColor ("Sonar colour", Color) = (1,0.85,0.5,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "MinimapComposite"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _FogTex;
            fixed4 _Color;
            float4 _ClipRect;

            float4 _AtlasRect;
            float4 _FogRect;
            float4 _View;
            float4 _Shape;
            float4 _Player;
            float4 _Look;
            float4 _Look2;
            float4 _Tint;
            float4 _Weather;
            float4 _FogLight;
            float4 _FogDark;
            float4 _Frontier;
            float4 _Cloud;
            float4 _Void;
            float4 _Fog2;
            float4 _Ping;
            float4 _PingColor;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // Value noise, hashed from integer lattice points. Two octaves are enough for ink
            // clouds on a 170 px disc, and they are cheap enough for a 1200 px full map.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                return vnoise(p) * 0.62 + vnoise(p * 2.13 + 7.7) * 0.38;
            }

            float luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

            float3 saturateColor(float3 c, float s)
            {
                float l = luma(c);
                return lerp(l.xxx, c, s);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 d = (uv - 0.5) * 2.0;

                // ── Shape ────────────────────────────────────────────────────
                float shapeA = 1.0;
                float r = length(d);
                if (_Shape.x > 0.5)
                {
                    float aa = max(fwidth(r) * 1.25, _Shape.y);
                    shapeA = 1.0 - smoothstep(1.0 - aa, 1.0, r);
                }

                // ── World position of this pixel ─────────────────────────────
                float2 world = _View.xy + d * _View.zw;
                float pixelWorld = max(fwidth(world.x), 1e-4);

                // ── Terrain ──────────────────────────────────────────────────
                float2 auv = (world - _AtlasRect.xy) / _AtlasRect.zw;
                float inAtlas = step(0.0, auv.x) * step(0.0, auv.y) * step(auv.x, 1.0) * step(auv.y, 1.0);
                float4 terrain = tex2D(_MainTex, auv);
                float ground = terrain.a * inAtlas;

                // Edge ink: luminance gradient one screen pixel across, so the line stays a
                // hairline at every zoom instead of thickening as the map zooms in.
                float2 px = fwidth(auv);
                float lx = luma(tex2D(_MainTex, auv + float2(px.x, 0)).rgb) - luma(tex2D(_MainTex, auv - float2(px.x, 0)).rgb);
                float ly = luma(tex2D(_MainTex, auv + float2(0, px.y)).rgb) - luma(tex2D(_MainTex, auv - float2(0, px.y)).rgb);
                float ink = saturate(length(float2(lx, ly)) * 2.2);

                float3 col = terrain.rgb;
                col *= 1.0 - ink * _Look.w;
                col = saturateColor(col, _Look.x);
                col = (col - 0.5) * _Look.y + 0.5;
                col *= _Look.z;
                col = lerp(_Void.rgb, col, ground);

                // Water lives: blue-dominant ground gets a slow travelling glint. Detected from the
                // baked colour itself, so every water tile in every pack shimmers with no tagging.
                float waterMask = saturate((terrain.b - max(terrain.r, terrain.g * 0.92) - 0.035) * 9.0) * ground;
                if (_Fog2.w > 0.0)
                {
                    // Low frequency on purpose: at one cycle per world unit the glints were one
                    // or two screen pixels each and read as static, not as light on water.
                    float2 wq = world * 0.28 + float2(_Weather.z * 0.09, _Weather.z * 0.05);
                    float band = vnoise(wq) * 0.6 + vnoise(wq * 2.1 + 3.7) * 0.4;
                    float glint = smoothstep(0.58, 0.82, band);
                    col += float3(0.45, 0.65, 0.95) * glint * waterMask * _Fog2.w * 0.35;
                }

                // ── Seen vs remembered ───────────────────────────────────────
                float distPlayer = distance(world, _Player.xy);
                float seen = (1.0 - smoothstep(_Player.z * 0.72, _Player.z, distPlayer)) * _Player.w;
                float3 remembered = saturateColor(col, _Look2.y) * _Look2.x;
                col = lerp(remembered, col, seen);

                // ── Cartographic grid ────────────────────────────────────────
                if (_Look2.z > 0.0)
                {
                    float2 g = abs(frac(world / _Look2.w + 0.5) - 0.5) * _Look2.w;
                    float gridLine = 1.0 - smoothstep(0.0, pixelWorld * 1.1, min(g.x, g.y));
                    col = lerp(col, col * 0.55 + 0.08, gridLine * _Look2.z * ground);
                }

                // ── Day / night and weather ──────────────────────────────────
                col = lerp(col, col * _Tint.rgb * 1.15, _Tint.a);
                col = lerp(col, luma(col).xxx, _Weather.x);
                col = lerp(col, col * 0.7 + 0.3, _Weather.y);

                // ── Fog of war ───────────────────────────────────────────────
                float2 fuv = (world - _FogRect.xy) / _FogRect.zw;
                float inFog = step(0.0, fuv.x) * step(0.0, fuv.y) * step(fuv.x, 1.0) * step(fuv.y, 1.0);
                float explored = tex2D(_FogTex, fuv).r * inFog;
                float2 drift = float2(_Weather.z, _Weather.z * 0.61) * _Cloud.z;
                float cloud = fbm(world * _Cloud.y + drift);
                float cloud2 = fbm(world * _Cloud.y * 2.7 - drift * 1.7 + 3.1);
                // Contrast-stretched: raw fbm sits in 0.3..0.7, which on two dark inks is a
                // difference nobody can see. Stretched around its middle it reads as weather.
                float c = saturate(((cloud * 0.7 + cloud2 * 0.3) - 0.5) * (1.0 + 1.5 * _Cloud.x) + 0.5);
                float3 fogCol = lerp(_FogDark.rgb, _FogLight.rgb, c);
                // A faint echo of the ground, so unexplored reads as UNKNOWN rather than as void.
                // Outside the atlas there is no ground to echo; a flat mid-tone stands in for it
                // so the edge of the world does not show through the fog as a straight seam.
                float3 echo = lerp(float3(0.22, 0.24, 0.26), saturateColor(terrain.rgb, 0.25), ground);
                fogCol += echo * _Fog2.x;
                // Cartographer's hatching: diagonal hairlines, antialiased to one screen pixel.
                if (_Fog2.y > 0.0)
                {
                    float hs = max(_Fog2.z, 0.1);
                    float hp = abs(frac((world.x + world.y) / hs) - 0.5) * hs * 0.7071;
                    float hatch = 1.0 - smoothstep(0.0, pixelWorld * 0.9, hp);
                    fogCol += _FogLight.rgb * 0.9 * hatch * _Fog2.y;
                }
                float reveal = smoothstep(0.30, 0.62, explored);
                // Frontier band: bright where the mask crosses its middle, i.e. along the edge.
                float band = saturate(1.0 - abs(explored - 0.42) / 0.20) * (0.55 + 0.45 * c);
                col = lerp(fogCol, col, reveal);
                col += _Frontier.rgb * _Frontier.a * band * band;

                // ── Sonar ────────────────────────────────────────────────────
                if (_Ping.y > 0.0 && _Player.w > 0.5)
                {
                    float ring = exp(-pow((distPlayer - _Ping.x) / max(_Ping.z, 1e-3), 2.0));
                    col += _PingColor.rgb * ring * _Ping.y * (0.35 + 0.65 * reveal);
                }

                // ── Vignette and rim shade (circle only) ─────────────────────
                if (_Shape.x > 0.5)
                {
                    col *= 1.0 - _Shape.z * r * r * r;
                    col *= lerp(1.0, 1.0 - _Shape.w, smoothstep(0.84, 1.0, r));
                }
                else
                {
                    float2 e = abs(d);
                    float edge = max(e.x, e.y);
                    col *= 1.0 - _Shape.z * 0.6 * smoothstep(0.7, 1.0, edge);
                }

                half4 outc = half4(col, shapeA) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                outc.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(outc.a - 0.001);
                #endif
                return outc;
            }
        ENDCG
        }
    }
}
