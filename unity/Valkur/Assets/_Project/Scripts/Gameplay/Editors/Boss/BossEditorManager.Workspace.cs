using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>Boss Editor — what it remembers between sessions.</summary>
    public partial class BossEditorManager : IProvidesWorkspaceState
    {
        private const string WS_BOSS  = "selectedBoss";
        private const string WS_PHASE = "selectedPhase";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            ws.SetString(WS_BOSS, _selectedBoss != null ? _selectedBoss.name : string.Empty);
            ws.SetInt(WS_PHASE, _selectedPhaseIndex);

            // _selectedChart / _selectedCueIndex are NOT captured. A cue index is a position
            // in a timeline the author is actively editing, so it means something different
            // the moment a cue is inserted or removed — the same reason no editor here
            // stores a list index as an id.
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            string bossName = ws.GetString(WS_BOSS, null);
            if (string.IsNullOrEmpty(bossName) || _allBossDefs == null) return;

            for (int i = 0; i < _allBossDefs.Length; i++)
            {
                if (_allBossDefs[i] == null || _allBossDefs[i].name != bossName) continue;

                SelectBoss(_allBossDefs[i]);

                // SelectBoss resets the phase to 0, so the stored phase is applied after it
                // and only when it still exists — a boss that lost a phase between sessions
                // keeps the first one rather than pointing the panel past the end.
                int phase = ws.GetInt(WS_PHASE, -1);
                var phases = _allBossDefs[i].phases;
                if (phase >= 0 && phases != null && phase < phases.Length) SelectPhase(phase);
                return;
            }

            // The definition was renamed or removed. Nothing selected, and said where the
            // author is looking rather than in the console.
            SetStatus("El jefe seleccionado antes ya no existe.");
        }
    }
}
