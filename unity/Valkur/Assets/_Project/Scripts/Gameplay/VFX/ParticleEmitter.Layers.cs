using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticleEmitter
    {
        // ------------------------------------------------------------------ composite layers

        /// <summary>
        /// One child ParticleSystem per valid entry in the applied preset's
        /// <see cref="ParticlePresetDefinition.layers"/> list, in list order. Built and
        /// torn down by <see cref="SyncLayers"/>; never includes the root <c>_ps</c>.
        /// </summary>
        private readonly List<ParticleSystem> _layerSystems = new List<ParticleSystem>();

        /// <summary>
        /// Read-only view of the current layer ParticleSystems, exposed so callers that
        /// need to reach a specific layer (tooling, tests, future VFX polish) don't have
        /// to guess at child GameObject names. Empty when the applied preset has no
        /// layers, or none of them were valid.
        /// </summary>
        public IReadOnlyList<ParticleSystem> LayerSystems => _layerSystems;

        /// <summary>
        /// Builds/updates one child ParticleSystem per valid entry in
        /// <paramref name="preset"/>.layers, so a single placed instance (or spell slot)
        /// can be a whole stack — additive light over alpha mass, fast sparks over slow
        /// haze — without hand-placing N separate presets. See the vfx-authoring skill
        /// §1 "Layering".
        ///
        /// A layer entry is skipped when it is null, references the SAME preset (which
        /// would recurse forever if it did not), carries no vfx block, or is itself a
        /// "lightning" kind — lightning draws with a LineRenderer, not a ParticleSystem,
        /// and <see cref="ConfigureParticleSystem"/> has no path for it. A layer's own
        /// <c>layers</c> list is intentionally NOT recursed — one level deep only, so
        /// authoring cannot build an infinite or exploding tree by accident.
        ///
        /// Reuses child GameObjects by index (<c>Layer_0</c>, <c>Layer_1</c>, ...) across
        /// repeated calls: the F1 preview emitter (and the View panel) re-applies a preset
        /// — often a DIFFERENT preset — to the same ParticleEmitter every time the user
        /// clicks a picker entry, so without reuse every click would leak a fresh set of
        /// children. A child left over from a previously applied preset that had MORE
        /// valid layers than this one is destroyed, never left running.
        /// </summary>
        private void SyncLayers(ParticlePresetDefinition preset, float scale)
        {
            var layers = preset.layers;
            int layerCount = layers?.Count ?? 0;
            int writeIndex = 0;

            for (int i = 0; i < layerCount; i++)
            {
                var layer = layers[i];
                if (!IsValidLayer(preset, layer)) continue;

                ParticleSystem ps = writeIndex < _layerSystems.Count ? _layerSystems[writeIndex] : null;
                if (ps == null)
                {
                    ps = CreateLayerSystem(writeIndex);
                    if (writeIndex < _layerSystems.Count) _layerSystems[writeIndex] = ps;
                    else _layerSystems.Add(ps);
                }

                // Same "reactivate + stop + configure + play" recipe ApplyPreset uses for
                // the root: a burst layer's stopAction can disable its own GameObject, and
                // several main-module writes are rejected while a system is still playing.
                if (!ps.gameObject.activeSelf) ps.gameObject.SetActive(true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ConfigureParticleSystem(ps, layer.vfx, scale);
                ps.Play();

                writeIndex++;
            }

            // Extras left over from a preset with more (valid) layers than this one.
            for (int i = _layerSystems.Count - 1; i >= writeIndex; i--)
            {
                var extra = _layerSystems[i];
                if (extra != null) SafeDestroy.Of(extra.gameObject);
                _layerSystems.RemoveAt(i);
            }
        }

        /// <summary>
        /// Destroys every layer child outright. Called when the emitter switches to a
        /// "lightning" root preset — that path never calls <see cref="SyncLayers"/>, so
        /// without this any layers left over from a PREVIOUS preset would keep simulating
        /// underneath the bolt on a reused emitter (the F1 preview emitter serves every
        /// preset selection, lightning included).
        /// </summary>
        private void TeardownLayers()
        {
            for (int i = 0; i < _layerSystems.Count; i++)
            {
                var ps = _layerSystems[i];
                if (ps != null) SafeDestroy.Of(ps.gameObject);
            }
            _layerSystems.Clear();
        }

        private static bool IsValidLayer(ParticlePresetDefinition preset, ParticlePresetDefinition layer)
        {
            if (layer == null) return false;
            if (ReferenceEquals(layer, preset)) return false;
            if (layer.vfx == null) return false;
            if (layer.vfx.kind == "lightning") return false;
            return true;
        }

        private ParticleSystem CreateLayerSystem(int index)
        {
            var child = new GameObject($"Layer_{index}");
            child.transform.SetParent(transform, false);
            var ps = child.AddComponent<ParticleSystem>();
            // Same ordering constraint as the root's EnsureParticleSystem: AddComponent
            // starts the system immediately, and Unity rejects several main-module writes
            // (e.g. main.duration) while a system is still playing.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            return ps;
        }
    }
}
