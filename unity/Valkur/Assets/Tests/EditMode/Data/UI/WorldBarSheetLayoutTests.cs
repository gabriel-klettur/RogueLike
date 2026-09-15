using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.UI
{
    /// <summary>
    /// The contract between the painted sheet, the runtime generator and the importer.
    ///
    /// <para>Four things agree on these rectangles — the atlas <c>WorldBarArt</c> generates, the
    /// template an artist paints over, the slice the importer writes, and the size check that
    /// refuses a bad piece. They agree because they all read <see cref="WorldBarSheetLayout"/>,
    /// and this fixture is what stops that table drifting from what the pieces actually need.</para>
    /// </summary>
    public class WorldBarSheetLayoutTests
    {
        // Read off the style rather than copied from it. A second set of numbers here would go
        // stale the first time the geometry moves - which it did, the day the hand-drawn art
        // arrived and the rows went from 4/3/5/5 to 6/4/6/8.
        private static int HEALTH_ROW, RESOURCE_ROW, PIP, ICON;
        private const int ICONS = 8;

        [SetUp]
        public void ReadTheShippedGeometry()
        {
            var style = ScriptableObject.CreateInstance<WorldBarStyle>();
            try
            {
                HEALTH_ROW = style.healthRowTexels;
                RESOURCE_ROW = style.resourceRowTexels;
                PIP = style.pipTexels;
                ICON = style.iconTexels;
            }
            finally { Object.DestroyImmediate(style); }
        }

        private static WorldBarSheetLayout.Sheet Shipped()
            => WorldBarSheetLayout.Build(HEALTH_ROW, RESOURCE_ROW, PIP, ICON, ICONS);

        [Test]
        public void EveryPieceHasAUniqueId()
        {
            var seen = new HashSet<string>();
            foreach (var p in Shipped().Pieces)
                Assert.IsTrue(seen.Add(p.Id), $"'{p.Id}' appears twice. The id is the join between " +
                                              "the sheet, the style's slots and the importer.");
        }

        [Test]
        public void EveryPieceFitsInsideTheSheet()
        {
            var sheet = Shipped();
            foreach (var p in sheet.Pieces)
            {
                Assert.GreaterOrEqual(p.X, 0, p.Id);
                Assert.GreaterOrEqual(p.Y, 0, p.Id);
                Assert.LessOrEqual(p.X + p.Width, sheet.Width, p.Id + " runs off the right edge");
                Assert.LessOrEqual(p.Y + p.Height, sheet.Height, p.Id + " runs off the top edge");
            }
        }

        [Test]
        public void NoTwoPiecesTouch()
        {
            // The gutter is not tidiness: a stretched 9-slice samples right up to its rect edge,
            // so two adjacent rects let a wide bar pull one texel of its neighbour into its own
            // end cap.
            var pieces = Shipped().Pieces;
            for (int i = 0; i < pieces.Count; i++)
            {
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    var a = pieces[i];
                    var b = pieces[j];
                    bool separated =
                        a.X + a.Width + WorldBarSheetLayout.GUTTER <= b.X ||
                        b.X + b.Width + WorldBarSheetLayout.GUTTER <= a.X ||
                        a.Y + a.Height + WorldBarSheetLayout.GUTTER <= b.Y ||
                        b.Y + b.Height + WorldBarSheetLayout.GUTTER <= a.Y;
                    Assert.IsTrue(separated,
                        $"'{a.Id}' and '{b.Id}' are closer than one texel apart.");
                }
            }
        }

        [Test]
        public void APlateAndAFillAreTheRowsInteriorAndAFrameIsTheWholeRow()
        {
            var sheet = Shipped();

            Assert.IsTrue(sheet.TryFind(WorldBarSheetLayout.FRAME_HEALTH, out var frame));
            Assert.AreEqual(HEALTH_ROW, frame.Height, "the frame IS the row");

            Assert.IsTrue(sheet.TryFind(WorldBarSheetLayout.PLATE_HEALTH, out var plate));
            Assert.IsTrue(sheet.TryFind(WorldBarSheetLayout.FILL_HEALTH, out var fill));
            Assert.AreEqual(HEALTH_ROW - 2, plate.Height,
                "the plate is the interior: the frame takes one texel top and bottom");
            Assert.AreEqual(HEALTH_ROW - 2, fill.Height, "and so is the fill");
        }

        [Test]
        public void SlicingIsLegalForEveryPiece()
        {
            // A border wider than the piece has no middle left to stretch, and Unity draws the two
            // halves over each other rather than complaining.
            foreach (var p in Shipped().Pieces)
            {
                Assert.Less(p.Border.x + p.Border.z, p.Width,
                    $"'{p.Id}' has no horizontal middle between its borders");
                Assert.Less(p.Border.y + p.Border.w, p.Height,
                    $"'{p.Id}' has no vertical middle between its borders");
            }
        }

        [Test]
        public void OnlyFramesAreSlicedVertically()
        {
            // The plate and the fill stretch WHOLE, which is exactly why their height is fixed:
            // art painted at another height would be resampled and lose the crispness the texel
            // grid exists for.
            foreach (var p in Shipped().Pieces)
            {
                bool isFrame = p.Id == WorldBarSheetLayout.FRAME_HEALTH ||
                               p.Id == WorldBarSheetLayout.FRAME_RESOURCE;
                if (isFrame)
                    Assert.AreEqual(1, p.Border.y, $"'{p.Id}' must keep its top and bottom rims");
                else
                    Assert.AreEqual(0, p.Border.y, $"'{p.Id}' must not be sliced vertically");
            }
        }

        [Test]
        public void TheGlyphsAreOneCellPerStatusKind_InEnumOrder()
        {
            int kinds = System.Enum.GetValues(typeof(StatusEffectKind)).Length;
            var sheet = WorldBarSheetLayout.Build(HEALTH_ROW, RESOURCE_ROW, PIP, ICON, kinds);

            int found = 0;
            foreach (var p in sheet.Pieces)
            {
                if (p.IconIndex < 0) continue;
                Assert.AreEqual(WorldBarSheetLayout.IconId(p.IconIndex), p.Id);
                Assert.AreEqual(ICON, p.Width, "glyphs are square, at the style's icon size");
                Assert.AreEqual(ICON, p.Height);
                found++;
            }
            Assert.AreEqual(kinds, found,
                "One cell per kind, whether or not that kind has a generated glyph — the importer " +
                "builds its rects from the same enum, and a disagreement shifts every icon rect.");
        }

        [Test]
        public void TheSheetIsPowerOfTwoAndFitsTheImportersMaxSize()
        {
            var sheet = Shipped();
            Assert.AreEqual(0, sheet.Width & (sheet.Width - 1), "width is not a power of two");
            Assert.AreEqual(0, sheet.Height & (sheet.Height - 1), "height is not a power of two");
            Assert.LessOrEqual(sheet.Width, 512, "wider than the importer's maxTextureSize");
            Assert.LessOrEqual(sheet.Height, 512);
        }

        [Test]
        public void TheShippedStyleProducesAValidSheet()
        {
            var style = ScriptableObject.CreateInstance<WorldBarStyle>();
            try
            {
                int kinds = System.Enum.GetValues(typeof(StatusEffectKind)).Length;
                var sheet = WorldBarSheetLayout.Build(style.healthRowTexels, style.resourceRowTexels,
                                                      style.pipTexels, style.iconTexels, kinds);
                Assert.AreEqual(11 + kinds, sheet.Pieces.Count,
                    "eleven structural pieces (two frames, plates, fills and caps, the solid, " +
                    "the pip's ring and core) plus one glyph per status kind");
                foreach (var p in sheet.Pieces)
                {
                    Assert.Greater(p.Width, 0, p.Id);
                    Assert.Greater(p.Height, 0, p.Id);
                }
            }
            finally { Object.DestroyImmediate(style); }
        }
    }
}
