using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Infrastructure;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        private void OnExpandClicked()
        {
            // Persist the size of the OUTGOING mode so each layout remembers its own footprint.
            CacheSizeForCurrentMode();
            _isExpanded = !_isExpanded;
            // Adopt the size cached for the INCOMING mode.
            widgetWidth  = _isExpanded ? _expandedW : _simpleW;
            widgetHeight = _isExpanded ? _expandedH : _simpleH;
            ApplyExpandedState();
            PlayerPrefs.SetInt(PrefKeyExpanded, _isExpanded ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void ApplyExpandedState()
        {
            if (_rt != null) _rt.sizeDelta = new Vector2(BaseW, CurrentBaseH);
            if (_spectrumPanel != null) _spectrumPanel.SetActive(_isExpanded);
            if (_expandIcon != null)    _expandIcon.sprite = _isExpanded ? SpriteChevronDown : SpriteChevronUp;
            // Re-clamp current pixel size in the new vertical reference frame.
            ApplySize(widgetWidth, widgetHeight);
            PersistSize();
        }

        private void CacheSizeForCurrentMode()
        {
            if (_isExpanded) { _expandedW = widgetWidth; _expandedH = widgetHeight; }
            else             { _simpleW   = widgetWidth; _simpleH   = widgetHeight; }
        }

        // Called by the ResizeHandle while the user drags.
        // Width and height are independent: the user can stretch the widget
        // freely on either axis without the other one following.
        internal void ApplySize(float w, float h)
        {
            float wn = Mathf.Clamp(w, minSize.x, maxSize.x);
            float hn = Mathf.Clamp(h, minSize.y, maxSize.y);

            // Hard clamp to the actual on-screen rectangle so the widget never
            // overflows the top/left of the screen (especially in expanded mode).
            // CRITICAL: widget sizes are in CANVAS units, but Screen.* / pixelRect
            // are in SCREEN pixels. Convert via canvas.scaleFactor so the clamp
            // is correct under any CanvasScaler setting.
            float screenW = Screen.width;
            float screenH = Screen.height;
            if (_canvas != null)
            {
                var pr = _canvas.pixelRect;
                if (pr.width  > 0f) screenW = pr.width;
                if (pr.height > 0f) screenH = pr.height;
            }
            float canvasScale = (_canvas != null && _canvas.scaleFactor > 0.0001f) ? _canvas.scaleFactor : 1f;
            // Available room for the widget expressed in CANVAS units.
            float availW = Mathf.Max(minSize.x, screenW / canvasScale - edgeInset * 2f);
            float availH = Mathf.Max(minSize.y, screenH / canvasScale - edgeInset * 2f - bottomLift);
            wn = Mathf.Min(wn, availW);
            hn = Mathf.Min(hn, availH);

            widgetWidth  = wn;
            widgetHeight = hn;
            ApplyScaleFromSize();
        }

        private void ApplyScaleFromSize()
        {
            if (_rt == null) return;
            // Independent X / Y stretch so the user can freely change aspect ratio.
            float sx = widgetWidth  / BaseW;
            float sy = widgetHeight / CurrentBaseH;
            _rt.localScale = new Vector3(sx, sy, 1f);
        }

        // Called by the ResizeHandle on EndDrag to persist the choice.
        internal void PersistSize()
        {
            // Always cache the size for the mode we're currently in, then write
            // the per-mode keys so the simple and expanded layouts stay independent.
            CacheSizeForCurrentMode();
            PlayerPrefs.SetFloat(PrefKeyWSimple,   _simpleW);
            PlayerPrefs.SetFloat(PrefKeyHSimple,   _simpleH);
            PlayerPrefs.SetFloat(PrefKeyWExpanded, _expandedW);
            PlayerPrefs.SetFloat(PrefKeyHExpanded, _expandedH);
            PlayerPrefs.Save();
        }

        internal Vector2 CurrentSize => new Vector2(widgetWidth, widgetHeight);

        /// <summary>Called by ProgressBarSeekHandler when the user clicks/drags the yellow bar.</summary>
        internal void SeekToFraction(float frac01)
        {
            if (_audio == null) return;
            var clip = _audio.CurrentMusicClip;
            if (clip == null) return;
            float t = Mathf.Clamp01(frac01) * clip.length;
            _audio.SeekMusic(t);
            // Update the visible fill immediately so the drag feels responsive.
            if (_progressFill != null) _progressFill.fillAmount = Mathf.Clamp01(frac01);
        }

        /// <summary>Called by WaveformZoomHandler when the user scrolls the wheel over the waveform.</summary>
        internal void AdjustWaveformAmplitude(float factor)
        {
            float prev = _waveformAmplitude;
            _waveformAmplitude = Mathf.Clamp(_waveformAmplitude * factor, 0.25f, 6f);
            if (Mathf.Approximately(prev, _waveformAmplitude)) return;
            PlayerPrefs.SetFloat(PrefKeyAmplitude, _waveformAmplitude);
            PlayerPrefs.Save();
            // Force a full waveform repaint with the new amplitude.
            // For bulk-mode (non-streaming) clips this re-bakes the static peaks.
            // For progressive (streaming) clips it scales subsequent peaks; existing
            // peaks are scaled in-place so the viewer reflects the new zoom now.
            if (_audio != null)
            {
                var clip = _audio.CurrentMusicClip;
                if (clip != null)
                {
                    if (_waveformProgressive)
                    {
                        // Rescale already-painted peak columns visually.
                        if (_waveformColumnPeak != null && _waveformWorkPixels != null && _waveformGridPixels != null)
                        {
                            int midY = WaveformTexH / 2;
                            float halfH = WaveformTexH * 0.45f;
                            float scaleK = _waveformAmplitude / Mathf.Max(0.0001f, prev);
                            var waveC = new Color32(220, 180, 90, 255);
                            const int BarW = 3;
                            const int GapW = 1;
                            int stride = BarW + GapW;
                            for (int gx = 0; gx < WaveformTexW; gx += stride)
                            {
                                _waveformColumnPeak[gx] = Mathf.Clamp01(_waveformColumnPeak[gx] * scaleK);
                                int span = Mathf.Max(0, (int)(_waveformColumnPeak[gx] * halfH));
                                int y0 = Mathf.Clamp(midY - span, 0, WaveformTexH - 1);
                                int y1 = Mathf.Clamp(midY + span, 0, WaveformTexH - 1);
                                int xEnd = Mathf.Min(gx + BarW, WaveformTexW);
                                for (int x = gx; x < xEnd; x++)
                                {
                                    for (int y = 0; y < WaveformTexH; y++)
                                        _waveformWorkPixels[y * WaveformTexW + x] = _waveformGridPixels[y * WaveformTexW + x];
                                    if (span > 0)
                                        for (int y = y0; y <= y1; y++)
                                            _waveformWorkPixels[y * WaveformTexW + x] = waveC;
                                }
                            }
                            _waveformTex.SetPixels32(_waveformWorkPixels);
                            _waveformTex.Apply(false, false);
                        }
                    }
                    else
                    {
                        // Force a clean rebuild for bulk clips.
                        _cachedWaveformClip = null;
                        UpdateWaveform(true);
                    }
                }
            }
        }

        // ── Tap-tempo / live tempo override ──────────────────────────────────
        // We store *music-time* timestamps (CurrentMusicTime) instead of wall-clock
        // time so the BPM estimate is robust against pause/seek and matches the
        // beat clock 1:1. Tap intervals are also folded over candidate beat-multiples
        // ({1, 1/2, 2, 1/3, 3, 1/4, 4}) so the user can tap on any subdivision
        // (eighths, half-notes…) and we still recover the song's true beat period.
        // (_tapTimes and TapResetSec are declared in MusicPlayerHUD.cs with the other fields.)

        /// <summary>
        /// Called by <see cref="BeatDotsTapHandler"/> on each pointer-up that wasn't a drag.
        /// Builds a rolling music-time tap window, derives a robust BPM via the median
        /// inter-tap interval, snaps the latest tap to the nearest beat boundary so the
        /// downbeat counter stays continuous, persists the result per-track, and fires
        /// <see cref="MusicBeatClock.OverrideTempo"/> so the change applies to the rest
        /// of the song.
        /// </summary>
        internal void RegisterBeatTap()
        {
            if (_audio == null) return;
            if (_clock == null) _clock = MusicBeatClock.Instance;
            if (_clock == null) return;

            // Music time is the right reference: ignores pause/scale and matches the clock.
            float now = _audio.CurrentMusicTime;
            if (_tapTimes.Count > 0 && now - _tapTimes[_tapTimes.Count - 1] > TapResetSec)
                _tapTimes.Clear();
            _tapTimes.Add(now);
            const int MaxTaps = 12;
            if (_tapTimes.Count > MaxTaps) _tapTimes.RemoveRange(0, _tapTimes.Count - MaxTaps);
            if (_tapTimes.Count < 2) return;

            // Median inter-tap interval (robust to one mistimed tap).
            int n = _tapTimes.Count - 1;
            var ipi = new float[n];
            for (int i = 0; i < n; i++) ipi[i] = _tapTimes[i + 1] - _tapTimes[i];
            System.Array.Sort(ipi);
            float medianIpi = (n % 2 == 1) ? ipi[n / 2] : 0.5f * (ipi[n / 2 - 1] + ipi[n / 2]);
            if (medianIpi <= 0.05f) return; // impossibly fast taps

            // Try multiple beat-multiples and keep the BPM closest (in log space) to the
            // existing one — lets the user tap halves/quarters/eighths interchangeably.
            float[] mults = { 1f, 0.5f, 2f, 1f / 3f, 3f, 0.25f, 4f };
            float currentBpm = _clock.Bpm > 0f ? _clock.Bpm : 120f;
            float bestBpm = currentBpm;
            float bestErr = float.MaxValue;
            for (int i = 0; i < mults.Length; i++)
            {
                float secPerBeat = medianIpi * mults[i];
                float candidate  = 60f / secPerBeat;
                if (candidate < 40f || candidate > 240f) continue;
                float err = Mathf.Abs(Mathf.Log(candidate / currentBpm));
                if (err < bestErr) { bestErr = err; bestBpm = candidate; }
            }

            // Snap the latest tap to the nearest beat: choose offset so the rounded
            // beat index k = round((tap - prevOffset) / secPerBeat) lands exactly on
            // the tap. Keeps the bar/beat counter continuous instead of jumping.
            float secPerBeat2 = 60f / bestBpm;
            float prevOffset  = _clock.FirstBeatOffsetSec;
            float relTap      = now - prevOffset;
            int   k           = Mathf.Max(0, Mathf.RoundToInt(relTap / secPerBeat2));
            float newOffset   = now - k * secPerBeat2;
            while (newOffset < 0f)           newOffset += secPerBeat2;
            while (newOffset >= secPerBeat2) newOffset -= secPerBeat2;

            _clock.OverrideTempo(bestBpm, newOffset);

            // Only persist once we have a stable estimate (>=3 taps) so a single
            // accidental click doesn't permanently retune the track.
            if (_tapTimes.Count >= 3)
            {
                string trackId = _audio.CurrentTrackId;
                if (!string.IsNullOrEmpty(trackId))
                {
                    PlayerPrefs.SetFloat(string.Format(PrefKeyTempoBpmFmt,    trackId), bestBpm);
                    PlayerPrefs.SetFloat(string.Format(PrefKeyTempoOffsetFmt, trackId), newOffset);
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>
        /// Called by <see cref="BeatDotsTapHandler"/> while the user drags. Horizontal delta
        /// fine-tunes BPM (right = faster), vertical delta fine-tunes the first-beat offset.
        /// Persisted per-track so manual tweaks survive across plays.
        /// </summary>
        internal void AdjustTempoByDrag(Vector2 deltaPixels)
        {
            if (_clock == null) _clock = MusicBeatClock.Instance;
            if (_clock == null || _clock.Bpm <= 0f) return;
            float bpm = Mathf.Clamp(_clock.Bpm + deltaPixels.x * 0.2f, 40f, 240f);
            float off = Mathf.Max(0f, _clock.FirstBeatOffsetSec - deltaPixels.y * 0.005f);
            _clock.OverrideTempo(bpm, off);
            string trackId = _audio != null ? _audio.CurrentTrackId : null;
            if (!string.IsNullOrEmpty(trackId))
            {
                // PlayerPrefs writes are coalesced; one Save() per drag burst is enough,
                // but Unity's Save is cheap so we just write each tick.
                PlayerPrefs.SetFloat(string.Format(PrefKeyTempoBpmFmt,    trackId), bpm);
                PlayerPrefs.SetFloat(string.Format(PrefKeyTempoOffsetFmt, trackId), off);
            }
        }
    }

    /// <summary>
    /// Drag handler for the top-left resize grip of <see cref="MusicPlayerHUD"/>.
    /// Because the widget is anchored bottom-right (pivot 1,0), moving the
    /// pointer left/up grows the widget; right/down shrinks it.
    /// </summary>
    internal sealed class ResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private MusicPlayerHUD _owner;
        private Vector2 _startSize;
        private Vector2 _startPointer;

        public void Init(MusicPlayerHUD owner) { _owner = owner; }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_owner == null) return;
            _startSize = _owner.CurrentSize;
            _startPointer = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_owner == null) return;
            float scale = 1f;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.0001f) scale = canvas.scaleFactor;

            Vector2 delta = (e.position - _startPointer) / scale;
            // Bottom-right anchored: drag LEFT (negative x) → wider; UP (positive y) → taller.
            float newW = _startSize.x - delta.x;
            float newH = _startSize.y + delta.y;
            _owner.ApplySize(newW, newH);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_owner != null) _owner.PersistSize();
        }
    }

    /// <summary>
    /// Click + drag the progress bar to seek the music. Uses the bar's RectTransform
    /// to convert pointer X into a 0..1 fraction, then asks the HUD to seek.
    /// </summary>
    internal sealed class ProgressBarSeekHandler : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private MusicPlayerHUD _owner;
        private RectTransform _rt;

        public void Init(MusicPlayerHUD owner, RectTransform rt) { _owner = owner; _rt = rt; }

        public void OnPointerDown(PointerEventData e) => Seek(e);
        public void OnDrag(PointerEventData e)        => Seek(e);
        public void OnPointerUp(PointerEventData e)   => Seek(e);

        private void Seek(PointerEventData e)
        {
            if (_owner == null || _rt == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var local))
                return;
            // Bar is left-pivot (0,1) with full width; local.x is 0..rect.width.
            float w = _rt.rect.width;
            if (w <= 0f) return;
            float frac = Mathf.Clamp01(local.x / w);
            _owner.SeekToFraction(frac);
        }
    }

    /// <summary>
    /// Mouse-wheel zoom on the waveform area. Each scroll step multiplies the
    /// vertical amplitude by ~1.2 (or 1/1.2 down). Persisted via PlayerPrefs.
    /// </summary>
    internal sealed class WaveformZoomHandler : MonoBehaviour, IScrollHandler
    {
        private MusicPlayerHUD _owner;
        public void Init(MusicPlayerHUD owner) { _owner = owner; }
        public void OnScroll(PointerEventData e)
        {
            if (_owner == null) return;
            float dy = e.scrollDelta.y;
            if (Mathf.Abs(dy) < 0.001f) return;
            float factor = dy > 0f ? 1.2f : (1f / 1.2f);
            _owner.AdjustWaveformAmplitude(factor);
        }
    }

    /// <summary>
    /// Beat-dots strip input handler. Distinguishes:
    ///  • Click (no drag) → tap-tempo: each click contributes to a rolling BPM estimate.
    ///  • Drag             → live tempo nudge: horizontal = BPM, vertical = first-beat offset.
    /// </summary>
    internal sealed class BeatDotsTapHandler : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler
    {
        private MusicPlayerHUD _owner;
        private bool _isDragging;
        private const float DragThresholdPx = 4f;
        private Vector2 _downPos;

        public void Init(MusicPlayerHUD owner) { _owner = owner; }

        public void OnPointerDown(PointerEventData e)
        {
            _isDragging = false;
            _downPos = e.position;
        }

        public void OnPointerUp(PointerEventData e)
        {
            // Only count as a tap if the pointer didn't move enough to be a drag.
            if (_isDragging) { _isDragging = false; return; }
            if ((e.position - _downPos).sqrMagnitude > DragThresholdPx * DragThresholdPx) return;
            _owner?.RegisterBeatTap();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _isDragging = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_owner == null) return;
            float scale = 1f;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.0001f) scale = canvas.scaleFactor;
            Vector2 deltaPx = e.delta / scale;
            _owner.AdjustTempoByDrag(deltaPx);
        }
    }
}
