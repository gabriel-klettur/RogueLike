using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Infrastructure;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Renders the Boss Editor timeline strip as a procedural <see cref="Texture2D"/>
    /// painted onto a <see cref="RawImage"/> component.
    ///
    /// Lifecycle: created by <see cref="BossEditorManager.Timeline"/> and driven
    /// from its own Update(). Deactivated when the editor closes.
    ///
    /// Painting budget: pixels only change when the chart or beat position changes —
    /// the texture is marked dirty only on beat advancement or explicit
    /// <see cref="SetChart"/> calls, keeping per-frame cost to a single
    /// Texture2D.Apply() call (< 0.1 ms on a 512 × 24 texture).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class BossEditorTimelineRenderer : MonoBehaviour
    {
        // ── Configuration ──────────────────────────────────────────────────────

        private const int   TEX_W      = 512;
        private const int   TEX_H      = 24;
        private const float BAR_ALPHA  = 0.80f;
        private const float BEAT_ALPHA = 0.45f;

        // Cue colours by type (index = (int)BossCueType).
        private static readonly Color32[] CUE_COLORS =
        {
            new Color32(255, 100,  40, 220),   // CastSpell  — orange
            new Color32( 80, 200, 255, 220),   // PlaySfx    — cyan
            new Color32(200,  80, 255, 220),   // SwitchPhase — purple
            new Color32( 80, 255, 120, 220),   // SpawnAdd   — green
            new Color32(255, 220,  60, 220),   // Taunt      — yellow
            new Color32(180, 180, 180, 220),   // PlayAnim   — grey
        };

        // ── State ──────────────────────────────────────────────────────────────

        private RawImage  _image;
        private Texture2D _tex;
        private Color32[] _pixels;

        private BossChart _chart;
        private int       _beatsPerBar = 4;
        private float     _lastPlayhead = -1f;
        private bool      _dirty;

        // ── Public API ─────────────────────────────────────────────────────────

        public void SetChart(BossChart chart, int beatsPerBar)
        {
            _chart      = chart;
            _beatsPerBar = Mathf.Max(1, beatsPerBar);
            _dirty      = true;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            _image  = GetComponent<RawImage>();
            _tex    = new Texture2D(TEX_W, TEX_H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp,
            };
            _pixels = new Color32[TEX_W * TEX_H];
            _image.texture = _tex;
            _dirty = true;
        }

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }

        private void Update()
        {
            var clock = MusicBeatClock.Instance;
            float playhead = -1f;

            if (clock != null && clock.IsActive && _chart != null)
            {
                int barsPerLoop  = Mathf.Max(1, _chart.barsPerLoop);
                int totalBeats   = barsPerLoop * _beatsPerBar;
                int beatInLoop   = clock.CurrentBeat % Mathf.Max(1, totalBeats);
                playhead = (beatInLoop + clock.BeatPhase01) / totalBeats;
            }

            bool playheadMoved = Mathf.Abs(playhead - _lastPlayhead) > 0.001f;

            if (_dirty || playheadMoved)
            {
                _lastPlayhead = playhead;
                Repaint(playhead);
                _dirty = false;
            }
        }

        // ── Painting ───────────────────────────────────────────────────────────

        private void Repaint(float playhead)
        {
            // Background.
            var bg = new Color32(18, 18, 20, 230);
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = bg;

            if (_chart != null)
            {
                int barsPerLoop = Mathf.Max(1, _chart.barsPerLoop);
                int totalBeats  = barsPerLoop * _beatsPerBar;

                // Beat grid lines.
                for (int b = 0; b < totalBeats; b++)
                {
                    float t  = (float)b / totalBeats;
                    int   px = Mathf.RoundToInt(t * (TEX_W - 1));
                    bool  isBar = (b % _beatsPerBar) == 0;
                    byte  alpha = isBar
                        ? (byte)(BAR_ALPHA  * 255)
                        : (byte)(BEAT_ALPHA * 255);
                    var lineCol = isBar
                        ? new Color32(180, 180, 180, alpha)
                        : new Color32(80,  80,  80,  alpha);
                    PaintColumn(px, lineCol);
                }

                // Cue markers.
                if (_chart.cues != null)
                {
                    foreach (var cue in _chart.cues)
                    {
                        float cueBeats = cue.TotalBeats(_beatsPerBar);
                        int   loopTotal = barsPerLoop * _beatsPerBar;
                        // Fold to loop window.
                        float frac = (loopTotal > 0) ? (cueBeats % loopTotal) / loopTotal : 0f;
                        frac = Mathf.Clamp01(frac);
                        int px = Mathf.RoundToInt(frac * (TEX_W - 1));
                        int typeIdx = Mathf.Clamp((int)cue.type, 0, CUE_COLORS.Length - 1);
                        PaintRect(Mathf.Max(0, px - 2), Mathf.Min(TEX_W - 1, px + 2), CUE_COLORS[typeIdx]);
                    }
                }
            }

            // Playhead.
            if (playhead >= 0f)
            {
                int px = Mathf.RoundToInt(Mathf.Clamp01(playhead) * (TEX_W - 1));
                PaintColumn(px, new Color32(255, 255, 255, 200));
                // 1-pixel flanks for visibility.
                if (px > 0)         PaintColumn(px - 1, new Color32(255, 255, 255, 80));
                if (px < TEX_W - 1) PaintColumn(px + 1, new Color32(255, 255, 255, 80));
            }

            _tex.SetPixels32(_pixels);
            _tex.Apply(false);
        }

        private void PaintColumn(int x, Color32 col)
        {
            if (x < 0 || x >= TEX_W) return;
            for (int y = 0; y < TEX_H; y++)
                _pixels[y * TEX_W + x] = col;
        }

        private void PaintRect(int x0, int x1, Color32 col)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || x >= TEX_W) continue;
                // Leave a 2-px margin top/bottom so it reads as a marker not a grid line.
                for (int y = 2; y < TEX_H - 2; y++)
                    _pixels[y * TEX_W + x] = col;
            }
        }
    }
}
