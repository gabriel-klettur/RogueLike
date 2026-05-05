using System;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TimeWeather
{
    /// <summary>
    /// Cycle panel: phase shortcut buttons that jump the cycle's
    /// <see cref="DayNightCycle.TimeNormalized"/> to a canonical hour and
    /// re-enable lighting. The OFF row toggles
    /// <see cref="DayNightCycle.LightingEnabled"/> off so the world reads
    /// at native colours (no day/night tint).
    /// </summary>
    public partial class TimeWeatherEditor
    {
        // Time-of-day each shortcut jumps to. Index lines up with
        // TimeWeatherEditorUIBuilder.CYCLE_ROWS.
        private static readonly float[] CYCLE_NORMALIZED_TIMES = new[]
        {
            5.5f / 24f,    // Amanecer  — 05:30
            9f   / 24f,    // Mañana    — 09:00
            12f  / 24f,    // Mediodía  — 12:00
            18.5f/ 24f,    // Atardecer — 18:30
            0f   / 24f,    // Medianoche — 00:00
        };

        private int _activeCycleIdx = -1;

        // Build the per-row click callbacks once; stored by the UIBuilder so
        // each row's button targets its captured index.
        private Action[] BuildCycleRowCallbacks()
        {
            var arr = new Action[CYCLE_NORMALIZED_TIMES.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                int captured = i;
                arr[i] = () => OnCycleRowClicked(captured);
            }
            return arr;
        }

        private void OnCycleRowClicked(int idx)
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            // Picking a phase shortcut implicitly re-enables lighting + resumes
            // the cycle so the player sees the tint they just asked for.
            cycle.LightingEnabled = true;
            cycle.Paused          = false;
            cycle.SetTimeNormalized(CYCLE_NORMALIZED_TIMES[idx]);
            _activeCycleIdx = idx;
            ApplyCycleHighlight();
            SetStatus($"Current time: {TimeWeatherEditorUIBuilder.CYCLE_ROWS[idx].Label}.");
        }

        private void OnCycleOffClicked()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            cycle.LightingEnabled = false;
            cycle.Paused          = true;
            _activeCycleIdx = -1;
            ApplyCycleHighlight();
            SetStatus("No filter — cycle paused.");
        }

        // Tracks the live cycle (supports edits coming from the Lighting Editor
        // scrubber, savegame load, etc.) and keeps the row highlight in sync.
        private void SyncCycleHighlightFromLive()
        {
            if (_ui.CycleRowBgs == null) return;
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;

            int nearest = cycle.LightingEnabled
                ? NearestCycleIdx(cycle.TimeNormalized)
                : -1;
            if (nearest != _activeCycleIdx)
            {
                _activeCycleIdx = nearest;
                ApplyCycleHighlight();
            }
        }

        private int NearestCycleIdx(float t)
        {
            int   best = 0;
            float bestD = float.PositiveInfinity;
            for (int i = 0; i < CYCLE_NORMALIZED_TIMES.Length; i++)
            {
                // Wrap-aware distance on the unit circle so a shortcut at
                // 23:30 wins when the clock reads 00:05 etc.
                float d = Mathf.Abs(CYCLE_NORMALIZED_TIMES[i] - t);
                if (d > 0.5f) d = 1f - d;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private void ApplyCycleHighlight()
        {
            for (int i = 0; i < _ui.CycleRowBgs.Length; i++)
                TimeWeatherEditorUIBuilder.ApplyPhaseRowStyle(
                    _ui.CycleRowBgs[i], _ui.CycleRowLabels[i], i == _activeCycleIdx);

            bool noneActive = _activeCycleIdx < 0;
            TimeWeatherEditorUIBuilder.ApplyOffRowStyle(_ui.CycleOffImg, _ui.CycleOffTmp, noneActive);
        }
    }
}
