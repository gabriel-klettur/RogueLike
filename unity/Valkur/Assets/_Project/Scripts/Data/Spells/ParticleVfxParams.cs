using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// VFX configuration block for a particle preset.
    /// All numeric values are stored in Unity-native units (world units, seconds).
    /// </summary>
    [Serializable]
    public class ParticleVfxParams
    {
        // --------------- Kind ---------------
        [Tooltip("Preset kind. Python kinds: aura, dash, laser, lightning, slash, explosion, " +
                 "smoke, smoke_emitter, arcane_flame, firework, water_fountain, falling_leaf, water_flow.")]
        public string kind = "explosion";

        // --------------- Loop behaviour ---------------
        [Tooltip("If true, the particle system runs continuously (looped). " +
                 "If false, it is a finite one-shot burst that dies after playing once. " +
                 "Auto-set by importer based on kind (explosion/smoke_burst/slash/firework → false, all others → true), " +
                 "but persisted in the asset so it can be tweaked per-preset without re-importing.")]
        public bool loops = true;

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

        // --------------- Orbit ---------------
        [Tooltip("Degrees per second every particle turns AROUND the emitter's centre. " +
                 "This is angular, so the whole field rotates as one body no matter where " +
                 "each particle sits — the one motion `speed` cannot express, since speed " +
                 "throws each particle straight out along its own spawn direction and a " +
                 "swarm of straight lines never reads as a swirl. Positive is " +
                 "counter-clockwise. Pair with a negative radialSpeed for a vortex that " +
                 "actually spirals inward instead of orbiting forever at spawn radius.")]
        public float orbitalSpeedDegrees = 0f;

        [Tooltip("World units per second every particle moves along the line from the " +
                 "emitter's centre through itself. NEGATIVE draws inward (a portal " +
                 "swallowing motes), positive pushes outward (a shockwave that keeps " +
                 "expanding). Scaled by the emitter's scale, so a preset spirals into its " +
                 "centre at the same fraction of its radius whatever size it is placed at. " +
                 "Reaching the centre takes radius / |radialSpeed| seconds — author the " +
                 "lifespan to match, or the motes pile up at the middle and sit there.")]
        public float radialSpeed = 0f;

        // --------------- Lifetime ---------------
        [Tooltip("Particle lifetime (seconds). Python: lifespan / 60  OR  life_ms / 1000.")]
        public float lifespan = 1f;

        // --------------- Size ---------------
        [Tooltip("Minimum particle size (world units). Python: size_range[0] / 16.")]
        public float sizeMin = 0.1f;

        [Tooltip("Maximum particle size (world units). Python: size_range[1] / 16.")]
        public float sizeMax = 0.3f;

        [Tooltip("Width divided by height. sizeMin/sizeMax give the HEIGHT and this scales " +
                 "the width against it, so 0.5 is a particle twice as tall as it is wide. " +
                 "1 keeps the square particle every preset has always had, and is the " +
                 "default, so nothing existing changes. Useful for anything with a long " +
                 "axis — leaves, petals, shards — which a square quad cannot express.")]
        public float sizeAspect = 1f;

        // --------------- Color ---------------
        [Tooltip("List of gradient colors. Uses all entries for a colour-over-lifetime gradient. " +
                 "Python: colors list (RGB 0-255).")]
        public Color[] colors = { Color.white };

        [Tooltip("Single primary color (used when 'colors' is empty). Python: color.")]
        public Color color = Color.white;

        [Tooltip("Use additive blending. Python: blend_mode == 'additive'.")]
        public bool additive = false;

        [Tooltip("Brightness multiplier on every start colour. 1 = as authored; above 1 " +
                 "overdrives toward glow (most useful on additive presets), below 1 dims. " +
                 "Applied to RGB only — alpha is untouched.")]
        public float colorIntensity = 1f;

        // --------------- Texture ---------------
        [Tooltip("Billboard texture shape. 'Auto' derives it from kind + additive. " +
                 "'None' restores the legacy untextured quad.")]
        public ParticleTextureShape textureShape = ParticleTextureShape.Auto;

        [Tooltip("Edge falloff of the procedural texture. 0 = hard disc, 1 = very soft haze. " +
                 "Ignored when a customSprite is assigned or textureShape is None.")]
        [Range(0f, 1f)]
        public float textureSoftness = 0.5f;

        [Tooltip("Optional hand-authored sprite. Overrides textureShape when set.")]
        public Sprite customSprite;

        // --------------- Rotation ---------------
        [Tooltip("Particles are born with a random rotation in ±this many degrees. " +
                 "0 = all axis-aligned, which makes identical billboards read as a repeated stamp.")]
        public float startRotationJitterDegrees = 0f;

        [Tooltip("Degrees per second each particle spins over its life. By default the sign " +
                 "is randomised per particle, so a positive value means 'spin this fast, " +
                 "either way' — see rotationOneWay to fix the direction.")]
        public float rotationSpeedDegrees = 0f;

        [Tooltip("Spin every particle the SAME way, in the direction rotationSpeedDegrees " +
                 "signs (positive = counter-clockwise), instead of randomising the sign per " +
                 "particle. The random default is right for fire and debris, where a system " +
                 "all turning together reads as one rotating texture. It is wrong for any " +
                 "shape that IS the effect — a Vortex gate is drawn by a couple of " +
                 "long-lived overlapping quads, and two of them turning opposite ways " +
                 "cancel into a flicker instead of a spin.")]
        public bool rotationOneWay = false;

        // --------------- Simulation space ---------------
        [Tooltip("Emit into world space instead of the emitter's local space. Required for any " +
                 "trail: on a moving emitter, local-space particles travel WITH it and nothing " +
                 "is ever left behind. Default false so existing presets are unchanged.")]
        public bool worldSpace = false;

        // --------------- Sorting & depth ---------------
        [Tooltip("Sorting layer this emitter draws in. Empty = 'VFX', which is what " +
                 "ParticleEmitter has always hard-coded for every system it builds, " +
                 "composite layers included. VFX sits ABOVE Entities, Decorations, " +
                 "WallsTop, ObjectsHigh and Projectiles, so every falling leaf and every " +
                 "mote of pollen draws in front of the player, every NPC, every wall top " +
                 "and every tree canopy — a falling_leaf_canopy preset cannot pass behind " +
                 "the trunk it falls from until this is set. Authored as the layer NAME, " +
                 "never the int ID: sorting-layer IDs are not stable across edits to " +
                 "ProjectSettings > Tags and Layers, and these presets are hand-authored " +
                 "data that has to survive one.")]
        public string sortingLayer = "";

        [Tooltip("Order within sortingLayer. 0 is the value ParticleEmitter hard-coded for " +
                 "every emitter it built, so leaving it alone reproduces today's draw " +
                 "order exactly.")]
        public int sortingOrder = 0;

        [Tooltip("Tie-break bias between transparent renderers that share a layer AND an " +
                 "order. Unity adds it to the system's distance from the camera before the " +
                 "transparency sort, so LOWER values draw in front. It is the only tool " +
                 "that can order the co-located systems of a composite preset against each " +
                 "other: they all land on the same layer at the same order and the " +
                 "instance loader pins every emitter to z = 0, so without it their draw " +
                 "order is Unity's internal tie-break instead of the author's choice. " +
                 "Hand-written C# VFX always had this — AuraController sets sortingLayerID " +
                 "+ sortingOrder + sortingFudge deliberately so an aura stays behind every " +
                 "Entities sprite; the capability was simply denied to authored presets.")]
        public float sortingFudge = 0f;

        // --------------- Ambient light ---------------
        [Tooltip("Tint this emitter by the world's ambient light instead of rendering at " +
                 "the authored daylight brightness forever. Every particle material is " +
                 "built on 'Universal Render Pipeline/Particles/Unlit', so Light2D never " +
                 "touches these quads: DayNightCycle drives the global light down to " +
                 "(0.20, 0.25, 0.45) at intensity 0.15, the tilemap goes near-black, and " +
                 "the leaves and pollen on top of it keep rendering at noon values. " +
                 "DayNightCycle's own doc comment on CurrentColor has claimed since it was " +
                 "written that 'other systems (vignette, particles) read this' — no " +
                 "particle code ever has. Default false, so existing presets are " +
                 "unchanged.")]
        public bool respondsToAmbientLight = false;

        // --------------- Shape & Radius ---------------
        [Tooltip("Emission radius for aura/circle shapes (world units). Python: radius / 16.")]
        public float radius = 1.5f;

        [Tooltip("How much of the emission shape is used, 0 = its rim only, 1 = its whole " +
                 "area. -1 (default) keeps whatever the kind hard-codes — `aura` and " +
                 "`portal` emit from the rim, every other circle from the full disc — so " +
                 "existing presets are unchanged. Author it when the kind's choice is the " +
                 "wrong one: an aura that should fill its circle instead of ringing it has " +
                 "had no way to say so.")]
        [Range(-1f, 1f)]
        public float shapeFill = -1f;

        [Tooltip("Arc range in degrees for slash emitters. Python: arc_range_degrees.")]
        public float arcRangeDegrees = 45f;

        // --------------- Lightning-specific ---------------
        [Tooltip("Number of zigzag segments for lightning. Python: segments.")]
        public int segments = 10;

        [Tooltip("Max lateral offset per segment for lightning zigzag (world units). Python: offset / 16.")]
        public float lightningOffset = 0.625f;

        [Tooltip("Beam / ribbon thickness (world units). Python: thickness / 16.")]
        public float thickness = 0.1f;

        // --------------- Falling Leaf / Sway ---------------
        [Tooltip("Horizontal sway amplitude (world units). Python: sway_amp / 16.")]
        public float swayAmp = 0.04f;

        [Tooltip("Sway frequency (cycles per second). Python: sway_speed (dimensionless tuning value).")]
        public float swaySpeed = 0.12f;

        // --------------- Spawn area & direction ---------------
        [Tooltip("Width of the spawn area (world units), measured ACROSS the emission " +
                 "heading — horizontal when the heading is up, the default. Above 0 this " +
                 "overrides the kind's built-in emission shape with a centred box of exactly " +
                 "this footprint — the kinds otherwise hard-code their areas (falling_leaf a " +
                 "2-unit strip, water_flow a 3-unit strip, smoke a circle of `dispersion`). " +
                 "0 = keep the kind's own shape.")]
        public float spawnWidth = 0f;

        [Tooltip("Height of the spawn area (world units), measured ALONG the emission " +
                 "heading — vertical when the heading is up, the default. Same override rule " +
                 "as spawnWidth; setting either engages the box.")]
        public float spawnHeight = 0f;

        [Tooltip("Direction particles are emitted toward, in degrees: 0 = right, 90 = up, " +
                 "180 = left, 270 = down. Works through `speed`, which becomes the throw " +
                 "along this heading. -1 (default) keeps the kind's own behaviour. Replaces " +
                 "the old `direction` Vector2, which was imported from Python and read by " +
                 "nothing.")]
        public float directionDegrees = -1f;

        [Tooltip("Half-angle of the emission cone around directionDegrees. 0 is a laser-" +
                 "straight stream, 15 a gentle fan, 60 a wide spray. Ignored while " +
                 "directionDegrees is -1.")]
        public float directionSpreadDegrees = 15f;

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

        // --------------- Flipbook (Texture Sheet Animation) ---------------
        [Tooltip("Ordered animation frames. When non-empty the emitter switches to Unity's " +
                 "Texture Sheet Animation in Sprites mode and every particle plays this " +
                 "sequence over its lifetime instead of showing one static texture. All " +
                 "frames must live in the same SpriteAtlas or Unity cannot batch them.")]
        public Sprite[] flipbookFrames;

        [Tooltip("How many times the whole frame sequence plays across one particle lifetime. " +
                 "1 = the frames tell a single story from birth to death (the usual choice for " +
                 "an evolving shape such as a smoke puff). Higher values loop the sequence.")]
        public int flipbookCycles = 1;

        [Tooltip("Start each particle on a random frame. Leave OFF when the frames depict a " +
                 "progression — a particle that starts half-dissipated reads as a glitch. " +
                 "Turn ON only for sequences where every frame is a valid starting state.")]
        public bool flipbookRandomStartFrame = false;

        // --------------- Noise (turbulence) ---------------
        [Tooltip("Enable Unity's noise module. This is what separates drifting smoke from a " +
                 "blob that merely shrinks. Kept opt-in so existing presets keep their look; " +
                 "the falling_leaf kind still gets its legacy sway when this is off.")]
        public bool noiseEnabled = false;

        [Tooltip("Noise displacement in world units. Scaled by the emitter's scale.")]
        public float noiseStrength = 0f;

        [Tooltip("Noise frequency — low values give slow, broad billowing; high values give " +
                 "fast, fine jitter.")]
        public float noiseFrequency = 0.5f;

        [Tooltip("How fast the noise field scrolls. Keeps a long-lived particle from settling " +
                 "into a static offset.")]
        public float noiseScrollSpeed = 0.2f;

        [Tooltip("Vertical share of the noise, 0..1. Noise displaces on every axis, so a " +
                 "strength comparable to the fall speed pushes particles UPWARD as often as " +
                 "down — which is wrong for anything that must always descend, like a " +
                 "falling leaf. Below 1 this enables separateAxes and scales only the Y " +
                 "component, keeping the horizontal flutter at full width. 1 = the original " +
                 "uniform behaviour, so existing presets are unchanged.")]
        [Range(0f, 1f)]
        public float noiseVerticalScale = 1f;

        [Tooltip("Times the particle turns over across its life. Above 0 this narrows the " +
                 "WIDTH on a cosine while leaving the height alone, so the quad reads as a " +
                 "flat thing rotating about its long axis — wide face-on, almost gone " +
                 "edge-on. It is how a leaf can look like a leaf while descending a straight " +
                 "vertical, without the lateral drift that normally carries that impression. " +
                 "0 = off, and every existing preset keeps its uniform sizeOverLife.")]
        public int turnoverCycles = 0;

        [Tooltip("How thin the particle gets edge-on, as a fraction of full width. Never 0: " +
                 "a quad at zero width pops out of existence for a frame instead of turning.")]
        [Range(0.02f, 1f)]
        public float turnoverMinWidth = 0.12f;

        // --------------- Portal ---------------
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
