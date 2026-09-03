using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The four colours a root field is drawn from, all derived from ONE authored swatch
    /// (<c>SpellDefinition.particleColor</c>) so a designer picks a colour and gets a
    /// coherent plant — and so it is impossible to author one whose soil is brighter than
    /// its sap. Same contract as <see cref="KiPalette"/>, different ramp.
    ///
    /// <para>WHY NOT <see cref="KiPalette"/>. That one derives its Core near-WHITE on
    /// purpose (measured at saturation 0.25 for the shipped orange) because a ki spine is
    /// meant to be almost colourless. A root is the opposite: it is matter, not light, and
    /// its most colourful part is the living tip. Running a plant from a white core to a
    /// dark edge washes it out exactly where it should be greenest.</para>
    ///
    /// <para>THE SOIL IS DEEPENED IN HSV, NEVER MULTIPLIED. Plain multiplication
    /// desaturates towards black and turns a crimson root's base grey, which is the one
    /// place the eye is looking for "wet earth". The same reasoning
    /// <see cref="KiPalette"/> records for its edge.</para>
    /// </summary>
    internal struct RootPalette
    {
        /// <summary>Wet earth at the base of a stem, and the colour of a thrown clod.</summary>
        public Color Soil;
        /// <summary>The bulk of the stem.</summary>
        public Color Bark;
        /// <summary>The living tip, and what the ground ring is drawn in.</summary>
        public Color Leaf;
        /// <summary>The glow the light and the sprout flash use. Brightest of the four.</summary>
        public Color Sap;

        /// <summary>
        /// Opaque white is the project-wide "nobody authored this" sentinel (the same one
        /// <c>KiPalette.IsUnauthored</c> and <c>SpellCastFlourishFX</c> test), and it is
        /// indistinguishable from a deliberate white. A root field that fell back to white
        /// would read as bone, so the fallback is a bark green.
        /// </summary>
        private static readonly Color Fallback = new Color(0.30f, 0.55f, 0.20f, 1f);

        public static RootPalette From(Color authored)
        {
            Color baseColor = IsUnauthored(authored) ? Fallback : authored;

            Color.RGBToHSV(baseColor, out float h, out float s, out float v);

            // A grey swatch is a real request — the absence of colour — and has no hue at
            // all (RGBToHSV reports 0 for achromatic, which is RED). Short-circuit to a
            // neutral ramp rather than lighting a grey spell pink, exactly as
            // ElementalPalette.Retint does.
            bool achromatic = s < 0.02f;

            return new RootPalette
            {
                // Soil: same hue, pushed towards earth. Saturation UP (wet ground is more
                // saturated than dry stem), value well down.
                Soil = achromatic
                    ? new Color(v * 0.30f, v * 0.30f, v * 0.30f, 1f)
                    : Color.HSVToRGB(WarmTowardsEarth(h), Mathf.Clamp01(s * 1.15f + 0.10f), Mathf.Clamp01(v * 0.34f)),

                Bark = achromatic
                    ? new Color(v * 0.62f, v * 0.62f, v * 0.62f, 1f)
                    : Color.HSVToRGB(h, Mathf.Clamp01(s * 0.92f), Mathf.Clamp01(v * 0.72f)),

                Leaf = achromatic
                    ? new Color(v, v, v, 1f)
                    : Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(Mathf.Max(v, 0.70f))),

                // The one layer allowed to approach white, and only in VALUE — the hue is
                // kept so a red root's glow is not a green spell's glow.
                Sap = achromatic
                    ? Color.white
                    : Color.HSVToRGB(h, Mathf.Clamp01(s * 0.55f), 1f),
            };
        }

        /// <summary>
        /// Nudges a hue a few degrees towards earth-brown (~30 deg) so soil reads as soil
        /// rather than as a darker copy of the stem. Bounded hard: a full push would make
        /// every spell's ground the same brown and cost the palette the one thing it is
        /// for, which is that the author's colour survives into all four fields.
        /// </summary>
        private static float WarmTowardsEarth(float h)
        {
            const float earth = 30f / 360f;
            const float pull = 0.22f;
            // Shortest way round the wheel, so a magenta root warms backwards rather than
            // sweeping through every hue between it and brown.
            float delta = Mathf.DeltaAngle(h * 360f, earth * 360f) / 360f;
            float outHue = h + delta * pull;
            return outHue - Mathf.Floor(outHue);
        }

        /// <summary>Opaque white: the project's sentinel for an untouched swatch.</summary>
        public static bool IsUnauthored(Color c)
            => c.a >= 0.999f && c.r >= 0.999f && c.g >= 0.999f && c.b >= 0.999f;
    }
}
