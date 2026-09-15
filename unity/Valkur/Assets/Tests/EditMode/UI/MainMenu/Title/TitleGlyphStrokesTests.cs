using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.UI;

namespace Valkur.Tests.EditMode.UI.MainMenu.Title
{
    /// <summary>
    /// The title's letterforms. Pure data and pure maths, so all of this runs with no scene, no
    /// canvas and no Play Mode — which is the reason the title is a stroke table rather than a
    /// texture or a TextMeshPro readback in the first place.
    /// </summary>
    public class TitleGlyphStrokesTests
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Digits = "0123456789";

        [Test]
        public void EveryLetterAndDigit_HasStrokesAndAnAdvance()
        {
            foreach (char c in Alphabet + Digits)
            {
                Assert.IsTrue(TitleGlyphStrokes.Has(c), $"'{c}' has no glyph");
                var strokes = TitleGlyphStrokes.Strokes(c);
                Assert.IsNotNull(strokes, $"'{c}' resolved to null strokes");
                Assert.Greater(strokes.Length, 0, $"'{c}' has an empty stroke list");
                Assert.Greater(TitleGlyphStrokes.Advance(c), 0f, $"'{c}' has no advance");
                foreach (var stroke in strokes)
                    Assert.GreaterOrEqual(stroke.Length, 2,
                        $"'{c}' has a stroke with fewer than two points, which draws nothing");
            }
        }

        /// <summary>
        /// The alphabet is complete even though the shipped title is six letters. That is what
        /// makes the title DATA: renaming the game is a string change, not an art change.
        /// </summary>
        [Test]
        public void TheAlphabetIsComplete_SoTheTitleIsAString()
        {
            Assert.IsTrue(TitleGlyphStrokes.Has(' '), "a space must be spellable");
            foreach (char c in "VALKUR")
                Assert.IsTrue(TitleGlyphStrokes.Has(c), $"the shipped title needs '{c}'");
        }

        [Test]
        public void EveryPoint_SitsInsideItsOwnEmBox()
        {
            foreach (char c in Alphabet + Digits)
            {
                float advance = TitleGlyphStrokes.Advance(c);
                foreach (var stroke in TitleGlyphStrokes.Strokes(c))
                    foreach (var p in stroke)
                    {
                        Assert.GreaterOrEqual(p.x, -0.02f, $"'{c}' reaches left of its box");
                        // Q's tail ends exactly ON advance + 0.02, and a float comparison at the
                        // boundary loses by one ulp. The bound is about a glyph ESCAPING its box,
                        // not about float equality, so it is stated with room to be exact.
                        Assert.LessOrEqual(p.x, advance + 0.04f, $"'{c}' reaches past its advance");
                        // Q's tail and the comma descend BELOW the baseline on purpose, so the
                        // floor is not zero. The ceiling is the cap line PLUS the declared
                        // overshoot: a pointed or round letter is drawn a little past its line on
                        // purpose, and reading that allowance from TitleGlyphStrokes.Overshoot
                        // rather than from a literal is what keeps this a guard instead of a
                        // number somebody widens whenever it goes red.
                        Assert.GreaterOrEqual(p.y, -0.12f, $"'{c}' descends further than a tail");
                        Assert.LessOrEqual(p.y, 1f + TitleGlyphStrokes.Overshoot + 0.001f,
                                           $"'{c}' rises above the cap height by more than the " +
                                           "declared overshoot");
                    }
            }
        }

        [Test]
        public void AnUnknownCharacter_IsSkipped_NotSubstituted()
        {
            // Drawing a box or a fallback letter would put a character in the game's name that
            // nobody authored.
            Assert.IsFalse(TitleGlyphStrokes.Has('Ñ'), "the table should not claim N-tilde");
            Assert.AreEqual(0f, TitleGlyphStrokes.Advance('Ñ'));

            float withUnknown = TitleGlyphStrokes.MeasureEm("VAÑLKUR");
            float without = TitleGlyphStrokes.MeasureEm("VALKUR");
            Assert.AreEqual(without, withUnknown, 0.0001f,
                "an unknown character must cost nothing, not an advance");
        }

        [Test]
        public void Measure_CountsTrackingBetweenGlyphsAndNeverAfterTheLast()
        {
            float one = TitleGlyphStrokes.MeasureEm("V", tracking: 0.2f);
            float two = TitleGlyphStrokes.MeasureEm("VV", tracking: 0.2f);
            Assert.AreEqual(TitleGlyphStrokes.Advance('V'), one, 0.0001f);
            Assert.AreEqual(one * 2f + 0.2f, two, 0.0001f);
        }

