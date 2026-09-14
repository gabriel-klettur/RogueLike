using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The sprites the cut target is drawn with, generated once.
    ///
    /// <para><b>THE BANDS ARE PAINTED, NOT STACKED.</b> Five concentric <c>ElementalSprites.Ring</c>
    /// quads 0.1 units apart would overlap their soft falloffs into one smear — the very thing the
    /// target exists to prevent, "which part is the centre". One texture paints each band as a flat
    /// fill in its grade's colour with a crisp rim at every edge, so band boundaries are lines, not
    /// gradients. It is painted from the skill's own band reaches and cached per set of reaches.</para>
    ///
    /// <para><b>A DARK DISC UNDER IT, ON THE ALPHA MATERIAL.</b> The bands are light drawn additively
    /// and the bark behind them is often pale; additive gold on pale bark washes out to cream. The
    /// backing is the one layer that REMOVES light, and on an additive material a dark pixel adds
    /// nothing, so it has to be on the alpha one (the rule <c>KiAuraFX</c> records for its debris).</para>
    /// </summary>
    public static class CutTargetTextures
    {
        private const int TARGET_PX = 192;
        private const int RING_PX = 128;

        private static Dictionary<string, Sprite> _targets = new Dictionary<string, Sprite>();
        private static Sprite _backing, _ring;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _targets = new Dictionary<string, Sprite>();
            _backing = null;
            _ring = null;
        }

        /// <summary>Fill alpha per band, centre first. The centre is the most opaque on purpose.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of literal alphas, never written after type initialisation.")]
        private static readonly float[] FillAlpha = { 0.62f, 0.38f, 0.30f, 0.26f, 0.22f };

        /// <summary>
        /// The bullseye for a set of band edges (fractions of the outer radius, centre first). A
        /// 1x1-unit sprite: scale it to the target's diameter.
        /// </summary>
        public static Sprite Target(float[] bandFractions)
        {
            string key = string.Join("|", System.Array.ConvertAll(bandFractions, f => f.ToString("0.000")));
            if (_targets.TryGetValue(key, out var cached) && cached != null) return cached;

            var colours = new Color[5];
            for (int i = 0; i < 5; i++) colours[i] = RhythmCallouts.CutColour(CutGrade.Perfect + i);

            float px = 1f / (TARGET_PX * 0.5f);            // one texel, in normalized radius
            var tex = NewTexture(TARGET_PX, "CutTarget");
            var pixels = new Color[TARGET_PX * TARGET_PX];
            for (int y = 0; y < TARGET_PX; y++)
            for (int x = 0; x < TARGET_PX; x++)
            {
                float dx = (x + 0.5f) / TARGET_PX * 2f - 1f, dy = (y + 0.5f) / TARGET_PX * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * TARGET_PX + x] = TargetPixel(r, bandFractions, colours, px);
            }
            tex.SetPixels(pixels);
            tex.Apply();

            var sprite = MakeSprite(tex);
            _targets[key] = sprite;
            return sprite;
        }

        private static Color TargetPixel(float r, float[] edges, Color[] colours, float px)
        {
            float outer = edges[4];
            if (r > outer + px * 2f) return Color.clear;

            int band = 4;
            for (int i = 0; i < 5; i++)
                if (r <= edges[i]) { band = i; break; }

            Color c = colours[band];
            float a = FillAlpha[band] * (1f - Smooth(outer - px, outer + px, r));

            // A crisp bright rim on every band's outer edge; thicker on the target's own edge.
            for (int i = 0; i < 5; i++)
            {
                float width = i == 4 ? px * 1.6f : px * 1.1f;
                float rim = 1f - Smooth(width * 0.5f, width * 1.5f, Mathf.Abs(r - edges[i]));
                if (rim <= 0f) continue;
                Color rc = Color.Lerp(colours[i], Color.white, 0.45f);
                c = Color.Lerp(c, rc, rim);
                a = Mathf.Max(a, 0.95f * rim);
            }

            // A white-hot dot at the dead centre, half the Perfect band wide: the bull.
            float bull = 1f - Smooth(edges[0] * 0.28f, edges[0] * 0.5f, r);
            if (bull > 0f)
            {
                c = Color.Lerp(c, Color.white, bull);
                a = Mathf.Max(a, bull);
            }

            c.a = Mathf.Clamp01(a);
            return c;
        }

        /// <summary>A soft dark disc, opaque to ~90 % of its radius. Drawn on the alpha material.</summary>
        public static Sprite Backing
        {
            get
            {
                if (_backing != null) return _backing;
                var tex = Paint(RING_PX, "CutTargetBacking", r => 1f - Smooth(0.86f, 1f, r), new Color(0.03f, 0.02f, 0.04f));
                return _backing = MakeSprite(tex);
            }
        }

        /// <summary>A thin crisp circle whose line sits exactly on the sprite's edge (radius 1).</summary>
        public static Sprite ApproachRing
        {
            get
            {
                if (_ring != null) return _ring;
                float px = 1f / (RING_PX * 0.5f);
                var tex = Paint(RING_PX, "CutTargetApproach",
                    r => 1f - Smooth(px * 1.2f, px * 2.6f, Mathf.Abs(r - (1f - px * 3f))), Color.white);
                return _ring = MakeSprite(tex);
            }
        }

        private static Texture2D Paint(int size, string name, System.Func<float, float> alpha, Color colour)
        {
            var tex = NewTexture(size, name);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                var c = colour;
                c.a = Mathf.Clamp01(alpha(Mathf.Sqrt(dx * dx + dy * dy)));
                pixels[y * size + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D NewTexture(int size, string name) =>
            new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                // Light drawn over the world: bilinear, like the blade strokes it sits among.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

        // FullRect: Sprite.Create defaults to Tight, which traces the alpha outline.
        private static Sprite MakeSprite(Texture2D tex)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                tex.width, 0, SpriteMeshType.FullRect);
            s.name = tex.name;
            return s;
        }

        private static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.00001f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
