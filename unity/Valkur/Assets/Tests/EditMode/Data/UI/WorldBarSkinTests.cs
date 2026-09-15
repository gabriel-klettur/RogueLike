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

    /// <summary>
    /// The slot block on the style: what a painted piece is looked up by, and what happens when
    /// nobody has painted one.
    /// </summary>
    public class WorldBarSkinTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private Sprite MakeSprite(string name)
        {
            var tex = new Texture2D(4, 4) { hideFlags = HideFlags.HideAndDontSave };
            _spawned.Add(tex);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 16f,
                                       0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _spawned.Add(sprite);
            return sprite;
        }

        [Test]
        public void AFreshSkinIsEmpty()
        {
            // The shipped state. Empty means "draw the generated art", which is what makes the
            // hand-painted sheet optional rather than a prerequisite.
            Assert.IsTrue(new WorldBarSkin().IsEmpty);
        }

        [Test]
        public void EveryLayoutIdRoundTripsThroughSetAndFind()
        {
            // Set and Find are two switches over the same ids. If one of them misses a piece, that
            // piece silently stays on the generated art no matter what an artist paints.
            var skin = new WorldBarSkin();
            int kinds = System.Enum.GetValues(typeof(StatusEffectKind)).Length;
            var sheet = WorldBarSheetLayout.Build(4, 3, 5, 5, kinds);

            foreach (var piece in sheet.Pieces)
            {
                var sprite = MakeSprite(piece.Id);
                skin.Set(piece.Id, sprite, kinds);
                Assert.AreSame(sprite, skin.Find(piece.Id),
                    $"'{piece.Id}' does not come back out of the slot it went into.");
            }

            Assert.IsFalse(skin.IsEmpty);
            skin.Clear();
            Assert.IsTrue(skin.IsEmpty, "Clear must put every slot back, glyphs included.");
        }

        [Test]
        public void AnUnknownIdResolvesToNothing()
        {
            var skin = new WorldBarSkin();
            Assert.IsNull(skin.Find("not_a_piece"));
            Assert.IsNull(skin.Find(""));
            Assert.IsNull(skin.Find(null));
            Assert.IsNull(skin.Find("icon_999"), "a glyph past the end of the array is absent, " +
                                                 "not an exception in the middle of a fight");
        }

        [Test]
        public void SettingAGlyphGrowsTheArrayRatherThanDroppingIt()
        {
            var skin = new WorldBarSkin();
            var last = MakeSprite("icon_7");
            skin.Set(WorldBarSheetLayout.IconId(7), last, 8);
            Assert.AreSame(last, skin.IconAt(7));
            Assert.AreEqual(8, skin.statusIcons.Length,
                "the array is sized for every kind, so a later Set does not reallocate over it");
        }

        /// <summary>
        /// The shipped skin agrees with the builder's own manifest, and every piece it claims to
        /// have painted is correctly imported.
        ///
        /// <para>This test used to assert the opposite — that no painted art shipped — and then,
        /// for one afternoon, that EVERY piece was painted. Both were wrong for the same reason:
        /// <b>a skin is deliberately PARTIAL.</b> The colour sheet draws two frames, a pill and
        /// eight icons; the plate, the two fills and the solid stay on the generated art because
        /// they are the pieces the palette COLOURS, and coloured art has nothing left to tint —
        /// see <c>WorldBarArt.TintFor</c>. So the thing to pin is not completeness, it is that the
        /// asset and the sheet agree about WHICH pieces are painted. The manifest is the builder's
        /// own record; comparing the two catches a re-import that silently dropped a slot, and a
        /// rebuild whose output nobody imported.</para>
        /// </summary>
        [Test]
        public void TheShippedSkinMatchesTheBuildersManifest_AndEveryPaintedPieceIsImported()
        {
            var style = Resources.Load<WorldBarStyle>(WorldBarStyle.ResourcePath);
            if (style == null) Assert.Ignore("WorldBarStyle.asset is not in Resources/UI.");

            var painted = PaintedIdsFromTheBuildManifest();
            Assert.IsNotEmpty(painted,
                "The build manifest lists no painted piece. Re-run " +
                "tools/atlas/worldbars/build_world_bar_sheet.py.");
            Assert.IsFalse(style.skin == null || style.skin.IsEmpty,
                "The shipped style carries no painted art. Run Valkur > UI > Import World Bar Skin.");

            int kinds = System.Enum.GetValues(typeof(StatusEffectKind)).Length;
            var sheet = WorldBarSheetLayout.Build(style.healthRowTexels, style.resourceRowTexels,
                                                  style.pipTexels, style.iconTexels, kinds);

            Texture2D texture = null;
            foreach (var piece in sheet.Pieces)
            {
                var sprite = style.skin.Find(piece.Id);
                bool wanted = painted.Contains(piece.Id);

                if (!wanted)
                {
                    Assert.IsNull(sprite,
                        $"'{piece.Id}' is NOT in the sheet the builder wrote, yet the style holds " +
                        "art for it. A stale slot draws a piece the sheet no longer contains.");
                    continue;
                }

                Assert.IsNotNull(sprite,
                    $"'{piece.Id}' is painted in the sheet and missing from the style. Re-run " +
                    "Valkur > UI > Import World Bar Skin.");

                Assert.AreEqual(WorldBarGeometry.TEXELS_PER_UNIT, sprite.pixelsPerUnit, 0.01f,
                    $"'{piece.Id}' imported at the wrong PPU. Art/UI/ forces 100; this sheet has " +
                    "to live in Art/WorldBars/.");
                Assert.AreEqual(piece.Width, Mathf.RoundToInt(sprite.rect.width), piece.Id + " width");
                Assert.AreEqual(piece.Height, Mathf.RoundToInt(sprite.rect.height), piece.Id + " height");
                Assert.AreEqual(piece.Border, sprite.border,
                    $"'{piece.Id}' lost its 9-slice border. Borders are per-sprite data the " +
                    "importer writes; nothing else carries them.");

                // Sliced draw mode cannot use a tight mesh, and both Sprite.Create and the
                // importer default to Tight. A quad is two triangles.
                Assert.LessOrEqual(sprite.triangles.Length / 3, 2,
                    $"'{piece.Id}' was imported Tight rather than Full Rect.");

                Assert.AreEqual(FilterMode.Point, sprite.texture.filterMode,
                    $"'{piece.Id}' is filtered. An ATLAS overrides the filter its members were " +
                    "imported with — ui.spriteatlas did exactly that once.");

                texture = texture ?? sprite.texture;
                Assert.AreSame(texture, sprite.texture,
                    "Every PAINTED piece must come from ONE texture, or the readout costs a draw " +
                    "call per source.");
            }
        }

        /// <summary>
        /// The <c>painted</c> list out of <c>world_bars_build.json</c>, read as TEXT.
        ///
        /// <para>Read with a regex rather than through a JSON parser on purpose: the fixture must
        /// not depend on which of this project's two MiniJson implementations the test assembly
        /// can see, and the shape being read is one flat array of strings.</para>
        /// </summary>
        private static HashSet<string> PaintedIdsFromTheBuildManifest()
        {
            const string path = "Assets/_Project/Art/WorldBars/world_bars_build.json";
            var ids = new HashSet<string>();
            string full = System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName, path);
            if (!System.IO.File.Exists(full)) return ids;

            const string Q = "\"";
            string text = System.IO.File.ReadAllText(full);
            var block = System.Text.RegularExpressions.Regex.Match(
                text, Q + "painted" + Q + @"\s*:\s*\[(.*?)\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (!block.Success) return ids;

            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         block.Groups[1].Value, Q + "([^" + Q + "]+)" + Q))
                ids.Add(m.Groups[1].Value);
            return ids;
        }
    }
}
