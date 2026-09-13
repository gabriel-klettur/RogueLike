// Death dissolve for a sprite: the body is eaten away cell by cell with an ember-coloured
// edge, until nothing is left. Included by both HDR sprite shaders.
//
// Cells are WORLD-space and about a texel wide, hashed rather than sampled from a noise
// texture, so a pixel-art body crumbles in pixel-art pieces and two corpses side by side do
// not crumble in the same pattern. The amount is per renderer through a property block; a
// renderer that never sets it pays one branch that is always false.
#ifndef VALKUR_DISSOLVE_INCLUDED
#define VALKUR_DISSOLVE_INCLUDED

float ValkurDissolveHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

half4 ValkurApplyDissolve(half4 c, float2 positionWS, float amount, float4 edgeColour)
{
    if (amount <= 0.0) return c;
    // 14 cells per unit: a little coarser than the 16 PPU texel, so a chunk is a chunk.
    float n    = ValkurDissolveHash(floor(positionWS * 14.0));
    float edge = amount * 1.12 - n;
    if (edge <= 0.0) return c;
    // A thin band at the boundary glows before it goes.
    const float band = 0.10;
    if (edge < band)
    {
        float t = 1.0 - edge / band;
        c.rgb = lerp(edgeColour.rgb * c.a, c.rgb, t * t);
        return c;
    }
    c.a = 0.0;
    c.rgb = 0.0;
    return c;
}

#endif
