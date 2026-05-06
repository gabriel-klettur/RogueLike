using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Cue authoring actions for the Boss Editor.
    /// Every mutation goes through <see cref="UndoStack"/> so it can be undone.
    /// Persistence (<see cref="BossEditorManager.Persistence"/>) is called
    /// after each undoable edit.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        // ── Phase operations ───────────────────────────────────────────────────

        private void AddPhase()
        {
            if (_selectedBoss == null) { SetStatus("Select a boss first."); return; }

            var newPhase = new BossDefinition.Phase
            {
                label        = $"Phase {_selectedBoss.phases.Length + 1}",
                hpThreshold  = 0.5f,
                autoCastList = System.Array.Empty<string>(),
                charts       = System.Array.Empty<BossChart>(),
            };

            var oldPhases = _selectedBoss.phases;
            var newPhases = new BossDefinition.Phase[oldPhases.Length + 1];
            System.Array.Copy(oldPhases, newPhases, oldPhases.Length);
            newPhases[oldPhases.Length] = newPhase;

            int newIdx = newPhases.Length - 1;
            _undo.Do($"Add Phase {newIdx}",
                () =>
                {
                    _selectedBoss.phases = newPhases;
                    MarkDirty(_selectedBoss);
                    _selectedPhaseIndex = newIdx;
                    RefreshPhasesPanel();
                    RefreshCuesPanel();
                },
                () =>
                {
                    _selectedBoss.phases = oldPhases;
                    MarkDirty(_selectedBoss);
                    _selectedPhaseIndex = Mathf.Clamp(_selectedPhaseIndex, -1, oldPhases.Length - 1);
                    RefreshPhasesPanel();
                    RefreshCuesPanel();
                });
            RefreshUndoRedoButtons();
        }

        private void RequestDeletePhase(int phaseIndex)
        {
            if (_selectedBoss == null) return;
            if (phaseIndex < 0 || phaseIndex >= _selectedBoss.phases.Length) return;
            string label = _selectedBoss.phases[phaseIndex].label;
            ShowConfirm(
                $"Delete phase <b>{label}</b>? This also removes all its charts.",
                () => DeletePhaseConfirmed(phaseIndex));
        }

        private void DeletePhaseConfirmed(int phaseIndex)
        {
            if (_selectedBoss == null) return;
            var oldPhases = _selectedBoss.phases;
            if (phaseIndex < 0 || phaseIndex >= oldPhases.Length) return;

            var newPhases = new List<BossDefinition.Phase>(oldPhases);
            var removed   = newPhases[phaseIndex];
            newPhases.RemoveAt(phaseIndex);
            var arr = newPhases.ToArray();

            _undo.Do($"Delete Phase {phaseIndex}",
                () =>
                {
                    _selectedBoss.phases = arr;
                    MarkDirty(_selectedBoss);
                    _selectedPhaseIndex  = Mathf.Clamp(phaseIndex - 1, 0, Mathf.Max(0, arr.Length - 1));
                    _selectedChart       = null;
                    _selectedCueIndex    = -1;
                    RefreshPhasesPanel();
                    RefreshCuesPanel();
                },
                () =>
                {
                    _selectedBoss.phases = oldPhases;
                    MarkDirty(_selectedBoss);
                    _selectedPhaseIndex  = phaseIndex;
                    RefreshPhasesPanel();
                    RefreshCuesPanel();
                });
            RefreshUndoRedoButtons();
        }

        // ── Chart operations ───────────────────────────────────────────────────

        private void AddChart()
        {
            var phase = GetSelectedPhase();
            if (phase == null) { SetStatus("Select a phase first."); return; }

#if UNITY_EDITOR
            string dir  = "Assets/_Project/Data/Bosses/Charts";
            if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/Data/Bosses", "Charts");

            var chart = ScriptableObject.CreateInstance<BossChart>();
            chart.name          = $"{_selectedBoss.name}_Phase{_selectedPhaseIndex}_Chart";
            chart.musicTrackId  = "default";
            chart.barsPerLoop   = 4;
            chart.cues          = new System.Collections.Generic.List<BossCue>();

            string assetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(
                $"{dir}/{chart.name}.asset");
            UnityEditor.AssetDatabase.CreateAsset(chart, assetPath);
            UnityEditor.AssetDatabase.SaveAssets();

            var oldCharts = phase.charts ?? System.Array.Empty<BossChart>();
            var newCharts = new BossChart[oldCharts.Length + 1];
            System.Array.Copy(oldCharts, newCharts, oldCharts.Length);
            newCharts[oldCharts.Length] = chart;

            _undo.Do("Add Chart",
                () =>
                {
                    phase.charts = newCharts;
                    MarkDirty(_selectedBoss);
                    SelectChart(chart);
                    RefreshPhasesPanel();
                },
                () =>
                {
                    phase.charts = oldCharts;
                    MarkDirty(_selectedBoss);
                    SelectChart(null);
                    RefreshPhasesPanel();
                });
            RefreshUndoRedoButtons();
            SetStatus($"Chart created: {assetPath}");
#else
            SetStatus("Chart creation requires Unity Editor.");
#endif
        }

        private void RequestDeleteChart(BossChart chart)
        {
            if (chart == null) return;
            ShowConfirm(
                $"Delete chart <b>{chart.name}</b>? The .asset file will be removed from disk.",
                () => DeleteChartConfirmed(chart));
        }

        private void DeleteChartConfirmed(BossChart chart)
        {
            var phase = GetSelectedPhase();
            if (phase == null || chart == null) return;

            var oldCharts = phase.charts ?? System.Array.Empty<BossChart>();
            var newList   = new System.Collections.Generic.List<BossChart>(oldCharts);
            if (!newList.Remove(chart)) return;
            var newCharts = newList.ToArray();

            _undo.Do("Delete Chart",
                () =>
                {
                    phase.charts = newCharts;
                    MarkDirty(_selectedBoss);
                    if (_selectedChart == chart) SelectChart(null);
                    RefreshPhasesPanel();
#if UNITY_EDITOR
                    string path = UnityEditor.AssetDatabase.GetAssetPath(chart);
                    if (!string.IsNullOrEmpty(path)) UnityEditor.AssetDatabase.DeleteAsset(path);
#endif
                },
                () =>
                {
                    phase.charts = oldCharts;
                    MarkDirty(_selectedBoss);
                    RefreshPhasesPanel();
                    SetStatus("Chart deletion undone — .asset was already deleted from disk.");
                });
            RefreshUndoRedoButtons();
        }

        // ── Cue operations ─────────────────────────────────────────────────────

        private void AddCue()
        {
            if (_selectedChart == null) { SetStatus("Select a chart first."); return; }

            var newCue = new BossCue
            {
                bar          = 0,
                beat         = 0,
                beatFraction = 0f,
                type         = BossCueType.CastSpell,
                targeting    = BossCueTargeting.ToPlayer,
                targetKey    = "",
                payload      = 0f,
                note         = "",
            };

            var chart    = _selectedChart;
            int insertAt = chart.cues.Count;

            _undo.Do("Add Cue",
                () =>
                {
                    chart.cues.Add(newCue);
                    MarkDirty(chart);
                    _selectedCueIndex = chart.cues.Count - 1;
                    RefreshCuesPanel();
                },
                () =>
                {
                    if (chart.cues.Count > insertAt) chart.cues.RemoveAt(insertAt);
                    MarkDirty(chart);
                    _selectedCueIndex = Mathf.Clamp(_selectedCueIndex, -1, chart.cues.Count - 1);
                    RefreshCuesPanel();
                });
            RefreshUndoRedoButtons();
        }

        private void DuplicateCue(int cueIndex)
        {
            if (_selectedChart == null) return;
            if (cueIndex < 0 || cueIndex >= _selectedChart.cues.Count) return;

            var chart    = _selectedChart;
            var src      = chart.cues[cueIndex];
            var dup      = src; // BossCue is a struct — copy by value
            int insertAt = cueIndex + 1;

            _undo.Do("Duplicate Cue",
                () =>
                {
                    chart.cues.Insert(insertAt, dup);
                    MarkDirty(chart);
                    _selectedCueIndex = insertAt;
                    RefreshCuesPanel();
                },
                () =>
                {
                    if (insertAt < chart.cues.Count) chart.cues.RemoveAt(insertAt);
                    MarkDirty(chart);
                    _selectedCueIndex = cueIndex;
                    RefreshCuesPanel();
                });
            RefreshUndoRedoButtons();
        }

        private void RequestDeleteCue(int cueIndex)
        {
            if (_selectedChart == null) return;
            if (cueIndex < 0 || cueIndex >= _selectedChart.cues.Count) return;
            ShowConfirm(
                $"Delete cue #{cueIndex + 1}?",
                () => DeleteCueConfirmed(cueIndex));
        }

        private void DeleteCueConfirmed(int cueIndex)
        {
            if (_selectedChart == null) return;
            if (cueIndex < 0 || cueIndex >= _selectedChart.cues.Count) return;

            var chart   = _selectedChart;
            var removed = chart.cues[cueIndex];

            _undo.Do("Delete Cue",
                () =>
                {
                    chart.cues.RemoveAt(cueIndex);
                    MarkDirty(chart);
                    _selectedCueIndex = Mathf.Clamp(cueIndex - 1, -1, chart.cues.Count - 1);
                    RefreshCuesPanel();
                },
                () =>
                {
                    chart.cues.Insert(cueIndex, removed);
                    MarkDirty(chart);
                    _selectedCueIndex = cueIndex;
                    RefreshCuesPanel();
                });
            RefreshUndoRedoButtons();
        }

        /// <summary>
        /// Called by the cue-row widgets when any field value changes.
        /// Commits the edited cue back into the list (struct semantics require
        /// explicit write-back) and marks the chart dirty.
        /// </summary>
        private void ApplyCueEdit(int cueIndex, BossCue edited)
        {
            if (_selectedChart == null) return;
            if (cueIndex < 0 || cueIndex >= _selectedChart.cues.Count) return;

            var chart = _selectedChart;
            var old   = chart.cues[cueIndex];

            _undo.Do("Edit Cue",
                () =>
                {
                    chart.cues[cueIndex] = edited;
                    MarkDirty(chart);
                },
                () =>
                {
                    chart.cues[cueIndex] = old;
                    MarkDirty(chart);
                });
            RefreshUndoRedoButtons();
        }

        // ── Undo/redo button refresh ───────────────────────────────────────────

        private void RefreshUndoRedoButtons()
        {
            if (_ui.UndoBtnLabel != null)
            {
                string ul = _undo.PeekUndoLabel();
                _ui.UndoBtnLabel.text = ul != null ? $"Undo: {ul}" : "Undo";
            }
            if (_ui.RedoBtnLabel != null)
            {
                string rl = _undo.PeekRedoLabel();
                _ui.RedoBtnLabel.text = rl != null ? $"Redo: {rl}" : "Redo";
            }
        }
    }
}