        [Test]
        public void Sampling_IsDeterministic()
        {
            var a = TitleGlyphStrokes.Sample("VALKUR", 120f, 16f, 5f, 4);
            var b = TitleGlyphStrokes.Sample("VALKUR", 120f, 16f, 5f, 4);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.AreEqual(a[i].Position, b[i].Position,
                    "the same arguments must give the same cloud, or the title differs between runs");
        }

        [Test]
        public void Sampling_ScalesWithCapHeight()
        {
            var small = TitleGlyphStrokes.Sample("VALKUR", 60f, 8f, 4f, 3);
            var large = TitleGlyphStrokes.Sample("VALKUR", 120f, 16f, 4f, 3);
            Assert.Greater(Width(large), Width(small) * 1.8f,
                "doubling the cap height should roughly double the word");
        }

        /// <summary>
        /// The drawn bar is as thick as it was AUTHORED, whatever the row count — and the row
        /// count is what fills it, not what widens it.
        ///
        /// <para><b>This test used to assert the opposite, and the implementation was right.</b>
        /// It read "more rows across must widen the drawn stroke", measured a single row at
        /// <b>14.3</b> against five rows at <b>21.6</b> on a stroke authored 20 wide, and failed.
        /// The premise was false: the jitter is scaled by the row SPACING
        /// (<c>/ max(1, rowsAcross - 1)</c>), so with one row the row IS the bar and that single
        /// row scatters across nearly all of it. A bar whose thickness grew with its sampling
        /// density would be the defect — <c>strokeWidth</c> would stop meaning anything, and the
        /// title's weight would change every time the mote budget thinned the cloud.</para>
        ///
        /// <para><b>And asking it at four row counts is what found the real defect.</b> At the
        /// shipped five the bar was 8 % too wide, which nobody would ever see; at TWO it was
        /// <b>30.4 on a stroke of 20</b>, half again as thick, because the jitter was scaled by
        /// the row SPACING and at two rows the spacing is the whole bar. One row count would have
        /// passed a tolerance and hidden it.</para>
        ///
        /// <para>The apostrophe, not 'I'. 'I' carries two horizontal serifs 0.28 em wide which
        /// dominate its box, so an x-extent over it measures the serifs rather than the stem.</para>
        /// </summary>
        [Test]
        public void TheDrawnBar_IsAsThickAsItWasAuthored_AtAnyRowCount()
        {
            const float strokeWidth = 20f;
            foreach (int rows in new[] { 1, 2, 5, 9 })
            {
                var points = TitleGlyphStrokes.Sample("'", 120f, strokeWidth, 4f, rows);
                float w = Width(points);
                Assert.Greater(w, strokeWidth * 0.5f,
                    $"{rows} row(s) drew a hairline, not a bar ({w:F1} of {strokeWidth})");
                Assert.LessOrEqual(w, strokeWidth * 1.001f,
                    $"{rows} row(s) spilled outside the authored thickness ({w:F1} of {strokeWidth})");
            }
        }

        /// <summary>
        /// What <c>rowsAcross</c> actually buys: points. It is the ink density inside a bar of
        /// fixed width, which is the dial the title's budget thins.
        /// </summary>
        [Test]
        public void MoreRowsAcross_BuyPoints_NotWidth()
        {
            int one = TitleGlyphStrokes.Sample("'", 120f, 20f, 4f, 1).Count;
            int five = TitleGlyphStrokes.Sample("'", 120f, 20f, 4f, 5).Count;
            Assert.AreEqual(one * 5, five, "each row samples the same path once");
        }

        /// <summary>
        /// The one quantity no mask or texture readback could have supplied: how far a point sits
        /// from the middle of its own stroke. It is what lets the word be lit like hot metal.
        /// </summary>
        [Test]
        public void EveryPoint_CarriesADepthAndATangent()
        {
            var points = TitleGlyphStrokes.Sample("VALKUR", 120f, 16f, 5f, 5);
            Assert.Greater(points.Count, 100);

            bool sawCore = false, sawEdge = false;
            foreach (var p in points)
            {
                Assert.GreaterOrEqual(p.Depth, 0f);
                Assert.LessOrEqual(p.Depth, 1f);
                Assert.AreEqual(1f, p.Tangent.magnitude, 0.01f, "the tangent must be a unit vector");
                Assert.GreaterOrEqual(p.Across, 0f);
                Assert.LessOrEqual(p.Across, 1f);
                if (p.Depth < 0.2f) sawCore = true;
                if (p.Depth > 0.8f) sawEdge = true;
            }
            Assert.IsTrue(sawCore, "no point near the middle of a stroke: the word has no hot core");
            Assert.IsTrue(sawEdge, "no point near a stroke's edge: the word has no rim");
        }

