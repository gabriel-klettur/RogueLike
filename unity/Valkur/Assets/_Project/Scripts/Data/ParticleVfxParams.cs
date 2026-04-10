using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// VFX configuration block for a particle preset.
    /// Maps to the "vfx.particles" block in Python's particles.json.
    ///
    /// All numeric values are stored in Unity-native units (world units, seconds).
    /// The ParticlePresetImporter converts Python pixel/tick units during import.
    ///   Python px → Unity units:  value / 32
    ///   Python px/tick → Unity units/s:  value * 60 / 32
    ///   Python px/tick² → Unity units/s²:  value * 3600 / 32
    ///   Python ticks → seconds:  value / 60
    ///   Python emit_rate (particles/tick) → particles/s:  value * 60
    ///   Python RGB [0..255] → Unity Color:  component / 255
    /// </summary>
    [Serializable]
    public class ParticleVfxParams
    {
        // --------------- Kind ---------------
        [Tooltip("Preset kind. Python kinds: aura, dash, laser, lightning, slash, explosion, " +
                 "smoke, smoke_emitter, arcane_flame, firework, water_fountain, falling_leaf, water_flow.")]
        public string kind = "explosion";

        // --------------- Emission ---------------
        [Tooltip("Particles emitted per second for looping emitters. Python: emit_rate * 60.")]
        public float emitRate = 10f;

        [Tooltip("Particle count for burst emitters (explosion, smoke_burst, slash, firework). Python: count.")]
        public int count = 12;

        [Tooltip("Interval between each auto-burst emission cycle (seconds). Python: interval_ms / 1000.")]
        public float burstIntervalSeconds = 1f;

        // --------------- Motion ---------------
        [Tooltip("Initial particle speed (world units/s). Python: speed * 60 / 16.")]
        public float speed = 2f;

        [Tooltip("Gravity acceleration (world units/s²). Python: gravity * 3600 / 16. " +
                 "Positive = downward (Unity Y-up, so applied as negative Y force).")]
        public float gravity = 0f;

        [Tooltip("Velocity damping factor [0..1] applied per second. Python: drag.")]
        [Range(0f, 0.98f)]
        public float drag = 0f;

        [Tooltip("Emission direction for directional emitters (world-space, normalised). Python: direction.")]
        public Vector2 direction = Vector2.down;

        // --------------- Lifetime ---------------
        [Tooltip("Particle lifetime (seconds). Python: lifespan / 60  OR  life_ms / 1000.")]
        public float lifespan = 1f;

        // --------------- Size ---------------
        [Tooltip("Minimum particle size (world units). Python: size_range[0] / 16.")]
        public float sizeMin = 0.1f;

        [Tooltip("Maximum particle size (world units). Python: size_range[1] / 16.")]
        public float sizeMax = 0.3f;

        // --------------- Color ---------------
        [Tooltip("List of gradient colors. Uses all entries for a colour-over-lifetime gradient. " +
                 "Python: colors list (RGB 0-255).")]
        public Color[] colors = { Color.white };

        [Tooltip("Single primary color (used when 'colors' is empty). Python: color.")]
        public Color color = Color.white;

        [Tooltip("Use additive blending. Python: blend_mode == 'additive'.")]
        public bool additive = false;

        // --------------- Shape & Radius ---------------
        [Tooltip("Emission radius for aura/circle shapes (world units). Python: radius / 16.")]
        public float radius = 1.5f;

        [Tooltip("Arc range in degrees for slash emitters. Python: arc_range_degrees.")]
        public float arcRangeDegrees = 45f;

        // --------------- Lightning-specific ---------------
        [Tooltip("Number of zigzag segments for lightning. Python: segments.")]
        public int segments = 10;

        [Tooltip("Max lateral offset per segment for lightning zigzag (world units). Python: offset / 16.")]
        public float lightningOffset = 0.625f;

        [Tooltip("Beam / ribbon thickness (world units). Python: thickness / 16.")]
        public float thickness = 0.1f;

        // --------------- Water Fountain ---------------
        [Tooltip("Normalised X positions of water spouts in [0..1]. Python: spouts.")]
        public float[] spouts = { 0.5f };

        [Tooltip("Number of splash particles per droplet on landing. Python: splash_count.")]
        public int splashCount = 2;

        [Tooltip("Droplet particle size (world units). Python: droplet_size / 16.")]
        public float dropletSize = 0.1f;

        // --------------- Falling Leaf / Sway ---------------
        [Tooltip("Horizontal sway amplitude (world units). Python: sway_amp / 16.")]
        public float swayAmp = 0.04f;

        [Tooltip("Sway frequency (cycles per second). Python: sway_speed (dimensionless tuning value).")]
        public float swaySpeed = 0.12f;

        // --------------- Water Flow ---------------
        [Tooltip("Gap between flow stripes (world units). Python: stripe_gap / 16.")]
        public float stripeGap = 0.5f;

        [Tooltip("Ripple amplitude for water surface effect. Python: ripple_amp.")]
        public float rippleAmp = 0.6f;

        [Tooltip("Base alpha (0-255). Python: alpha_base.")]
        [Range(0, 255)]
        public int alphaBase = 110;

        [Tooltip("Alpha wave amplitude (0-255). Python: alpha_wave.")]
        [Range(0, 255)]
        public int alphaWave = 70;

        [Tooltip("Secondary highlight colour for water flow. Python: highlight_color.")]
        public Color highlightColor = new Color(0.23f, 0.43f, 0.63f, 1f);

        // --------------- Smoke Dispersion ---------------
        [Tooltip("Emission spread for smoke_emitter kind (world units). Python: dispersion / PPU.")]
        public float dispersion = 0f;

        // --------------- Gravity vector ---------------
        [Tooltip("Gravity as X,Y vector (world units/s²). Python: gravity [gx,gy] * TICK² / PPU. " +
                 "Used when Python passes gravity as a list [gx, gy]. Y-flipped for Unity.")]
        public Vector2 gravityVector = Vector2.zero;

        [Tooltip("True if Python gravity was a [gx,gy] vector instead of a scalar.")]
        public bool useGravityVector = false;

        // --------------- Curves ---------------
        [Tooltip("Size over lifetime keyframes [[t, scale], ...]. Python: size_over_life.")]
        public Keyframe2D[] sizeOverLife;

        [Tooltip("Alpha over lifetime keyframes [[t, alpha01], ...]. Python: alpha_over_life.")]
        public Keyframe2D[] alphaOverLife;

        [Tooltip("Color over lifetime keyframes [[t, [R,G,B]], ...]. Python: color_over_life.")]
        public ColorKeyframe[] colorOverLife;

        // --------------- Portal ---------------
        [Tooltip("Ellipse aspect ratio for portal rendering. Python: ellipse_ratio.")]
        public float ellipseRatio = 1f;

        [Tooltip("Outer ring radius for portal presets (world units). Python: outer_radius / PPU.")]
        public float outerRadius = 0f;
    }

    /// <summary>Simple 2D keyframe: time [0..1] → value.</summary>
    [Serializable]
    public struct Keyframe2D
    {
        public float time;
        public float value;

        public Keyframe2D(float t, float v) { time = t; value = v; }
    }

    /// <summary>Color keyframe: time [0..1] → color.</summary>
    [Serializable]
    public struct ColorKeyframe
    {
        public float time;
        public Color color;

        public ColorKeyframe(float t, Color c) { time = t; color = c; }
    }
}
