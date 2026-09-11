using UnityEngine;

namespace Valkur.Core.UI
{
    /// <summary>
    /// The arithmetic behind every world-space bar drawn over an entity's head, with no
    /// Unity object in sight so an EditMode test can prove it without a scene.
    ///
    /// <para><b>Why a texel grid rather than free-floating world units.</b>
    /// <c>CameraSetup.SnapOrthoSize</c> keeps the camera on a ladder where one art texel is a
    /// whole number of screen pixels: <c>ortho = pixelHeight / (2 · snapPPU · N)</c> with
    /// <c>snapPPU = 16</c>, so <c>pixelsPerUnit</c> is always an integer multiple of 16.
    /// It follows that <b>any length that is a multiple of 1/16 of a world unit lands on a whole
    /// screen pixel at every supported resolution</b>, and any length that is not, never does.
    /// The bars shipped at 0.8 / 0.1 / 0.07 / 0.04 units — none of them a multiple of 1/16 —
    /// so measured at 80 px/unit their border padding was 3.2 px and their height 5.6 px, i.e.
    /// every edge of the readout sat at a fraction of a pixel in a game that goes to some
    /// trouble to be pixel-exact everywhere else.</para>
    ///
    /// <para>What this cannot fix, and does not pretend to: the entity itself moves
    /// continuously, so the bar's ABSOLUTE screen position still lands between pixels the same
    /// way every sprite in the game does. What the grid buys is that the bar's own parts —
    /// frame, plate, fill, notches — keep whole-pixel sizes and therefore whole-pixel edges
    /// relative to each other, and that the source texels map 1:1 onto screen pixels instead of
    /// being resampled at 1.6 or 5.6 pixels per texel.</para>
    /// </summary>
    public static class WorldBarGeometry
    {
        /// <summary>Texels per world unit for bar art. Matches <c>CameraSetup</c>'s snap PPU.</summary>
        public const int TEXELS_PER_UNIT = 16;

        /// <summary>One texel, in world units.</summary>
        public const float TEXEL = 1f / TEXELS_PER_UNIT;

        /// <summary>Convert a texel count to world units. The only sanctioned way to size a bar.</summary>
        public static float Texels(float count) => count * TEXEL;

        /// <summary>Nearest length on the texel grid.</summary>
        public static float SnapToTexel(float units) => Mathf.Round(units * TEXELS_PER_UNIT) * TEXEL;

        /// <summary>How many texels a length is, rounded.</summary>
        public static int TexelsOf(float units) => Mathf.RoundToInt(units * TEXELS_PER_UNIT);

        /// <summary>
        /// True when a length is a whole number of texels — the property that makes it land on
        /// whole screen pixels at every snapped ortho size. Used by the tests to refuse a
        /// dimension authored off the grid.
        /// </summary>
        public static bool IsOnTexelGrid(float units, float tolerance = 1e-4f)
        {
            float t = units * TEXELS_PER_UNIT;
            return Mathf.Abs(t - Mathf.Round(t)) <= tolerance;
        }

        /// <summary>
        /// Screen pixels per world unit for an orthographic camera. Returns 0 for a degenerate
        /// camera so callers can treat "unknown" as "do not quantise" rather than dividing by it.
        /// </summary>
        public static float PixelsPerUnit(int viewportHeightPx, float orthographicSize)
        {
            if (orthographicSize <= 0f || viewportHeightPx <= 0) return 0f;
            return viewportHeightPx / (2f * orthographicSize);
        }

        /// <summary>
        /// Round a length to a whole screen pixel. This is what stops a fill that is lerping
        /// toward its target from drawing a boiling edge: the animation stays continuous, the
        /// drawn width does not.
        /// </summary>
        public static float QuantizeToPixel(float units, float pixelsPerUnit)
        {
            if (pixelsPerUnit <= 0f) return units;
            return Mathf.Round(units * pixelsPerUnit) / pixelsPerUnit;
        }

