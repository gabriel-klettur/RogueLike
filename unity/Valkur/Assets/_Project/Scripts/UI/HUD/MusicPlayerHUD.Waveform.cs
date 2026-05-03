using UnityEngine;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        // ── Waveform overview (Ableton-style) ──────────────────────────────
        private void UpdateWaveform(bool playing)
        {
            if (_waveformImage == null || _audio == null) return;

            var clip = _audio.CurrentMusicClip;
            float bpm    = _audio.CurrentTrackBpm;
            float offset = _audio.CurrentTrackBeatOffsetSec;
            int   bpb    = Mathf.Max(1, _audio.CurrentTrackBeatsPerBar);

            bool needsRebuild = clip != null && (
                clip != _cachedWaveformClip ||
                !Mathf.Approximately(bpm,    _cachedWaveformBpm) ||
                !Mathf.Approximately(offset, _cachedWaveformOffset) ||
                bpb != _cachedWaveformBeatsPerBar);

            if (needsRebuild)
                RebuildWaveformTexture(clip, bpm, offset, bpb);

            // Move the playhead to the current playback position.
            if (clip != null && _waveformPlayhead != null)
            {
                float duration = clip.length;
                float t = _audio.CurrentMusicTime;
                float p = duration > 0f ? Mathf.Clamp01(t / duration) : 0f;
                _waveformPlayhead.anchorMin = new Vector2(p, 0f);
                _waveformPlayhead.anchorMax = new Vector2(p, 1f);
                _waveformPlayhead.gameObject.SetActive(true);

                // Progressive streaming-clip waveform: sample output, write column.
                if (playing && _waveformProgressive && _waveformWorkPixels != null && duration > 0f)
                    BakeProgressiveWaveformColumn(p);
            }
            else if (_waveformPlayhead != null)
            {
                _waveformPlayhead.gameObject.SetActive(false);
            }
        }

        private void BakeProgressiveWaveformColumn(float position01)
        {
            if (_waveformOutputBuf == null) _waveformOutputBuf = new float[256];
            if (!_audio.GetMusicOutputData(_waveformOutputBuf)) return;

            // Peak amplitude in this audio buffer.
            float peak = 0f;
            for (int i = 0; i < _waveformOutputBuf.Length; i++)
            {
                float a = _waveformOutputBuf[i];
                if (a < 0f) a = -a;
                if (a > peak) peak = a;
            }
            peak = Mathf.Clamp01(peak * 1.4f * _waveformAmplitude); // gentle gain + user zoom

            int col = Mathf.Clamp(Mathf.FloorToInt(position01 * WaveformTexW), 0, WaveformTexW - 1);
            if (_waveformColumnPeak == null) _waveformColumnPeak = new float[WaveformTexW];

            // Snap to the bar grid so the progressive output renders as discrete
            // bars (matching the bulk path style: 3 px bar + 1 px gap).
            const int BarW = 3;
            const int GapW = 1;
            int stride = BarW + GapW;
            int barStart = (col / stride) * stride;
            int barEnd   = Mathf.Min(barStart + BarW, WaveformTexW);

            // Accumulate (max) peak for this bar group.
            if (peak > _waveformColumnPeak[barStart]) _waveformColumnPeak[barStart] = peak;

            int midY = WaveformTexH / 2;
            float halfH = WaveformTexH * 0.45f;
            int span = Mathf.Max(1, (int)(_waveformColumnPeak[barStart] * halfH));
            var waveC = new Color32(220, 180, 90, 255);

            // Restore baseline + draw the symmetric peak across the whole bar width.
            int y0 = Mathf.Clamp(midY - span, 0, WaveformTexH - 1);
            int y1 = Mathf.Clamp(midY + span, 0, WaveformTexH - 1);
            for (int x = barStart; x < barEnd; x++)
            {
                for (int y = 0; y < WaveformTexH; y++)
                    _waveformWorkPixels[y * WaveformTexW + x] = _waveformGridPixels[y * WaveformTexW + x];
                for (int y = y0; y <= y1; y++)
                    _waveformWorkPixels[y * WaveformTexW + x] = waveC;
            }

            // Throttle GPU upload — apply once per ~33 ms or when column advances.
            _waveformDirtyTimer += Time.unscaledDeltaTime;
            if (col != _waveformLastCol || _waveformDirtyTimer >= 0.033f)
            {
                _waveformLastCol = col;
                _waveformDirtyTimer = 0f;
                _waveformTex.SetPixels32(_waveformWorkPixels);
                _waveformTex.Apply(false, false);
            }
        }

        private void RebuildWaveformTexture(AudioClip clip, float bpm, float offsetSec, int beatsPerBar)
        {
            if (clip == null) return;

            if (_waveformTex == null)
            {
                _waveformTex = new Texture2D(WaveformTexW, WaveformTexH, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }

            // Build the static baseline first: bg + center axis + bar/beat grid.
            var grid = new Color32[WaveformTexW * WaveformTexH];
            var bgC   = new Color32(8, 8, 16, 230);
            var axisC = new Color32(40, 40, 60, 200);
            for (int i = 0; i < grid.Length; i++) grid[i] = bgC;
            int midY = WaveformTexH / 2;
            for (int x = 0; x < WaveformTexW; x++) grid[midY * WaveformTexW + x] = axisC;

            float duration = clip.length;
            if (bpm > 0f && duration > 0f)
            {
                float spb = 60f / bpm;
                int beatCount = Mathf.Max(0, Mathf.FloorToInt((duration - offsetSec) / spb)) + 1;
                int safeBpb = Mathf.Max(1, beatsPerBar);
                // If beats are too dense (less than ~4 px apart), only draw downbeats.
                float pxPerBeat = WaveformTexW * (spb / duration);
                bool drawOffBeats = pxPerBeat >= 4f;
                var downbeatC = new Color32(255, 220, 80, 220);
                var beatC     = new Color32(140, 140, 180, 110);
                for (int b = 0; b < beatCount; b++)
                {
                    float ts = offsetSec + b * spb;
                    if (ts < 0f || ts > duration) continue;
                    int x = Mathf.Clamp((int)(ts / duration * WaveformTexW), 0, WaveformTexW - 1);
                    bool downbeat = (b % safeBpb) == 0;
                    if (!downbeat && !drawOffBeats) continue;
                    var c = downbeat ? downbeatC : beatC;
                    for (int y = 0; y < WaveformTexH; y++)
                    {
                        int i = y * WaveformTexW + x;
                        if (downbeat) grid[i] = c;
                        else
                        {
                            var prev = grid[i];
                            float ca = c.a / 255f;
                            byte r  = (byte)(prev.r * (1f - ca) + c.r * ca);
                            byte g2 = (byte)(prev.g * (1f - ca) + c.g * ca);
                            byte bl = (byte)(prev.b * (1f - ca) + c.b * ca);
                            grid[i] = new Color32(r, g2, bl, 255);
                        }
                    }
                }
            }

            _waveformGridPixels = grid;
            _waveformWorkPixels = new Color32[grid.Length];
            System.Array.Copy(grid, _waveformWorkPixels, grid.Length);

            if (_waveformColumnPeak == null || _waveformColumnPeak.Length != WaveformTexW)
                _waveformColumnPeak = new float[WaveformTexW];
            else
                System.Array.Clear(_waveformColumnPeak, 0, _waveformColumnPeak.Length);
            _waveformLastCol = -1;
            _waveformDirtyTimer = 0f;

            // Streaming clips can't use GetData → fall back to progressive sampling.
            // CompressedInMemory + DecompressOnLoad both support GetData.
            _waveformProgressive = clip.loadType == AudioClipLoadType.Streaming;

            if (!_waveformProgressive)
            {
                int channels = Mathf.Max(1, clip.channels);
                int totalPerChannel = clip.samples;
                float[] data = null;
                if (totalPerChannel > 0)
                {
                    try
                    {
                        data = new float[totalPerChannel * channels];
                        if (!clip.GetData(data, 0)) data = null;
                    }
                    catch
                    {
                        data = null;
                    }
                }

                if (data != null)
                {
                    var waveC = new Color32(220, 180, 90, 255);
                    float halfH = WaveformTexH * 0.45f;
                    float amp = _waveformAmplitude;
                    // Bar-style rendering (like the spectrum below): paint groups of
                    // BarW columns and skip GapW between them, so the waveform reads
                    // as discrete bars instead of a solid silhouette.
                    const int BarW = 3;
                    const int GapW = 1;
                    int stride = BarW + GapW;
                    for (int gx = 0; gx < WaveformTexW; gx += stride)
                    {
                        // Aggregate peak across the bar's pixel-column span.
                        long lo = (long)gx * totalPerChannel / WaveformTexW;
                        long hi = (long)Mathf.Min(gx + BarW, WaveformTexW) * totalPerChannel / WaveformTexW;
                        if (hi <= lo) hi = lo + 1;
                        float vMin = 0f, vMax = 0f;
                        for (long s = lo; s < hi; s++)
                        {
                            int idx = (int)s * channels;
                            float v = data[idx];
                            if (v < vMin) vMin = v;
                            if (v > vMax) vMax = v;
                        }
                        vMin = Mathf.Clamp(vMin * amp, -1f, 1f);
                        vMax = Mathf.Clamp(vMax * amp, -1f, 1f);
                        int y0 = Mathf.Clamp(midY + (int)(vMin * halfH), 0, WaveformTexH - 1);
                        int y1 = Mathf.Clamp(midY + (int)(vMax * halfH), 0, WaveformTexH - 1);
                        int xEnd = Mathf.Min(gx + BarW, WaveformTexW);
                        for (int x = gx; x < xEnd; x++)
                            for (int y = y0; y <= y1; y++)
                                _waveformWorkPixels[y * WaveformTexW + x] = waveC;
                    }
                }
            }

            _waveformTex.SetPixels32(_waveformWorkPixels);
            _waveformTex.Apply(false, false);
            if (_waveformSprite == null)
                _waveformSprite = Sprite.Create(_waveformTex, new Rect(0, 0, WaveformTexW, WaveformTexH),
                                                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _waveformImage.sprite = _waveformSprite;
            _waveformImage.SetMaterialDirty();

            _cachedWaveformClip        = clip;
            _cachedWaveformBpm         = bpm;
            _cachedWaveformOffset      = offsetSec;
            _cachedWaveformBeatsPerBar = beatsPerBar;
        }
    }
}
