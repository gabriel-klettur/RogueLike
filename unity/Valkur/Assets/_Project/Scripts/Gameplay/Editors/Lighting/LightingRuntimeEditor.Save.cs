using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Lighting Editor — Save / Undo / Redo. Persistence flushes the live light
    /// list to <c>StreamingAssets/Lights/light_instances.json</c> through
    /// <see cref="WorldLightLoader.SaveAll"/>.
    /// </summary>
    public partial class LightingRuntimeEditor
    {
        /// <summary>
        /// How many lights this session has deleted on purpose and not undone.
        ///
        /// <para>It exists to keep the save guard honest once deleting is easy. The guard refuses
        /// to write far fewer lights than the file holds, because that is what a half-loaded world
        /// looks like — and it is also exactly what deleting six of ten lights looks like. Without
        /// a way to tell the two apart, the Delete key would produce edits the file could never be
        /// given, which is data loss wearing an error message. The editor is the only thing that
        /// knows the difference, so it states it rather than leaving the loader to infer it.</para>
        ///
        /// <para>Maintained by the delete command itself — the removal, its redo and its undo all
        /// move it — so it tracks the WORLD rather than the stack, and never counts a removal that
        /// did not happen. Reset when the session ends, when the world is rebuilt underneath the
        /// history, and after a successful write: once the file agrees with the world there is no
        /// longer a drop to account for, and a stale allowance carried into a later half-loaded
        /// save is precisely the hole the guard exists to close.</para>
        /// </summary>
        private int _authoredRemovals;

        // ── Autosave ─────────────────────────────────────────────────────────
        //
        // The Lighting editor's ONLY save trigger was Ctrl+S. Every sibling that authors world
        // content — Buildings, Particles, Spawners, Entities — writes on its own, and the
        // Spawner editor's own note records what the gap costs: "its single save trigger was
        // the toolbar button, so a session of placing spawners was lost on restart unless the
        // user happened to click it. That read as broken persistence when the persistence
        // itself was fine." Lights had exactly that, and the evidence is on disk — Entities,
        // FSM, Items and Buildings all carry .bak sidecars from real writes and
        // light_instances.json carries none, so no save had ever landed.
        //
        // Debounced rather than per-edit because a drag calls MoveLight every frame; writing
        // the whole file sixty times a second during a drag is the other way to lose data.

        private const float AUTOSAVE_DEBOUNCE_SECONDS = 0.75f;

        private bool  _autosavePending;
        private float _autosaveDueAt;

        /// <summary>
        /// Records that the lights changed. Every mutation funnels through here rather than
        /// calling save directly, so a new edit path cannot forget to persist — which is
        /// precisely how this editor ended up with no automatic save at all.
        /// </summary>
        private void MarkLightsDirty()
        {
            _autosavePending = true;
            _autosaveDueAt   = Time.unscaledTime + AUTOSAVE_DEBOUNCE_SECONDS;
        }

        /// <summary>Writes once the debounce has elapsed. Called every active frame.</summary>
        private void TickAutosave()
        {
            if (!_autosavePending) return;
            if (Time.unscaledTime < _autosaveDueAt) return;
            DoSave(auto: true);
        }

        /// <summary>Writes immediately if anything is pending — on close, and on quit.</summary>
        private void FlushAutosave()
        {
            if (_autosavePending) DoSave(auto: true);
        }

        private void OnApplicationQuit()
        {
            if (_active) FlushAutosave();
        }

        private void DoSave(bool auto = false)
        {
            _autosavePending = false;

            if (WorldLightLoader.Instance == null)
            {
                Toast("WorldLightLoader missing — cannot save.");
                return;
            }

            // Inside an interior the base world is legitimately torn down, so the scene holds no
            // authored lights while the file holds ten. An autosave there would try to write the
            // emptiness; the count guard refuses it, but refusing by NAME is the right answer and
            // is what Buildings, Particles and Entities already do. Stating the fact beats
            // inferring it from a count.
            if (WorldTransitionService.RefuseWorldContentWrite("lights"))
            {
                if (!auto) Toast("Not saved — base-world lights are suspended (you are in an interior).");
                return;
            }

            int written = WorldLightLoader.Instance.SaveAll(_authoredRemovals);
            if (written == WorldLightLoader.SaveAborted)
            {
                // The guard refused. Say so loudly rather than reporting a success the user would
                // trust: this is the one place where believing a false "saved" costs the file.
                // Loud on an AUTOSAVE too — a silent refusal here is the exact failure this whole
                // change exists to end.
                Toast("Save ABORTED — the world holds far fewer lights than the file, and the drop " +
                      "is more than this session deleted. See console.");
                return;
            }
            _authoredRemovals = 0;
            Toast(auto
                ? $"Autosaved {written} light instance(s)."
                : $"Saved {written} light instance(s) to light_instances.json.");
        }

        private void DoUndo()
        {
            if (DiscardHistoryIfWorldChanged()) { Toast("History cleared — the world was reloaded."); return; }
            if (!_undo.CanUndo) { Toast("Nothing to undo."); return; }
            string label = _undo.PeekUndoLabel();
            _undo.Undo();
            RebuildInstancesList();
            Toast($"Undo: {label}");
        }

        private void DoRedo()
        {
            if (DiscardHistoryIfWorldChanged()) { Toast("History cleared — the world was reloaded."); return; }
            if (!_undo.CanRedo) { Toast("Nothing to redo."); return; }
            string label = _undo.PeekRedoLabel();
            _undo.Redo();
            RebuildInstancesList();
            Toast($"Redo: {label}");
        }

        /// <summary>
        /// Throw the history away if the world underneath it has been rebuilt, and report whether
        /// it did.
        ///
        /// Every command in the stack names its light by id. Ids are unique within one loaded
        /// world and are re-minted by the next one, so replaying a command across a map-slot
        /// switch or a <c>reloadworld</c> does not fail — it succeeds, on a different light. That
        /// is the worst available outcome, so the history is dropped rather than trusted.
        /// Call it before every undo and redo, not only on activation: the DevConsole can reload
        /// the world without the editor ever closing.
        /// </summary>
        private bool DiscardHistoryIfWorldChanged()
        {
            var loader = WorldLightLoader.Instance;
            if (loader == null) return false;

            // An UNSEEDED history is not a stale one. Adopt the current generation on first sight
            // rather than reading the sentinel as "the world changed" — otherwise the very first
            // undo of a session throws away the edit it was asked to reverse, which is precisely
            // the failure this guard exists to prevent.
            if (_undoWorldGeneration < 0) { _undoWorldGeneration = loader.WorldGeneration; return false; }
            if (loader.WorldGeneration == _undoWorldGeneration) return false;

            _undoWorldGeneration = loader.WorldGeneration;
            bool hadHistory = _undo.CanUndo || _undo.CanRedo;
            _undo.Clear();
            // The count described lights in the world that was just thrown away. Carrying it into
            // the new one would excuse a drop nobody authored there.
            _authoredRemovals = 0;
            _selectedLight = null;
            _hoveredLight  = null;
            RebuildInstancesList();
            return hadHistory;
        }
    }
}
