using NUnit.Framework;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.HUD;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Gameplay.HUD.Grimoire
{
    /// <summary>
    /// The grimoire's layout, as arithmetic.
    ///
    /// <para>uGUI performs no layout in Edit Mode, so the ONLY honest way to pin a window's
    /// geometry is to state the rule as a function and check the function. Every structural
    /// probe of the shipped Controls editor was green while its window was unreadable, and the
    /// character sheet's own tab strip overflowed its band by 68 units with nothing able to
    /// see it.</para>
    /// </summary>
    [TestFixture]
    public class GrimoireGeometryTests
    {
        private GrimoireStyle _style;

        [SetUp]
        public void SetUp()
        {
            _style = ScriptableObject.CreateInstance<GrimoireStyle>();
            _style.hideFlags = HideFlags.HideAndDontSave;
        }

        [TearDown]
        public void TearDown()
        {
            if (_style != null) Object.DestroyImmediate(_style);
        }

        // ── The panel ───────────────────────────────────────────────────────────

        [Test]
        public void ThePanelIsMeasuredAgainstTheHudReference()
        {
            var canvas = GrimoireGeometry.PanelCanvasSize();
            Assert.AreEqual((GrimoireGeometry.PanelRight - GrimoireGeometry.PanelLeft)
                            * HudLayout.ReferenceWidth, canvas.x, 0.01f);
            Assert.AreEqual((GrimoireGeometry.PanelTop - GrimoireGeometry.PanelBottom)
                            * HudLayout.ReferenceHeight, canvas.y, 0.01f);
        }

        [Test]
        public void PanelTexels_AreWholeAndFloored_AtEveryShippedScale()
        {
            // 2 at 1600x800, 3 at 1080p, 5 at 4K. Half a texel of room is no room: a rect that
            // claims it lands off the grid, which is the point of having one.
            foreach (int scale in new[] { 1, 2, 3, 5 })
            {
                var texels = GrimoireGeometry.PanelTexels(scale);
                Assert.Greater(texels.x, 0, "scale " + scale);
                Assert.Greater(texels.y, 0, "scale " + scale);
                Assert.LessOrEqual(texels.x * scale, GrimoireGeometry.PanelCanvasSize().x + 0.001f,
                    "scale " + scale + " claims more canvas than the panel has");
                Assert.LessOrEqual(texels.y * scale, GrimoireGeometry.PanelCanvasSize().y + 0.001f,
                    "scale " + scale);
            }
        }

        [Test]
        public void AZeroOrNegativeScale_IsTreatedAsOne_NeverDividedBy()
        {
            Assert.AreEqual(GrimoireGeometry.PanelTexels(1), GrimoireGeometry.PanelTexels(0));
            Assert.AreEqual(GrimoireGeometry.PanelTexels(1), GrimoireGeometry.PanelTexels(-4));
        }

        // ── The three columns ───────────────────────────────────────────────────

        [Test]
        public void TheColumnsTileTheInnerWidth_WithoutOverlapping()
        {
            var panel = GrimoireGeometry.PanelTexels(2);
            var frame = GrimoireGeometry.Split(panel, _style);

            Assert.AreEqual(frame.Rail.xMax + GrimoireGeometry.ColumnGap, frame.Board.xMin,
                "the board starts one gap after the rail");
            Assert.AreEqual(frame.Board.xMax + GrimoireGeometry.ColumnGap, frame.Card.xMin,
                "the card starts one gap after the board");
            Assert.LessOrEqual(frame.Card.xMax, panel.x - _style.paddingTexels,
                "the card must not run past the panel's padding");
        }

        [Test]
        public void EveryBandStaysInsideThePanel()
        {
            var panel = GrimoireGeometry.PanelTexels(2);
            var frame = GrimoireGeometry.Split(panel, _style);

            foreach (var band in new[] { frame.Rail, frame.Board, frame.Card,
                                         frame.Title, frame.Footer })
            {
                Assert.GreaterOrEqual(band.xMin, 0);
                Assert.GreaterOrEqual(band.yMin, 0);
                Assert.LessOrEqual(band.xMax, panel.x);
                Assert.LessOrEqual(band.yMax, panel.y);
                Assert.Greater(band.width, 0, "a band with no width is a band nobody can see");
                Assert.Greater(band.height, 0);
            }
        }

        [Test]
        public void TheTitleAndFooterDoNotOverlapTheColumns()
        {
            var panel = GrimoireGeometry.PanelTexels(2);
            var frame = GrimoireGeometry.Split(panel, _style);

            Assert.GreaterOrEqual(frame.Title.yMin, frame.Board.yMax,
                "the title band sits above the columns");
            Assert.LessOrEqual(frame.Footer.yMax, frame.Board.yMin,
                "the footer sits below them");
        }

        [Test]
        public void TheRailKeepsItsFullWidth_AndTheBoardTakesWhatIsLeft()
        {
            // Squeezing the rail is what truncates school names, which is the defect this
            // window was rebuilt out of. The board is the only column whose content scales.
            var panel = GrimoireGeometry.PanelTexels(2);
            var frame = GrimoireGeometry.Split(panel, _style);

            Assert.AreEqual(_style.railWidthTexels, frame.Rail.width,
                "the rail is wide enough at the reference resolution to keep its authored width");
            Assert.Greater(frame.Board.width, frame.Rail.width,
                "the constellation should be the largest thing in the window");
        }

        [Test]
        public void ANarrowPanel_ShrinksTheColumns_WithoutProducingNegativeWidths()
        {
            var frame = GrimoireGeometry.Split(new Vector2Int(60, 40), _style);
            Assert.GreaterOrEqual(frame.Rail.width, 0);
            Assert.GreaterOrEqual(frame.Board.width, 0);
            Assert.GreaterOrEqual(frame.Card.width, 0);
            Assert.GreaterOrEqual(frame.Rail.height, 0);
        }

        // ── The constellation ───────────────────────────────────────────────────

        [Test]
        public void SpacingIsDerivedFromTheNodeSize_SoRetuningOneMovesBoth()
        {
            float before = GrimoireGeometry.DepthSpacing(_style);
            _style.nodeTexels *= 2;
            Assert.AreEqual(before * 2f, GrimoireGeometry.DepthSpacing(_style), 0.001f,
                "a spacing that did not follow the node size would let the two disagree");
        }

        [Test]
        public void SiblingSpacing_ClearsTheNodeAndItsTwoCaptions()
        {
            Assert.Greater(GrimoireGeometry.SiblingSpacing(_style),
                           _style.nodeTexels + _style.captionTexels * 2,
                           "neighbouring captions would touch");
        }

        [Test]
        public void TheRimIsInsideTheNode_SoALinkStopsBeforeTheSocketsFace()
        {
            float rim = GrimoireGeometry.RimRadius(_style);
            Assert.Greater(rim, 0f);
            Assert.Less(rim, _style.nodeTexels * 0.5f,
                "the socket ring is not opaque: a wire 'behind' a node is drawn across it");
        }

        [Test]
        public void FitZoom_NeverScalesASchoolUp()
        {
            // Every school must render at the SAME node size, or a node visibly resizes on
            // every rail click.
            float zoom = GrimoireGeometry.FitZoom(new Vector2(10f, 10f),
                                                  new Vector2(400f, 400f), 4f);
            Assert.AreEqual(1f, zoom, 0.0001f);
        }

        [Test]
        public void FitZoom_ShrinksABoardTooBigToFit_AndIsNotClampedFromBelow()
        {
            float zoom = GrimoireGeometry.FitZoom(new Vector2(1000f, 200f),
                                                  new Vector2(200f, 200f), 4f);
            Assert.Less(zoom, 1f);
            Assert.Greater(zoom, 0f,
                "a school too big to fit must still be shown whole; the shape is the point");
        }

        [Test]
        public void FitZoom_SurvivesADegenerateViewport()
        {
            Assert.AreEqual(1f, GrimoireGeometry.FitZoom(Vector2.zero, Vector2.zero, 4f), 0.0001f);
            Assert.AreEqual(1f, GrimoireGeometry.FitZoom(new Vector2(100f, 100f),
                                                         new Vector2(2f, 2f), 4f), 0.0001f);
        }

        [Test]
        public void AnEmptySchool_MeasuresToSomethingDivisible()
        {
            var board = GrimoireGeometry.Measure(null, _style);
            Assert.Greater(board.Size.x, 0f);
            Assert.Greater(board.Size.y, 0f);
        }

        // The rail row, measured rather than looked at
        // ----------------------------------------------------------------------
        // Both numbers below overflowed on a RENDERED FRAME before anybody could see them,
        // which is the expensive way to learn a layout. Stated as arithmetic they are
        // answerable here, against the shipped font and the shipped style, with no canvas.

        // The node caption's wrap
        // ----------------------------------------------------------------------
        // The caption used to be a uGUI Text with resizeTextForBestFit, which solves every
        // label INDEPENDENTLY - so a board of eight spells drew eight type sizes. The rail had
        // already been rebuilt out of that exact defect. A bitmap face cannot shrink, so the
        // fitting has to happen in the WRAP, and a wrap is arithmetic a test can hold.

        [Test]
        public void TheWrap_BreaksOnSpaces_AndEveryLineFits()
        {
            int width = 48;
            var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);

            foreach (var name in new[] { "ANDANADA", "LLUVIA DE METEOROS",
                                         "MALDICION DE FRAGILIDAD", "ESQUIRLA RASTREADORA",
                                         "TAJO (ESTOCADA)", "KI ESPIRITUAL" })
            {
                var lines = GrimoireCaption.Wrap(name, width, 2);
                Assert.Greater(lines.Count, 0, name);
                Assert.LessOrEqual(lines.Count, 2, name);

                foreach (var line in lines)
                {
                    // A single word wider than the box is allowed to overhang rather than be
                    // cut, so the assertion is on lines that HAVE a break available.
                    if (line.Contains(" "))
                        Assert.LessOrEqual(
                            HudPixelFont.MeasureWidth(line, HudFontFace.Small, glyphs), width,
                            name + " -> '" + line + "'");
                }
            }
        }

        [Test]
        public void TheWrap_KeepsEveryWord()
        {
            // A caption that silently drops its tail is indistinguishable from a spell with a
            // short name, which is the worst kind of missing text: it looks like data.
            var lines = GrimoireCaption.Wrap("UNO DOS TRES CUATRO CINCO SEIS", 20, 2);
            var joined = string.Join(" ", lines.ToArray());
            foreach (var word in new[] { "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS" })
                Assert.IsTrue(joined.Contains(word), word + " was dropped from '" + joined + "'");
        }

        [Test]
        public void TheWrap_NeverCutsAWordThatCannotFit()
        {
            // There is nothing to cut in the shipped 71, and if one ever arrives, overhanging
            // is the failure somebody can SEE - a cut word is a different word, and this same
            // caption also carries the reason a node is shut.
            var lines = GrimoireCaption.Wrap("ELECTROENCEFALOGRAFISTA", 12, 2);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("ELECTROENCEFALOGRAFISTA", lines[0]);
        }

        [Test]
        public void TheWrap_SurvivesNothingToSay()
        {
            Assert.AreEqual(0, GrimoireCaption.Wrap(null, 40, 2).Count);
            Assert.AreEqual(0, GrimoireCaption.Wrap("", 40, 2).Count);
            Assert.AreEqual(0, GrimoireCaption.Wrap("   ", 40, 2).Count,
                "a string of spaces has no words in it, and a blank line is not a line");
        }

        // The pixel scale's ceiling
        // ----------------------------------------------------------------------

        [Test]
        public void TheScale_IsCappedByWhatTheWindowCanSpell()
        {
            // A higher-DPI screen gets FEWER texels for this window, because the panel is a
            // fixed fraction of a fixed canvas. Everything sized in texels rides that happily;
            // the two things sized by a BITMAP FACE cannot, because such a face has exactly
            // one size. Measured before the cap: at 1080p the board fell to 110 texels for a
            // chip row that inks 270, so the window was at its BEST on the smallest screen.
            int ideal = 270;
            int band = 190;
            int capped = GrimoireGeometry.PixelScaleFor(5, _style, ideal, band);

            Assert.LessOrEqual(capped, 5);
            Assert.GreaterOrEqual(capped, 1);
            Assert.GreaterOrEqual(GrimoireGeometry.PanelTexels(capped).x,
                                  GrimoireGeometry.MinPanelWidth(_style, ideal),
                                  "the scale the window settles on must leave it room to " +
                                  "lay out its own fixed-size text");

            // BOTH axes. The width happened to bind first for the shipped data, which is luck
            // rather than a reason: at scale 3 the panel fails the width by 160 AND the band
            // by 44, and the rail's nine rows alone need 170 texels against 142 of column.
            Assert.GreaterOrEqual(GrimoireGeometry.PanelTexels(capped).y,
                                  GrimoireGeometry.MinPanelHeight(_style, band),
                                  "a cap that only checks the width is right by coincidence " +
                                  "until somebody retunes a rail row's height");
        }

        [Test]
        public void TheBandMinimum_TakesTheWorseOfTheBoardAndTheRail()
        {
            // The rail does not scroll and the board does not shrink, so the band has to hold
            // whichever of them is taller - not the board alone, which is the half that first
            // shipped and is exactly the half that hides a dropped ninth school.
            int railHeavy = GrimoireGeometry.BandMinTexels(_style, 10, 20);
            int boardHeavy = GrimoireGeometry.BandMinTexels(_style, 400, 1);

            Assert.GreaterOrEqual(railHeavy, 20 * _style.railRowTexels,
                "twenty rail rows must be counted even when the constellation is tiny");
            Assert.GreaterOrEqual(boardHeavy, 400,
                "a tall constellation must be counted even when the rail is one row");
        }

        [Test]
        public void TheBandMinimum_CountsTheFilterRowAgainstTheBoard()
        {
            // The chip row shares the board's column rather than living in the footer, so it
            // comes off the same budget. Forgetting it is thirteen texels of constellation
            // drawn underneath eight chips.
            Assert.AreEqual(100 + _style.filterChipTexels,
                            GrimoireGeometry.BandMinTexels(_style, 100, 0));
        }

        [Test]
        public void TheScale_NeverRisesAboveWhatWasAskedFor()
        {
            // The cap only ever takes scale AWAY. A window that chose its own scale upward
            // would be drawing at a different pixel size from the panel beside it, which is
            // the one property the whole texel grid exists to hold.
            for (int wanted = 1; wanted <= 6; wanted++)
                Assert.LessOrEqual(GrimoireGeometry.PixelScaleFor(wanted, _style, 270, 190), wanted);
        }

        [Test]
        public void TheScale_FallsBackToOne_RatherThanRefusingToDraw()
        {
            // An impossible demand must still produce a window. A panel that cannot fit its
            // filter row is worse at scale 1 than at scale 3 and is still a readable board,
            // whereas refusing is a tab that opens onto nothing.
            Assert.AreEqual(1, GrimoireGeometry.PixelScaleFor(4, _style, 100000, 190));
        }

        [Test]
        public void TheChosenScale_GivesAnUnclampedRail_AtEveryShippedScreen()
        {
            // Split clamps the rail to a third of the inner column, and a clamped rail is a
            // truncated school name - the defect this window was rebuilt out of, coming back
            // through the resolution rather than through the style.
            foreach (var wanted in new[] { 1, 2, 3, 4, 5 })
            {
                int scale = GrimoireGeometry.PixelScaleFor(wanted, _style, 270, 190);
                var frame = GrimoireGeometry.Split(GrimoireGeometry.PanelTexels(scale), _style);
                Assert.AreEqual(_style.railWidthTexels, frame.Rail.width,
                    "at scale " + scale + " (asked " + wanted + ") the rail is clamped");
            }
        }

        [Test]
        public void MinPanelWidth_CountsTheRailTheCardAndTheBoard()
        {
            int a = GrimoireGeometry.MinPanelWidth(_style, 100);
            int b = GrimoireGeometry.MinPanelWidth(_style, 400);
            Assert.Greater(b, a, "a wider filter row needs a wider panel");
            Assert.GreaterOrEqual(a, _style.railWidthTexels * 3,
                "anything narrower than three rails clamps the rail");
        }

        [Test]
        public void TheRailRow_ReservesItsMarkItsNameAndItsCount()
        {
            var frame = GrimoireGeometry.Split(GrimoireGeometry.PanelTexels(2), _style);
            int used = GrimoireGeometry.RailNameX
                     + GrimoireGeometry.RailNameBox(frame.Rail.width)
                     + GrimoireGeometry.RailNameGap
                     + GrimoireGeometry.RailCountTexels;

            Assert.LessOrEqual(used, frame.Rail.width,
                "the row's three pieces must fit the rail they are drawn in");
        }

        [Test]
        public void TheRailsMark_IsDrawnAtItsNativeSize()
        {
            // Point filtering does not scale a 9x9 mark down to 7 - it DROPS two rows and two
            // columns, so the martial X loses its arms and the cryomancy star loses two points.
            // Coverage, not detail: the number lives in the atlas and the draw lives here.
            Assert.AreEqual(GrimoireArt.SigilTexels, GrimoireGeometry.RailSigilTexels,
                "the rail must draw a sigil at the size the atlas authors it");
        }

        // The filter row, as a total
        // ----------------------------------------------------------------------

        [Test]
        public void TheFilterRow_NeverAsksForMoreThanItsColumn()
        {
            var frame = GrimoireGeometry.Split(GrimoireGeometry.PanelTexels(2), _style);
            var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);

            // The labels the row really draws, taken from the production string source rather
            // than retyped: their ACCENTS are the whole difference - "PROTECCION" and
            // "PROTECCION" with an accent are different widths in a bitmap face, and a test
            // measuring the wrong one reports a row that fits while the shipped one overflows.
            var roles = new System.Collections.Generic.List<string> { GrimoireText.AllRoles };
            foreach (SpellRole role in System.Enum.GetValues(typeof(SpellRole)))
                roles.Add(GrimoireText.Role(role));

            var widths = new int[roles.Count];
            for (int i = 0; i < roles.Count; i++)
            {
                string label = roles[i].ToUpperInvariant();
                Assert.IsTrue(HudPixelFont.CanSpell(label, glyphs),
                    label + " cannot be spelled by the chip face, so it would draw as a hole");
                widths[i] = HudPixelFont.MeasureWidth(label, HudFontFace.Small, glyphs)
                          + GrimoireGeometry.ChipPadding;
            }

            int available = GrimoireGeometry.FilterAvailable(
                frame.Board.width, roles.Count, _style.filterChipGapTexels);

            GrimoireGeometry.SqueezeToFit(widths, available);

            int total = 0;
            foreach (var w in widths) total += w;
            Assert.LessOrEqual(total, available,
                "the eight chips are sized by their own ink, so only their TOTAL can say " +
                "whether the row runs off the board. It fitted for two iterations and then " +
                "the rail took width from the board and the last chip was cut in half.");
        }

        [Test]
        public void TheSqueeze_TakesFromTheWidestChipsFirst()
        {
            // Proportional, never left to right. A greedy walk from the left hands the whole
            // overflow to whichever chip comes first, which on this row is the shortest word.
            var widths = new[] { 12, 40, 60 };
            GrimoireGeometry.SqueezeToFit(widths, 100);

            Assert.AreEqual(100, widths[0] + widths[1] + widths[2]);
            Assert.Greater(widths[2], widths[1], "the longest word stays the widest chip");
            Assert.Greater(widths[1], widths[0]);
            Assert.Greater(widths[0], 8,
                "the shortest chip must not pay for the row on its own");
        }

        [Test]
        public void TheSqueeze_LeavesARowThatAlreadyFitsAlone()
        {
            var widths = new[] { 12, 40, 60 };
            GrimoireGeometry.SqueezeToFit(widths, 400);
            Assert.AreEqual(new[] { 12, 40, 60 }, widths);
        }

        [Test]
        public void TheSqueeze_SurvivesARowThatCannotFitAtAll()
        {
            // Every chip already at the floor and still too wide: it must not loop forever
            // and must not produce a negative width, which draws as an inverted rect.
            var widths = new[] { GrimoireGeometry.ChipMinTexels, GrimoireGeometry.ChipMinTexels };
            GrimoireGeometry.SqueezeToFit(widths, 4);
            foreach (var w in widths) Assert.GreaterOrEqual(w, GrimoireGeometry.ChipMinTexels);
        }
    }
}
