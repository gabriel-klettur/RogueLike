using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TimeWeather
{
    /// <summary>
    /// Settings panel: per-phase tuning of the cinematic look. Two tabs
    /// (Día / Noche) — only the two real keyframes are stored on
    /// <see cref="DayNightCycle"/>; transitions (Dawn / Dusk) interpolate
    /// between them. Each tab exposes 5 sliders driving Tono / Saturación /
    /// Brillo / Calidez / Viñeta plus DEFECTO / NEUTRO presets.
    ///
    /// Clicking a tab also jumps the live cycle to that phase's central
    /// moment (12:00 for Día, 22:05 for Noche) and pauses, so the player
    /// sees their slider edits land on a frozen world.
    /// </summary>
    public partial class TimeWeatherEditor
    {
        // Index lines up with TimeWeatherEditorUIBuilder.SETTINGS_TABS.
        private static readonly DayNightCycle.DayPhase[] SETTINGS_PHASES = new[]
        {
            DayNightCycle.DayPhase.Day,
            DayNightCycle.DayPhase.Night,
        };

        // Time-of-day each tab snaps to. Day = noon; Night = deep into the
        // night band so the night colour fully dominates.
        private static readonly float[] SETTINGS_PHASE_TIMES = new[] { 0.500f, 0.920f };

        // 5 slider indices.
        private const int IDX_HUE     = 0;
        private const int IDX_SAT     = 1;
        private const int IDX_BRIGHT  = 2;
        private const int IDX_WARMTH  = 3;
        private const int IDX_VIGN    = 4;

        private int  _selectedSettingsIdx;
        private bool _suppressSettingsEvents;

        private void OnSettingsTabClicked(int idx)
        {
            _selectedSettingsIdx = Mathf.Clamp(idx, 0, SETTINGS_PHASES.Length - 1);
            ApplySettingsTabHighlight();

            // Jump the live cycle to the chosen phase's centre + pause so the
            // edits land on a stable world. Re-enables lighting in case the
            // OFF row was active.
            var cycle = DayNightCycle.Instance;
            if (cycle != null)
            {
                cycle.LightingEnabled = true;
                cycle.Paused          = true;
                cycle.SetTimeNormalized(SETTINGS_PHASE_TIMES[_selectedSettingsIdx]);
            }

            SyncSettingsSlidersFromCycle();
            SetStatus($"Editing: {TimeWeatherEditorUIBuilder.SETTINGS_TABS[_selectedSettingsIdx].Label}.");
        }

        private void OnSettingsSliderChanged(int sliderIdx, float v)
        {
            if (_suppressSettingsEvents) return;
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;

            var phase = SETTINGS_PHASES[_selectedSettingsIdx];
            var look  = cycle.GetPhaseLook(phase);

            // Recompose color from current slider state for any color-affecting
            // change (Hue / Sat). Other sliders write their field directly.
            switch (sliderIdx)
            {
                case IDX_HUE:
                {
                    Color.RGBToHSV(look.color, out _, out float s, out _);
                    look.color = Color.HSVToRGB(Mathf.Clamp01(v / 360f), s, 1f);
                    break;
                }
                case IDX_SAT:
                {
                    Color.RGBToHSV(look.color, out float h, out _, out _);
                    look.color = Color.HSVToRGB(h, Mathf.Clamp01(v), 1f);
                    break;
                }
                case IDX_BRIGHT: look.intensity     = Mathf.Clamp(v, 0f, 1.5f);  break;
                case IDX_WARMTH: look.warmth        = Mathf.Clamp(v, -1f, 1f);   break;
                case IDX_VIGN:   look.vignetteAlpha = Mathf.Clamp01(v);          break;
            }

            cycle.SetPhaseLook(phase, look);
            if (sliderIdx == IDX_HUE || sliderIdx == IDX_SAT)
                _ui.SettingsSwatchImg.color = new Color(look.color.r, look.color.g, look.color.b, 1f);
            RefreshSettingsValueLabels();
        }

        // Reset to the cinematic defaults baked into DayNightCycle (matches
        // the SerializeField initialisers so a designer who tweaks the asset
        // and clicks DEFECTO sees their *new* defaults, not stale literals).
        private void OnSettingsResetClicked()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            cycle.SetPhaseLook(SETTINGS_PHASES[_selectedSettingsIdx],
                DefaultLookFor(SETTINGS_PHASES[_selectedSettingsIdx]));
            SyncSettingsSlidersFromCycle();
            SetStatus("Reset to default values.");
        }

        // "No filter" preset: white tint at full intensity, no warmth, no
        // vignette. Different from "all sliders to 0" — that produces a dark
        // cool tint because Saturation=0+Hue=any=white but Warmth=-1=blue and
        // Brightness=0=clamped to MinIntensity.
        private void OnSettingsNeutroClicked()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            cycle.SetPhaseLook(SETTINGS_PHASES[_selectedSettingsIdx],
                new DayNightCycle.PhaseLook
                {
                    color         = Color.white,
                    intensity     = 1.00f,
                    warmth        = 0.00f,
                    vignetteAlpha = 0.00f,
                });
            SyncSettingsSlidersFromCycle();
            SetStatus("Phase neutralized — no filter applied.");
        }

        private void SyncSettingsFromLive()
        {
            // Cheap call — only updates label text + tab highlight if anything
            // changed. Settings sliders themselves are only synced explicitly
            // (on tab click / preset click) to avoid fighting user dragging.
            ApplySettingsTabHighlight();
        }

        private void SyncSettingsSlidersFromCycle()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null || _ui.SettingsSliders == null) return;

            var phase = SETTINGS_PHASES[_selectedSettingsIdx];
            var look  = cycle.GetPhaseLook(phase);

            if (_ui.SettingsNameTmp != null)
                _ui.SettingsNameTmp.text = TimeWeatherEditorUIBuilder.SETTINGS_TABS[_selectedSettingsIdx].Label;
            if (_ui.SettingsSwatchImg != null)
                _ui.SettingsSwatchImg.color = new Color(look.color.r, look.color.g, look.color.b, 1f);

            // Decompose color into HSV. Hue + Saturation are the two sliders
            // we expose; Value is folded into Brightness.
            Color.RGBToHSV(look.color, out float h, out float s, out _);

            _suppressSettingsEvents = true;
            try
            {
                _ui.SettingsSliders[IDX_HUE].value     = h * 360f;
                _ui.SettingsSliders[IDX_SAT].value     = s;
                _ui.SettingsSliders[IDX_BRIGHT].value  = look.intensity;
                _ui.SettingsSliders[IDX_WARMTH].value  = look.warmth;
                _ui.SettingsSliders[IDX_VIGN].value    = look.vignetteAlpha;
            }
            finally { _suppressSettingsEvents = false; }

            RefreshSettingsValueLabels();
        }

        private void ApplySettingsTabHighlight()
        {
            if (_ui.SettingsTabImgs == null) return;
            for (int i = 0; i < _ui.SettingsTabImgs.Length; i++)
                TimeWeatherEditorUIBuilder.ApplySettingsTabStyle(
                    _ui.SettingsTabImgs[i], i == _selectedSettingsIdx);
        }

        private void RefreshSettingsValueLabels()
        {
            if (_ui.SettingsValues == null) return;
            for (int i = 0; i < _ui.SettingsValues.Length; i++)
            {
                if (_ui.SettingsValues[i] == null || _ui.SettingsSliders[i] == null) continue;
                _ui.SettingsValues[i].text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    TimeWeatherEditorUIBuilder.SETTINGS_SLIDER_FORMAT[i],
                    _ui.SettingsSliders[i].value);
            }
        }

        // Defaults must stay in sync with DayNightCycle's SerializeField
        // initialisers. Day = white identity (no filter), Night = dark blue
        // so manually-placed point lights / torches matter.
        private static DayNightCycle.PhaseLook DefaultLookFor(DayNightCycle.DayPhase phase) => phase switch
        {
            DayNightCycle.DayPhase.Day   => new DayNightCycle.PhaseLook { color = new Color(1.00f, 1.00f, 1.00f), intensity = 1.00f, warmth =  0.00f, vignetteAlpha = 0.00f },
            DayNightCycle.DayPhase.Night => new DayNightCycle.PhaseLook { color = new Color(0.20f, 0.25f, 0.45f), intensity = 0.15f, warmth = -0.10f, vignetteAlpha = 0.30f },
            _                             => new DayNightCycle.PhaseLook { color = Color.white, intensity = 1f, warmth = 0f, vignetteAlpha = 0.00f },
        };
    }
}
