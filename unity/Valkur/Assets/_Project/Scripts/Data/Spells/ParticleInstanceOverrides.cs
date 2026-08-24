using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Per-INSTANCE size overrides for a placed particle emitter: how much wider, taller and
    /// further-reaching this one placement is than the preset says.
    ///
    /// Particle parameters live on the preset, which every placement of it shares — edit
    /// <c>spawnWidth</c> there and all 84 leaf fields in the world change together. Resizing
    /// one field in the F1 editor has to reach only that field, so the instance carries its
    /// own multipliers and the emitter applies them on top of the shared preset when it
    /// builds its systems.
    ///
    /// RATIOS, not absolute sizes, for three reasons:
    ///
    ///  • They survive preset retuning. An author who widens the pollen preset by 20% keeps
    ///    every instance's intent ("this one is half again as wide as the preset") instead of
    ///    freezing the old absolute number into the world file.
    ///  • They compose with <c>scale_multiplier</c>, which is already a ratio, without one
    ///    silently overriding the other.
    ///  • They are unit-free, so a preset that is a circle today and a box tomorrow does not
    ///    need its instances migrated.
    ///
    /// The editor's drag handles work in world units and convert; the JSON stores the ratio.
    /// </summary>
    [Serializable]
    public struct ParticleInstanceOverrides
    {
        /// <summary>
        /// Smallest and largest ratio a handle may drag to. The floor is not zero: a zero-width
        /// emitter emits from a line the author then cannot grab to undo it, and a ratio that
        /// small is indistinguishable from "delete this instance", which is a different button.
        /// </summary>
        public const float MinRatio = 0.05f;
        public const float MaxRatio = 20f;

        [Tooltip("Horizontal size of this instance's emission area, as a ratio of the preset's. 1 = inherit.")]
        public float spawnScaleX;

        [Tooltip("Vertical size of this instance's emission area, as a ratio of the preset's. 1 = inherit.")]
        public float spawnScaleY;

        [Tooltip("How far this instance's particles travel from where they are born, as a ratio " +
                 "of the preset's. Multiplies every motion term at once — speed, drift, gravity, " +
                 "radial pull and turbulence — because they are what carries a particle away " +
                 "from its spawn point, and scaling one of them alone changes the effect's " +
                 "character rather than its size.")]
        public float reachScale;

        /// <summary>The identity override: inherit everything from the preset.</summary>
        public static ParticleInstanceOverrides None => new ParticleInstanceOverrides(1f, 1f, 1f);

        public ParticleInstanceOverrides(float spawnScaleX, float spawnScaleY, float reachScale)
        {
            this.spawnScaleX = spawnScaleX;
            this.spawnScaleY = spawnScaleY;
            this.reachScale = reachScale;
        }

        /// <summary>
        /// True when this instance adds nothing to its preset. The emitter checks it to keep
        /// the common path allocation-free: an instance that has never been resized shares the
        /// preset's own vfx blocks instead of cloning them.
        /// </summary>
        public bool IsDefault =>
            Mathf.Abs(spawnScaleX - 1f) < 1e-4f &&
            Mathf.Abs(spawnScaleY - 1f) < 1e-4f &&
            Mathf.Abs(reachScale - 1f) < 1e-4f;

        /// <summary>
        /// Clamped, NaN-free copy. Everything that comes from JSON, from a drag, or from an
        /// undo record goes through here: a NaN ratio propagates into every size and radius the
        /// emitter writes and takes the whole ParticleSystem down with it, silently.
        /// </summary>
        public ParticleInstanceOverrides Sanitized()
        {
            return new ParticleInstanceOverrides(
                Clamp(spawnScaleX), Clamp(spawnScaleY), Clamp(reachScale));
        }

        private static float Clamp(float ratio)
        {
            // A default-constructed struct is all zeros, which would otherwise read as "shrink
            // this emitter to nothing". Zero means "not set" and resolves to inherit.
            if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f) return 1f;
            return Mathf.Clamp(ratio, MinRatio, MaxRatio);
        }

        public override string ToString()
            => $"x{spawnScaleX:0.###} y{spawnScaleY:0.###} reach{reachScale:0.###}";
    }
}
