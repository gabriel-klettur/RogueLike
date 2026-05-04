using UnityEngine;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Lighting Editor — day/night cycle controls. Always reads the live
    /// <see cref="DayNightCycle"/> singleton (no duplicate state) and writes back
    /// through the public API the cycle exposes (<see cref="DayNightCycle.SetTimeNormalized"/>,
    /// <see cref="DayNightCycle.RealSecondsPerDay"/>, etc.).
    ///
    /// The Sync method is called every frame to keep the sliders in step with
    /// the cycle even when it is running freely (no user input). The
    /// <c>_suppressCycleEvents</c> flag prevents an infinite UI ↔ cycle loop
    /// when the slider OnChanged callback would write back the value we just read.
    /// </summary>
    public partial class LightingRuntimeEditor
    {
        // ── Frame sync (UI ← live cycle) ─────────────────────────────────────

        private void SyncCycleFromLive()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null || _ui.ClockText == null) return;

            int totalMin = cycle.MinuteOfDay;
            int h = totalMin / 60;
            int m = totalMin % 60;
            string phase = cycle.CurrentPhase.ToString();
            _ui.ClockText.text = $"{h:D2}:{m:D2} — {phase}";

            if (_ui.PhaseHintText != null)
            {
                _ui.PhaseHintText.text = cycle.CurrentPhase switch
                {
                    DayNightCycle.DayPhase.Dawn          => "Civil dawn — cool, pre-sunrise hue",
                    DayNightCycle.DayPhase.GoldenMorning => "Golden Hour — warm honey morning light",
                    DayNightCycle.DayPhase.Day           => "Bright daylight — point lights may be off",
                    DayNightCycle.DayPhase.GoldenEvening => "Golden Hour — warm copper evening light",
                    DayNightCycle.DayPhase.Dusk          => "Civil dusk — point lights coming on",
                    DayNightCycle.DayPhase.BlueHour      => "Blue Hour — deep cool indigo",
                    DayNightCycle.DayPhase.Night         => "Night — point lights at full effect",
                    _                                     => string.Empty,
                };
            }

            _suppressCycleEvents = true;
            try
            {
                if (_ui.TimeScrubSlider     != null) _ui.TimeScrubSlider.value         = cycle.TimeNormalized;
                if (_ui.DayLengthSlider     != null) _ui.DayLengthSlider.value         = cycle.RealSecondsPerDay;
                if (_ui.MinIntensitySlider  != null) _ui.MinIntensitySlider.value      = cycle.MinIntensity;
                if (_ui.LightsWindowStartSlider != null) _ui.LightsWindowStartSlider.value = cycle.LightsDisableStartNormalized;
                if (_ui.LightsWindowEndSlider   != null) _ui.LightsWindowEndSlider.value   = cycle.LightsDisableEndNormalized;
            }
            finally { _suppressCycleEvents = false; }

            if (_ui.DayLengthTmp     != null) _ui.DayLengthTmp.text     = $"{cycle.RealSecondsPerDay:0}s";
            if (_ui.MinIntensityTmp  != null) _ui.MinIntensityTmp.text  = $"{cycle.MinIntensity:0.00}";

            if (_ui.LightsWindowToggleImg != null)
                LightingEditorUIBuilder.ApplyToggleBtnStyle(_ui.LightsWindowToggleImg, cycle.LightsDisableWindowEnabled);

            if (_ui.PauseBtnImg != null)
            {
                bool paused = cycle.Paused;
                _ui.PauseBtnImg.color = paused
                    ? LightingEditorUIBuilder.CYCLE_PAUSED
                    : EditorUIHelpers.BTN_NORMAL;
                if (_ui.PauseBtnTmp != null)
                    _ui.PauseBtnTmp.text = paused ? "RESUME CYCLE" : "PAUSE CYCLE";
            }

            if (_ui.LightsWindowRangeTmp != null)
            {
                _ui.LightsWindowRangeTmp.text =
                    $"{NormalizedToHHMM(cycle.LightsDisableStartNormalized)} → {NormalizedToHHMM(cycle.LightsDisableEndNormalized)}";
            }
        }

        // ── Slider callbacks (UI → cycle) ────────────────────────────────────

        private void OnScrubTime(float t)
        {
            if (_suppressCycleEvents || DayNightCycle.Instance == null) return;
            DayNightCycle.Instance.SetTimeNormalized(t);
        }

        private void OnDayLengthChanged(float seconds)
        {
            if (_suppressCycleEvents || DayNightCycle.Instance == null) return;
            DayNightCycle.Instance.RealSecondsPerDay = seconds;
        }

        private void OnMinIntensityChanged(float v)
        {
            if (_suppressCycleEvents || DayNightCycle.Instance == null) return;
            DayNightCycle.Instance.MinIntensity = v;
        }

        private void OnLightsWindowStart(float v)
        {
            if (_suppressCycleEvents || DayNightCycle.Instance == null) return;
            DayNightCycle.Instance.LightsDisableStartNormalized = v;
        }

        private void OnLightsWindowEnd(float v)
        {
            if (_suppressCycleEvents || DayNightCycle.Instance == null) return;
            DayNightCycle.Instance.LightsDisableEndNormalized = v;
        }

        private void ToggleLightsWindow()
        {
            var c = DayNightCycle.Instance;
            if (c == null) return;
            c.LightsDisableWindowEnabled = !c.LightsDisableWindowEnabled;
            SetStatus($"Lights-disable window: {(c.LightsDisableWindowEnabled ? "ON" : "OFF")}.");
        }

        private void ToggleCyclePaused()
        {
            var c = DayNightCycle.Instance;
            if (c == null) return;
            c.Paused = !c.Paused;
            SetStatus(c.Paused ? "Cycle paused." : "Cycle running.");
        }

        private void JumpToTime(float normalized)
        {
            var c = DayNightCycle.Instance;
            if (c == null) return;
            c.SetTimeNormalized(normalized);
            SetStatus($"Time set to {NormalizedToHHMM(normalized)}.");
        }

        // ── Global toggles (Modes panel) ─────────────────────────────────────

        private void ToggleAmbient()
        {
            _ambientEnabled = !_ambientEnabled;
            // We do not have a direct "ambient on/off" gate on Light2D — emulate
            // by saving and restoring the live intensity. When OFF the Global
            // Light goes black so the scene reads as fully unlit (helps debug
            // point-light contribution).
            var cycle = DayNightCycle.Instance;
            if (cycle == null) { SetStatus("DayNightCycle missing — cannot toggle ambient."); return; }
            if (_ambientEnabled)
            {
                cycle.MinIntensity = _cachedDayLightIntensity > 0f ? _cachedDayLightIntensity : 0.20f;
            }
            else
            {
                _cachedDayLightIntensity = cycle.MinIntensity;
                cycle.MinIntensity = 0f;
            }
            LightingEditorUIBuilder.ApplyToggleBtnStyle(_ui.AmbientToggleImg, _ambientEnabled);
            SetStatus($"Ambient (Global Light): {(_ambientEnabled ? "ON" : "OFF")}.");
        }

        private void TogglePointLights()
        {
            _pointLightsEnabled = !_pointLightsEnabled;
            if (WorldLightLoader.Instance != null)
                WorldLightLoader.Instance.PointLightsEnabled = _pointLightsEnabled;
            LightingEditorUIBuilder.ApplyToggleBtnStyle(_ui.PointLightsToggleImg, _pointLightsEnabled);
            SetStatus($"Point lights: {(_pointLightsEnabled ? "ON" : "OFF")}.");
        }

        private static string NormalizedToHHMM(float t)
        {
            int total = Mathf.FloorToInt(Mathf.Repeat(t, 1f) * DayNightCycle.MinutesPerDay);
            return $"{total / 60:D2}:{total % 60:D2}";
        }
    }
}
