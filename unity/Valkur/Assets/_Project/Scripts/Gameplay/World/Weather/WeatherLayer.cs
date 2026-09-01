using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// One depth slice of a weather effect: a child <see cref="ParticleSystem"/> plus the
    /// authored constants <see cref="WeatherEffect"/> needs in order to drive it (emission
    /// rate, start colour, how much of the day/night tint it takes, how hard the wind pushes it).
    ///
    /// A weather used to be a single ParticleSystem, which is why it read as a decal taped to
    /// the lens: every drop was the same size, the same brightness and moving at the same
    /// speed, so the eye had nothing to resolve depth from. Splitting one effect into three or
    /// four slices — small/dim/slow far away, large/faint/fast up close — is what turns a flat
    /// overlay into something the camera is standing inside of.
    ///
    /// The class holds no update logic on purpose. The effect owns the frame loop; a layer is
    /// the state that loop reads and the two setters it writes.
    /// </summary>
    public sealed class WeatherLayer
    {
        public WeatherLayer(ParticleSystem system, float depth)
        {
            System   = system;
            Renderer = system.GetComponent<ParticleSystemRenderer>();
            Depth    = Mathf.Clamp01(depth);
        }

        /// <summary>The child system this slice draws with.</summary>
        public readonly ParticleSystem System;

        /// <summary>Cached renderer — fetched once, written on every wind change.</summary>
        public readonly ParticleSystemRenderer Renderer;

        /// <summary>0 = furthest slice, 1 = closest to the lens. Drives the parallax terms.</summary>
        public readonly float Depth;

        /// <summary>Particles per second at density 1.0, before the fade and the level.</summary>
        public float BaseRate = 50f;

        /// <summary>
        /// The colour the layer would have in full daylight. Alpha is the layer's authored
        /// coverage; the live start colour is this multiplied by the ambient tint (RGB) and
        /// the activation fade (alpha).
        /// </summary>
        public Color BaseColor = Color.white;

        /// <summary>
        /// How much of the day/night tint this layer takes, 0..1. Rain and airborne dust are
        /// lit almost entirely by the sky and take nearly all of it; snow takes less, because
        /// a snowfield stays legibly bright at night and a flake tinted to match the ground
        /// simply disappears.
        /// </summary>
        public float AmbientResponse = 1f;

        /// <summary>
        /// Multiplier on <see cref="WeatherWind.VelocityX"/> for this slice. Above 1 for near
        /// layers so a gust visibly shears the depth stack instead of translating it — the
        /// parallax cue that makes the near slice read as near.
        /// </summary>
        public float WindFactor = 1f;

        /// <summary>
        /// When true the emission rate is scaled by how much world area the viewport covers,
        /// relative to <see cref="ReferenceArea"/>. Layers that spawn ACROSS the visible area
        /// (splashes, settled snow) need this or their density would halve when the camera
        /// zooms out; layers that spawn along one EDGE do not, because their emitter box
        /// already grows with the viewport.
        /// </summary>
        public bool RateScalesWithViewportArea;

        /// <summary>
        /// How much wider than the viewport this layer's spawn slab currently is, written by
        /// <c>WeatherEffect.LayoutFallingLayer</c>.
        ///
        /// A crosswind is not a rotation of the curtain, it is a horizontal displacement that
        /// grows with how long a particle is in the air — so the slab a falling layer spawns
        /// from has to be widened upwind by the full drift distance, or the upwind third of
        /// the screen stays dry while the storm piles up on the other side. Spreading the same
        /// emission rate over a wider slab then thins the ON-SCREEN density by exactly this
        /// factor, which is why the rate is multiplied back up by it: without that, turning
        /// the wind up makes the rain visibly stop.
        /// </summary>
        public float SpawnWidthScale = 1f;

        /// <summary>
        /// The viewport area, in square world units, that <see cref="BaseRate"/> was authored
        /// against: the shipped 2:1 viewport at ortho 5, which is 20 x 10 world units — the
        /// zoom the game is actually played at.
        /// </summary>
        public const float ReferenceArea = 200f;

        /// <summary>Applies the live emission rate. Cheap enough to call every frame.</summary>
        public void SetRate(float ratePerSecond)
        {
            var emission = System.emission;
            emission.rateOverTime = Mathf.Max(0f, ratePerSecond);
        }

        /// <summary>
        /// Rebuilds the start colour from <see cref="BaseColor"/>, the ambient tint and the
        /// activation fade.
        ///
        /// Start colour rather than a colour-over-lifetime key because colorOverLifetime
        /// MULTIPLIES the start colour — so every layer's own fade-in/fade-out gradient
        /// inherits the tint without knowing the day/night cycle exists. Note Unity does not
        /// recolour particles already alive; over a fade measured in seconds against a drop
        /// lifetime under two, the trailing generation is never distinguishable.
        /// </summary>
        public void SetTint(Color ambient, float fadeAlpha)
        {
            Color rgb = Color.Lerp(Color.white, ambient, AmbientResponse);
            var main  = System.main;
            main.startColor = new Color(
                BaseColor.r * rgb.r,
                BaseColor.g * rgb.g,
                BaseColor.b * rgb.b,
                BaseColor.a * Mathf.Clamp01(fadeAlpha));
        }
    }
}
