using System;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.TimeWeather
{
    /// <summary>
    /// Weather panel: toggles individual weather effects through
    /// <see cref="WeatherManager"/>. Wind / Rain / Snow stack freely (Wind +
    /// Rain = stormy). The OFF row clears every active weather in one click.
    /// Subscribes to <see cref="WeatherManager.OnWeatherChanged"/> so
    /// programmatic edits from elsewhere (debug commands, save load) keep
    /// the row highlights honest.
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
            if (WeatherManager.Instance == null) return;
            bool on = WeatherManager.Instance.Toggle(WEATHER_TYPES[idx]);
            ApplyWeatherHighlights();
            SetStatus($"{TimeWeatherEditorUIBuilder.WEATHER_ROWS[idx].Label}: {(on ? "ON" : "OFF")}.");
        }

        private void OnWeatherOffClicked()
        {
            if (WeatherManager.Instance != null) WeatherManager.Instance.ClearAll();
            ApplyWeatherHighlights();
            SetStatus("Clear — no active weather.");
        }

        private void OnWeatherChangedExternally(WeatherType type, bool active)
        {
            // ApplyWeatherHighlights pulls from WeatherManager directly, so we
            // don't need to do anything per-event other than re-paint.
            if (_active) ApplyWeatherHighlights();
        }

        private void SyncWeatherHighlightsFromLive() => ApplyWeatherHighlights();

        private void ApplyWeatherHighlights()
        {
            if (_ui.WeatherRowBgs == null) return;
            bool noneActive = true;
            for (int i = 0; i < WEATHER_TYPES.Length; i++)
            {
                bool on = WeatherManager.Instance != null && WeatherManager.Instance.IsActive(WEATHER_TYPES[i]);
                if (on) noneActive = false;
                TimeWeatherEditorUIBuilder.ApplyWeatherRowStyle(
                    _ui.WeatherRowBgs[i], _ui.WeatherRowLabels[i], on,
                    TimeWeatherEditorUIBuilder.WEATHER_ROWS[i].Accent);
            }
            TimeWeatherEditorUIBuilder.ApplyOffRowStyle(
                _ui.WeatherOffImg, _ui.WeatherOffTmp, noneActive);
        }
    }
}
