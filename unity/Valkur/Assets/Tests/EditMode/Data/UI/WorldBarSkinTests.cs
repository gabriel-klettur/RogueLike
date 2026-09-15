using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;
namespace Valkur.Tests.EditMode.Data.UI
{
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
