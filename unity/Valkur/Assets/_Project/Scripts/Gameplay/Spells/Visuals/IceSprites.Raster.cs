using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>Texture generation for <see cref="IceSprites"/>. One builder per map.</summary>
    internal static partial class IceSprites
    {
        // The three stops of the vertical gradient every ice surface is painted with.
        // Deep and saturated where the crystal is thick and shadowed, near-white where it
        // thins out to a point. A single flat blue reads as plastic.
        private static readonly Color IceDeep = new Color(0.20f, 0.44f, 0.82f);
        private static readonly Color IceMid = new Color(0.50f, 0.79f, 0.99f);
        private static readonly Color IceTip = new Color(0.90f, 0.99f, 1.00f);

        /// <summary>
        /// The crystal itself. Colour is baked (see the class doc) and so is the internal
        /// shading: the core is darker and more translucent than the edges, which is the
        /// opposite of how a soft glow behaves and is precisely what makes a surface read as
        /// hard and refractive rather than as a light.
        /// </summary>
        private static Sprite BuildBody(ShardShape shape)
        {
            var tex = NewTexture(ShardW, ShardH);
            var px = new Color[ShardW * ShardH];
            float edgeSoft = 1.4f / ShardW;

            for (int y = 0; y < ShardH; y++)
            {
                float v = (y + 0.5f) / ShardH;
                for (int x = 0; x < ShardW; x++)
                {
                    float u = (x + 0.5f) / ShardW - 0.5f;
                    float signed = shape.Inside(y, u, out float hw, out float cx);
                    float cover = Mathf.Clamp01(signed / edgeSoft);
                    if (cover <= 0f || hw <= 1e-5f) { px[y * ShardW + x] = Color.clear; continue; }

                    float toEdge = Mathf.Clamp01(1f - signed / hw);   // 0 at the core, 1 at the edge

                    Color c = Color.Lerp(IceDeep, IceMid, Mathf.Pow(v, 0.75f));
                    c = Color.Lerp(c, IceTip, Mathf.Pow(v, 2.2f) * 0.85f);
                    c = Color.Lerp(c * 0.82f, c * 1.22f, Mathf.Pow(toEdge, 2.0f));

                    // Specular streak down one facet.
                    float streak = Gaussian((u - cx) - hw * shape.StreakOffset, hw * 0.24f);
                    c += new Color(0.30f, 0.38f, 0.44f, 0f) * streak * (0.30f + 0.55f * v);

                    // Two internal fractures, fading out before the tip.
                    float fracture = Mathf.Max(
                        Gaussian((u - cx) - hw * shape.FractureA, hw * 0.07f),
                        Gaussian((u - cx) - hw * shape.FractureB, hw * 0.06f)) * Mathf.Clamp01(1f - v * 0.85f);
                    c *= Mathf.Lerp(1f, 0.70f, fracture);

                    float alpha = cover * Mathf.Lerp(0.74f, 0.98f, Mathf.Pow(toEdge, 1.4f));
                    px[y * ShardW + x] = new Color(c.r, c.g, c.b, alpha);
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0f), ShardW);
        }

        /// <summary>
        /// A thin band hugging the inside of the outline. Drawn on the additive material it
        /// is the cold rim light that separates the wall from whatever is behind it — the
        /// single cheapest thing that stops a flat sprite reading as a sticker.
        /// </summary>
        private static Sprite BuildRim(ShardShape shape)
        {
            var tex = NewTexture(ShardW, ShardH);
            var px = new Color[ShardW * ShardH];

            for (int y = 0; y < ShardH; y++)
            {
                float v = (y + 0.5f) / ShardH;
                for (int x = 0; x < ShardW; x++)
                {
                    float u = (x + 0.5f) / ShardW - 0.5f;
                    float signed = shape.Inside(y, u, out float hw, out _);
                    if (signed <= 0f || hw <= 1e-5f) { px[y * ShardW + x] = Color.clear; continue; }

                    float band = Mathf.Clamp01(1f - signed / Mathf.Max(0.004f, hw * 0.22f));
                    // Brighter towards the tip: the thin end of a crystal catches more light.
                    float a = Mathf.Pow(band, 1.6f) * Mathf.Lerp(0.55f, 1f, Mathf.Pow(v, 0.8f));
                    px[y * ShardW + x] = new Color(0.78f, 0.95f, 1f, a);
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0f), ShardW);
        }

        /// <summary>The moving highlight: a soft wedge the animation sweeps brightness across.</summary>
        private static Sprite BuildFacet(ShardShape shape)
        {
            var tex = NewTexture(ShardW, ShardH);
            var px = new Color[ShardW * ShardH];

            for (int y = 0; y < ShardH; y++)
            {
                float v = (y + 0.5f) / ShardH;
                for (int x = 0; x < ShardW; x++)
                {
                    float u = (x + 0.5f) / ShardW - 0.5f;
                    float signed = shape.Inside(y, u, out float hw, out float cx);
                    if (signed <= 0f || hw <= 1e-5f) { px[y * ShardW + x] = Color.clear; continue; }

                    float wedge = Gaussian((u - cx) - hw * shape.StreakOffset, hw * 0.34f);
                    float a = wedge * Mathf.Pow(v, 0.45f) * Mathf.Clamp01(signed / (hw * 0.5f));
                    px[y * ShardW + x] = new Color(0.85f, 0.97f, 1f, a * 0.9f);
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0f), ShardW);
        }

        /// <summary>
        /// Fracture lines, held at alpha 0 until the wall is struck. Clipped to the
        /// silhouette so a crack can never appear in the air beside the crystal.
        /// </summary>
        private static Sprite BuildCrack(ShardShape shape, int variant)
        {
            var tex = NewTexture(ShardW, ShardH);
            var px = new Color[ShardW * ShardH];

            var segments = BuildCrackSegments(new System.Random(0x51CE + variant * 104729));

            for (int y = 0; y < ShardH; y++)
            {
                float v = (y + 0.5f) / ShardH;
                for (int x = 0; x < ShardW; x++)
                {
                    float u = (x + 0.5f) / ShardW - 0.5f;
                    float signed = shape.Inside(y, u, out float hw, out float cx);
                    if (signed <= 0f || hw <= 1e-5f) { px[y * ShardW + x] = Color.clear; continue; }

                    // Crack coordinates are normalised INSIDE the crystal, so the same
                    // polyline fits a wide shard and a narrow one without leaving it.
                    var p = new Vector2((u - cx) / Mathf.Max(1e-4f, hw), v);
                    float best = 10f;
                    for (int i = 0; i < segments.Length; i += 2)
                        best = Mathf.Min(best, DistanceToSegment(p, segments[i], segments[i + 1]));

                    float core = Mathf.Clamp01(1f - best / 0.075f);
                    float glow = Mathf.Clamp01(1f - best / 0.28f) * 0.35f;
                    float a = Mathf.Clamp01(Mathf.Pow(core, 1.3f) + glow) * Mathf.Clamp01(signed / (hw * 0.25f));
                    px[y * ShardW + x] = new Color(0.92f, 0.99f, 1f, a);
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0f), ShardW);
        }

        /// <summary>Three jagged polylines in normalised crystal space, as segment pairs.</summary>
        private static Vector2[] BuildCrackSegments(System.Random rng)
        {
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var pts = new System.Collections.Generic.List<Vector2>();

            for (int branch = 0; branch < 3; branch++)
            {
                var cur = new Vector2(Range(-0.7f, 0.7f), Range(0.06f, 0.35f));
                var dir = new Vector2(Range(-0.5f, 0.5f), Range(0.5f, 1f)).normalized;
                int steps = 3 + branch;
                for (int s = 0; s < steps; s++)
                {
                    var next = cur + dir * Range(0.14f, 0.30f);
                    next.x = Mathf.Clamp(next.x, -0.92f, 0.92f);
                    next.y = Mathf.Clamp(next.y, 0f, 0.98f);
                    pts.Add(cur);
                    pts.Add(next);
                    cur = next;
                    dir = (dir + new Vector2(Range(-0.7f, 0.7f), Range(-0.25f, 0.35f))).normalized;
                }
            }
            return pts.ToArray();
        }

        private static float Gaussian(float d, float sigma)
        {
            if (sigma <= 1e-5f) return 0f;
            float k = d / sigma;
            return Mathf.Exp(-k * k);
        }
    }
}
