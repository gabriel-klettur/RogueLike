#ifndef VALKUR_SNOW_INCLUDED
#define VALKUR_SNOW_INCLUDED

// Snow accumulation, shared by Valkur/SpriteHDRTint and Valkur/SpriteHDRTintLit.
//
// Two independent questions, answered by two different mechanisms, because neither one alone
// produces snow that looks like it fell:
//
//   WHERE, in the world, has snow settled?  ->  _SnowMap, a world-space accumulation buffer
//   that individual flakes stamp as they land (Gameplay/World/Weather/SnowSplatMap.cs). This
//   is the half that makes it physical: drifts build up where the snow actually fell, the
//   wind piles them on one side, a spot the fall has not reached yet stays bare, and it all
//   melts back unevenly. A single global scalar — which is what this used to be — spreads the
//   same value over every pixel in the world at once, and no amount of tuning makes that read
//   as accumulation rather than as a colour grade.
//
//   HOW does it sit on THIS surface?  ->  the sprite's own alpha. Snow settles where nothing
//   is above it, so the shader walks up to six texels up and asks how far it is from open sky.
//   That is what discovers the roof line of a building it has never seen, the crown of a tree,
//   the top rail of a fence — for all 969 building templates and every generated tile pack,
//   with no snow art authored anywhere, and it keeps working after the instance is rescaled,
//   mirrored or recoloured, because it is reading the pixels actually being drawn.
//
// The two multiply: the local depth decides how DEEP the cap grows down from the silhouette's
// top edge. A first dusting is a one-texel crest on the ridge line; a deep drift creeps five
// or six texels down the roof. That is the whole reason the reach is a variable and not the
// constant it started as.
//
// Six texels is a hard ceiling and it is bounded by the atlases, not by taste: the shipped
// packs use padding 2. It is nonetheless safe, because the distance is a MINIMUM over the
// samples — the first transparent texel found wins, and for a pixel near the top of a sprite
// that is always the padding, before any sample can reach a neighbour. The samples that DO
// leave the sprite only ever come from pixels with six opaque texels above them, which are by
// construction deep inside it. Those atlases also pack with rotation disabled, which is the
// only reason "up in texture space" is up in the world.
//
// Measured through a render, on a mid-grey (0.188 linear) wall with open sky above, at full
// local depth: top row 0.784, the row under it 0.380, the interior untouched, and nothing
// written into the empty pixels above the silhouette — a wall gets a crest, not a repaint,
// and snow can never end up hanging in the air beside a roof.

// Written once per frame by Valkur.Gameplay.World.Weather.SnowAccumulation and
// SnowSplatMap. TRUE globals, so they belong outside any UnityPerMaterial cbuffer.
float  _ValkurSnowAmount;
float4 _ValkurSnowColor;

TEXTURE2D(_ValkurSnowMap);
SAMPLER(sampler_ValkurSnowMap);
// xy = the map's world-space origin (bottom-left), zw = its world-space size.
float4 _ValkurSnowMapRect;

// The including shader must declare, BEFORE this file:
//   float  _SnowRole          per material: 0 = none, 1 = cap, 2 = blanket
//   float4 _MainTex_TexelSize the sprite atlas texel size, for the cap's neighbour reads
// They are per-material rather than global, so a shader that keeps a UnityPerMaterial
// cbuffer has to declare them INSIDE it — declaring them here would put a material property
// outside that cbuffer and silently cost the shader its SRP Batcher compatibility.
// _SnowRole is owned by Valkur.Core.Rendering.WorldSpriteMaterials, which hands out one
// shared material per role.

#define VALKUR_SNOW_ROLE_CAP     1.0
#define VALKUR_SNOW_ROLE_BLANKET 2.0

// How many texels the cap may reach down from the silhouette's top edge at full depth.
#define VALKUR_SNOW_MAX_TEXELS   6

/// Local snow depth at a world position, 0..1.
///
/// Outside the map — the buffer follows the camera and covers a finite region — this falls
/// back to the global amount rather than to zero. A hard rectangular boundary between snowed
/// and bare world is the one artefact a scrolling accumulation buffer must never show, and
/// off-screen geometry is exactly where nobody can tell the difference.
float ValkurSnowDepthAt(float2 positionWS)
{
    float2 uv = (positionWS - _ValkurSnowMapRect.xy) / max(_ValkurSnowMapRect.zw, 1e-4);

    float2 inside2 = step(0.0, uv) * step(uv, 1.0);
    float  inside  = inside2.x * inside2.y;

    float local = SAMPLE_TEXTURE2D(_ValkurSnowMap, sampler_ValkurSnowMap, saturate(uv)).r;
    local = lerp(1.0, local, inside);

    return saturate(_ValkurSnowAmount * local);
}

/// Returns <paramref name="albedo"/> with snow blended in. Fully branched off when nothing is
/// falling: a bare world pays one float compare per pixel and never touches the map.
float3 ValkurApplySnow(float3 albedo, float alpha, float2 uv, float2 positionWS,
                       TEXTURE2D_PARAM(snowTex, snowSampler), float4 texelSize)
{
    if (_ValkurSnowAmount <= 0.0 || _SnowRole <= 0.0 || alpha <= 0.004)
        return albedo;

    float depth = ValkurSnowDepthAt(positionWS);
    if (depth <= 0.002)
        return albedo;

    // Snow is not white paint. Carrying a fraction of the surface's own luminance keeps the
    // pixel art legible underneath — a flat wash erases the very detail the world is made of,
    // and a snowed roof that has lost its tiles reads as a rendering bug.
    float  lum  = dot(albedo, float3(0.299, 0.587, 0.114));
    float3 tint = _ValkurSnowColor.rgb * (0.78 + 0.30 * lum);

    if (_SnowRole >= VALKUR_SNOW_ROLE_BLANKET)
    {
        // Blanket: in a top-down projection the floor faces the sky across its whole area, so
        // all of it collects. Coverage lags the depth so a dusting reads as a dusting rather
        // than as a white floor — the ground is most of the frame and the first thing to look
        // wrong.
        return lerp(albedo, tint, depth * 0.72);
    }

    // Cap: how many opaque texels sit between this pixel and the sky directly above it.
    // A MINIMUM over the samples, so the first transparency found decides — which is what
    // keeps the deeper reads from mattering when the sprite ends before them.
    float distanceToSky = VALKUR_SNOW_MAX_TEXELS;
    [unroll]
    for (int i = 1; i <= VALKUR_SNOW_MAX_TEXELS; i++)
    {
        float a = SAMPLE_TEXTURE2D(snowTex, snowSampler, uv + float2(0.0, texelSize.y * i)).a;
        distanceToSky = min(distanceToSky, a < 0.5 ? (float)(i - 1) : (float)VALKUR_SNOW_MAX_TEXELS);
    }

    // The depth of the drift, in texels, growing DOWNWARD from the exposed edge. Starts below
    // one so the first snow of a fall is a partial crest on the ridge rather than a full row
    // of white appearing at once.
    float reach = 0.55 + depth * (VALKUR_SNOW_MAX_TEXELS - 0.55);
    float cover = saturate(reach - distanceToSky);

    // Bias the very top row up: snow builds from the exposed edge inward, and a linear ramp
    // draws a band of uniform white that reads as an outline rather than as a load of snow.
    cover *= lerp(1.0, 0.82, saturate(distanceToSky / VALKUR_SNOW_MAX_TEXELS));

    return lerp(albedo, tint, saturate(cover));
}

#endif // VALKUR_SNOW_INCLUDED
