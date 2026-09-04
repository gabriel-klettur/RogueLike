using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The structural maps: membrane panels, their fractures, the anchor posts, the lattice
    /// edge and the chips a failing panel throws. One builder per map.
    /// </summary>
    internal static partial class ArcaneSprites
    {
        /// <summary>
        /// One hexagonal cell of the membrane.
        ///
        /// <para>The interior is deliberately FAINT and the rim is bright: what makes a plane
        /// read as a barrier rather than as fog is that it has cells with EDGES, and on an
        /// additive surface a filled hexagon at any useful brightness is a solid slab. The
        /// interior carries a slow interference ripple instead, visible only where dozens of
        /// panels overlap, which is exactly where a woven surface should look busiest.</para>
        /// </summary>
        private static Sprite BuildPanel(int variant)
        {
            var tex = NewTexture(PanelPx, PanelPx);
            var px = new Color[PanelPx * PanelPx];
            var rng = new System.Random(9001 + variant * 613);

            float radius = 0.86f - variant * 0.03f;
            float rimHalf = 0.055f + (float)rng.NextDouble() * 0.02f;
            float ripplePhase = (float)rng.NextDouble() * Mathf.PI * 2f;
            float aa = 2.2f / PanelPx;

            for (int y = 0; y < PanelPx; y++)
            {
                float ny = (y + 0.5f) / PanelPx * 2f - 1f;
                for (int x = 0; x < PanelPx; x++)
                {
                    float nx = (x + 0.5f) / PanelPx * 2f - 1f;
                    float d = HexDistance(new Vector2(nx, ny), radius);

                    if (d > aa) { px[y * PanelPx + x] = Color.clear; continue; }

                    // The rim is a band centred ON the boundary, so it reads as an edge with a
                    // thickness rather than as an inner border shrinking the cell.
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(d) / rimHalf);
                    rim = rim * rim * (3f - 2f * rim);

                    float inside = Mathf.Clamp01(-d / aa);
                    float ripple = 0.5f + 0.5f * Mathf.Sin((nx * 5.4f + ny * 3.1f) * Mathf.PI + ripplePhase);
                    float fill = inside * (0.055f + 0.075f * ripple);

                    px[y * PanelPx + x] = Lum(fill + rim * 0.95f);
                }
            }

            return Finish(tex, px, new Vector2(0.5f, 0.5f), PanelPx);
        }

        /// <summary>
        /// Fracture lines for the matching panel: a spine crossing the cell with two branches
        /// off it, clipped to the hexagon so a broken panel never paints outside its own edge.
        /// </summary>
        private static Sprite BuildFracture(int variant)
        {
            var tex = NewTexture(PanelPx, PanelPx);
            var px = new Color[PanelPx * PanelPx];
            var rng = new System.Random(4400 + variant * 977);

            var segments = new Vector2[4][];
            Vector2 a = OnRim(rng, 0.78f);
            Vector2 b = OnRim(rng, 0.78f);
            Vector2 mid = Vector2.Lerp(a, b, 0.35f + (float)rng.NextDouble() * 0.3f);
            segments[0] = new[] { a, mid };
            segments[1] = new[] { mid, b };
            segments[2] = new[] { mid, OnRim(rng, 0.72f) };
            segments[3] = new[] { Vector2.Lerp(a, mid, 0.6f), OnRim(rng, 0.66f) };

            for (int y = 0; y < PanelPx; y++)
            {
                float ny = (y + 0.5f) / PanelPx * 2f - 1f;
                for (int x = 0; x < PanelPx; x++)
                {
                    float nx = (x + 0.5f) / PanelPx * 2f - 1f;
                    var p = new Vector2(nx, ny);
                    if (HexDistance(p, 0.86f) > 0f) { px[y * PanelPx + x] = Color.clear; continue; }

                    float nearest = float.MaxValue;
                    for (int s = 0; s < segments.Length; s++)
                        nearest = Mathf.Min(nearest, DistanceToSegment(p, segments[s][0], segments[s][1]));

                    // Thin and hot: a fracture is a tear letting the raw weave through, so it
                    // is brighter than the surface it splits, not darker.
                    float line = Mathf.Clamp01(1f - nearest / 0.055f);
                    px[y * PanelPx + x] = Lum(line * line * 0.95f + Mathf.Clamp01(1f - nearest / 0.18f) * 0.16f);
                }
            }

            return Finish(tex, px, new Vector2(0.5f, 0.5f), PanelPx);
        }

        private static Vector2 OnRim(System.Random rng, float radius)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>
        /// An anchor post: a hot vertical core with an exponential falloff either side, a
        /// bloom where it meets the floor, and a taper OUT at the top.
        ///
        /// <para>The taper matters. A post with a flat top is a pillar, which is a BUILT thing.
        /// A post that thins into nothing is force being held in place, which is what a ward
        /// is, and it is also what leaves the lattice edge free to read as the real top of the
        /// barrier.</para>
        /// </summary>
        private static Sprite BuildPost()
        {
            var tex = NewTexture(PostW, PostH);
            var px = new Color[PostW * PostH];

            for (int y = 0; y < PostH; y++)
            {
                float t = (y + 0.5f) / PostH;                       // 0 at the floor
                float taper = Mathf.Pow(Mathf.Clamp01(1f - t), 0.55f);
                float footBloom = Mathf.Exp(-Mathf.Pow(t / 0.10f, 2f));
                float width = Mathf.Lerp(0.16f, 0.05f, t) + footBloom * 0.22f;

                for (int x = 0; x < PostW; x++)
                {
                    float nx = (x + 0.5f) / PostW * 2f - 1f;
                    float core = Mathf.Exp(-Mathf.Pow(nx / width, 2f));
                    float halo = Mathf.Exp(-Mathf.Pow(nx / (width * 3.4f), 2f)) * 0.30f;
                    px[y * PostW + x] = Lum((core + halo) * taper * (0.72f + 0.45f * footBloom));
                }
            }

            return Finish(tex, px, new Vector2(0.5f, 0f), PostH / PostUnitHeight);
        }

        /// <summary>
        /// The lattice contour: a thin bright line whose ends fade out, so an edge laid along
        /// the barrier enters and leaves rather than stopping dead in mid-air.
        /// </summary>
        private static Sprite BuildEdge()
        {
            var tex = NewTexture(EdgeW, EdgeH);
            var px = new Color[EdgeW * EdgeH];

            for (int y = 0; y < EdgeH; y++)
            {
                float ny = (y + 0.5f) / EdgeH * 2f - 1f;
                float across = Mathf.Exp(-Mathf.Pow(ny / 0.34f, 2f));
                float bleed = Mathf.Exp(-Mathf.Pow(ny / 0.95f, 2f)) * 0.22f;

                for (int x = 0; x < EdgeW; x++)
                {
                    float nx = (x + 0.5f) / EdgeW;
                    float ends = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.14f))
                               * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - nx) / 0.14f));
                    px[y * EdgeW + x] = Lum((across + bleed) * ends);
                }
            }

            return Finish(tex, px, new Vector2(0.5f, 0.5f), EdgeW / 2f);
        }

        /// <summary>An angular chip of failed weave, thrown when a panel gives way.</summary>
        private static Sprite BuildShard(int size)
        {
            var tex = NewTexture(size, size);
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float d = HexDistance(new Vector2(nx, ny * 1.5f), 0.72f);
                    if (d > 0f) { px[y * size + x] = Color.clear; continue; }
                    px[y * size + x] = Lum(0.35f + Mathf.Clamp01(1f + d / 0.22f) * 0.65f);
                }
            }

            return Finish(tex, px, new Vector2(0.5f, 0.5f), size);
        }
    }
}
