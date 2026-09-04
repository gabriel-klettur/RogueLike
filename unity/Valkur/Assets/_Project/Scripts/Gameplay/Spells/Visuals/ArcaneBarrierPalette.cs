using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The six colours a woven barrier is painted with, all derived from ONE authored swatch.
    ///
    /// <para>WHY DERIVED. <see cref="KiPalette"/> made the same argument for the ki aura and it
    /// holds here for the same reason: the RELATIONS are the design. The lattice is always
    /// closer to white than the weave it holds, the glyphs are always the most saturated thing
    /// on the barrier, and the seal burnt into the floor is always the darkest. Six hand-tuned
    /// colours per barrier would be six numbers that have to stay in that order, and the first
    /// person to author a barrier whose lattice is darker than its weave gets a shape with no
    /// readable edge.</para>
    ///
    /// <para>WHY THE TEXTURES ARE GREY. Every <c>ArcaneSprites</c> map is luminance only, so
    /// the colour arrives through <c>SpriteRenderer.color</c> and a barrier can be any hue its
    /// <c>SpellDefinition.particleColor</c> asks for. <see cref="IceSprites"/> could not do
    /// that — an ice crystal runs deep blue at the base to near-white at the tip, and no
    /// single tint expresses a HUE ramp. A magic membrane is one hue with a LUMINANCE ramp,
    /// and a tint expresses that exactly.</para>
    /// </summary>
    internal struct ArcaneBarrierPalette
    {
        /// <summary>
        /// The hard contour: anchor shafts, the top edge, fractures, the flash of a hit.
        ///
        /// <para>The SAME HUE as <see cref="Weave"/> and most of its saturation — hotter, never
        /// whiter. It used to lerp 82 % of the way to white, which took a violet barrier's
        /// contour to (0.903, 0.821, 1.000): the one hard line in the whole rig was the one
        /// piece that did not carry the spell's colour, and the anchors read as white posts
        /// holding up a violet membrane.</para>
        ///
        /// <para>Brightness is NOT the lever that makes it read as an edge, which is why giving
        /// the colour back costs nothing. Alpha is coverage on an additive surface, and the
        /// contour's texture is solid where the membrane's interior is about a tenth opaque —
        /// so at an identical colour the lattice still adds roughly eight times the light per
        /// pixel. An HDR overdrive would be the wrong tool here for the opposite reason: with
        /// no tonemapping the framebuffer clamps at 1, so pushing a violet past it lands on
        /// white, which is the exact defect being removed.</para>
        /// </summary>
        public Color Lattice;

        /// <summary>The membrane itself. What the barrier reads AS.</summary>
        public Color Weave;

        /// <summary>Panel interiors and the plane haze. Dim, so the rims stay the shape.</summary>
        public Color Deep;

        /// <summary>Glyphs and motes. The most saturated thing on the barrier.</summary>
        public Color Rune;

        /// <summary>What the Light2D throws. Between lattice and weave, so lit surfaces agree.</summary>
        public Color Light;

        /// <summary>The seal inscribed into the floor. The one NON-additive layer.</summary>
        public Color Seal;

        /// <summary>
        /// Build from the spell's own <c>particleColor</c>.
        ///
        /// <para>Opaque white is this project's "nobody authored this" sentinel, and the test
        /// for it is <see cref="KiPalette.IsUnauthored"/> rather than a copy — a second copy is
        /// how the two answers drift apart the first time the sentinel is reconsidered. The
        /// fallback is arcane violet, which is what a spell named <c>arcane_barrier</c> would
        /// have asked for anyway.</para>
        ///
        /// <para>An ACHROMATIC swatch is a separate case and needs its own branch: RGBToHSV
        /// reports hue 0 for grey, and hue 0 is RED, so deriving a saturated glyph colour from
        /// a grey barrier the naive way lights it pink. Below the saturation floor every field
        /// stays neutral at its own brightness.</para>
        ///
        /// <para>A NEAR-BLACK swatch is the third case and it is the one that fails hardest.
        /// Every layer but the seal is ADDITIVE, and near-black adds nothing at all: a barrier
        /// authored at value 0.04 would not be a dark barrier, it would be an absent one —
        /// measured before the floor below, the weave came out (0.040, 0.040, 0.050) and the
        /// glyphs (0.102, 0.102, 0.158). <c>ElementPalette.Retint</c> records the identical
        /// rule for the cast flourish. The floors lift VALUE only, so the hue a designer
        /// picked survives and a dark barrier still reads as the darkest one they can ask
        /// for. The seal is exempt: it is the one non-additive layer and dark is what it is
        /// FOR.</para>
        /// </summary>
        public static ArcaneBarrierPalette From(Color authored)
        {
            Color baseColor = KiPalette.IsUnauthored(authored)
                ? new Color(0.72f, 0.48f, 1f, 1f)
                : new Color(authored.r, authored.g, authored.b, 1f);

            Color.RGBToHSV(baseColor, out float h, out float s, out float v);

            bool achromatic = s < 0.02f;

            // How much of the weave's saturation the contour keeps. Driving VALUE to 1 is what
            // makes it hotter; this is what stops that becoming a desaturation. A very dark or
            // very washed swatch still ends up pale simply because it has little saturation to
            // keep, which is the correct outcome — that barrier has no colour to show.
            const float LatticeSaturationKeep = 0.78f;

            // Additive floors. See the class doc: below these the layer stops existing rather
            // than getting darker.
            const float WeaveValueFloor = 0.34f;
            const float RuneValueFloor = 0.52f;
            Color weave = v < WeaveValueFloor
                ? (achromatic
                    ? new Color(WeaveValueFloor, WeaveValueFloor, WeaveValueFloor, 1f)
                    : Color.HSVToRGB(h, s, WeaveValueFloor))
                : baseColor;

            return new ArcaneBarrierPalette
            {
                Lattice = achromatic
                    ? new Color(1f, 1f, 1f, 1f)
                    : Color.HSVToRGB(h, Mathf.Clamp01(s * LatticeSaturationKeep), 1f),
                Weave = weave,
                Deep = achromatic
                    ? new Color(v * 0.55f, v * 0.55f, v * 0.55f, 1f)
                    : Color.HSVToRGB(h, Mathf.Clamp01(s * 1.1f), Mathf.Max(0.18f, v * 0.55f)),
                Rune = achromatic
                    ? new Color(1f, 1f, 1f, 1f)
                    : Color.HSVToRGB(h, Mathf.Clamp01(s * 1.35f + 0.08f),
                        Mathf.Clamp01(Mathf.Max(RuneValueFloor, v * 1.15f + 0.10f))),
                Light = Color.Lerp(baseColor, Color.white, 0.30f),
                // Deliberately dark: the seal is the barrier's only unlit layer, and a dark
                // mark on the floor is the whole of what says the world has been altered
                // rather than merely lit. On an additive surface it would add nothing.
                Seal = achromatic
                    ? new Color(v * 0.30f, v * 0.30f, v * 0.30f, 1f)
                    : Color.HSVToRGB(h, Mathf.Clamp01(s * 1.2f), Mathf.Max(0.12f, v * 0.34f)),
            };
        }
    }
}
