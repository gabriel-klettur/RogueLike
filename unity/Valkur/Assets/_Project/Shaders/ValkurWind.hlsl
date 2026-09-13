// Wind sway for a sprite whose base is planted: the top of the quad leans with the gust, the
// bottom row never moves. Included by both HDR sprite shaders behind the _VALKUR_SWAY keyword.
//
// The displacement is in WORLD units and applied to the world position, so a rescaled
// building sways by the same amount as its neighbour: an author who doubles a bush must not
// double its wind. The phase is the sprite's world X, so a row of trees is never a chorus
// line — each leans a little out of step with the next, which is most of what separates
// "wind" from "everything scrolling".
//
// uv.y squared: the split line of a canopy is at uv.y = 0 and must not open a crack against
// the trunk drawn under it, and a linear ramp reads as the whole sprite tilting on a hinge.
#ifndef VALKUR_WIND_INCLUDED
#define VALKUR_WIND_INCLUDED

// x = amplitude in world units, y = time, z = gust 0..1, w = blow direction (-1 / +1).
// Published by WindSway.Publish once per frame.
float4 _ValkurWind;

float ValkurWindSway(float3 positionWS, float2 uv)
{
    float amp   = _ValkurWind.x;
    float t     = _ValkurWind.y;
    float phase = positionWS.x * 0.61 + positionWS.y * 0.23;
    // Two sines out of ratio: a single one is a metronome, and a canopy in a gust is not.
    float s = sin(t * 1.7 + phase) * 0.62 + sin(t * 3.1 + phase * 1.37) * 0.38;
    // A slight lean downwind on top of the oscillation, growing with the gust.
    float lean = _ValkurWind.w * _ValkurWind.z * 0.35;
    float h = uv.y * uv.y;
    return (s + lean) * amp * h;
}

#endif
