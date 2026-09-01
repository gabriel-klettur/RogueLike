using System;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.TimeWeather
{
    /// <summary>
    /// Weather panel: authors the weather OF THE ZONE THE PLAYER IS STANDING IN, through
    /// <see cref="WeatherManager"/>. Wind / Rain / Snow stack freely within a zone (Wind +
    /// Rain reads as a wind-driven rainstorm, because the wind effect raises the field the
    /// rain slants with). The OFF row clears that zone.
    ///
    /// Zone-scoped rather than global because weather belongs to a place: "it snows in the
    /// forest" is a statement about the world, "it snows" is a statement about the session.
    /// The zone is the right unit for it — it is the world's named unit, and the music and
    /// ambience already resolve against it.
    ///
    /// Two consequences the panel has to make visible or it reads as broken:
    ///   • The zone being edited is written at the top. Without it, an author sets Rain HEAVY,
    ///     walks thirty units into the next zone, watches the rain fade out, and has no way to
    ///     tell that from a bug.
    ///   • Inside an interior the rows are inert and say so. <c>ZoneManager</c> suspends
    ///     detection there, so there is no zone to author — and no weather either, because you
    ///     are under a roof.
    ///
    /// A row click CYCLES the level — Off → Light → Medium → Heavy → Off — rather than
    /// toggling. The panel was a plain toggle for as long as the effects could only be
    /// authored at one density; now that a level is a real density multiplier, a toggle could
    /// only ever ask for one third of what the effects can do.
    ///
    /// Subscribes to <see cref="WeatherManager.OnWeatherChanged"/> so programmatic edits from
    /// elsewhere (the <c>weather</c> console command, a future climate system) keep the rows
    /// honest.
    /// </summary>
    public partial class TimeWeatherEditor
    {
        // Index lines up with TimeWeatherEditorUIBuilder.WEATHER_ROWS.
        private static readonly WeatherType[] WEATHER_TYPES = new[]
        {
            WeatherType.Wind,
            WeatherType.Rain,
            WeatherType.Snow,
        };

        private void OnEnable()  => WeatherManager.OnWeatherChanged += OnWeatherChangedExternally;
        private void OnDisable() => WeatherManager.OnWeatherChanged -= OnWeatherChangedExternally;

        private Action[] BuildWeatherRowCallbacks()
        {
            var arr = new Action[WEATHER_TYPES.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                int captured = i;
                arr[i] = () => OnWeatherRowClicked(captured);
            }
            return arr;
        }

        private void OnWeatherRowClicked(int idx)
        {
            var manager = WeatherManager.Instance;
            if (manager == null) return;

            string label = TimeWeatherEditorUIBuilder.WEATHER_ROWS[idx].Label;

            if (manager.IsIndoors)
            {
                SetStatus($"{label}: not indoors — step outside to author this zone's weather.");
                return;
            }
            if (!manager.HasActiveZone)
            {
                SetStatus($"{label}: no zone detected yet — weather has nowhere to be authored.");
                return;
            }

            var level = manager.Cycle(WEATHER_TYPES[idx]);
            ApplyWeatherHighlights();
            SetStatus($"{label}: {level.ToLabel()} in {manager.ActiveZone}.");
        }

        private void OnWeatherOffClicked()
        {
            var manager = WeatherManager.Instance;
            if (manager == null) return;

            if (!manager.HasActiveZone)
            {
                SetStatus("No zone detected yet — nothing to clear.");
                return;
            }

            string zone = manager.ActiveZone;
            manager.ClearAll();
            ApplyWeatherHighlights();
            SetStatus($"Clear — no weather in {zone}. (Console: 'weather clear all' for every zone.)");
        }

        private void OnWeatherChangedExternally(string zone, WeatherType type, WeatherIntensity level)
        {
            // ApplyWeatherHighlights pulls from WeatherManager directly, so there is nothing to
            // do per-event other than re-paint — and only when the change touched what is on
            // screen, which is the active zone.
            if (!_active) return;
            var manager = WeatherManager.Instance;
            if (manager != null && !string.Equals(zone, manager.ActiveZone,
                                                  StringComparison.OrdinalIgnoreCase))
                return;
            ApplyWeatherHighlights();
        }

        /// <summary>
        /// Called from the editor's per-frame tick. Repainting unconditionally is what makes
        /// walking between zones update the rows: crossing a boundary changes what every row
        /// means without raising <see cref="WeatherManager.OnWeatherChanged"/>, because
        /// nothing was authored — the author simply moved.
        /// </summary>
        private void SyncWeatherHighlightsFromLive() => ApplyWeatherHighlights();

        private void ApplyWeatherHighlights()
        {
            if (_ui.WeatherRowBgs == null) return;

            var manager  = WeatherManager.Instance;
            bool indoors = manager != null && manager.IsIndoors;
            string zone  = manager == null ? string.Empty : manager.ActiveZone;

            if (_ui.WeatherZoneTmp != null)
            {
                _ui.WeatherZoneTmp.text =
                    manager == null   ? "NO WEATHER MANAGER"
                    : indoors         ? "INDOORS — sheltered, no weather"
                    : string.IsNullOrEmpty(zone) ? "NO ZONE DETECTED"
                    : $"ZONE · {zone}";
            }

            bool noneActive = true;
            for (int i = 0; i < WEATHER_TYPES.Length; i++)
            {
                // Indoors the rows show Off: there is no weather here, and showing the outdoor
                // zone's levels would invite a click that cannot land.
                var level = (manager == null || indoors)
                    ? WeatherIntensity.Off
                    : manager.LevelOf(WEATHER_TYPES[i]);

                bool on = level != WeatherIntensity.Off;
                if (on) noneActive = false;

                var row = TimeWeatherEditorUIBuilder.WEATHER_ROWS[i];
                if (_ui.WeatherRowLabels != null && _ui.WeatherRowLabels[i] != null)
                    _ui.WeatherRowLabels[i].text = on ? $"{row.Label}  ·  {level.ToLabel()}" : row.Label;

                TimeWeatherEditorUIBuilder.ApplyWeatherRowStyle(
                    _ui.WeatherRowBgs[i], _ui.WeatherRowLabels[i], on, row.Accent);
            }

            TimeWeatherEditorUIBuilder.ApplyOffRowStyle(
                _ui.WeatherOffImg, _ui.WeatherOffTmp, noneActive);
        }
    }
}
