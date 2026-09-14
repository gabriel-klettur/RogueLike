using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Options → Audio.
    ///
    /// <para><b>What it replaces.</b> Eight rows of which five were mixing knobs — the ambience
    /// interval pair and the ducking attenuation, hold and release — and no master volume at
    /// all. Worse, the attenuation row printed <c>-400</c> for -4 dB, because the value format
    /// was chosen by <c>row.max &lt;= 1f</c>: the dB range is -24…0, so its max IS 0, the test
    /// passed and a decibel was rendered as a percentage. A number that is real, internally
    /// consistent and about something else.</para>
    ///
    /// <para><b>The format now comes from the row's ROLE</b>, which is what the test was trying
    /// to infer. Four kinds, one formatter each, and a kind cannot be guessed wrong.</para>
    ///
    /// <para><b>The advanced rows are still here and still reachable</b>, behind a row that opens
    /// them. Removing them would take a real tool away from whoever tunes the mix; showing them
    /// first was the defect.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        /// <summary>How a value is spelled. Replaces guessing it from the range's maximum.</summary>
        private enum ValueKind { Percent, Seconds, Decibels, Milliseconds }

        private sealed class AudioRowSpec
        {
            public string Label;
            public float Min, Max, Step;
            public ValueKind Kind;
            public Func<float> Get;
            public Action<float> Set;
            public bool Advanced;
            public int Notches;
        }

        private MenuPanelView _audioPanel;
        private MenuList _audioList;
        private readonly List<AudioRowSpec> _audioSpecs = new List<AudioRowSpec>();
        private readonly List<MenuSlider> _audioSliders = new List<MenuSlider>();
        private readonly List<MenuRow> _audioRows = new List<MenuRow>();
        private bool _audioShowAdvanced;
        private int _audioAdvancedRowIndex = -1;
        private int _audioTestRowIndex = -1;
        private AudioClip _audioTestClip;

        private void BuildAudioPanel(Transform canvas)
        {
            var style = Style;
            var gs = GameSettings.Instance;

            _audioSpecs.Clear();
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioMaster, Min = 0f, Max = 1f, Step = 0.02f, Kind = ValueKind.Percent,
                Notches = 5, Get = () => gs.masterVolume, Set = v => gs.masterVolume = v,
            });
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioMusic, Min = 0f, Max = 1f, Step = 0.02f, Kind = ValueKind.Percent,
                Notches = 5, Get = () => gs.musicVolume, Set = v => gs.musicVolume = v,
            });
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioSfx, Min = 0f, Max = 1f, Step = 0.02f, Kind = ValueKind.Percent,
                Notches = 5, Get = () => gs.sfxVolume, Set = v => gs.sfxVolume = v,
            });
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioAmbient, Min = 0f, Max = 1f, Step = 0.02f, Kind = ValueKind.Percent,
                Notches = 5, Get = () => gs.ambientVolume, Set = v => gs.ambientVolume = v,
            });
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioAmbientMin, Min = 0f, Max = 60f, Step = 0.5f, Kind = ValueKind.Seconds,
                Advanced = true, Get = () => gs.ambientMinInterval, Set = v => gs.ambientMinInterval = v,
            });
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioAmbientMax, Min = 0f, Max = 120f, Step = 0.5f, Kind = ValueKind.Seconds,
                Advanced = true, Get = () => gs.ambientMaxInterval, Set = v => gs.ambientMaxInterval = v,
            });
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioDuckAtten, Min = -24f, Max = 0f, Step = 1f, Kind = ValueKind.Decibels,
                Advanced = true, Get = () => gs.duckingAttenuation, Set = v => gs.duckingAttenuation = v,
            });
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioDuckHold, Min = 0f, Max = 2000f, Step = 25f, Kind = ValueKind.Milliseconds,
                Advanced = true, Get = () => gs.duckingHoldMs, Set = v => gs.duckingHoldMs = v,
            });
            _audioSpecs.Add(new AudioRowSpec
            {
                Label = MenuText.AudioDuckRelease, Min = 0f, Max = 2000f, Step = 25f, Kind = ValueKind.Milliseconds,
                Advanced = true, Get = () => gs.duckingReleaseMs, Set = v => gs.duckingReleaseMs = v,
            });

            _audioPanel = new MenuPanelView(canvas, _art, style, MenuText.AudioTitle,
                                            style.panelWidth, ReduceMotion);
            _audioList = new MenuList(_audioPanel.Body, _art, style, ReduceMotion);
            _audioSliders.Clear();
            _audioRows.Clear();

            for (int i = 0; i < _audioSpecs.Count; i++)
            {
                var spec = _audioSpecs[i];
                var row = _audioList.Add(_art, spec.Label);
                _audioRows.Add(row);
                int captured = i;
                var slider = new MenuSlider(row.Content, _art, style, spec.Min, spec.Max,
                                            spec.Get(), spec.Step,
                                            v => OnAudioValueChanged(captured, v), spec.Notches,
                                            _audioList.Motes);
                _audioSliders.Add(slider);
                // Touching row.Value is what CREATES the value column — it is built lazily, so a
                // row with nothing to say costs no TMP component. The discard says the call is
                // for its effect and not for its result, which is also what keeps the compiler
                // from warning about a local nobody reads.
                _ = row.Value;
                // And then the number moves right, out of the slider's way. Two layouts, two
                // names: a value at the default column would sit on the track it describes.
                row.UseWideContentColumns();
            }

            // A row that PLAYS something. Without it the effects slider is the one channel the
            // player adjusts while it is silent — they are setting it blind.
            _audioTestRowIndex = _audioList.Count;
            var testRow = _audioList.Add(_art, MenuText.AudioTest);
            testRow.Value.text = MenuText.AudioTestValue;
            _audioRows.Add(testRow);
            _audioSliders.Add(null);

            _audioAdvancedRowIndex = _audioList.Count;
            var advRow = _audioList.Add(_art, MenuText.AudioAdvanced);
            advRow.Value.text = MenuText.VideoOff;
            _audioRows.Add(advRow);
            _audioSliders.Add(null);

            _audioList.Changed += _ => _sfx?.Move();
            _audioList.Chosen += OnAudioRowChosen;

            _audioPanel.FitToContent(_audioList.ContentHeight);
            _audioList.SetViewport(_audioPanel.BodyHeight);
            _audioPanel.SetHint(MenuText.AudioHint);
            _audioPanel.Close();
            ApplyAudioAdvancedVisibility();
        }

        private void OnAudioRowChosen(int index)
        {
            if (index == _audioTestRowIndex) { PlayAudioTest(); return; }
            if (index == _audioAdvancedRowIndex)
            {
                _audioShowAdvanced = !_audioShowAdvanced;
                ApplyAudioAdvancedVisibility();
                _sfx?.Confirm();
                return;
            }
            // Every other row is a slider; Enter on one leaves the panel, which is what a
            // player who has finished adjusting expects.
            SaveAudio();
            OptionsGoBack();
        }

        /// <summary>
        /// The advanced rows are hidden by COLLAPSING them to zero height rather than by being
        /// destroyed and rebuilt: the panel is a fixed list and rebuilding it would drop the
        /// slider objects the sliders' own callbacks close over.
        /// </summary>
        private void ApplyAudioAdvancedVisibility()
        {
            float y = 0f;
            var style = Style;
            for (int i = 0; i < _audioRows.Count; i++)
            {
                bool advanced = i < _audioSpecs.Count && _audioSpecs[i].Advanced;
                bool show = !advanced || _audioShowAdvanced;
                var row = _audioRows[i];
                row.Root.gameObject.SetActive(show);
                row.Interactable = show;
                if (!show) continue;
                row.Root.anchoredPosition = new Vector2(0f, -y);
                y += style.rowHeight + style.rowGap;
            }
            if (_audioAdvancedRowIndex >= 0 && _audioAdvancedRowIndex < _audioRows.Count)
            {
                _audioRows[_audioAdvancedRowIndex].Value.text =
                    _audioShowAdvanced ? MenuText.VideoOn : MenuText.VideoOff;
                _audioRows[_audioAdvancedRowIndex].SetToggle(_audioShowAdvanced);
            }

            float content = Mathf.Max(0f, y - style.rowGap);
            _audioPanel?.FitToContent(content);
            // Opening the advanced rows is what pushed this panel off the bottom of the screen
            // (660 tall from a top of 296 on an 800 canvas). The panel clamps and the list gets
            // the window it actually has.
            if (_audioPanel != null) _audioList?.SetViewport(_audioPanel.BodyHeight);
            _audioList?.Tick(999f);       // snap the highlight onto the row that moved under it
        }

        private void OnAudioValueChanged(int index, float value)
        {
            if (index < 0 || index >= _audioSpecs.Count) return;
            _audioSpecs[index].Set(value);
            RefreshAudioRow(index);
            ServiceLocator.Get<IAudioService>()?.ApplySettings();
            GameSettings.Instance?.Save();
        }

        private void SaveAudio()
        {
            ServiceLocator.Get<IAudioService>()?.ApplySettings();
            GameSettings.Instance?.Save();
        }

        private void RefreshAudioRows()
        {
            if (_audioList == null) return;
            for (int i = 0; i < _audioSpecs.Count; i++)
            {
                _audioSliders[i]?.SetValue(_audioSpecs[i].Get());
                RefreshAudioRow(i);
            }
        }

        private void RefreshAudioRow(int index)
        {
            if (index < 0 || index >= _audioSpecs.Count) return;
            var spec = _audioSpecs[index];
            _audioRows[index].Value.text = FormatValue(spec.Get(), spec.Kind);
        }

        /// <summary>
        /// The formatter the shipped panel did not have. Its <c>row.max &lt;= 1f</c> test meant
        /// "is this a 0..1 fraction", and the ducking attenuation's range (-24…0) answered yes.
        /// </summary>
        private static string FormatValue(float v, ValueKind kind)
        {
            switch (kind)
            {
                case ValueKind.Percent: return Mathf.RoundToInt(v * 100f) + " %";
                case ValueKind.Seconds: return v.ToString("0.#") + " s";
                case ValueKind.Decibels: return (v > 0f ? "+" : string.Empty) + v.ToString("0.#") + " dB";
                case ValueKind.Milliseconds: return Mathf.RoundToInt(v) + " ms";
                default: return v.ToString("0.##");
            }
        }

        /// <summary>
        /// A short, deliberately plain tone. Synthesised for the reason <see cref="MenuSfx"/>
        /// gives: the catalogue holds no <c>ui_*</c> id and <c>PlaySfxById</c> warns once for
        /// every one that fails to resolve.
        /// </summary>
        private void PlayAudioTest()
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;
            if (_audioTestClip == null)
            {
                const int rate = 44100;
                int n = rate / 4;
                var data = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float env = Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-t * 7f);
                    data[i] = Mathf.Sin(2f * Mathf.PI * 440f * t) * env * 0.5f;
                }
                _audioTestClip = AudioClip.Create("ui_audio_test", n, 1, rate, false);
                _audioTestClip.SetData(data, 0);
                _audioTestClip.hideFlags = HideFlags.DontSave;
            }
            audio.PlaySFX(_audioTestClip);
        }

        private void HandleAudioInput()
        {
            if (InputCompat.NavLeftPressed()) { NudgeAudio(-1); return; }
            if (InputCompat.NavRightPressed()) { NudgeAudio(1); return; }
            HandleListInput(_audioList, () => { SaveAudio(); OptionsGoBack(); });
        }

        private void NudgeAudio(int dir)
        {
            int i = _audioList != null ? _audioList.Index : -1;
            if (i < 0 || i >= _audioSliders.Count || _audioSliders[i] == null) { _sfx?.Refuse(); return; }
            _audioSliders[i].Nudge(dir);
            _sfx?.Move();
        }
    }
}