        [Test]
        public void GlyphIndex_AdvancesLeftToRight()
        {
            var points = TitleGlyphStrokes.Sample("VALKUR", 120f, 16f, 5f, 3);
            var firstX = new Dictionary<int, float>();
            foreach (var p in points)
                if (!firstX.ContainsKey(p.GlyphIndex) || p.Position.x < firstX[p.GlyphIndex])
                    firstX[p.GlyphIndex] = p.Position.x;

            Assert.AreEqual(6, firstX.Count, "VALKUR is six glyphs");
            for (int i = 1; i < 6; i++)
                Assert.Greater(firstX[i], firstX[i - 1],
                    "glyph " + i + " must start to the right of the one before it");
        }

        [Test]
        public void ZeroOrNegativeArguments_ReturnAnEmptyCloud_RatherThanThrowing()
        {
            Assert.AreEqual(0, TitleGlyphStrokes.Sample(null, 120f, 16f, 5f, 4).Count);
            Assert.AreEqual(0, TitleGlyphStrokes.Sample("", 120f, 16f, 5f, 4).Count);
            Assert.AreEqual(0, TitleGlyphStrokes.Sample("VALKUR", 0f, 16f, 5f, 4).Count);
            Assert.AreEqual(0, TitleGlyphStrokes.Sample("VALKUR", 120f, 16f, 0f, 4).Count);
        }

