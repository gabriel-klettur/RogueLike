using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Selection state for the Boss Editor: tracks which boss, phase, chart
    /// and cue are currently active. Provides <see cref="OpenWithBoss"/> so
    /// the Entities Editor (F5) can hand off directly to this editor.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        // ── Selection state ───────────────────────────────────────────────────

        private BossDefinition _selectedBoss;
        private int            _selectedPhaseIndex  = -1;
        private BossChart      _selectedChart;
        private int            _selectedCueIndex    = -1;

        // Cache of all BossDefinition assets (rebuilt on Activate + on demand).
        private BossDefinition[] _allBossDefs;

        // ── Boss cache ─────────────────────────────────────────────────────────

        private void RebuildBossCache()
        {
#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets(
                "t:BossDefinition",
                new[] { "Assets/_Project/Data" });
            var list = new List<BossDefinition>(guids.Length);
            foreach (var guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<BossDefinition>(path);
                if (def != null) list.Add(def);
            }
            _allBossDefs = list.ToArray();
#else
            _allBossDefs = System.Array.Empty<BossDefinition>();
#endif
        }

        // ── Public API (called by Entities Editor F5) ─────────────────────────

        /// <summary>
        /// Opens this editor with the given BossDefinition pre-selected.
        /// Activates the editor via GameEditorManager.OpenExclusive if it
        /// is not already active so the F5 hand-off is seamless.
        /// </summary>
        public void OpenWithBoss(BossDefinition boss)
        {
            if (boss == null) return;

            // Ensure the editor singleton is activated before we touch selection state.
            if (!_active)
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.OpenExclusive(this);
                else
                    Activate();
            }

            SelectBoss(boss);
        }

        // ── Selection helpers ─────────────────────────────────────────────────

        private void SelectBoss(BossDefinition def)
        {
            _selectedBoss        = def;
            _selectedPhaseIndex  = def != null && def.phases != null && def.phases.Length > 0 ? 0 : -1;
            _selectedChart       = null;
            _selectedCueIndex    = -1;
            RefreshBossList();
            RefreshPhasesPanel();
            RefreshCuesPanel();
            SetStatus(_selectedBoss != null ? $"Boss: {_selectedBoss.name}" : "No boss selected.");
            OnBossSelectionChangedPreview();
        }

        private void SelectPhase(int phaseIndex)
        {
            _selectedPhaseIndex = phaseIndex;
            _selectedChart      = null;
            _selectedCueIndex   = -1;
            RefreshPhasesPanel();
            RefreshCuesPanel();
        }

        private void SelectChart(BossChart chart)
        {
            _selectedChart    = chart;
            _selectedCueIndex = -1;
            RefreshCuesPanel();
            SetStatus(_selectedChart != null
                ? $"Chart: {_selectedChart.name} ({_selectedChart.musicTrackId})"
                : "No chart selected.");
            ApplyTimelineChart();
            // Respawn sandbox when chart changes so BossConfigurator targets the new chart.
            OnBossSelectionChangedPreview();
        }

        private void SelectCue(int cueIndex)
        {
            _selectedCueIndex = cueIndex;
        }

        // ── Phase convenience ─────────────────────────────────────────────────

        private BossDefinition.Phase GetSelectedPhase()
        {
            if (_selectedBoss == null) return null;
            if (_selectedPhaseIndex < 0 || _selectedPhaseIndex >= _selectedBoss.phases.Length) return null;
            return _selectedBoss.phases[_selectedPhaseIndex];
        }
    }
}
