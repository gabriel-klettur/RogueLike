using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The inscribed maps: floating glyphs, the seal burnt along the floor, and the ticked
    /// disc that turns under each anchor post.
    ///
    /// <para>These are what separate a magic barrier from a coloured pane of glass. A membrane
    /// alone is a surface; a membrane with WRITING on it is a surface somebody made on
    /// purpose, and that reading is most of what the word "arcane" is doing in the spell name.
    /// They are also the layer that keeps the effect being LOOKED at: a glyph igniting and
    /// going out is a discrete event, and continuous motion alone stops being read after about
    /// a second (the same argument that put crawling discharges on the vortex funnel).</para>
    /// </summary>
    internal static partial class ArcaneSprites
    {
        /// <summary>
        /// One glyph, drawn as strokes inside a ring.
        ///
        /// <para>Generated rather than authored, and mirrored about the vertical axis, because
        /// a glyph has to read as DELIBERATE at 32 screen pixels. Random strokes read as noise;
        /// symmetric strokes read as a character in an alphabet nobody has been taught, which
        /// is the thing being aimed at. The ring is what stops the strokes dissolving into the
        /// membrane behind them.</para>
        /// </summary>
        private static Sprite BuildRune(int variant)
        {
            var tex = NewTexture(RunePx, RunePx);
            var px = new Color[RunePx * RunePx];
            var rng = new System.Random(2718 + variant * 1301);

            int strokeCount = 3 + rng.Next(3);
            var strokes = new Vector2[strokeCount][];
            for (int s = 0; s < strokeCount; s++)
            {
                // Endpoints snap to a coarse lattice: a stroke that starts and stops on a grid
                // node looks written, one that stops anywhere looks scribbled.
                strokes[s] = new[] { LatticePoint(rng), LatticePoint(rng) };
            }

            const float ringRadius = 0.80f;
            const float ringHalf = 0.055f;

            for (int y = 0; y < RunePx; y++)
            {
                float ny = (y + 0.5f) / RunePx * 2f - 1f;
                for (int x = 0; x < RunePx; x++)
                {
                    float nx = (x + 0.5f) / RunePx * 2f - 1f;

                    // Mirrored: sample the strokes at |x|, so both halves carry the same marks.
                    var p = new Vector2(Mathf.Abs(nx), ny);
                    float nearest = float.MaxValue;
                    for (int s = 0; s < strokeCount; s++)
                        nearest = Mathf.Min(nearest, DistanceToSegment(p, strokes[s][0], strokes[s][1]));

                    float ink = Mathf.Clamp01(1f - nearest / 0.075f);
                    ink = ink * ink;

                    float ring = Mathf.Clamp01(
                        1f - Mathf.Abs(new Vector2(nx, ny).magnitude - ringRadius) / ringHalf);
                    ring = ring * ring * 0.72f;

                    px[y * RunePx + x] = Lum(ink + ring);
                }
            }

            return Finish(tex, px, new Vector2(0.5f, 0.5f), RunePx);
        }

        private static Vector2 LatticePoint(System.Random rng)
            => new Vector2(rng.Next(0, 3) * 0.28f, (rng.Next(0, 5) - 2) * 0.28f);

        /// <summary>
        /// The seal along the floor: a dark inscribed band with a hot centre line and ticks
        /// crossing it at intervals.
        ///
        /// <para>This is the barrier's ONE non-additive layer and it exists for the reason the
        /// vortex keeps opaque ground debris and the ki aura keeps its scorch: every other
        /// piece of this effect is light being added to the scene, and light alone says the
        /// ground is LIT. A mark on the floor says the ground has been ALTERED. Folding it into
        /// the additive stack as a tidy-up would delete the statement with nothing failing.</para>
        /// </summary>
        private static Sprite BuildSeal(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float ny = (y + 0.5f) / h * 2f - 1f;
                float band = Mathf.Exp(-Mathf.Pow(ny / 0.62f, 2f));
                float spine = Mathf.Exp(-Mathf.Pow(ny / 0.11f, 2f));

                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w;
                    float ends = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.10f))
                               * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - nx) / 0.10f));

                    // Ticks: short marks crossing the spine, ten along the band.
                    float phase = Mathf.Repeat(nx * 10f, 1f);
                    float tick = Mathf.Clamp01(1f - Mathf.Abs(phase - 0.5f) / 0.045f)
                               * Mathf.Exp(-Mathf.Pow(ny / 0.48f, 2f));

                    px[y * w + x] = Lum((band * 0.34f + spine * 0.85f + tick * 0.55f) * ends);
                }
            }

            return Finish(tex, px, new Vector2(0.5f, 0.5f), w / 2f);
        }

        /// <summary>
        /// The disc that turns under an anchor post: two concentric rings with radial ticks
        /// between them. Slow rotation on this is the cheapest possible "something is holding
        /// this up" signal, and it sits on the floor where nothing else competes with it.
        /// </summary>
        private static Sprite BuildSigil(int size)
        {
            var tex = NewTexture(size, size);
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float r = new Vector2(nx, ny).magnitude;
                    if (r > 1f) { px[y * size + x] = Color.clear; continue; }

                    float outer = Mathf.Clamp01(1f - Mathf.Abs(r - 0.92f) / 0.05f);
                    float inner = Mathf.Clamp01(1f - Mathf.Abs(r - 0.54f) / 0.04f);

                    float angle = Mathf.Atan2(ny, nx) / (Mathf.PI * 2f) + 0.5f;
                    float phase = Mathf.Repeat(angle * 12f, 1f);
                    float tick = Mathf.Clamp01(1f - Mathf.Abs(phase - 0.5f) / 0.16f)
                               * Mathf.Clamp01(1f - Mathf.Abs(r - 0.73f) / 0.19f);

                    px[y * size + x] = Lum(outer * outer * 0.9f + inner * inner * 0.7f + tick * 0.42f);
                }
            }

            return Finish(tex, px, new Vector2(0.5f, 0.5f), size);
        }
    }
}
