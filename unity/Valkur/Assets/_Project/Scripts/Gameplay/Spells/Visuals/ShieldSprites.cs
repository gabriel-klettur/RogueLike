using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Procedural sprite family for the shield sphere: the hexagonal facets that tile its
    /// surface, the motes that orbit it, and the sheen that slides across its front.
    ///
    /// <para>WHITE AND ALPHA ONLY, for the same reason as <see cref="KiSprites"/>: the shield's
    /// hue is authored per spell on <c>particleColor</c>, so baking a colour in would mean one
    /// texture set per swatch. Shaping lives in the alpha, which on an additive material is
    /// brightness.</para>
    ///
    /// <para>The facet is drawn with its FLAT SIDES vertical and its points left and right,
    /// because the rig scales it non-uniformly along its local X to foreshorten it against the
    /// curve of the sphere — see <c>ShieldSphereFX.Shell</c>. A facet drawn point-up would
    /// compress along the wrong axis and the tiling would visibly shear at the rim.</para>
    /// </summary>
    internal static class ShieldSprites
    {
        private static Sprite _facet;
        private static Sprite _facetSolid;
        private static Sprite _mote;
        private static Sprite _sheen;

        /// <summary>A hollow hexagon: bright edges, empty middle. The resting cell.</summary>
        public static Sprite Facet { get { EnsureAll(); return _facet; } }

        /// <summary>The same hexagon filled. Used only where the shell is being struck.</summary>
        public static Sprite FacetSolid { get { EnsureAll(); return _facetSolid; } }

        /// <summary>A soft round mote with a hot centre. Centre-pivoted, 1x1 world unit.</summary>
        public static Sprite Mote { get { EnsureAll(); return _mote; } }

        /// <summary>A soft elliptical highlight — light sliding across curved glass.</summary>
        public static Sprite Sheen { get { EnsureAll(); return _sheen; } }

        /// <summary>
        /// Domain Reload is OFF, so these carry DESTROYED native objects into the next Play
        /// session. Nulling the field is a plain <c>stsfld</c>, the only shape
        /// <c>DomainReloadStaticResetTests</c> reads as a reset.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _facet = null;
            _facetSolid = null;
            _mote = null;
            _sheen = null;
        }

        public static void EnsureAll()
        {
            if (_facet != null) return;

            _facet = BuildHexagon(64, hollow: true);
            _facetSolid = BuildHexagon(64, hollow: false);
            _mote = BuildMote(48);
            _sheen = BuildSheen(96, 48);
        }

        // ── generators ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A hexagon with points on the horizontal axis. Hollow means only the border carries
        /// alpha, which is what makes a field of them read as a MESH rather than as a field of
        /// blobs — the character has to stay visible through the shell.
        /// </summary>
        private static Sprite BuildHexagon(int size, bool hollow)
        {
            var tex = NewTexture(size, size);
            var px = new Color[size * size];

            // Point-right hexagon inscribed in the unit square, with a little margin so the
            // border's own feather is never clipped by the texture edge.
            const float r = 0.88f;
            var poly = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                poly[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.92f);
            }

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    var p = new Vector2(nx, ny);

                    // Signed distance to the hexagon border, as a fraction of its radius.
                    float inward = InwardDistance(p, poly);
                    if (inward <= 0f) { px[y * size + x] = Color.clear; continue; }

                    float a;
                    if (hollow)
                    {
                        // A band hugging the border. Peaks just inside the edge and falls off
                        // both ways, so the line has no hard side.
                        const float bandWidth = 0.17f;
                        float t = Mathf.Clamp01(inward / bandWidth);
                        a = Mathf.Pow(1f - t, 1.6f);
                        // Feather the outer side too, or the hexagon is a jagged polygon at
                        // this resolution.
                        a *= Mathf.Clamp01(inward / 0.035f);
                        a *= 0.95f;
                    }
                    else
                    {
                        // Filled, but still brighter at the border: a lit pane, not a sticker.
                        a = 0.42f + 0.58f * Mathf.Pow(1f - Mathf.Clamp01(inward / 0.35f), 1.4f);
                        a *= Mathf.Clamp01(inward / 0.035f);
                    }

                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>
        /// A mote: a hot pinpoint inside a soft bloom. Two falloffs rather than one, because a
        /// single gaussian reads as fog and a single hard dot reads as a pixel.
        /// </summary>
        private static Sprite BuildMote(int size)
        {
            var tex = NewTexture(size, size);
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    if (d >= 1f) { px[y * size + x] = Color.clear; continue; }

                    float bloom = Mathf.Pow(1f - d, 2.6f) * 0.55f;
                    float core = Mathf.Exp(-Mathf.Pow(d / 0.20f, 2f));
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(bloom + core));
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>
        /// A wide soft ellipse for the travelling specular. Drawn tall-and-thin so it can be
        /// laid across the sphere as a band of light; the falloff is asymmetric because a
        /// highlight on glass has a defined leading edge and a long tail.
        /// </summary>
        private static Sprite BuildSheen(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float ny = (y + 0.5f) / h * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    if (d >= 1f) { px[y * w + x] = Color.clear; continue; }

                    float across = Mathf.Exp(-Mathf.Pow(nx / 0.32f, 2f));
                    float along = Mathf.Pow(1f - Mathf.Abs(ny), 0.8f);
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(across * along * 0.85f));
                }
            }
            return Finish(tex, px, new Vector2(0.5f, 0.5f), w);
        }

        // ── shared ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Distance from <paramref name="p"/> to the nearest edge of a CONVEX polygon wound
        /// counter-clockwise, positive inside and 0 outside. Both shapes here are convex, so
        /// the cheap half-plane minimum is exact.
        /// </summary>
        private static float InwardDistance(Vector2 p, Vector2[] poly)
        {
            float nearest = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 edge = poly[i] - poly[j];
                float length = edge.magnitude;
                if (length < 1e-5f) continue;
                // Left of every edge == inside, for counter-clockwise winding.
                float side = (edge.x * (p.y - poly[j].y) - edge.y * (p.x - poly[j].x)) / length;
                if (side <= 0f) return 0f;
                if (side < nearest) nearest = side;
            }
            return nearest == float.MaxValue ? 0f : nearest;
        }

        private static Texture2D NewTexture(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        private static Sprite Finish(Texture2D tex, Color[] px, Vector2 pivot, float ppu)
        {
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu);
        }
    }
}
