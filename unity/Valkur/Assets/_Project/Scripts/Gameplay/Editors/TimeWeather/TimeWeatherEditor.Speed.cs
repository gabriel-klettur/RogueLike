using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TimeWeather
{
    /// <summary>
    /// Speed panel: drives <see cref="DayNightCycle.RealSecondsPerDay"/> from
    /// 7 discrete preset multipliers (1× / 2× / 5× / 10× / 20× / 50× / 100×).
    /// Two-way sync — if the cycle's real-seconds-per-day is changed from
    /// elsewhere (Lighting Editor, savegame load) the slider snaps to the
    /// nearest preset.
    /// </summary>
    public partial class TimeWeatherEditor
    {
        // 1× keeps the canonical Python-parity 60 real-min day; 100× compresses
        // it to 36 real-sec — perfect for visual tuning of phase boundaries.
        private const float BASELINE_REAL_SECONDS_PER_DAY = 3600f;
        private static readonly int[] SPEED_MULTIPLIERS = { 1, 2, 5, 10, 20, 50, 100 };

        private bool _suppressSpeedEvents;
        private int  _activeSpeedIdx = -1;

        private void OnSpeedSliderChanged(float v)
        {
            if (_suppressSpeedEvents) return;
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            int idx  = Mathf.Clamp(Mathf.RoundToInt(v), 0, SPEED_MULTIPLIERS.Length - 1);
            int mult = SPEED_MULTIPLIERS[idx];
            cycle.RealSecondsPerDay = BASELINE_REAL_SECONDS_PER_DAY / mult;
            UpdateSpeedValueLabel(idx);
            _activeSpeedIdx = idx;
        }

        // Pick the preset whose ratio is closest in *log space* to the live
        // RealSecondsPerDay. Log space avoids the 1×↔100× spread biasing the
        // nearest match toward the slowest preset on small differences.
        private void SyncSpeedFromCycle()
        {
            if (_ui.SpeedSlider == null) return;
            var cycle = DayNightCycle.Instance;
            if (cycle == null || cycle.RealSecondsPerDay <= 0f) return;

            float liveMult = BASELINE_REAL_SECONDS_PER_DAY / cycle.RealSecondsPerDay;
            int   bestIdx  = 0;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < SPEED_MULTIPLIERS.Length; i++)
            {
                float d = Mathf.Abs(Mathf.Log(SPEED_MULTIPLIERS[i] / Mathf.Max(0.0001f, liveMult)));
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            if (bestIdx == _activeSpeedIdx) return;
            _activeSpeedIdx = bestIdx;
            _suppressSpeedEvents = true;
            try { _ui.SpeedSlider.value = bestIdx; }
            finally { _suppressSpeedEvents = false; }
            UpdateSpeedValueLabel(bestIdx);
        }

        private void UpdateSpeedValueLabel(int idx)
        {
            if (_ui.SpeedValueTmp == null) return;
            int mult = SPEED_MULTIPLIERS[Mathf.Clamp(idx, 0, SPEED_MULTIPLIERS.Length - 1)];
            _ui.SpeedValueTmp.text = $"{mult}x";
        }
    }
}
