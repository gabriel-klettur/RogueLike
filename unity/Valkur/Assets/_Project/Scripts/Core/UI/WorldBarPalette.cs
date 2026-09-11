using UnityEngine;

namespace Valkur.Core.UI
{
    /// <summary>The four tones a bar fill is drawn in, derived from one authored colour.</summary>
    public struct WorldBarRamp
    {
        /// <summary>The fill's top row: lighter, less saturated, leaning toward yellow.</summary>
        public Color Highlight;

        /// <summary>The authored colour itself — the body of the fill.</summary>
        public Color Base;

        /// <summary>The fill's bottom row: darker, a touch more saturated, leaning toward blue.</summary>
        public Color Shadow;

        /// <summary>The leading-edge column: the brightest tone, nearly white.</summary>
        public Color Edge;
    }

    /// <summary>
    /// The colour arithmetic behind a bar, with no Unity object in sight so a test can prove it.
    ///
    /// <para><b>Why a HUE-SHIFTED ramp and not a darker/lighter copy of one colour.</b> The fill
    /// was one colour multiplied into a greyscale gradient, which is exactly how a bar looks
    /// "programmer-drawn": every tone of it sits on the same hue line, so the top reads as a paler
    /// sticker and the bottom as dirt. Pixel artists shade the other way — highlights travel
    /// toward the warm light (yellow), shadows toward the cool ambient (blue) — and that single
    /// habit is most of the difference between a flat bar and one that reads as a lit, rounded
    /// tube. It is derived rather than authored so that ONE colour per bar is still the only
    /// decision a designer makes, and so the low-health amber and every rank's fill get the same
    /// treatment without anybody picking twelve extra swatches.</para>
    ///
    /// <para>The alpha of the authored colour is kept on every tone: alpha on a bar is the fade,
    /// never a shading decision.</para>
    /// </summary>
    public static class WorldBarPalette
    {
        private const float WARM_HUE = 1f / 6f;   // yellow
        private const float COOL_HUE = 2f / 3f;   // blue

        /// <summary>The four tones for an authored fill colour.</summary>
        public static WorldBarRamp Ramp(Color authored)
        {
            Color.RGBToHSV(authored, out float h, out float s, out float v);
            float a = authored.a;
            return new WorldBarRamp
            {
                Highlight = Hsv(Toward(h, WARM_HUE, 0.18f), s * 0.62f, Mathf.Min(1f, v * 1.08f + 0.16f), a),
                Base = authored,
                Shadow = Hsv(Toward(h, COOL_HUE, 0.14f), Mathf.Min(1f, s * 1.08f), v * 0.60f, a),
                // Desaturated harder than the highlight so it is the brightest tone for EVERY hue.
                // At s*0.45 a blue's highlight — which leans further toward yellow, i.e. through
                // cyan, the brightest part of the wheel — came out lighter than its own edge.
                Edge = Hsv(Toward(h, WARM_HUE, 0.10f), s * 0.30f, Mathf.Min(1f, v * 1.15f + 0.30f), a),
            };
        }

        /// <summary>
        /// Move a hue a fraction of the way toward a target along the SHORTER arc. The shorter arc
        /// matters for red: its hue is 0, and toward-yellow must go up while toward-blue must go
        /// down through magenta, not the long way round through green.
        /// </summary>
        public static float Toward(float hue, float target, float amount)
        {
            float d = Mathf.Repeat(target - hue + 0.5f, 1f) - 0.5f;
            return Mathf.Repeat(hue + d * amount, 1f);
        }

        /// <summary>WCAG relative luminance of a colour, in linear light.</summary>
        public static float Luminance(Color c)
        {
            return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
        }

        /// <summary>WCAG contrast ratio between two opaque colours, 1..21.</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            if (la < lb) (la, lb) = (lb, la);
            return (la + 0.05f) / (lb + 0.05f);
        }

        /// <summary>What a translucent colour looks like over an opaque one.</summary>
        public static Color Over(Color top, Color bottom)
        {
            float k = Mathf.Clamp01(top.a);
            return new Color(Mathf.Lerp(bottom.r, top.r, k), Mathf.Lerp(bottom.g, top.g, k),
                             Mathf.Lerp(bottom.b, top.b, k), 1f);
        }

        private static float Linear(float v)
            => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        private static Color Hsv(float h, float s, float v, float a)
        {
            var c = Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v));
            c.a = a;
            return c;
        }
    }
}
