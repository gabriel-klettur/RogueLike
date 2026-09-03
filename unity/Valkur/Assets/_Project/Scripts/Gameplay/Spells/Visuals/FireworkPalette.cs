using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The colours a firework shell burns, derived from ONE authored swatch.
    ///
    /// <para>A firework is the one effect in this project whose identity is that it is NOT
    /// monochrome — a chrysanthemum shell that draws in a single hue is a flare. So unlike
    /// <see cref="KiPalette"/>, which derives a ramp from core to edge, this derives a small
    /// SPREAD of star colours around the authored hue, plus the near-white flash the shell
    /// makes at the instant it opens.</para>
    ///
    /// <para>The spread is in HUE, held at high value and saturation, because that is what
    /// separates a firework from a fire: burning magnesium salts all reach roughly the same
    /// brightness and differ in colour alone. Deriving them keeps a designer's job to picking
    /// one colour in F4, and makes it impossible to author a shell whose stars are darker than
    /// the sky they are in.</para>
    ///
    /// <para>An unauthored swatch (opaque white — the project-wide sentinel, tested through
    /// <see cref="KiPalette.IsUnauthored"/> so the two cannot drift) does NOT fall back to a
    /// single colour. It falls back to the full festival spread, because "nobody chose a
    /// colour" is the exact case where a firework should be every colour at once.</para>
    /// </summary>
    internal struct FireworkPalette
    {
        /// <summary>The star colours. Never fewer than three, or the shell reads as monochrome.</summary>
        public Color[] Stars;

        /// <summary>The instant the shell opens: near-white, barely tinted by the swatch.</summary>
        public Color Flash;

        /// <summary>What the sky is pushed toward. Between the flash and the mean star.</summary>
        public Color Sky;

        /// <summary>The authored colour itself, for the rocket's trail and the launch sparks.</summary>
        public Color Trail;

        /// <summary>True when the shell took the festival spread rather than one authored hue.</summary>
        public bool IsFestival;

        /// <summary>
        /// The five-colour festival spread. These are the colours the executor shipped by hand
        /// since it was written, kept because they are good — red, gold, green, magenta, cyan
        /// is a real shell assortment and not an arbitrary rainbow.
        ///
        /// <para>Built per call rather than cached in a <c>static readonly</c> field, which is
        /// what the old <c>FireworkColors</c> was: an array static can never be reassigned, so
        /// the Domain-Reload ratchet has no way to see it reset and it sat on the
        /// unreset-statics baseline as accepted debt. Five <c>Color</c>s once per cast is not
        /// worth a line in that file.</para>
        /// </summary>
        private static Color[] Festival() => new[]
        {
            new Color(1.00f, 0.30f, 0.20f, 1f),
            new Color(1.00f, 0.85f, 0.20f, 1f),
            new Color(0.40f, 1.00f, 0.30f, 1f),
            new Color(1.00f, 0.45f, 1.00f, 1f),
            new Color(0.30f, 0.85f, 1.00f, 1f),
        };

        /// <summary>
        /// How far the derived stars wander from the authored hue, in turns. Wide enough that
        /// a red shell throws orange and magenta stars, narrow enough that it is still a RED
        /// shell — past about 0.12 the identity of the authored colour is gone.
        /// </summary>
        private const float HueSpread = 0.075f;

        public static FireworkPalette From(Color authored)
        {
            if (KiPalette.IsUnauthored(authored))
            {
                Color[] festival = Festival();
                Color mean = Mean(festival);
                return new FireworkPalette
                {
                    Stars = festival,
                    // The mean of the spread is a warm off-white, which is what a mixed shell
                    // actually flashes. Deriving it rather than hardcoding keeps the two in
                    // step if the assortment is ever retuned.
                    Flash = Whiten(mean, 0.72f),
                    Sky = Whiten(mean, 0.45f),
                    Trail = new Color(1.00f, 0.80f, 0.35f, 1f),
                    IsFestival = true,
                };
            }

            Color baseColor = new Color(authored.r, authored.g, authored.b, 1f);
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);

            // An achromatic swatch has no hue and RGBToHSV reports 0 for it, which is RED —
            // the same trap ElementPalette.Retint records. A grey shell is a real request and
            // what it asks for is the ABSENCE of colour, so it stays grey and only spreads in
            // brightness.
            bool achromatic = s < 0.02f;

            // Stars are held bright: a shell burns at one temperature and differs in hue.
            float starV = Mathf.Max(0.85f, v);
            float starS = achromatic ? 0f : Mathf.Clamp01(Mathf.Max(0.55f, s));

            var stars = new Color[5];
            for (int i = 0; i < stars.Length; i++)
            {
                // -2..+2 around the authored hue, so the authored colour is the CENTRE star
                // and not one end of a ramp.
                float step = (i - (stars.Length - 1) * 0.5f) / ((stars.Length - 1) * 0.5f);
                float hue = achromatic ? 0f : Mathf.Repeat(h + step * HueSpread, 1f);
                // The flanks are marginally cooler in value so the centre star reads as the
                // one the shell is named after.
                float falloff = 1f - 0.10f * Mathf.Abs(step);
                stars[i] = Color.HSVToRGB(hue, starS, Mathf.Clamp01(starV * falloff));
            }

            return new FireworkPalette
            {
                Stars = stars,
                Flash = Whiten(baseColor, 0.80f),
                Sky = Whiten(baseColor, 0.50f),
                Trail = Whiten(baseColor, 0.25f),
                IsFestival = false,
            };
        }

        /// <summary>Pick a star colour. Random rather than rotating: a shell has no order.</summary>
        public Color RandomStar()
        {
            if (Stars == null || Stars.Length == 0) return Color.white;
            return Stars[Random.Range(0, Stars.Length)];
        }

        private static Color Whiten(Color c, float amount)
            => Color.Lerp(c, Color.white, Mathf.Clamp01(amount));

        private static Color Mean(Color[] colors)
        {
            float r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < colors.Length; i++) { r += colors[i].r; g += colors[i].g; b += colors[i].b; }
            float n = Mathf.Max(1, colors.Length);
            return new Color(r / n, g / n, b / n, 1f);
        }
    }
}
