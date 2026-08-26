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
        private void DoSave()
        {
            if (WorldLightLoader.Instance == null)
            {
                Toast("WorldLightLoader missing — cannot save.");
                return;
            }
            int written = WorldLightLoader.Instance.SaveAll();
            if (written == WorldLightLoader.SaveAborted)
            {
                // The guard refused. Say so loudly rather than reporting a success the user would
                // trust: this is the one place where believing a false "saved" costs the file.
                Toast("Save ABORTED — the world holds far fewer lights than the file. See console.");
                return;
            }
            Toast($"Saved {written} light instance(s) to light_instances.json.");
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
            _selectedLight = null;
            _hoveredLight  = null;
            RebuildInstancesList();
            return hadHistory;
        }
    }
}
