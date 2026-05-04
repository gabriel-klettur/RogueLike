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
            Toast($"Saved {written} light instance(s) to light_instances.json.");
        }

        private void DoUndo()
        {
            if (!_undo.CanUndo) { Toast("Nothing to undo."); return; }
            string label = _undo.PeekUndoLabel();
            _undo.Undo();
            RebuildInstancesList();
            Toast($"Undo: {label}");
        }

        private void DoRedo()
        {
            if (!_undo.CanRedo) { Toast("Nothing to redo."); return; }
            string label = _undo.PeekRedoLabel();
            _undo.Redo();
            RebuildInstancesList();
            Toast($"Redo: {label}");
        }
    }
}
