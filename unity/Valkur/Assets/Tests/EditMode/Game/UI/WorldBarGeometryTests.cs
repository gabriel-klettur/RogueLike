using NUnit.Framework;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// The grid the world bars are drawn on, proved without a scene.
    ///
    /// <para>These are the tests the old bars could not have passed. Measured live at 80 px per
    /// world unit, the shipped health bar was 64 x 8 screen pixels — plausible — inside a border
    /// whose padding was <b>3.2 px</b>, above a mana bar <b>5.6 px</b> tall, at a height of
    /// <b>158.3 px</b> above the feet. Not one of those is a whole pixel, in a game whose camera
    /// is snapped to a ladder specifically so that art texels land on whole pixels.</para>
    /// </summary>
    public class WorldBarGeometryTests
    {
        // -- The grid itself --------------------------------------------------

        [Test]
        public void OneTexel_IsOneSixteenthOfAWorldUnit()
        {
            Assert.AreEqual(16, WorldBarGeometry.TEXELS_PER_UNIT);
            Assert.AreEqual(0.0625f, WorldBarGeometry.TEXEL, 1e-6f);
        }

        [Test]
        public void ATexelIsAWholeNumberOfPixels_AtEveryRungOfTheCameraLadder()
        {
            // CameraSetup.SnapOrthoSize solves ortho = pixelHeight / (2 * 16 * N), so px/unit is
            // 16*N for whole N. That is the entire reason a texel-sized bar is crisp and a
            // 0.07-unit one never can be.
            for (int n = 1; n <= 12; n++)
            {
                int viewportHeight = 2 * 16 * n * 5;              // ortho 5 at this rung
                float ppu = WorldBarGeometry.PixelsPerUnit(viewportHeight, 5f);
                float texelPx = ppu * WorldBarGeometry.TEXEL;
                Assert.AreEqual(Mathf.Round(texelPx), texelPx, 1e-4f,
                    $"A texel must be a whole number of pixels at rung {n} (ppu {ppu}).");
            }
        }

        [Test]
        public void TheShippedViewport_MeasuresEightyPixelsPerUnit()
        {
            // The number this whole audit was measured against: 1600x800 at ortho 5.
            Assert.AreEqual(80f, WorldBarGeometry.PixelsPerUnit(800, 5f), 1e-4f);
        }

        [Test]
        public void TheOldBarDimensions_AreAllOffTheGrid()
        {
            // Kept as a regression: these four numbers are what shipped, and every one of them
            // put an edge of the readout at a fraction of a screen pixel.
            Assert.IsFalse(WorldBarGeometry.IsOnTexelGrid(0.8f), "old bar width");
            Assert.IsFalse(WorldBarGeometry.IsOnTexelGrid(0.1f), "old health height");
            Assert.IsFalse(WorldBarGeometry.IsOnTexelGrid(0.07f), "old mana/dash height");
            Assert.IsFalse(WorldBarGeometry.IsOnTexelGrid(0.04f), "old border padding");
        }

        // -- Width ------------------------------------------------------------

        /// <summary>A tall-enough body, so the height cap is not what is under test.</summary>
        private static int Width(float bodyWidth, float bodyHeight = 100f)
            => WorldBarGeometry.WidthTexelsForBody(bodyWidth, bodyHeight, 0.7f, 14, 30);

        [Test]
        public void WidthFromBody_IsAlwaysEven()
        {
            // Odd would put the left inner edge — the one edge a left-anchored fill never moves —
            // at half a texel.
            for (float w = 0.4f; w < 4f; w += 0.031f)
            {
                int t = Width(w);
                Assert.AreEqual(0, t & 1, $"width {w} gave {t} texels");
            }
        }

        [Test]
        public void WidthFromBody_IsClampedBothWays()
        {
            Assert.AreEqual(14, Width(0.05f));
            Assert.AreEqual(30, Width(40f));
        }

        [Test]
        public void WidthFromBody_TracksTheRosterItHasToServe()
        {
            // Measured live: the dwarf's body is 1.219 world units wide and the vampire is baked
            // at 256 px / PPU 96, i.e. 1.48x the dwarf's height. One fixed 0.8 could not serve
            // both, which is what the old bars used for every creature in the game.
            int dwarf = Width(1.219f);
            int wide = Width(1.80f);
            Assert.Greater(wide, dwarf, "A wider body must get a wider bar.");
            Assert.AreEqual(20, dwarf, "1.219 u is 19.5 texels, rounded up to the next even.");
        }

        [Test]
        public void AnUntrimmedSquareCellIsBoundedByItsOwnHeight()
        {
            // The valkyrie's legacy strips are 128 px SQUARE cells: measured live, her sprite is
            // 2.000 x 2.000 whatever she fills of it, and the first live capture gave her a bar
            // 1.875 units wide — 1.6x her drawn body, against the dwarf's 1.03x.
            int valkyrie = WorldBarGeometry.WidthTexelsForBody(2.0f, 2.0f, 0.7f, 14, 30);
            Assert.AreEqual(22, valkyrie,
                "2.0 u capped at 0.7 of its own height is 1.4 u, i.e. 22.4 texels rounded down " +
                "to 22 by the clamp and left even.");

            // And the trimmed art it must not touch: the dwarf, measured, is 0.656 wide over tall.
            int dwarfCapped = WorldBarGeometry.WidthTexelsForBody(1.219f, 1.859f, 0.7f, 14, 30);
            Assert.AreEqual(Width(1.219f), dwarfCapped,
                "A trimmed character is already inside the cap, so it must come out unchanged.");
        }

        // -- Fill -------------------------------------------------------------

        [Test]
        public void Fill_IsQuantisedToWholePixels()
        {
            const float ppu = 80f;
            float inner = WorldBarGeometry.Texels(18);
            for (float r = 0.01f; r <= 1f; r += 0.017f)
            {
                float w = WorldBarGeometry.FillWidth(r, inner, ppu);
                float px = w * ppu;
                Assert.AreEqual(Mathf.Round(px), px, 1e-3f,
                    $"ratio {r} drew {px} pixels, which is not a whole one.");
            }
        }

        [Test]
        public void Fill_NeverExceedsTheBar_AndNeverRoundsANonZeroAway()
        {
            const float ppu = 80f;
            float inner = WorldBarGeometry.Texels(18);

            Assert.AreEqual(0f, WorldBarGeometry.FillWidth(0f, inner, ppu), 1e-6f,
                "Empty must be empty: a sliver at zero would say the creature is alive.");
            Assert.LessOrEqual(WorldBarGeometry.FillWidth(1f, inner, ppu), inner + 1e-6f);

            float tiny = WorldBarGeometry.FillWidth(0.001f, inner, ppu);
            Assert.Greater(tiny, 0f,
                "One point of health left must still draw. Rounding it away is worse than one " +
                "pixel of dishonesty, because the player cannot tell it from a corpse.");
        }

        [Test]
        public void Fill_IsAnchoredOnItsLeftEdge()
        {
            float inner = WorldBarGeometry.Texels(18);
            float leftAtFull = WorldBarGeometry.FillCentreX(inner, inner) - inner * 0.5f;
            float half = WorldBarGeometry.FillWidth(0.5f, inner, 80f);
            float leftAtHalf = WorldBarGeometry.FillCentreX(half, inner) - half * 0.5f;
            Assert.AreEqual(leftAtFull, leftAtHalf, 1e-5f,
                "The left edge is the one edge that must never move. Centring the remainder " +
                "instead moves both edges and puts each on a half pixel at every odd width.");
        }

        // -- The style asset --------------------------------------------------

        [Test]
        public void EveryShippedDimension_IsAWholeNumberOfTexels()
        {
            var style = ScriptableObject.CreateInstance<WorldBarStyle>();
            try
            {
                Assert.IsTrue(WorldBarGeometry.IsOnTexelGrid(style.HealthRowHeight), "health row");
                Assert.IsTrue(WorldBarGeometry.IsOnTexelGrid(style.ResourceRowHeight), "resource row");
                Assert.IsTrue(WorldBarGeometry.IsOnTexelGrid(WorldBarGeometry.Texels(style.rowGapTexels)));
                Assert.IsTrue(WorldBarGeometry.IsOnTexelGrid(WorldBarGeometry.Texels(style.headMarginTexels)));
                Assert.IsTrue(WorldBarGeometry.IsOnTexelGrid(WorldBarGeometry.Texels(style.pipTexels)));
                Assert.IsTrue(WorldBarGeometry.IsOnTexelGrid(WorldBarGeometry.Texels(style.iconTexels)));
            }
            finally { Object.DestroyImmediate(style); }
        }

        [Test]
        public void ARowIsTallEnoughForItsOwnFrame()
        {
            // A frame is one texel top and bottom. A row of two would have no interior at all,
            // and the sliced renderer would draw its two borders over each other.
            var style = ScriptableObject.CreateInstance<WorldBarStyle>();
            try
            {
                Assert.GreaterOrEqual(style.healthRowTexels, 3);
                Assert.GreaterOrEqual(style.resourceRowTexels, 3);
                Assert.Greater(style.healthRowTexels, style.resourceRowTexels,
                    "Health is the value the player acts on. It must be the thicker row, or the " +
                    "stack is three interchangeable rectangles again.");
            }
            finally { Object.DestroyImmediate(style); }
        }

        [Test]
        public void EveryStatusKind_HasATintAndAGlyph()
        {
            var style = ScriptableObject.CreateInstance<WorldBarStyle>();
            try
            {
                foreach (StatusEffectKind kind in System.Enum.GetValues(typeof(StatusEffectKind)))
                {
                    var tint = style.StatusTint((int)kind);
                    Assert.Greater(tint.a, 0f, $"{kind} has no tint.");
                    Assert.Greater(tint.r + tint.g + tint.b, 0.2f,
                        $"{kind}'s tint is near black, which on any background is an absent icon.");
                }
                Assert.AreEqual(System.Enum.GetValues(typeof(StatusEffectKind)).Length,
                    style.statusTints.Length,
                    "The tint array is indexed by the enum's integer value. A kind past the end " +
                    "of it draws white, which says nothing.");
            }
            finally { Object.DestroyImmediate(style); }
        }

        [Test]
        public void EveryRank_HasAFrameAndAHealthColour()
        {
            var style = ScriptableObject.CreateInstance<WorldBarStyle>();
            try
            {
                foreach (WorldBarRank rank in System.Enum.GetValues(typeof(WorldBarRank)))
                {
                    Assert.Greater(style.FrameFor(rank).a, 0f, $"{rank} frame");
                    Assert.Greater(style.HealthFor(rank).a, 0f, $"{rank} health");
                }
                Assert.AreNotEqual(style.FrameFor(WorldBarRank.Boss),
                                   style.FrameFor(WorldBarRank.Normal),
                    "A boss whose frame matches an ordinary creature's is a rank that says nothing.");
            }
            finally { Object.DestroyImmediate(style); }
        }
    }
}
