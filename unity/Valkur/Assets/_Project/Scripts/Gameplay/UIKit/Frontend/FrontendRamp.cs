using UnityEngine;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// The vertical light profile of a FILL — the loading bar's molten body and the menu's
    /// selected row are the same shape of light, at two different depths.
    ///
    /// <para>Luminance only, 0..1, multiplied by the tint by whoever draws it, so amber and red
    /// states keep the profile and change the hue.</para>
    ///
    /// <para><b>Two depths, because text sits on one of them.</b> The bar's floor goes down to
    /// ~0.16 of the tint: nothing is written on the bar, and a dark molten floor is what makes
    /// the band two thirds up read as light. A selected ROW carries a label in dark ink, so its
    /// profile keeps the same band and lip over a floor that never drops below
    /// <see cref="RowFloor"/> — measured in <c>MenuContrastTests</c> against the darkest point
    /// under the text, not against a flat colour.</para>
    /// </summary>
    public static class FrontendRamp
    {
        /// <summary>The lowest luminance factor a selected row reaches anywhere under its label.</summary>
        public const float RowFloor = 0.80f;

        /// <summary>The loading bar's profile, unchanged from the sprite it used to bake. v = 0 foot, 1 head.</summary>
        public static float Bar(float v)
        {
            float body = Mathf.Lerp(0.34f, 0.80f, Mathf.SmoothStep(0f, 1f, v / 0.62f));
            float band = Mathf.Exp(-Mathf.Pow((v - 0.74f) / 0.07f, 2f)) * 0.2f;
            float lip = v > 0.9f ? Mathf.Lerp(0f, 0.22f, (v - 0.9f) / 0.1f) : 0f;
            float floor = v < 0.1f ? Mathf.Lerp(0.18f, 0f, v / 0.1f) : 0f;
            return Mathf.Clamp01(body + band - lip - floor);
        }

        /// <summary>The selected row's profile: the bar's band and lip over a floor dark ink stays legible on.</summary>
        public static float Row(float v)
        {
            float body = Mathf.Lerp(RowFloor, 0.94f, Mathf.SmoothStep(0f, 1f, v / 0.62f));
            float band = Mathf.Exp(-Mathf.Pow((v - 0.74f) / 0.08f, 2f)) * 0.10f;
            float lip = v > 0.9f ? Mathf.Lerp(0f, 0.12f, (v - 0.9f) / 0.1f) : 0f;
            return Mathf.Clamp(body + band - lip, RowFloor, 1f);
        }

        /// <summary>The additive gloss over the upper half of a fill: a soft highlight, nothing below.</summary>
        public static float Gloss(float v)
            => Mathf.Clamp01(Mathf.Exp(-Mathf.Pow((v - 0.76f) / 0.12f, 2f)) * 0.8f + (v > 0.92f ? 0.15f : 0f));

        /// <summary>A tint scaled by a luminance factor, alpha kept.</summary>
        public static Color Shade(Color tint, float l)
            => new Color(tint.r * l, tint.g * l, tint.b * l, tint.a);
    }
}
