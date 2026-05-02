using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Select
{
    /// <summary>
    /// Pin-down tests for the <see cref="TileClipboard"/> POCO.
    /// The clipboard backs Copy / Cut / Paste in the Select tool — these tests guard
    /// the field semantics and the bounds-derived helpers that the paste loop depends on.
    /// </summary>
    [TestFixture]
    public class TileClipboardTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static Tile MakeTile(string spriteName = "t")
        {
            var tex = new Texture2D(2, 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1f);
            sprite.name = spriteName;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            return tile;
        }

        // ── Defaults ─────────────────────────────────────────────────────────

        [Test]
        public void Defaults_AreEmpty()
        {
            var c = new TileClipboard();
            Assert.IsTrue(c.IsEmpty,
                "A freshly-constructed clipboard must report IsEmpty = true so Paste no-ops.");
            Assert.IsFalse(c.IsCut, "IsCut must default to false (Copy doesn't set it).");
            Assert.IsNull(c.Tiles);
        }

        [Test]
        public void IsEmpty_TrueForZeroSizedBounds()
        {
            var c = new TileClipboard
            {
                Tiles = new TileBase[0, 0],
                SourceBounds = new BoundsInt(0, 0, 0, 0, 0, 1),
            };
            Assert.IsTrue(c.IsEmpty,
                "A zero-area clipboard must still report IsEmpty so Paste doesn't index a 0×0 array.");
        }

        [Test]
        public void IsEmpty_FalseWhenTilesAndBoundsArePopulated()
        {
            var c = new TileClipboard
            {
                Tiles = new TileBase[2, 3],
                SourceBounds = new BoundsInt(5, 6, 0, 2, 3, 1),
            };
            Assert.IsFalse(c.IsEmpty);
            Assert.AreEqual(2, c.Width);
            Assert.AreEqual(3, c.Height);
        }

        // ── Capture (mirroring what OnCopyClicked builds) ───────────────────

        [Test]
        public void Capture_StoresTilesAtRelativeOffsets()
        {
            // Simulate a 2x2 selection at (10, 20) with 2 of 4 cells filled.
            var bounds = new BoundsInt(10, 20, 0, 2, 2, 1);
            var arr    = new TileBase[2, 2];
            var tA     = MakeTile("A");
            var tB     = MakeTile("B");

            arr[0, 0] = tA; // (10, 20)
            arr[1, 1] = tB; // (11, 21)

            var c = new TileClipboard
            {
                Tiles        = arr,
                SourceBounds = bounds,
                SourceLayer  = TilemapLayerSetup.TilemapLayer.Ground,
                IsCut        = false,
            };

            Assert.AreEqual(tA, c.Tiles[0, 0], "Tiles must be addressable by relative (dx, dy).");
            Assert.AreEqual(tB, c.Tiles[1, 1]);
            Assert.IsNull(c.Tiles[0, 1], "Empty cells must be preserved as null.");
            Assert.IsNull(c.Tiles[1, 0]);
        }

        [Test]
        public void Capture_RecordsSourceLayer()
        {
            var c = new TileClipboard
            {
                Tiles        = new TileBase[1, 1],
                SourceBounds = new BoundsInt(0, 0, 0, 1, 1, 1),
                SourceLayer  = TilemapLayerSetup.TilemapLayer.WallsBottom,
            };
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.WallsBottom, c.SourceLayer,
                "SourceLayer is needed by the future cross-layer-paste extension; must round-trip.");
        }

        [Test]
        public void IsCut_FlagSetByCutOperation_DistinctFromCopy()
        {
            var copy = new TileClipboard { Tiles = new TileBase[1, 1], SourceBounds = new BoundsInt(0,0,0,1,1,1), IsCut = false };
            var cut  = new TileClipboard { Tiles = new TileBase[1, 1], SourceBounds = new BoundsInt(0,0,0,1,1,1), IsCut = true };

            Assert.IsFalse(copy.IsCut);
            Assert.IsTrue (cut.IsCut, "IsCut is the diagnostic that distinguishes a Copy from a Cut clipboard.");
        }
    }
}