        /// <summary>
        /// The bar's width in texels for a body of <paramref name="bodyWidth"/> world units.
        ///
        /// <para>EVEN on purpose: a fill is anchored to the bar's left inner edge, which sits at
        /// <c>-innerWidth/2</c>, and half of an odd texel count is a half texel — so an odd width
        /// would put the one edge that never moves off the grid.</para>
        ///
        /// <para>A fixed width was the old behaviour and it does not survive this roster: the
        /// dwarf's body measures 1.219 × 1.859 world units and the vampire's 2.667 tall, so one
        /// authored 0.8 is two thirds of one body and a third of the other.</para>
        ///
        /// <para><paramref name="maxFractionOfHeight"/> is what keeps the measurement honest for
        /// art whose frames are NOT trimmed — see the note in the body.</para>
        /// </summary>
        public static int WidthTexelsForBody(float bodyWidth, float bodyHeight,
                                             float maxFractionOfHeight,
                                             int minTexels, int maxTexels)
        {
            if (minTexels > maxTexels) { (minTexels, maxTexels) = (maxTexels, minTexels); }

            // A sprite's RECT is not its body. The five wave3 characters ship frames trimmed to
            // their own alpha, so the rect IS the body — the dwarf measures 1.219 x 1.859. The
            // legacy 8-direction art does not: the valkyrie is a 128 px SQUARE cell and measures
            // 2.000 x 2.000 however much of it she actually fills, so a width taken straight off
            // the rect gave her a bar 1.6x her drawn body while giving the dwarf one 1.03x his.
            //
            // No signal in the project separates the two pipelines, and the texture of an
            // atlas-packed sprite is not readable, so the alpha extents cannot be measured at
            // runtime. What IS available is the proportion: a humanoid is roughly two thirds as
            // wide as it is tall (the dwarf, measured, is 0.656), and a cell that is exactly as
            // wide as it is tall is padding. Capping the width at a fraction of the HEIGHT leaves
            // every trimmed character untouched and bounds the padded ones.
            if (bodyHeight > 0f && maxFractionOfHeight > 0f)
                bodyWidth = Mathf.Min(bodyWidth, bodyHeight * maxFractionOfHeight);

            int texels = Mathf.RoundToInt(bodyWidth * TEXELS_PER_UNIT);
            texels = Mathf.Clamp(texels, Mathf.Max(2, minTexels), Mathf.Max(2, maxTexels));
            if ((texels & 1) != 0) texels++;                 // round UP to even
            return Mathf.Min(texels, MakeEven(maxTexels));
        }

        private static int MakeEven(int v) => (v & 1) == 0 ? v : v + 1;

        /// <summary>
        /// Drawn width of a fill at <paramref name="ratio"/> inside <paramref name="innerWidth"/>,
        /// quantised to a whole screen pixel and clamped so a full bar is exactly full and a
        /// non-zero ratio never rounds away to nothing — a sliver of health the player cannot see
        /// is worse than one pixel of dishonesty.
        /// </summary>
        public static float FillWidth(float ratio, float innerWidth, float pixelsPerUnit)
        {
            ratio = Mathf.Clamp01(ratio);
            if (ratio <= 0f) return 0f;
            float raw = ratio * innerWidth;
            float snapped = QuantizeToPixel(raw, pixelsPerUnit);
            float onePixel = pixelsPerUnit > 0f ? 1f / pixelsPerUnit : raw;
            if (snapped < onePixel) snapped = Mathf.Min(onePixel, innerWidth);
            return Mathf.Min(snapped, innerWidth);
        }

        /// <summary>
        /// Left-anchored centre X for a fill of <paramref name="fillWidth"/> inside a bar of
        /// <paramref name="innerWidth"/>. Anchoring on the LEFT EDGE rather than centring the
        /// remainder is what keeps that edge on the grid: it is a constant, and only the right
        /// edge moves.
        /// </summary>
        public static float FillCentreX(float fillWidth, float innerWidth)
            => -innerWidth * 0.5f + fillWidth * 0.5f;
    }
}
