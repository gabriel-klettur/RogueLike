using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The two maps that are not part of a shard: the frost patch the wall stands in and
    /// the chunks it throws when it breaks.
    /// </summary>
    internal static partial class IceSprites
    {
        /// <summary>
        /// Ground rime: an elongated patch with a crystalline, ragged edge.
        ///
        /// <para>A plain soft ellipse would read as a shadow. What says "frost" is that the
        /// boundary is FEATHERY — spikes of rime reaching out along the ground — so the edge
        /// is pushed in and out by directional noise instead of being a clean falloff. It is
        /// drawn 2 units wide by 1 tall (160x80 at 80 PPU) so a caller scaling it to the
        /// wall's length and footprint never stretches it more than a little.</para>
        /// </summary>
        private static Sprite BuildRime(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float ny = (y + 0.5f) / h * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f;

                    float ang = Mathf.Atan2(ny, nx);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);

                    // Feathered boundary: the radius at which the patch ends wobbles with
                    // angle, so the silhouette grows spikes instead of staying an oval.
                    float feather =
                        0.14f * Mathf.Sin(ang * 9f) +
                        0.09f * Mathf.Sin(ang * 17f + 1.3f) +
                        0.06f * Mathf.Sin(ang * 29f + 2.7f);
                    float edge = 0.86f + feather;

                    float a = Mathf.Clamp01(1f - r / Mathf.Max(0.05f, edge));
                    a = Mathf.Pow(a, 1.9f);

                    // Crystalline speckle so the patch has texture instead of being a wash.
                    float speck = Mathf.PerlinNoise(nx * 9.5f + 31.7f, ny * 9.5f + 11.3f);
                    a *= Mathf.Lerp(0.55f, 1.15f, speck);

                    // Radial needles of rime, brightest near the centre line.
                    float needles = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 23f)), 6f) * Mathf.Clamp01(1f - r * 0.85f);
                    a = Mathf.Clamp01(a + needles * 0.28f);

                    float white = Mathf.Clamp01(0.55f + 0.45f * (1f - r));
                    px[y * w + x] = new Color(
                        Mathf.Lerp(0.62f, 0.95f, white),
                        Mathf.Lerp(0.85f, 0.99f, white),
                        1f,
                        a * 0.85f);
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), h);
        }

        /// <summary>
        /// A flying chunk: an angular five-sided sliver, not a disc. Debris that is round
        /// reads as a spark; ice has to break into pieces with corners.
        /// </summary>
        private static Sprite BuildDebris(int size)
        {
            var tex = NewTexture(size, size);
            var px = new Color[size * size];

            var poly = new[]
            {
                new Vector2(0.00f,  0.92f),
                new Vector2(0.46f,  0.12f),
                new Vector2(0.22f, -0.78f),
                new Vector2(-0.34f, -0.62f),
                new Vector2(-0.52f, 0.30f),
            };

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    var p = new Vector2(nx, ny);

                    bool inside = PointInPolygon(p, poly);
                    float dist = 10f;
                    for (int i = 0; i < poly.Length; i++)
                        dist = Mathf.Min(dist, DistanceToSegment(p, poly[i], poly[(i + 1) % poly.Length]));

                    float cover = inside ? Mathf.Clamp01(dist / (2f / size)) : 0f;
                    if (cover <= 0f) { px[y * size + x] = Color.clear; continue; }

                    float toEdge = Mathf.Clamp01(1f - dist / 0.5f);
                    Color c = Color.Lerp(IceDeep, IceTip, Mathf.Clamp01(ny * 0.5f + 0.5f));
                    c = Color.Lerp(c * 0.85f, c * 1.3f, Mathf.Pow(toEdge, 1.8f));
                    px[y * size + x] = new Color(c.r, c.g, c.b, cover * 0.95f);
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), size);
        }

        private static bool PointInPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (poly[i].y > p.y == poly[j].y > p.y) continue;
                float xCross = (poly[j].x - poly[i].x) * (p.y - poly[i].y) /
                               (poly[j].y - poly[i].y) + poly[i].x;
                if (p.x < xCross) inside = !inside;
            }
            return inside;
        }
    }
}
