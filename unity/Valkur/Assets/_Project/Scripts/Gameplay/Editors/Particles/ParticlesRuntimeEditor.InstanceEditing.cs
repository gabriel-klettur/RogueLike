using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// COPY ON PLACE, from the editor's side: which configuration the properties panel is
    /// editing, and how to put a placement back onto its preset.
    ///
    /// A placed emitter owns its configuration — it took a copy of its preset the moment it was
    /// placed (see <see cref="ParticleInstanceConfig"/>) — so the panel has two possible
    /// targets and the rule is simply which one the author is looking at:
    ///
    ///  • an emitter is selected on the map  → the rows edit THAT placement, and nothing else
    ///    in the world moves;
    ///  • only a preset is selected in the picker → the rows edit the asset, which decides what
    ///    the NEXT placement is born with.
    ///
    /// Before this, every row edited the asset, and an author tuning the emitter they had just
    /// clicked watched all eighty-four instances of it change at once. That coupling is what
    /// the two "reapply preset" actions now restore deliberately: they overwrite one placement,
    /// or every placement of a preset, on request rather than as a side effect.
    /// </summary>
    public partial class ParticlesRuntimeEditor
    {
        /// <summary>
        /// The emitter whose configuration the properties panel is editing, or null when the
        /// panel is showing a preset with nothing placed selected.
        ///
        /// Requires the selected instance to actually be running the preset the form was built
        /// for: selecting a preset in the picker while an unrelated emitter is still selected
        /// must not silently write the picker's rows into that emitter.
        /// </summary>
        private ParticleEmitter ActiveConfigEmitter(string formPresetId)
        {
            if (_activeInstance == null) return null;

            var emitter = _activeInstance.GetComponentInParent<ParticleEmitter>();
            if (emitter == null || !emitter.HasOwnConfig) return null;

            string instancePresetId = GetPresetIdFromGo(_activeInstance);
            if (!string.IsNullOrEmpty(formPresetId) && !string.IsNullOrEmpty(instancePresetId) &&
                !string.Equals(formPresetId, instancePresetId, System.StringComparison.OrdinalIgnoreCase))
                return null;

            return emitter;
        }

        /// <summary>
        /// The block the form's rows should DISPLAY: the selected placement's own root block,
        /// or the preset's when no placement is selected.
        /// </summary>
        private ParticleVfxParams PropertyFormSource(ParticlePresetDefinition def, string pid)
        {
            var emitter = ActiveConfigEmitter(pid);
            return emitter != null ? emitter.Config.vfx : def?.vfx;
        }

        /// <summary>Header text for the form, stating exactly how far an edit reaches.</summary>
        private string PropertyFormScopeHeader(string pid)
        {
            if (ActiveConfigEmitter(pid) != null)
                return "THIS PLACEMENT ONLY — THE PRESET IS UNTOUCHED";

            int placed = CountPlacedUsing(pid);
            return placed == 0
                ? "PRESET — NOTHING PLACED FROM IT YET"
                : $"PRESET — EDITS REACH NEW PLACEMENTS ONLY ({placed} ALREADY PLACED)";
        }

        /// <summary>
        /// Applies one edited row to whichever configuration is in scope. Returns false with a
        /// reason, the same contract <see cref="ParticlePresetFieldWriter"/> has, so the panel
        /// can put the row back rather than display a value nothing accepted.
        /// </summary>
        private bool TryApplyPropertyEdit(ParticlePresetDefinition def, string pid,
                                          string key, object value, out string error)
        {
            var emitter = ActiveConfigEmitter(pid);
            if (emitter == null)
                return ParticlePresetFieldWriter.TrySetField(def, key, value, out error);

            var identity = _activeInstance.GetComponentInParent<PersistedParticleInstance>();
            var before = emitter.Config.Clone();

            if (!ParticlePresetFieldWriter.TrySetField(emitter.Config.vfx, key, value, out error))
                return false;

            var after = emitter.Config.Clone();
            var target = emitter;
            var targetIdentity = identity;

            // One undo entry per accepted row, and each carries a COPY of both states: the live
            // config keeps being written in place by later edits, so an undo record holding the
            // object itself would restore whatever the newest edit left there.
            ExecutePersistedEdit($"Edit particle · {key}",
                () => ApplyInstanceConfig(target, targetIdentity, after.Clone()),
                () => ApplyInstanceConfig(target, targetIdentity, before.Clone()));

            return true;
        }

        /// <summary>Pushes a configuration onto a placement and rebuilds it.</summary>
        private static void ApplyInstanceConfig(ParticleEmitter emitter,
                                                PersistedParticleInstance identity,
                                                ParticleInstanceConfig config)
        {
            if (emitter == null) return;

            identity?.SetConfig(config);
            emitter.ApplyConfig(emitter.Preset, config, emitter.ScaleMultiplier);
        }

        // ── Reapply preset ───────────────────────────────────────────────────────

        /// <summary>
        /// Overwrites the selected placement with a fresh copy of its preset — the deliberate
        /// version of the coupling copy-on-place removed. Undoable, and it saves like any other
        /// instance edit.
        /// </summary>
        private void ReapplyPresetToActiveInstance()
        {
            if (_activeInstance == null) { SetStatus("Select a placed emitter first."); return; }

            var emitter = _activeInstance.GetComponentInParent<ParticleEmitter>();
            var identity = _activeInstance.GetComponentInParent<PersistedParticleInstance>();
            if (emitter == null || identity == null) return;

            var preset = _catalog?.GetById(identity.PresetId);
            if (preset == null)
            {
                SetStatus($"Preset '{identity.PresetId}' is not in the catalog — nothing to reapply.");
                return;
            }

            var before = emitter.HasOwnConfig ? emitter.Config.Clone() : null;
            var after = ParticleInstanceConfig.SnapshotOf(preset, ParticleInstanceOverrides.None);
            var target = emitter;
            var targetIdentity = identity;

            ExecutePersistedEdit("Reapply preset to instance",
                () => ApplyInstanceConfig(target, targetIdentity, after.Clone()),
                () => ApplyInstanceConfig(target, targetIdentity, before?.Clone()));

            RebuildPresetPropertyForm(identity.PresetId);
            SetStatus($"'{identity.PresetId}' reapplied to this placement.");
        }

        /// <summary>
        /// Overwrites EVERY placement of the selected preset with a fresh copy of it. This is
        /// how a mass retune still happens once each placement owns its configuration: tune the
        /// asset, then push it out deliberately.
        ///
        /// One undo entry for the whole sweep — an author who pushes eighty-four fields and
        /// regrets it wants one press of Undo, not eighty-four.
        /// </summary>
        private void ReapplyPresetToAllInstances(string presetId)
        {
            var preset = _catalog?.GetById(presetId);
            if (preset == null) { SetStatus("Select a preset first."); return; }

            var emitters = new List<ParticleEmitter>();
            var identities = new List<PersistedParticleInstance>();
            var before = new List<ParticleInstanceConfig>();

            foreach (var identity in FindObjectsOfType<PersistedParticleInstance>(true))
            {
                if (identity == null) continue;
                if (!string.Equals(identity.PresetId, presetId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var emitter = identity.GetComponentInParent<ParticleEmitter>();
                if (emitter == null) continue;

                emitters.Add(emitter);
                identities.Add(identity);
                before.Add(emitter.HasOwnConfig ? emitter.Config.Clone() : null);
            }

            if (emitters.Count == 0)
            {
                SetStatus($"No placed emitter uses '{presetId}'.");
                return;
            }

            ExecutePersistedEdit($"Reapply preset to {emitters.Count} placements",
                () =>
                {
                    for (int i = 0; i < emitters.Count; i++)
                        ApplyInstanceConfig(emitters[i], identities[i],
                            ParticleInstanceConfig.SnapshotOf(preset, ParticleInstanceOverrides.None));
                },
                () =>
                {
                    for (int i = 0; i < emitters.Count; i++)
                        ApplyInstanceConfig(emitters[i], identities[i], before[i]?.Clone());
                });

            RebuildPresetPropertyForm(presetId);
            SetStatus($"'{presetId}' reapplied to {emitters.Count} placement(s).");
        }
    }
}