        private static float Width(List<TitleGlyphStrokes.TitlePoint> points)
        {
            if (points.Count == 0) return 0f;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in points)
            {
                if (p.Position.x < min) min = p.Position.x;
                if (p.Position.x > max) max = p.Position.x;
            }
            return max - min;
        }
    
        // === Stress and flare: what makes it a lettering rather than a wireframe ===========

        /// <summary>
        /// A VERTICAL stroke keeps its weight and a HORIZONTAL one gives way — the stress every
        /// typeface with a personality has, and the shipped title had none of: all 45 glyphs drew
        /// every stroke at one width, which is what made a hand-built alphabet read as a default
        /// one.
        ///
        /// <para><b>Measured on two glyphs that are ONE stroke each</b>, the apostrophe (a bare
        /// vertical) and the hyphen (a bare horizontal). The first draft of this test used the T
        /// and the I and failed a correct implementation: a T's crossbar shares its band with the
        /// top of its stem, and an I carries two horizontal serifs 0.28 em wide that dominate any
        /// x-extent taken across it. It is the same trap as measuring the row against the column
        /// — a real number about the wrong object.</para>
        /// </summary>
        [Test]
        public void AHorizontalStroke_ThinsWhileAVerticalOneKeepsItsWeight()
        {
            const float w = 20f;
            float flatStem = Width(Sample("'", w, 0f));
            float flatBar = Height(Sample("-", w, 0f));
            float stressedStem = Width(Sample("'", w, 0.62f));
            float stressedBar = Height(Sample("-", w, 0.62f));

            Assert.AreEqual(flatStem, stressedStem, w * 0.08f,
                $"the vertical stem must keep its authored weight (flat {flatStem:F1}, " +
                $"stressed {stressedStem:F1})");
            Assert.Less(stressedBar, flatBar * 0.85f,
                $"the horizontal stroke did not thin (flat {flatBar:F1}, stressed {stressedBar:F1})");
            Assert.Greater(stressedBar, flatBar * 0.45f,
                "it thinned to almost nothing, which is a hairline rather than a light stroke");
        }

        [Test]
        public void ZeroContrast_DrawsExactlyWhatItAlwaysDrew()
        {
            // The SAME tracking on both sides. The first draft passed 0.17 to one and let the
            // other take TitleGlyphStrokes.DefaultTracking (0.12), so it compared two different
            // layouts of the word and reported a correct build as changed.
            var before = TitleGlyphStrokes.Sample("VALKUR", 132f, 21f, 4f, 5,
                                                  TitleGlyphStrokes.DefaultTracking);
            var after = TitleGlyphStrokes.Sample("VALKUR", 132f, 21f, 4f, 5,
                                                 TitleGlyphStrokes.DefaultTracking, 8117, 0f, 0f);
            Assert.AreEqual(before.Count, after.Count, "the neutral dials changed the cloud");
            for (int i = 0; i < before.Count; i += 97)
            {
                Assert.AreEqual(before[i].Position.x, after[i].Position.x, 0.001f, "x at " + i);
                Assert.AreEqual(before[i].Position.y, after[i].Position.y, 0.001f, "y at " + i);
            }
        }

        /// <summary>
        /// A stroke SWELLS at its two ends and keeps its authored width through the middle — a cut
        /// that is wider where the chisel entered and left. Cubed rather than linear, so the
        /// swelling is the last fifth of the stroke and not a lens along the whole of it.
        /// </summary>
        [Test]
        public void AStroke_SwellsAtItsEnds_AndNotInItsMiddle()
        {
            const float w = 20f;
            var plain = Sample("'", w, 0f, 0f);
            var flared = Sample("'", w, 0f, 0.6f);

            float plainMiddle = WidthInBand(plain, 0.40f, 0.60f);
            float flaredMiddle = WidthInBand(flared, 0.40f, 0.60f);
            float plainEnd = WidthInBand(plain, 0.0f, 0.12f);
            float flaredEnd = WidthInBand(flared, 0.0f, 0.12f);

            Assert.AreEqual(plainMiddle, flaredMiddle, w * 0.12f,
                $"the middle of the stroke changed weight (plain {plainMiddle:F1}, " +
                $"flared {flaredMiddle:F1})");
            Assert.Greater(flaredEnd, plainEnd * 1.12f,
                $"the terminal did not open out (plain {plainEnd:F1}, flared {flaredEnd:F1})");
        }

        /// <summary>
        /// The ink stays EVEN: a thinned stroke is drawn with fewer points rather than with the
        /// same points packed tighter. Otherwise the weight contrast would arrive on screen as a
        /// brightness difference, which is a different statement and a wrong one.
        /// </summary>
        [Test]
        public void ThinningAStroke_DropsPoints_RatherThanPackingThem()
        {
            int flat = Sample("-", 20f, 0f).Count;
            int stressed = Sample("-", 20f, 0.62f).Count;
            Assert.Less(stressed, flat,
                "a bare horizontal stroke thinned by a third has to cost points");
            Assert.Greater(stressed, flat * 0.5f,
                "it lost more than half its ink, which is a different stroke, not a lighter one");
        }

        // ── Helpers for the two single-stroke glyphs ─────────────────────────

        /// <summary>
        /// A DENSE sample on purpose. At the shipped spacing a short glyph carries about ten
        /// rings, so a band of a fifth of its height holds ~10 points and its extremes are
        /// decided by the jitter rather than by the width — and because changing a dial shifts
        /// how many randoms the walk consumes, the two clouds being compared draw different
        /// numbers entirely. The first draft measured exactly that and reported a 27 % swing in
        /// the middle of a stroke whose width had not moved.
        /// </summary>
        private static List<TitleGlyphStrokes.TitlePoint> Sample(
            string text, float strokeWidth, float weightContrast, float flare = 0f)
            => TitleGlyphStrokes.Sample(text, 120f, strokeWidth, 0.6f, 5,
                                        TitleGlyphStrokes.DefaultTracking, 8117,
                                        weightContrast, flare);

        private static float Height(List<TitleGlyphStrokes.TitlePoint> pts)
        {
            if (pts.Count == 0) return 0f;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in pts)
            {
                if (p.Position.y < min) min = p.Position.y;
                if (p.Position.y > max) max = p.Position.y;
            }
            return max - min;
        }

        /// <summary>The x-extent of the points sitting in a band of the glyph's own height.</summary>
        private static float WidthInBand(List<TitleGlyphStrokes.TitlePoint> pts,
                                         float fromY01, float toY01)
        {
            if (pts.Count == 0) return 0f;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                if (p.Position.y < minY) minY = p.Position.y;
                if (p.Position.y > maxY) maxY = p.Position.y;
            }
            float lo = Mathf.Lerp(minY, maxY, fromY01), hi = Mathf.Lerp(minY, maxY, toY01);
            float min = float.MaxValue, max = float.MinValue;
            int n = 0;
            foreach (var p in pts)
            {
                if (p.Position.y < lo || p.Position.y > hi) continue;
                n++;
                if (p.Position.x < min) min = p.Position.x;
                if (p.Position.x > max) max = p.Position.x;
            }
            return n == 0 ? 0f : max - min;
        }
}
}
