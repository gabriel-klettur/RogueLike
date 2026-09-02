using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A ki aura's colours and how hard it burns, derived from ONE authored swatch plus one
    /// authored intensity.
    ///
    /// <para>WHY DERIVED RATHER THAN AUTHORED. Seven charge spells differ only in hue and
    /// ferocity, so seven hand-tuned four-colour palettes would be twenty-eight numbers that
    /// have to stay in relation to each other — and the relation is the part that matters: the
    /// spine of a flame is always closer to white than its flanks, and the light it throws is
    /// always between the two. Deriving them means a designer picks a colour in the F4 editor
    /// and gets a coherent aura, and it is impossible to author an aura whose core is darker
    /// than its edge.</para>
    ///
    /// <para><see cref="Intensity"/> is the second dial and it is not a size: it decides how
    /// many tongues there are, how fast the ki streams off them, whether the ground breaks up,
    /// whether lightning crawls over the aura at all, and how hard the frame shakes. A
    /// low-intensity charge should look CALM, not small.</para>
    /// </summary>
    internal struct KiPalette
    {
        /// <summary>The white-hot spine of the aura. Almost colourless by design.</summary>
        public Color Core;

        /// <summary>The authored colour. What the aura reads AS.</summary>
        public Color Mid;

        /// <summary>Deepest and most saturated, for the outer tongues and the ground wash.</summary>
        public Color Edge;

        /// <summary>What the Light2D throws. Between core and mid, so lit surfaces agree.</summary>
        public Color Light;

        /// <summary>0 = a calm hum, 1 = the ground coming apart.</summary>
        public float Intensity;

        /// <summary>
        /// True once the aura is violent enough to crawl with lightning. Below this a charge
        /// is a glow; above it, the air itself is failing.
        /// </summary>
        public bool HasLightning => Intensity >= LightningThreshold;

        /// <summary>
        /// Chosen so the two calmest of the seven shipped charges stay clean. Arcs are the
        /// single loudest element here, and putting them on every tier would erase the
        /// difference between the bottom of the ladder and the top.
        /// </summary>
        public const float LightningThreshold = 0.60f;

        /// <summary>
        /// Build from the spell's own <c>particleColor</c>. A colour left at default white
        /// (which is what an unauthored field holds) would produce a colourless aura, so it
        /// falls back to the pale blue-white of a base charge rather than to nothing.
        /// </summary>
        public static KiPalette From(Color authored, float intensity01)
        {
            Color baseColor = IsUnauthored(authored)
                ? new Color(0.62f, 0.84f, 1f, 1f)
                : new Color(authored.r, authored.g, authored.b, 1f);

            // A very dark swatch (the void charge) still has to produce a visible spine, so
            // the core is pushed toward white by an amount that GROWS as the colour darkens.
            float luminance = baseColor.r * 0.299f + baseColor.g * 0.587f + baseColor.b * 0.114f;
            float whiten = Mathf.Lerp(0.86f, 0.62f, Mathf.Clamp01(luminance));

            return new KiPalette
            {
                Core = Color.Lerp(baseColor, Color.white, whiten),
                Mid = baseColor,
                Edge = Deepen(baseColor, 0.62f),
                Light = Color.Lerp(baseColor, Color.white, 0.28f),
                Intensity = Mathf.Clamp01(intensity01),
            };
        }

        /// <summary>
        /// Darker AND more saturated, not merely multiplied. Plain multiplication desaturates
        /// towards black and turns a crimson aura's edge grey — the edge is supposed to be the
        /// most colourful part of it.
        /// </summary>
        private static Color Deepen(Color c, float amount)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 1.25f + 0.05f), v * amount);
        }

        /// <summary>
        /// White with full alpha is what <c>SpellDefinition.particleColor</c> holds when nobody
        /// has touched it, so it cannot be told apart from a deliberate white — and a
        /// deliberate white aura is a real thing someone might want. Treating it as unauthored
        /// is the lesser evil: the fallback IS very nearly white, so a designer who meant white
        /// gets what they asked for anyway.
        ///
        /// <para>Public because this rule has a SECOND caller: <c>ElementPalette.RecolouredTo</c>
        /// asks the same question about the same field, and a copy of the test in two places is
        /// how the two drift apart the first time the sentinel is reconsidered.</para>
        /// </summary>
        public static bool IsUnauthored(Color c)
            => c.r > 0.99f && c.g > 0.99f && c.b > 0.99f;
    }
}
