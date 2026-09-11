using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The resonance, drawn as quads by ONE graphic: the spectrum as columns of pixel blocks
    /// with a slow-falling peak over each, and under it the whole song's loudness — baked
    /// offline, so it is complete from the first frame, costs nothing to compute, and does not
    /// go flat when the player mutes the music — with a phrase mark every eight bars and a gold
    /// playhead.
    ///
    /// <para><b>One graphic, not 64 Images.</b> The old analyser moved 64 <c>Image</c> anchors
    /// every frame, which dirtied the whole music canvas every frame. Here the mesh is rebuilt
    /// only when the owner hands in new levels (~30 Hz) and only while the resonance is open.</para>
    ///
    /// <para><b>Everything lands on whole texels.</b> A bar is <c>round(level * blocks)</c>
    /// blocks tall, never a fraction of one; the envelope column is a whole number of texels.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MusicResonanceGraphic : MaskableGraphic
    {
        /// <summary>Rows of the envelope strip.</summary>
        public const int EnvelopeRows = 8;

        /// <summary>Texels from the graphic's bottom to its lowest band block.</summary>
        public const int BandsBaseY = 2 + EnvelopeRows + 3;

        private MusicHudArt _art;
        private MusicHudStyle _style;
        private float[] _levels = System.Array.Empty<float>();
        private float[] _peaks = System.Array.Empty<float>();
        private float[] _envelope = System.Array.Empty<float>();
        private float[] _phrases = System.Array.Empty<float>();
        private float _progress;
        private int _bands, _blocks;

        public override Texture mainTexture => _art != null && _art.Atlas != null ? _art.Atlas : s_WhiteTexture;

        /// <summary>Blocks lit in a band right now. For the tests.</summary>
        public int BlocksIn(int band) =>
            band >= 0 && band < _bands ? Mathf.RoundToInt(Mathf.Clamp01(_levels[band]) * _blocks) : 0;

        /// <summary>The band with the most blocks lit, or -1. Where a bar's mote rises from.</summary>
        public int TallestBand
        {
            get
            {
                int best = -1, bestBlocks = 0;
                for (int b = 0; b < _bands; b++)
                {
                    int n = BlocksIn(b);
                    if (n > bestBlocks) { bestBlocks = n; best = b; }
                }
                return best;
            }
        }

        public int PhraseMarks => _phrases.Length;

        public static MusicResonanceGraphic Create(Transform parent, MusicHudArt art, MusicHudStyle style,
                                                   int x, int y, int w, int h)
        {
            var go = new GameObject("Resonance", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            HudRect.Place((RectTransform)go.transform, x, y, w, h);
            var g = go.AddComponent<MusicResonanceGraphic>();
            g._art = art;
            g._style = style;
            g._bands = Mathf.Max(1, style.bandCount);
            g._blocks = Mathf.Max(1, style.bandBlocks);
            g._levels = new float[g._bands];
            g._peaks = new float[g._bands];
            g.raycastTarget = false;
            return g;
        }

        /// <summary>Hands in this frame's band levels (0..1) and advances the peak markers.</summary>
        public void SetLevels(float[] levels, float dt)
        {
            if (levels == null) return;
            int n = Mathf.Min(levels.Length, _bands);
            for (int b = 0; b < n; b++)
            {
                _levels[b] = levels[b];
                _peaks[b] = Mathf.Max(levels[b], _peaks[b] - dt * 0.35f);
            }
            SetVerticesDirty();
        }

        /// <summary>The song's baked loudness, 0..1 per slice, and its phrase marks (0..1).</summary>
        public void SetSong(float[] envelope, float[] phraseMarks)
        {
            _envelope = Stretch(envelope);
            _phrases = phraseMarks ?? System.Array.Empty<float>();
            SetVerticesDirty();
        }

        /// <summary>
        /// Stretches a track's envelope between its own quietest and loudest slice. The bake is
        /// normalised against the loud passages, so most of a song sits in the top third of the
        /// range and eight rows of it drew as a flat plateau; the SHAPE is what the strip is for.
        /// </summary>
        private static float[] Stretch(float[] envelope)
        {
            if (envelope == null || envelope.Length == 0) return System.Array.Empty<float>();
            float min = 1f, max = 0f;
            foreach (var v in envelope) { if (v < min) min = v; if (v > max) max = v; }
            float span = Mathf.Max(0.05f, max - min);
            var result = new float[envelope.Length];
            for (int i = 0; i < envelope.Length; i++) result[i] = Mathf.Clamp01((envelope[i] - min) / span);
            return result;
        }

        public void SetProgress(float progress01)
        {
            progress01 = Mathf.Clamp01(progress01);
            int before = PlayheadColumn(_progress), after = PlayheadColumn(progress01);
            _progress = progress01;
            if (before != after) SetVerticesDirty();
        }

        private int EnvelopeWidth => Mathf.RoundToInt(rectTransform.rect.width) - 4;
        private int PlayheadColumn(float p) => Mathf.RoundToInt(p * Mathf.Max(1, EnvelopeWidth - 1));

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_art == null || _style == null || _art.White == null) return;
            var r = GetPixelAdjustedRect();
            var tex = _art.Atlas;
            var t = _art.White.textureRect;
            // Sample the centre of the white piece so the quads never bleed into a neighbour.
            var uv = new Vector2((t.x + t.width * 0.5f) / tex.width, (t.y + t.height * 0.5f) / tex.height);
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            float x0 = Mathf.Floor(r.xMin), y0 = Mathf.Floor(r.yMin);

            // Envelope strip along the bottom, 2 texels in from each side.
            int ex = 2, ey = 2, ew = w - 4;
            int head = PlayheadColumn(_progress);
            if (_envelope.Length > 0 && ew > 0)
            {
                for (int c = 0; c < ew; c++)
                {
                    float v = _envelope[Mathf.Clamp(c * _envelope.Length / ew, 0, _envelope.Length - 1)];
                    int rows = Mathf.Clamp(Mathf.RoundToInt(v * EnvelopeRows), 1, EnvelopeRows);
                    var col = c <= head ? _style.envelopePlayed : _style.envelopeAhead;
                    Quad(vh, x0 + ex + c, y0 + ey, 1, rows, col, uv);
                }
            }
            // Phrase marks: a dim notch over the strip every eight bars.
            var mark = _style.bandShadow;
            for (int i = 0; i < _phrases.Length; i++)
            {
                int c = Mathf.RoundToInt(_phrases[i] * Mathf.Max(1, ew - 1));
                Quad(vh, x0 + ex + c, y0 + ey + EnvelopeRows + 1, 1, 1, mark, uv);
            }
            // The playhead, gold, a texel taller than the strip on both ends.
            var gold = PlayerHudStyle.Active.gold;
            Quad(vh, x0 + ex + head, y0 + ey - 1, 1, EnvelopeRows + 3, gold, uv);

            // Spectrum above: bands of 3 texels with a 1-texel gap, centred.
            int bandsW = _bands * 4 - 1;
            int bx = Mathf.Max(0, (w - bandsW) / 2);
            int by = BandsBaseY;
            for (int b = 0; b < _bands; b++)
            {
                int blocks = BlocksIn(b);
                float px = x0 + bx + b * 4;
                for (int k = 0; k < blocks; k++)
                {
                    var col = k == blocks - 1 ? _style.bandLight : (k < 2 ? _style.bandShadow : _style.bandBody);
                    Quad(vh, px, y0 + by + k * 2, 3, 1, col, uv);
                }
                int peak = Mathf.RoundToInt(Mathf.Clamp01(_peaks[b]) * _blocks);
                if (peak > blocks && peak > 0)
                    Quad(vh, px, y0 + by + (peak - 1) * 2, 3, 1, _style.bandPeak, uv);
            }
        }

        private static void Quad(VertexHelper vh, float x, float y, float w, float h, Color c, Vector2 uv)
        {
            Color32 c32 = c;
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), c32, uv);
            vh.AddVert(new Vector3(x, y + h), c32, uv);
            vh.AddVert(new Vector3(x + w, y + h), c32, uv);
            vh.AddVert(new Vector3(x + w, y), c32, uv);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
