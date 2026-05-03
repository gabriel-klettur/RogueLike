using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        // ── Spectrum panel (expanded mode) ──────────────────────────────────

        private void BuildSpectrumPanel()
        {
            _specSamples  = new float[FftSize];
            _specSmoothed = new float[SpecBars];
            _specBars     = new Image[SpecBars];

            _spectrumPanel = NewChild("SpectrumPanel");
            var spRt = _spectrumPanel.GetComponent<RectTransform>();
            // Top of root: occupies the area above the simple content.
            spRt.anchorMin = new Vector2(0f, 0f);
            spRt.anchorMax = new Vector2(1f, 1f);
            spRt.pivot     = new Vector2(0.5f, 1f);
            // Sit between top of root and top of simple content (BaseHSimple px from bottom).
            spRt.offsetMin = new Vector2(8f, BaseHSimple);
            spRt.offsetMax = new Vector2(-8f, -8f);

            var bg = _spectrumPanel.AddComponent<Image>();
            bg.sprite = BuildRoundedRectSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.02f, 0.02f, 0.04f, 0.65f);
            bg.raycastTarget = false;

            // Beat dot row at the very top: BeatsPerBar markers, light up on the active beat.
            // The row is also interactive: clicks = tap-tempo; horizontal drag = BPM fine-tune,
            // vertical drag = first-beat offset fine-tune. See BeatDotsTapHandler.
            var dotsRoot = NewChild("BeatDots", _spectrumPanel.transform);
            var drRt = dotsRoot.GetComponent<RectTransform>();
            drRt.anchorMin = new Vector2(0f, 1f);
            drRt.anchorMax = new Vector2(1f, 1f);
            drRt.pivot     = new Vector2(0.5f, 1f);
            drRt.anchoredPosition = new Vector2(0f, -2f);
            // Make the strip tall enough to be a comfortable click/drag target.
            drRt.sizeDelta = new Vector2(0f, 18f);
            // Invisible-but-raycastable backplate so the whole strip catches input.
            var dotsBg = dotsRoot.AddComponent<Image>();
            dotsBg.color = new Color(1f, 1f, 1f, 0.001f);
            dotsBg.raycastTarget = true;
            var tapHandler = dotsRoot.AddComponent<BeatDotsTapHandler>();
            tapHandler.Init(this);

            // Build up to 16 dots; we only show BeatsPerBar of them per Update().
            const int MaxBeatDots = 16;
            _beatDots = new Image[MaxBeatDots];
            for (int i = 0; i < MaxBeatDots; i++)
            {
                var d = NewChild("Dot" + i, dotsRoot.transform);
                var dRt = d.GetComponent<RectTransform>();
                dRt.anchorMin = new Vector2(0f, 0.5f);
                dRt.anchorMax = new Vector2(0f, 0.5f);
                dRt.pivot     = new Vector2(0.5f, 0.5f);
                dRt.sizeDelta = new Vector2(8f, 8f);
                var di = d.AddComponent<Image>();
                di.sprite = BuildCircleSprite();
                di.color = new Color(1f, 1f, 1f, 0.25f);
                di.raycastTarget = false;
                _beatDots[i] = di;
            }

            // Full-song waveform overview (Ableton-style) sits between beat dots and bars.
            // It shows peaks per pixel column with bar/beat grid lines baked in,
            // plus a moving playhead so the user can visually align BPM with audio peaks.
            _waveformPanel = NewChild("Waveform", _spectrumPanel.transform);
            var wfRt = _waveformPanel.GetComponent<RectTransform>();
            wfRt.anchorMin = new Vector2(0f, 1f);
            wfRt.anchorMax = new Vector2(1f, 1f);
            wfRt.pivot     = new Vector2(0.5f, 1f);
            // Top-pinned: 4 px below the beat-dot row (which occupies top 18 px), 100 px tall.
            wfRt.offsetMin = new Vector2(6f, -122f);  // bottom = top - 122
            wfRt.offsetMax = new Vector2(-6f, -22f);  // top    = top - 22

            var wfBg = _waveformPanel.AddComponent<Image>();
            wfBg.color = new Color(0.03f, 0.03f, 0.06f, 0.9f);
            // Mouse-wheel zoom on the waveform area.
            wfBg.raycastTarget = true;
            var zoomHandler = _waveformPanel.AddComponent<WaveformZoomHandler>();
            zoomHandler.Init(this);

            _waveformImage = NewChild("WaveformImage", _waveformPanel.transform).AddComponent<Image>();
            var wiRt = _waveformImage.rectTransform;
            wiRt.anchorMin = Vector2.zero;
            wiRt.anchorMax = Vector2.one;
            wiRt.offsetMin = Vector2.zero;
            wiRt.offsetMax = Vector2.zero;
            _waveformImage.preserveAspect = false;
            _waveformImage.raycastTarget = false;
            _waveformImage.color = Color.white;

            // Playhead: 2 px vertical line that slides with playback.
            var phGo = NewChild("Playhead", _waveformPanel.transform);
            _waveformPlayhead = phGo.GetComponent<RectTransform>();
            _waveformPlayhead.anchorMin = new Vector2(0f, 0f);
            _waveformPlayhead.anchorMax = new Vector2(0f, 1f);
            _waveformPlayhead.pivot     = new Vector2(0.5f, 0.5f);
            _waveformPlayhead.sizeDelta = new Vector2(2f, 0f);
            _waveformPlayhead.anchoredPosition = Vector2.zero;
            var phImg = phGo.AddComponent<Image>();
            phImg.color = new Color(1f, 0.35f, 0.35f, 0.95f);
            phImg.raycastTarget = false;

            // Bars row sits below the waveform.
            var barsRoot = NewChild("Bars", _spectrumPanel.transform);
            var brRt = barsRoot.GetComponent<RectTransform>();
            brRt.anchorMin = new Vector2(0f, 0f);
            brRt.anchorMax = new Vector2(1f, 1f);
            brRt.offsetMin = new Vector2(6f, 6f);
            brRt.offsetMax = new Vector2(-6f, -126f); // leave 18 (dots) + 4 + 100 (waveform) + 4

            for (int i = 0; i < SpecBars; i++)
            {
                var b = NewChild("Bar" + i, barsRoot.transform);
                var bRt = b.GetComponent<RectTransform>();
                // Each bar: anchored to bottom of bars area, evenly distributed across X.
                float xMin = i / (float)SpecBars;
                float xMax = (i + 1) / (float)SpecBars;
                bRt.anchorMin = new Vector2(xMin, 0f);
                bRt.anchorMax = new Vector2(xMax, 0f);
                bRt.pivot     = new Vector2(0.5f, 0f);
                bRt.offsetMin = new Vector2(1f, 0f);
                bRt.offsetMax = new Vector2(-1f, 2f); // initial tiny height
                var bi = b.AddComponent<Image>();
                bi.sprite = BuildSolidSprite();
                bi.type = Image.Type.Sliced;
                bi.color = new Color(0.95f, 0.78f, 0.25f, 0.95f);
                bi.raycastTarget = false;
                _specBars[i] = bi;
            }
        }

        // ── Spectrum data update ────────────────────────────────────────────
        private void UpdateSpectrum(bool playing)
        {
            if (!_isExpanded || _specBars == null || _specSamples == null) return;

            bool got = playing && _audio != null && _audio.GetMusicSpectrumData(_specSamples);
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1f / 240f);
            float fall = 2.2f * dt; // smoothed bar fall-off rate

            // Map FFT bins to log-spaced bars: low bins occupy more screen than high bins,
            // matching how the human ear perceives frequency.
            int N = _specSamples.Length;
            for (int i = 0; i < SpecBars; i++)
            {
                float t0 = (float)i / SpecBars;
                float t1 = (float)(i + 1) / SpecBars;
                int lo = Mathf.Clamp(Mathf.FloorToInt(Mathf.Pow(N, t0)), 1, N - 1);
                int hi = Mathf.Clamp(Mathf.FloorToInt(Mathf.Pow(N, t1)), lo + 1, N);

                float v = 0f;
                if (got)
                {
                    for (int k = lo; k < hi; k++) v += _specSamples[k];
                    v /= Mathf.Max(1, hi - lo);
                    // Logarithmic compression so quiet detail is visible.
                    v = Mathf.Clamp01(Mathf.Log10(1f + v * 800f) * 0.5f);
                }

                if (v > _specSmoothed[i]) _specSmoothed[i] = v;
                else                       _specSmoothed[i] = Mathf.Max(0f, _specSmoothed[i] - fall);

                var bar = _specBars[i];
                if (bar == null) continue;
                var rt = bar.rectTransform;
                // Height as percentage of bar area, anchored bottom.
                rt.anchorMax = new Vector2(rt.anchorMax.x, _specSmoothed[i]);
                // Color shifts from gold → magenta with intensity.
                bar.color = Color.Lerp(new Color(0.45f, 0.65f, 1f, 0.9f),
                                       new Color(1f, 0.4f, 0.6f, 0.95f),
                                       _specSmoothed[i]);
            }

            // Beat dot row: light up the active beat in the bar.
            if (_beatDots != null)
            {
                int bpb = (_clock != null && _clock.IsActive) ? Mathf.Max(1, _clock.BeatsPerBar) : 4;
                bpb = Mathf.Min(bpb, _beatDots.Length);
                int active = (_clock != null && _clock.IsActive) ? _clock.CurrentBeatInBar : -1;
                float spacing = 1f / Mathf.Max(1, bpb);
                for (int i = 0; i < _beatDots.Length; i++)
                {
                    var dot = _beatDots[i];
                    if (dot == null) continue;
                    bool used = i < bpb;
                    dot.gameObject.SetActive(used);
                    if (!used) continue;
                    var dRt = dot.rectTransform;
                    dRt.anchorMin = new Vector2((i + 0.5f) * spacing, 0.5f);
                    dRt.anchorMax = dRt.anchorMin;
                    if (i == active && playing)
                    {
                        bool downbeat = (i == 0);
                        dot.color = downbeat ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(1f, 1f, 1f, 1f);
                        float pulse = 1f + (1f - (_clock != null ? _clock.BeatPhase01 : 0f)) * 0.5f;
                        dRt.localScale = new Vector3(pulse, pulse, 1f);
                    }
                    else
                    {
                        dot.color = (i == 0) ? new Color(1f, 0.85f, 0.3f, 0.45f) : new Color(1f, 1f, 1f, 0.30f);
                        dRt.localScale = Vector3.one;
                    }
                }
            }

            // Full-song waveform: rebuild on track change, update playhead every frame.
            UpdateWaveform(playing);
        }
    }
}
