using UnityEngine;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// The metal of the pre-game surfaces: the near-black outline, the bronze-to-gold bevel, the
    /// recessed channel and the dim stone of an unlit gem.
    ///
    /// <para><b>These are the housing, not the state.</b> What a surface is SAYING — selected,
    /// stalled, failed, dangerous — is the tint, and the tint always comes from
    /// <c>MenuStyle</c> (or from the fill the loading screen already owns). The bevel stays
    /// bronze whatever the tint is, which is what lets an amber or red element still read as the
    /// same object in a different mood.</para>
    ///
    /// <para>One copy. Before the kit existed the loading bar's frame and its segments each
    /// declared their own outline, and the two were one edit away from disagreeing.</para>
    /// </summary>
    public static class FrontendPalette
    {
        public static readonly Color Outline = new Color(0.035f, 0.024f, 0.016f, 1f);
        public static readonly Color BronzeDark = new Color(0.36f, 0.22f, 0.09f, 1f);
        public static readonly Color GoldLight = new Color(0.98f, 0.82f, 0.46f, 1f);
        public static readonly Color RecessTop = new Color(0.018f, 0.013f, 0.010f, 1f);
        public static readonly Color RecessBottom = new Color(0.095f, 0.065f, 0.045f, 1f);
        public static readonly Color GemDim = new Color(0.16f, 0.10f, 0.07f, 1f);
        public static readonly Color NotchDim = new Color(0.30f, 0.19f, 0.09f, 1f);

        /// <summary>The warm light the channel's lower lip catches. Twelve percent: a groove, not a stripe.</summary>
        public static readonly Color LipLight = new Color(1f, 0.78f, 0.45f, 0.12f);

        /// <summary>The deep orange an ember cools to. Particles blend toward it, never toward white.</summary>
        public static readonly Color EmberDeep = new Color(1.00f, 0.36f, 0.08f, 1f);

        /// <summary>The warm white of a sheen or an edge core. Pure white reads as a UI glitch over gold.</summary>
        public static readonly Color WarmWhite = new Color(1f, 0.97f, 0.88f, 1f);
    }
}
