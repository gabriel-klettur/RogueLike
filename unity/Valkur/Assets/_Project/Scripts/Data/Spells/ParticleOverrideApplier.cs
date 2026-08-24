using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Applies a placed instance's <see cref="ParticleInstanceOverrides"/> to a preset's
    /// <see cref="ParticleVfxParams"/>, producing the block that instance actually runs.
    ///
    /// ONE implementation, used by both the emitter that builds the particle systems and the
    /// footprint that draws the marker around them. That is the whole point of the type: a
    /// second copy of these rules is a marker that stops matching the effect the first time
    /// either is touched, which is the class of bug the footprint work spent its afternoon on.
    ///
    /// The source block is never mutated — it belongs to a ScriptableObject shared by every
    /// placement of that preset, and writing to it would resize all of them.
    /// </summary>
    public static class ParticleOverrideApplier
    {
        /// <summary>
        /// Hard-coded emission strips <c>ConfigureShape</c> uses for the two "line" kinds. An
        /// override has nothing to multiply on these, so they are materialised into an
        /// authored spawn box first — the same box, expressed as data the instance can scale.
        /// Kept in sync with ParticleEmitter.ConfigureShape by
        /// <c>ParticleFootprintCoverageTests</c>, which measures both against real particles.
        /// </summary>
        private const float FALLING_LEAF_STRIP_WIDTH = 2f;
        private const float WATER_FLOW_STRIP_WIDTH = 3f;
        private const float STRIP_HEIGHT = 0.1f;

        /// <summary>
        /// The block <paramref name="source"/> becomes under <paramref name="overrides"/>.
        /// Returns the source itself when the overrides are default, so the common case
        /// allocates nothing and keeps sharing the preset's data.
        /// </summary>
        public static ParticleVfxParams Apply(ParticleVfxParams source, ParticleInstanceOverrides overrides)
        {
            if (source == null) return null;

            var o = overrides.Sanitized();
            if (o.IsDefault) return source;

            var v = Clone(source);

            ApplySpawnScale(v, o.spawnScaleX, o.spawnScaleY);
            ApplyReachScale(v, o.reachScale);

            return v;
        }

        /// <summary>
        /// Deep copy through JSON. The block has some sixty fields and three curve arrays, and
        /// a hand-written copy that misses one produces an instance that silently diverges
        /// from its preset in a way no test would catch.
        /// </summary>
        public static ParticleVfxParams Clone(ParticleVfxParams source)
            => source == null ? null : JsonUtility.FromJson<ParticleVfxParams>(JsonUtility.ToJson(source));

        // ── Emission area ────────────────────────────────────────────────────────

        private static void ApplySpawnScale(ParticleVfxParams v, float sx, float sy)
        {
            if (Mathf.Abs(sx - 1f) < 1e-4f && Mathf.Abs(sy - 1f) < 1e-4f) return;

            // An authored box scales per axis — the one case where width and height are
            // genuinely independent.
            if (v.spawnWidth > 0f || v.spawnHeight > 0f)
            {
                v.spawnWidth = Mathf.Max(0.01f, v.spawnWidth) * sx;
                v.spawnHeight = Mathf.Max(0.01f, v.spawnHeight) * sy;
                return;
            }

            // The two strip kinds hard-code their box in the emitter, so there is nothing to
            // scale until it is written down. Materialising it changes nothing on its own:
            // the authored box override reproduces exactly the shape the kind would have built.
            if (string.Equals(v.kind, "falling_leaf", System.StringComparison.Ordinal) ||
                string.Equals(v.kind, "water_flow", System.StringComparison.Ordinal))
            {
                float width = string.Equals(v.kind, "falling_leaf", System.StringComparison.Ordinal)
                    ? FALLING_LEAF_STRIP_WIDTH
                    : WATER_FLOW_STRIP_WIDTH;

                v.spawnWidth = width * sx;
                v.spawnHeight = STRIP_HEIGHT * sy;
                return;
            }

            // Every remaining kind emits from a CIRCLE, which has one radius and cannot be
            // stretched on one axis — the emitter has no ellipse to give it. The two ratios
            // are folded into their geometric mean so a designer dragging one side still gets
            // a proportional change instead of nothing, and the footprint applies the very
            // same rule, so the marker keeps matching the effect.
            float uniform = Mathf.Sqrt(Mathf.Max(1e-4f, sx * sy));

            v.radius *= uniform;
            if (v.outerRadius > 0f) v.outerRadius *= uniform;
            if (v.dispersion > 0f) v.dispersion *= uniform;
        }

        // ── Reach ────────────────────────────────────────────────────────────────

        private static void ApplyReachScale(ParticleVfxParams v, float reach)
        {
            if (Mathf.Abs(reach - 1f) < 1e-4f) return;

            // Everything that carries a particle away from where it was born, and nothing
            // else. Lifespan is deliberately untouched: stretching it would change how many
            // particles are alive at once — the density and the frame cost — which is a
            // different edit from "this field reaches further".
            v.speed *= reach;
            v.gravity *= reach;
            v.gravityVector *= reach;
            v.radialSpeed *= reach;
            v.noiseStrength *= reach;
            v.swayAmp *= reach;
        }
    }
}
