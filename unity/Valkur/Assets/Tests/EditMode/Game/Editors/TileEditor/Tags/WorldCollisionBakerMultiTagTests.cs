using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.Editors.TileEditor.Tags
{
    /// <summary>
    /// M1.10 — verify that <see cref="WorldCollisionBaker"/> correctly DISTRIBUTES
    /// a Collision cell into multiple sub-tilemaps when the tag covers more than
    /// one visual layer, and that the single-tag + wildcard fast paths still
    /// route exactly as before (regression net for the M2.1 contract).
    ///
    /// Why the assertion shape:
    /// <see cref="WorldCollisionBaker"/> spawns 10 sub-tilemaps internally — one per
    /// physics layer + one WorldAll wildcard slot. After <see cref="WorldCollisionBaker.RebuildAll"/>,
    /// the tile at <c>(cx, cy)</c> must be present in every sub-tilemap whose
    /// layer index matches a bit set in the tag's canonical mask, and absent
    /// from every other slot. We reach the private <c>_subTilemaps</c> array via
    /// reflection because the bake routing is the contract under test.
    /// </summary>
    [TestFixture]
    public class WorldCollisionBakerMultiTagTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _bakerGo;
        private WorldCollisionBaker _baker;
        private CollisionTagMap _tagMap;
        private Tile _wallTile;

        [SetUp]
        public void SetUp()
        {
            if (WorldCollisionBaker.HasInstance)
                Object.DestroyImmediate(WorldCollisionBaker.Instance.gameObject);

            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _bakerGo = new GameObject(nameof(WorldCollisionBaker));
            _baker = _bakerGo.AddComponent<WorldCollisionBaker>();

            _tagMap = new CollisionTagMap();

            // Bind the baker to the grid + tag map exactly the way EnsureExists
            // would in production (we skip EnsureExists itself because it
            // calls FindObjectOfType to look up TileEditorManager, which we
            // don't want this fixture to depend on).
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var gridTransform = _grid.Grid != null ? _grid.Grid.transform : _grid.transform;
            _baker.Initialize(gridTransform, collision, _tagMap);

            _wallTile = ScriptableObject.CreateInstance<Tile>();
            _wallTile.name = "test_wall";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _wallTile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_bakerGo != null) Object.DestroyImmediate(_bakerGo);
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_wallTile);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private Tilemap[] GetSubTilemaps()
        {
            var field = typeof(WorldCollisionBaker).GetField(
                "_subTilemaps", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Reflection: _subTilemaps must exist.");
            return (Tilemap[])field.GetValue(_baker);
        }

        private void PaintAndBake(int x, int y, string tag)
        {
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(x, y, 0), _wallTile);
            _tagMap.Set(new Vector2Int(x, y), tag);
            _baker.RebuildAll();
        }

        private void AssertOnlyStampedInto(int x, int y, params int[] expectedSlots)
        {
            var subs = GetSubTilemaps();
            var pos = new Vector3Int(x, y, 0);
            for (int slot = 0; slot < subs.Length; slot++)
            {
                bool expected = System.Array.IndexOf(expectedSlots, slot) >= 0;
                bool actual = subs[slot]?.GetTile(pos) != null;
                Assert.AreEqual(expected, actual,
                    $"Slot {slot} ({(slot < WorldCollisionLayers.LayerCount ? $"WorldL{slot}" : "WorldAll")}): " +
                    $"expected {(expected ? "stamped" : "empty")}, got {(actual ? "stamped" : "empty")}.");
            }
        }

        // ── Single-tag fast paths (regression net for M2.1 routing) ─────────

        [Test]
        public void SingleDigitTag_StampsOneSubmap()
        {
            // Tag "4" must put the cell in slot 4 (WorldL4) and NOWHERE else —
            // not in WorldAll, not in any of the other 8 per-layer slots.
            PaintAndBake(3, 3, "4");
            AssertOnlyStampedInto(3, 3, WorldCollisionLayers.LayerCount > 4 ? 4 : -1);
        }

        [Test]
        public void Wildcard_StampsOnlyWorldAll()
        {
            // "*" must NOT explode into every sub-tilemap; one stamp into the
            // WorldAll slot is enough because every entity's includeLayers
            // already lists WorldAll.
            PaintAndBake(3, 3, "*");
            AssertOnlyStampedInto(3, 3, WorldCollisionBaker.WorldAllCompositeIndex);
        }

        [Test]
        public void NoExplicitTag_DefaultsToWildcard_StampsOnlyWorldAll()
        {
            // A cell with no explicit tag (legacy maps) → CollisionTagMap.Get
            // returns "*" → same routing as the explicit wildcard case.
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(7, 7, 0), _wallTile);
            // intentionally NO tagMap.Set call.
            _baker.RebuildAll();

            AssertOnlyStampedInto(7, 7, WorldCollisionBaker.WorldAllCompositeIndex);
        }

        // ── Multi-tag dispatch (the new contract) ────────────────────────────

        [Test]
        public void MultiTag_TwoLayers_StampsBothSubmaps()
        {
            // "0,4" must appear in BOTH slot 0 and slot 4 — but not in WorldAll,
            // not in any other slot. The entity-side includeLayers already opts
            // into WorldAll, so duplicating multi-tag cells there would inflate
            // the collider count without changing physics behaviour.
            PaintAndBake(5, 5, "0,4");
            AssertOnlyStampedInto(5, 5, 0, 4);
        }

        [Test]
        public void MultiTag_ThreeLayers_StampsAllThreeSubmaps()
        {
            PaintAndBake(2, 2, "0,2,5");
            AssertOnlyStampedInto(2, 2, 0, 2, 5);
        }

        [Test]
        public void MultiTag_AllNineDigits_CollapsesToWildcard_StampsOnlyWorldAll()
        {
            // "0,1,2,3,4,5,6,7,8" canonicalises to "*" on Set; the baker must
            // see the wildcard form and take the WorldAll fast path.
            PaintAndBake(8, 8, "0,1,2,3,4,5,6,7,8");
            AssertOnlyStampedInto(8, 8, WorldCollisionBaker.WorldAllCompositeIndex);
        }

        [Test]
        public void MultiTag_UnsortedInput_IsCanonicalisedBeforeBake()
        {
            // "5,2,0" → Set canonicalises to "0,2,5" → dispatch into slots 0,2,5.
            PaintAndBake(4, 4, "5,2,0");
            AssertOnlyStampedInto(4, 4, 0, 2, 5);
        }

        // ── Rebake idempotency ────────────────────────────────────────────

        [Test]
        public void Rebake_IsIdempotent_ForMultiTagCells()
        {
            PaintAndBake(1, 1, "3,7");
            AssertOnlyStampedInto(1, 1, 3, 7);

            _baker.RebuildAll();
            AssertOnlyStampedInto(1, 1, 3, 7);
        }

        [Test]
        public void Rebake_AfterTagChange_DropsOldStamps()
        {
            PaintAndBake(6, 6, "0,4");
            AssertOnlyStampedInto(6, 6, 0, 4);

            // Author changes the tag from "0,4" to "2,5" — old stamps in slots
            // 0 and 4 must disappear; new stamps in slots 2 and 5 appear.
            _tagMap.Set(new Vector2Int(6, 6), "2,5");
            _baker.RebuildAll();
            AssertOnlyStampedInto(6, 6, 2, 5);
        }
    }
}
