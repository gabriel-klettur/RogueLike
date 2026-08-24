using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One placed emitter's OWN particle configuration — the whole vfx block for its root
    /// system plus one per composite layer, owned outright rather than read from the preset.
    ///
    /// COPY ON PLACE. A preset is a starting point, not a live link: an instance takes a copy
    /// of it the moment it is placed and is independent from then on, so editing the preset
    /// afterwards reaches the NEXT placement and none of the existing ones. Before this, every
    /// field in the F1 properties panel edited the shared asset, and an author tuning the
    /// emitter they had just selected watched all eighty-four of them change at once.
    ///
    /// The preset id is still recorded on the instance — it names where this configuration came
    /// from, drives the picker's grouping and the same-preset outlines, and is what the two
    /// "reapply preset" actions read to overwrite a config on request. It just no longer
    /// decides how the emitter behaves.
    ///
    /// WHAT IS NOT SNAPSHOTTED: <c>customSprite</c> and <c>flipbookFrames</c>. They are
    /// UnityEngine.Object references, which have no meaning in a StreamingAssets JSON file, so
    /// they keep coming from the preset. No preset in the shipped catalog sets either — checked
    /// across all 153 — so nothing today depends on the distinction; the emitter re-applies
    /// them from the preset when it builds, and this comment is here for the day one does.
    /// </summary>
    [Serializable]
    public sealed class ParticleInstanceConfig
    {
        [Tooltip("The instance's own root vfx block. Null means this instance has no config of " +
                 "its own yet and still follows its preset.")]
        public ParticleVfxParams vfx;

        [Tooltip("One block per composite layer, in the order SyncLayers builds them.")]
        public List<ParticleVfxParams> layers = new List<ParticleVfxParams>();

        /// <summary>True when this instance carries nothing and must fall back to its preset.</summary>
        public bool IsEmpty => vfx == null;

        /// <summary>Layer count, null-safe.</summary>
        public int LayerCount => layers?.Count ?? 0;

        public ParticleInstanceConfig() { }

        public ParticleInstanceConfig(ParticleVfxParams root, List<ParticleVfxParams> layerBlocks)
        {
            vfx = root;
            layers = layerBlocks ?? new List<ParticleVfxParams>();
        }

        /// <summary>
        /// The configuration a fresh placement of <paramref name="preset"/> is born with, with
        /// any legacy per-instance size ratios already folded in.
        ///
        /// The layer list mirrors the emitter's own validity rule — null slots, self-references
        /// and lightning-kind layers render nothing and are skipped — so index i here is index i
        /// of the systems the emitter builds. Anything else would resize or recolour one layer
        /// with another's numbers.
        /// </summary>
        public static ParticleInstanceConfig SnapshotOf(ParticlePresetDefinition preset,
                                                        ParticleInstanceOverrides overrides)
        {
            if (preset == null || preset.vfx == null) return new ParticleInstanceConfig();

            var root = ParticleOverrideApplier.Clone(
                ParticleOverrideApplier.Apply(preset.vfx, overrides));

            var blocks = new List<ParticleVfxParams>();
            if (preset.layers != null)
            {
                for (int i = 0; i < preset.layers.Count; i++)
                {
                    var layer = preset.layers[i];
                    if (!IsSnapshotableLayer(preset, layer)) continue;

                    blocks.Add(ParticleOverrideApplier.Clone(
                        ParticleOverrideApplier.Apply(layer.vfx, overrides)));
                }
            }

            return new ParticleInstanceConfig(root, blocks);
        }

        /// <summary>
        /// The emitter's own layer-validity rule, kept in step with
        /// <c>ParticleEmitter.IsValidLayer</c>: a null slot, a self-reference (which would
        /// recurse), a layer with no vfx block, and a lightning layer (drawn by a LineRenderer,
        /// for which ConfigureParticleSystem has no path) all render nothing.
        /// </summary>
        public static bool IsSnapshotableLayer(ParticlePresetDefinition preset,
                                               ParticlePresetDefinition layer)
        {
            if (layer == null || layer == preset || layer.vfx == null) return false;
            return !string.Equals(layer.vfx.kind, "lightning", StringComparison.Ordinal);
        }

        /// <summary>Deep copy. Undo records hold one of these; sharing would let a later edit
        /// rewrite the state the undo was supposed to restore.</summary>
        public ParticleInstanceConfig Clone()
        {
            var copy = new ParticleInstanceConfig(ParticleOverrideApplier.Clone(vfx),
                                                  new List<ParticleVfxParams>());
            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                    copy.layers.Add(ParticleOverrideApplier.Clone(layers[i]));
            }
            return copy;
        }
    }
}
