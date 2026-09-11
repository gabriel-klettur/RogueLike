using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The dial's metalwork, generated in code: a bevelled gold ring, the soft shadow the disc
    /// casts, a white copy of the ring for the damage flash, the plate under the dial and the
    /// small round buttons on it.
    ///
    /// <para><b>Lit, not flat.</b> The ring this replaced was one flat band of gold at 55 %
    /// alpha — no highlight, no shadow, nothing that says the dial is an OBJECT sitting on the
    /// screen. Every ring pixel here is shaded by the angle of its surface against a light
    /// from the upper left, with a convex profile across the band, a dark lip on both edges and
    /// engraved notches every 30 degrees. That is the whole difference between a HUD element
    /// and a coloured circle.</para>
    ///
    /// <para><b>Baked colour.</b> Unlike the icon atlas these sprites carry their final
    /// colours (from <see cref="MinimapStyle"/>) and are drawn with a white Image colour; a
    /// gradient cannot be expressed as a single tint.</para>
    /// </summary>
    public static class MinimapChromeSprites
    {
        public const int RingTexture = 256;

        private static Sprite s_ring, s_ringFlash, s_shadow, s_plate, s_button, s_disc;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetChromeStatics()
        {
            s_ring = s_ringFlash = s_shadow = s_plate = s_button = s_disc = null;
        }

        /// <summary>
        /// Ring band as fractions of the sprite's radius. The map disc is drawn inside
        /// <see cref="RingInner"/>, so the ring's inner lip overlaps the map's antialiased edge.
        /// </summary>
        public const float RingInner = 0.905f;
        public const float RingOuter = 0.995f;

        public static Sprite Ring(MinimapStyle s) => s_ring != null ? s_ring : (s_ring = BuildRing(s, false));
        public static Sprite RingFlash(MinimapStyle s) => s_ringFlash != null ? s_ringFlash : (s_ringFlash = BuildRing(s, true));
        public static Sprite DiscShadow() => s_shadow != null ? s_shadow : (s_shadow = BuildShadow());
        public static Sprite Disc() => s_disc != null ? s_disc : (s_disc = BuildDisc());
        public static Sprite Plate(MinimapStyle s) => s_plate != null ? s_plate : (s_plate = BuildPlate(s));
        public static Sprite Button(MinimapStyle s) => s_button != null ? s_button : (s_button = BuildButton(s));

        private static Sprite BuildRing(MinimapStyle s, bool flash)
        {
            const int n = RingTexture;
            var tex = NewTex(n, n, "MinimapRing");
            var px = new Color32[n * n];
            float R = n * 0.5f;
            float rin = R * RingInner, rout = R * RingOuter;
            var light = new Vector2(-0.62f, 0.78f).normalized;

            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - R, dy = y + 0.5f - R;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(d - rin + 0.5f) * Mathf.Clamp01(rout - d + 0.5f);
                if (a <= 0f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }

                if (flash)
                {
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    continue;
                }

                float t = Mathf.InverseLerp(rin, rout, d);                 // 0 inner lip .. 1 outer lip
                var normal = new Vector2(dx, dy) / Mathf.Max(d, 1e-4f);
                // Convex band: the outer half faces outward, the inner half faces inward.
                float facing = Vector2.Dot(normal, light) * (t - 0.5f) * 2f;
                float crown = Mathf.Sin(t * Mathf.PI);                       // brightest along the middle
                float shade = Mathf.Clamp01(0.52f + facing * 0.55f) * (0.55f + 0.45f * crown);

                Color c = shade < 0.5f
                    ? Color.Lerp(s.ringShadow, s.ringGold, shade * 2f)
                    : Color.Lerp(s.ringGold, s.ringHighlight, (shade - 0.5f) * 2f);

                // Dark lips on both edges, so the ring separates from the map AND from the world.
                float lip = Mathf.Min(t, 1f - t);
                c = Color.Lerp(new Color(0.07f, 0.05f, 0.03f), c, Mathf.Clamp01(lip * 7f));

                // Engraved notches every 30 degrees (the cardinals sit on four of them).
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                float m = Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / 30f) * 30f));
                if (m < 1.1f && t > 0.25f && t < 0.75f) c = Color.Lerp(c, s.ringShadow * 0.6f, 0.7f);

                px[y * n + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static Sprite BuildShadow()
        {
            const int n = 128;
            var tex = NewTex(n, n, "MinimapShadow");
            var px = new Color32[n * n];
            float R = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - R, dy = y + 0.5f - R;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / R;           // 0 centre .. 1 edge
                float a = 1f - Mathf.SmoothStep(0.80f, 1f, d);
                px[y * n + x] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static Sprite BuildDisc()
        {
            const int n = 128;
            var tex = NewTex(n, n, "MinimapDisc");
            var px = new Color32[n * n];
            float R = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - R, dy = y + 0.5f - R;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(R - d) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Rounded plate: dark gradient body, a gold hairline, 9-sliced.</summary>
        private static Sprite BuildPlate(MinimapStyle s)
        {
            const int n = 32;
            const float radius = 7f;
            var tex = NewTex(n, n, "MinimapPlate");
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[n * n];
            var top = new Color(0.085f, 0.09f, 0.12f);
            var bottom = new Color(0.035f, 0.04f, 0.055f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = RoundedRectDistance(x + 0.5f, y + 0.5f, n, n, radius);
                float a = Mathf.Clamp01(0.5f - d);
                float border = Mathf.Clamp01(1f - Mathf.Abs(d + 1.1f));
                Color c = Color.Lerp(bottom, top, (float)y / n);
                c = Color.Lerp(c, s.ringGold * 0.85f, border * 0.85f);
                px[y * n + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 0.94f * 255f));
            }
            tex.SetPixels32(px);
            // Apply(true): the texture carries a mip chain, and mips left unwritten sample as
            // garbage — the first build showed four bright squares on the plate's corners.
            tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                                 new Vector4(10, 10, 10, 10));
        }

        /// <summary>Small round button: dark face, gold rim, soft top highlight.</summary>
        private static Sprite BuildButton(MinimapStyle s)
        {
            const int n = 64;
            var tex = NewTex(n, n, "MinimapButton");
            var px = new Color32[n * n];
            float R = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - R, dy = y + 0.5f - R;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(R - 1f - d + 0.5f);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(d - (R - 4.5f)) / 3f);
                float up = Mathf.Clamp01(dy / R * 0.5f + 0.5f);
                Color face = Color.Lerp(new Color(0.04f, 0.045f, 0.06f), new Color(0.13f, 0.13f, 0.16f), up);
                Color c = Color.Lerp(face, Color.Lerp(s.ringShadow, s.ringHighlight, up), rim);
                px[y * n + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static float RoundedRectDistance(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
            float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static Texture2D NewTex(int w, int h, string name) =>
            new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = name,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
    }
}
