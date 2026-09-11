using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Infrastructure;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        private IAudioService _audio;
        private IMusicSignalSource _signal;
        private AudioCatalogSO _catalog;
        private bool _catalogLooked;
        private MusicBeatClock _clock;
        private bool _clockBound;

        private string _trackId;
        private MusicTrackEntry _track;
        private string _zoneGroup = string.Empty;
        private bool _trackDirty = true;
        private bool _announceTrack;
        // int.MinValue, not -1: -1 is what an idle panel SHOWS, so a -1 cache would never write it.
        private int _shownSecond = int.MinValue, _shownLength = int.MinValue;
        private int _shownVolumePercent = -1;
        private bool _wasPlaying, _wasPaused, _wasActive;
        private float _volumeBeforeMute = 0.7f;
        private bool _unmutePending;

        private float[] _signalBuffer;
        private float _spectrumT;

        // -- Audio binding -----------------------------------------------------------

        private void BindAudio()
        {
            if (_audio != null) return;
            _audio = ServiceLocator.Get<IAudioService>();
            if (_audio == null) return;
            _audio.OnTrackChanged += HandleTrackChanged;
            _signal = _audio as IMusicSignalSource;
            _trackDirty = true;
            // Seed the pre-mute volume from the shared setting, so unmuting restores what the
            // player last chose even if they chose it in the pause menu's sound panel.
            float v = Mathf.Clamp01(GameSettings.Instance.musicVolume);
            if (v > 0.001f) _volumeBeforeMute = v;
            _volume?.SetVolume(_audio.MusicVolume, animateUp: false);
        }

        private void UnbindAudio()
        {
            if (_audio != null) _audio.OnTrackChanged -= HandleTrackChanged;
            _audio = null;
            _signal = null;
        }

        private void HandleTrackChanged(string id, string title, float bpm, int beatsPerBar)
        {
            _trackDirty = true;
            _announceTrack = true;
        }

        private AudioCatalogSO Catalog
        {
            get
            {
                if (_catalog == null && !_catalogLooked)
                {
                    _catalogLooked = true;
                    _catalog = Resources.Load<AudioCatalogSO>("AudioCatalog");
                }
                return _catalog;
            }
        }

        private MusicTrackEntry FindTrack(string id)
        {
            var tracks = Catalog != null ? Catalog.Tracks : null;
            if (tracks == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < tracks.Length; i++)
                if (tracks[i] != null && tracks[i].id == id) return tracks[i];
            return null;
        }

        // -- Beat clock (the resonance's phrase motes) ------------------------------------

        private void BindBeatClock()
        {
            if (_clockBound) return;
            _clock = MusicBeatClock.Instance;
            if (_clock == null) return;
            _clock.OnBar += OnBar;
            _clockBound = true;
        }

        private void UnbindBeatClock()
        {
            if (_clockBound && _clock != null) _clock.OnBar -= OnBar;
            _clockBound = false;
            _clock = null;
        }

        // -- Per frame ---------------------------------------------------------------------

        private void TickPlayback(float dt)
        {
            bool playing = _audio != null && _audio.IsMusicPlaying;
            bool paused = _audio != null && _audio.IsMusicPaused;
            bool active = playing || paused;
            // Starting or stopping altogether changes what the title says even with no track change.
            if (active != _wasActive) { _wasActive = active; _trackDirty = true; }

            if (_trackDirty) RefreshTrack(active);

            // Transport state changes are events: resuming gets a light, pausing gets quiet.
            if (active && _wasPaused && playing && !paused) OnResumed();
            _wasPlaying = playing;
            _wasPaused = paused;

            var clip = _audio != null ? _audio.CurrentMusicClip : null;
            float length = clip != null ? clip.length : 0f;
            float time = active ? _audio.CurrentMusicTime : 0f;
            float progress = length > 0f ? Mathf.Clamp01(time / length) : 0f;

            _groove.SetEnabled(active && length > 0f);
            if (!_groove.Seeking) _groove.SetProgress(progress);
            if (_expanded) _resonance.SetProgress(_groove.DisplayedFraction);

            // Times are rewritten only when the SECOND changes: a label write rebuilds its mesh.
            int second = active ? Mathf.FloorToInt(_groove.Seeking ? _groove.DisplayedFraction * length : time) : -1;
            int len = active ? Mathf.FloorToInt(length) : -1;
            if (second != _shownSecond)
            {
                _shownSecond = second;
                _timeNow.SetText(second >= 0 ? MusicTrackInfo.FormatTime(second) : "-:--");
            }
            if (len != _shownLength)
            {
                _shownLength = len;
                _timeTotal.SetText(len >= 0 ? MusicTrackInfo.FormatTime(len) : "-:--");
            }

            float volume = _audio != null ? _audio.MusicVolume : 0f;
            bool muted = volume <= 0.001f;
            _volume.SetVolume(volume, animateUp: _unmutePending);
            _unmutePending = false;
            int percent = Mathf.RoundToInt(volume * 100f);
            if (percent != _shownVolumePercent)
            {
                _shownVolumePercent = percent;
                _volumeLabel.SetText(muted ? "0%" : percent + "%");
            }

            _status.SetText(!active ? string.Empty : paused ? MusicHudText.Paused : muted ? MusicHudText.Muted : string.Empty);
            _play.SetGlyph(playing && !paused ? _art.Pause : _art.Play);
            _play.SetEnabled(active);
            bool canSkip = _audio != null && _audio.HasActivePlaylist;
            _prev.SetEnabled(canSkip);
            _next.SetEnabled(canSkip);
            _mute.SetGlyph(muted ? _art.SpeakerOff : _art.SpeakerOn);
            _mute.SetEnabled(_audio != null);
            _title.SetColour(paused ? _theme.textDim : _theme.text);
        }

        private void RefreshTrack(bool active)
        {
            _trackDirty = false;
            string id = _audio != null ? _audio.CurrentTrackId : null;
            string title = _audio != null ? _audio.CurrentTrackTitle : null;
            if (!active || string.IsNullOrEmpty(title))
            {
                _trackId = null;
                _track = null;
                _title.SetText(MusicHudText.Idle);
                _zone.SetText(string.Empty);
                SetGroup(string.Empty, announce: false);
                _resonance.SetSong(null, null);
                _announceTrack = false;
                return;
            }

            bool changed = id != _trackId;
            _trackId = id;
            _track = FindTrack(id);
            string group = MusicTrackInfo.GroupOf(title);
            var pos = MusicTrackInfo.PositionInGroup(Catalog != null ? Catalog.Tracks : null, id);

            string upper = title.ToUpperInvariant();
            _title.SetText(HudPixelFont.CanSpell(upper, _hudArt.Patterns(HudFontFace.Small))
                ? MusicTrackInfo.FitToWidth(upper, _titleMaxW, s => _hudArt.Measure(s, HudFontFace.Small))
                : MusicTrackInfo.FitToWidth(Ascii(upper), _titleMaxW, s => _hudArt.Measure(s, HudFontFace.Small)));
            string zone = group.ToUpperInvariant();
            if (pos.y > 1) zone += "  " + pos.x + "/" + pos.y;
            _zone.SetText(zone);

            bool groupChanged = !string.Equals(group, _zoneGroup, System.StringComparison.OrdinalIgnoreCase);
            SetGroup(group, announce: _announceTrack && groupChanged);
            _resonance.SetSong(_track != null ? _track.DecodeEnvelope() : null, PhraseMarks(_track, _audio));

            if (_announceTrack && changed) AnnounceTrack();
            _announceTrack = false;
        }

        private void SetGroup(string group, bool announce)
        {
            _zoneGroup = group ?? string.Empty;
            var sprite = _art.SigilFor(MusicTrackInfo.SigilOf(_zoneGroup));
            if (_sigil.sprite != sprite) _sigil.sprite = sprite;
            if (announce) AnnounceZone();
        }

        /// <summary>
        /// The phrase marks along the envelope: one every eight bars, from the analysed beat
        /// onsets when the catalog has them (real songs drift), else from the tempo.
        /// </summary>
        private static float[] PhraseMarks(MusicTrackEntry track, IAudioService audio)
        {
            var clip = audio != null ? audio.CurrentMusicClip : null;
            float length = clip != null ? clip.length : 0f;
            if (track == null || length <= 0f) return null;
            int perBar = Mathf.Max(1, track.beatsPerBar);
            int step = perBar * 8;
            var list = new System.Collections.Generic.List<float>();
            if (track.beatTimes != null && track.beatTimes.Length > step)
            {
                for (int i = 0; i < track.beatTimes.Length; i += step) list.Add(track.beatTimes[i] / length);
            }
            else if (track.bpm > 0f)
            {
                float phrase = step * 60f / track.bpm;
                for (float t = track.firstBeatOffsetSec; t < length; t += phrase) list.Add(t / length);
            }
            return list.ToArray();
        }

        /// <summary>Drops what the pixel face cannot spell, so a title never renders with holes.</summary>
        private string Ascii(string text)
        {
            var patterns = _hudArt.Patterns(HudFontFace.Small);
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text) if (c == ' ' || patterns.ContainsKey(c)) sb.Append(c);
            return sb.ToString();
        }

        private void TickResonance(float dt)
        {
            if (!_expanded) return;
            if (!_clockBound) BindBeatClock();
            _spectrumT -= dt;
            if (_spectrumT > 0f) return;
            float step = 1f / Mathf.Max(1f, _style.spectrumHz);
            _spectrumT += step;
            if (_spectrumT < -step) _spectrumT = 0f;

            int n = 0;
            if (_signal != null && _audio != null && _audio.IsMusicPlaying && !_audio.IsMusicPaused)
            {
                if (_signalBuffer == null || _signalBuffer.Length != _spectrum.Size) _signalBuffer = new float[_spectrum.Size];
                _spectrum.SetSampleRate(_signal.MusicSignalSampleRate);
                n = _signal.ReadMusicSignal(_signalBuffer);
            }
            _spectrum.Analyse(_signalBuffer, n, step);
            _resonance.SetLevels(_spectrum.Levels, step);
        }

        // -- Transport ---------------------------------------------------------------------

        private void OnPrevious()
        {
            if (_audio == null || !_audio.HasActivePlaylist) return;
            _audio.SkipToPreviousTrack();
            BurstFromKey(_prev, -1);
        }

        private void OnNext()
        {
            if (_audio == null || !_audio.HasActivePlaylist) return;
            _audio.SkipToNextTrack();
            BurstFromKey(_next, 1);
        }

        private void OnPlayPause()
        {
            if (_audio == null) return;
            if (_audio.IsMusicPaused) _audio.ResumeMusic();
            else if (_audio.IsMusicPlaying) _audio.PauseMusic();
        }

        private void OnMuteToggle()
        {
            if (_audio == null) return;
            if (_audio.MusicVolume > 0.001f)
            {
                _volumeBeforeMute = _audio.MusicVolume;
                ApplyAndPersistVolume(0f);
            }
            else
            {
                ApplyAndPersistVolume(_volumeBeforeMute > 0.05f ? _volumeBeforeMute : 0.7f);
            }
        }

        /// <summary>
        /// The single place the music volume changes: the shared setting (the pause and main
        /// menus' sound panels read the same field) and the running audio manager.
        /// </summary>
        private void ApplyAndPersistVolume(float v)
        {
            v = Mathf.Clamp01(v);
            bool unmuting = v > 0.001f && (_audio == null || _audio.MusicVolume <= 0.001f);
            var gs = GameSettings.Instance;
            gs.musicVolume = v;
            _audio?.SetMusicVolume(v);
            gs.Save();
            if (v > 0.001f) _volumeBeforeMute = v;
            if (unmuting) OnUnmuted();
        }

        private void OnSeek(float fraction)
        {
            var clip = _audio != null ? _audio.CurrentMusicClip : null;
            if (clip == null) return;
            _audio.SeekMusic(fraction * clip.length);
            BurstFromBead();
        }

        // -- Hover and tooltips ------------------------------------------------------------

        private bool _hoveringPanel;

        private void WireKeyTooltips()
        {
            Hover(_prev, () => (MusicHudText.TipPrevious, _prev.Enabled ? string.Empty : MusicHudText.TipNoPlaylist));
            Hover(_next, () => (MusicHudText.TipNext, _next.Enabled ? string.Empty : MusicHudText.TipNoPlaylist));
            Hover(_play, () => (_audio != null && _audio.IsMusicPlaying && !_audio.IsMusicPaused
                                    ? MusicHudText.TipPause : MusicHudText.TipPlay,
                                _play.Enabled ? string.Empty : MusicHudText.TipIdleBody));
            Hover(_mute, () => (_audio != null && _audio.MusicVolume <= 0.001f ? MusicHudText.TipUnmute : MusicHudText.TipMute, string.Empty));
            Hover(_resonanceKey, () => (_expanded ? MusicHudText.TipResonanceClose : MusicHudText.TipResonanceOpen,
                                        MusicHudText.TipResonanceBody));
            Hover(_close, () => (MusicHudText.TipClose, MusicHudText.TipCloseBody));
            _volume.HoverChanged += on =>
            {
                _hoveringPanel = on;
                if (on) ShowTip(MusicHudText.TipVolume + " " + _shownVolumePercent + " %", MusicHudText.TipVolumeBody,
                                _volume.Root.anchoredPosition.x + _volume.Root.sizeDelta.x * 0.5f);
                else HideTooltip();
            };
        }

        private void Hover(MusicHudKey key, System.Func<(string title, string body)> text)
        {
            key.HoverChanged += on =>
            {
                _hoveringPanel = on;
                if (!on) { HideTooltip(); return; }
                var (title, body) = text();
                ShowTip(title, body, key.Centre.x);
            };
        }

        private void ShowTip(string title, string body, float centreX)
        {
            _tooltip.ShowText(title, body, _theme.gold, centreX, _style.HeightTexels(_expanded), _style.widthTexels);
        }

        private void ShowTrackTooltip()
        {
            _hoveringPanel = true;
            if (_audio == null || string.IsNullOrEmpty(_trackId)) { ShowTip(MusicHudText.Idle, MusicHudText.TipIdleBody, _titleX + 20); return; }
            var pos = MusicTrackInfo.PositionInGroup(Catalog != null ? Catalog.Tracks : null, _trackId);
            string body = MusicHudText.GroupLine(_zoneGroup, pos);
            string tempo = MusicHudText.TempoLine(_track != null ? _track.bpm : 0f,
                                                  MusicTrackInfo.KeyInSpanish(MusicTrackInfo.TrustedKey(_track)));
            if (!string.IsNullOrEmpty(tempo)) body = string.IsNullOrEmpty(body) ? tempo : body + "\n" + tempo;
            ShowTip(_audio.CurrentTrackTitle, body, _titleX + 30);
        }

        private void OnGrooveHover(float fraction)
        {
            var clip = _audio != null ? _audio.CurrentMusicClip : null;
            if (float.IsNaN(fraction) || clip == null) { _hoveringPanel = false; HideTooltip(); return; }
            _hoveringPanel = true;
            string at = MusicTrackInfo.FormatTime(fraction * clip.length) + " / " + MusicTrackInfo.FormatTime(clip.length);
            ShowTip(at, MusicHudText.TipSeek, _groove.Root.anchoredPosition.x + fraction * _groove.Root.sizeDelta.x);
        }

        private void HideTooltip()
        {
            _hoveringPanel = false;
            _tooltip?.Hide();
        }
    }
}
