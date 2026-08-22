using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Debounced autosave for edited preset <see cref="ParticlePresetDefinition"/> assets.
    ///
    /// Placed INSTANCES autosave to StreamingAssets JSON on every edit (see
    /// <c>ParticlesRuntimeEditor.Persistence.cs</c> / <c>ExecutePersistedEdit</c>). PRESET
    /// edits used to be the opposite: the Properties panel, the Table cells and the Loops
    /// toggle all mutated the ScriptableObject in memory and marked it dirty, but nothing
    /// ever flushed that to disk except pressing Save — not closing the editor (F1), not
    /// leaving Play Mode, not quitting. A preset edit that silently waits for an explicit
    /// Save reads as "the editor forgot my change" next to instances that never forget.
    ///
    /// This partial closes that gap without going back to writing on every keystroke: a
    /// slider drag or a run of typed digits marks the preset dirty repeatedly, so the flush
    /// is debounced — each dirty mark pushes the deadline out by
    /// <see cref="PRESET_AUTOSAVE_DEBOUNCE_SECONDS"/> and only the first quiet moment after
    /// the last edit actually touches the AssetDatabase. The flush is also targeted
    /// (<see cref="UnityEditor.AssetDatabase.SaveAssetIfDirty"/> on exactly the presets this
    /// editor touched) rather than a project-wide <c>SaveAssets()</c>, so an unrelated dirty
    /// asset elsewhere in the project is never swept up as a side effect of opening F1.
    /// </summary>
    public partial class ParticlesRuntimeEditor
    {
        private const float PRESET_AUTOSAVE_DEBOUNCE_SECONDS = 0.75f;

        private readonly HashSet<ParticlePresetDefinition> _dirtyPresets = new HashSet<ParticlePresetDefinition>();

        /// <summary>Time.unscaledTime deadline for the next flush; below zero means nothing scheduled.</summary>
        private float _presetFlushDueAt = -1f;

        /// <summary>Test seam — EditMode tests swap this via reflection to observe/short-circuit disk writes.</summary>
        private Func<ParticlePresetDefinition, bool> _presetWriter = WritePresetAssetToDisk;

        /// <summary>The "edits last this session only" status is shown once per session, not on every flush.</summary>
        private bool _warnedPresetSaveUnsupported;

        /// <summary>
        /// Marks a preset edited and (re)schedules the debounced flush. Every editing path
        /// that touches a <see cref="ParticlePresetDefinition"/> — Properties form, Table
        /// cells, Loops toggle — must call this instead of setting
        /// <see cref="UnityEditor.EditorUtility"/> dirty directly, so the edit is guaranteed
        /// to eventually reach disk.
        /// </summary>
        private void MarkParticlePresetDirty(ParticlePresetDefinition def)
        {
            if (def == null) return;
            _dirtyPresets.Add(def);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(def);
#endif
            _presetFlushDueAt = Time.unscaledTime + PRESET_AUTOSAVE_DEBOUNCE_SECONDS;
        }

        /// <summary>Called from <c>Update()</c> — including while the editor is closed, so a debounce started right before F1 still lands.</summary>
        private void TickPresetAutosave()
        {
            if (_presetFlushDueAt < 0f || Time.unscaledTime < _presetFlushDueAt) return;
            FlushDirtyPresets("autosave");
        }

        /// <summary>
        /// Writes every dirty preset to its .asset via <see cref="_presetWriter"/>.
        /// Presets the writer fails to write (not in Play Mode, not an on-disk asset, editor
        /// build) are kept in the dirty set so the next flush retries — nothing is silently
        /// dropped.
        /// </summary>
        private int FlushDirtyPresets(string reason)
        {
            _presetFlushDueAt = -1f;
            if (_dirtyPresets.Count == 0) return 0;

            var snapshot = new List<ParticlePresetDefinition>(_dirtyPresets);
            var writtenIds = new List<string>();
            int count = 0;

            foreach (var def in snapshot)
            {
                if (def == null)
                {
                    _dirtyPresets.Remove(def);
                    continue;
                }
                if (_presetWriter(def))
                {
                    _dirtyPresets.Remove(def);
                    writtenIds.Add(def.id ?? "?");
                    count++;
                }
                // else: leave it in the set, retry on the next flush.
            }

            if (count > 0)
            {
                string ids = string.Join(", ", writtenIds);
                SetStatus($"Saved {count} preset(s) to .asset ({reason}): {ids}");
                Debug.Log($"[ParticlesEditor] Saved {count} preset asset(s) ({reason}): {ids}");
            }

            if (_dirtyPresets.Count > 0 && count == 0 && !_warnedPresetSaveUnsupported)
            {
                _warnedPresetSaveUnsupported = true;
                SetStatus("Preset edits cannot reach disk outside the Unity Editor — they last this session only.");
            }

            return count;
        }

        /// <summary>
        /// Writes a single preset asset to disk. Returns false (never throws) when the write
        /// cannot happen yet, so <see cref="FlushDirtyPresets"/> can retry later.
        /// </summary>
        private static bool WritePresetAssetToDisk(ParticlePresetDefinition def)
        {
#if UNITY_EDITOR
            // EditMode-test safety, same rule as the JSON stores: a fixture that builds this
            // editor must never reach a real .asset on disk.
            if (!Application.isPlaying) return false;
            // CreateInstance probes (tests, previews) have no file behind them.
            if (!UnityEditor.AssetDatabase.Contains(def)) return false;
            UnityEditor.AssetDatabase.SaveAssetIfDirty(def);
            return true;
#else
            return false;
#endif
        }
    }
}
