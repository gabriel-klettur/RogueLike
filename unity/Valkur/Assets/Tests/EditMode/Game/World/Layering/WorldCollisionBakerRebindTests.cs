using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Regression tests for <see cref="WorldCollisionBaker"/>'s ability to
    /// recover from a stale source-Collision-tilemap reference. This was the
    /// production bug: when the player switched zones, the previous scene's
    /// Collision tilemap was destroyed (Unity-pseudo-null), but the baker
    /// continued to hold the dead reference and never re-bound to the new
    /// scene's tilemap. The new Collision tilemap's own
    /// <see cref="TilemapCollider2D"/> stayed enabled on the <c>World</c>
    /// physics layer, and EVERY painted Collision cell blocked the player
    /// regardless of the cell's tag or the player's visual layer.
    ///
    /// The fix in <see cref="WorldCollisionBaker.EnsureExists"/> detects the
    /// stale reference (Unity's overloaded <c>==</c> treats destroyed
    /// Components as null) and re-invokes <see cref="WorldCollisionBaker.Initialize"/>
    /// against the new scene's <see cref="WorldGridBuilder"/>. Each
    /// <c>Initialize</c> call is itself idempotent and disables the new source
    /// tilemap's <see cref="TilemapCollider2D"/>, which is the actual fix
    /// — these tests pin the <c>Initialize</c> rebind contract.
    /// </summary>
    [TestFixture]
    public class WorldCollisionBakerRebindTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _bakerGo;
        private WorldCollisionBaker _baker;
        private CollisionTagMap _tagMap;

        [SetUp]
        public void SetUp()
        {
            // Other fixtures in the suite may have left the singleton in an
            // inconsistent state. Force-null the static instance via reflection
            // so AddComponent's Awake is free to register _baker cleanly.
            if (WorldCollisionBaker.HasInstance)
                Object.DestroyImmediate(WorldCollisionBaker.Instance.gameObject);

            _gridGo = new GameObject(nameof(WorldGridBuilder));
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _bakerGo = new GameObject(nameof(WorldCollisionBaker));
            _baker = _bakerGo.AddComponent<WorldCollisionBaker>();
            _tagMap = new CollisionTagMap();

            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var gridTransform = _grid.Grid != null ? _grid.Grid.transform : _grid.transform;
            _baker.Initialize(gridTransform, collision, _tagMap);
        }

        [TearDown]
        public void TearDown()
        {
            if (_bakerGo != null) Object.DestroyImmediate(_bakerGo);
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
        }

        private static Tilemap GetSourceCollision(WorldCollisionBaker baker)
        {
            var field = typeof(WorldCollisionBaker).GetField(
                "_sourceCollision", BindingFlags.Instance | BindingFlags.NonPublic);
            return (Tilemap)field.GetValue(baker);
        }

        // ── Baseline contract ---------------------------------------------

        [Test]
        public void Initialize_DisablesSourceTilemapCollider2D()
        {
            // The baker takes ownership of collisions and disables the
            // source's own TilemapCollider2D — without that, Collision cells
            // would be double-counted (source collider + per-layer composite),
            // and the source collider sits on the 'World' physics layer
            // (NOT per-layer-filtered) so the player would be blocked on every
            // visual layer regardless of tag. The single most important
            // invariant of the M2 collision pipeline.
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var srcCollider = collision.GetComponent<TilemapCollider2D>();
            Assert.IsNotNull(srcCollider, "Collision tilemap must own a TilemapCollider2D.");
            Assert.IsFalse(srcCollider.enabled,
                "Initialize MUST disable the source TilemapCollider2D — otherwise the M2 " +
                "per-visual-layer filter is bypassed and every cell blocks on every layer.");
        }

        [Test]
        public void Initialize_BindsSourceCollisionReference()
        {
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            Assert.AreSame(collision, GetSourceCollision(_baker),
                "_sourceCollision must reference the Collision tilemap passed into Initialize.");
        }

        // ── Stale-source rebind: the production bug -----------------------

        [Test]
        public void Initialize_CalledTwiceAgainstFreshGrid_RebindsToNewTilemap()
        {
            // Direct reproduction of the zone-change scenario the EnsureExists
            // self-heal handles in production. SetUp bound the baker to grid A.
            // Destroy grid A; create grid B; call Initialize against grid B's
            // Collision tilemap. The baker must:
            //   1) Rebind _sourceCollision to the new tilemap.
            //   2) Disable the NEW tilemap's own TilemapCollider2D so the new
            //      Collision cells are filtered through the per-layer baker
            //      (not the source's always-on collider on layer 'World').

            var oldCollision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            Assert.AreSame(oldCollision, GetSourceCollision(_baker),
                "Precondition: baker should be bound to grid A's Collision tilemap.");

            Object.DestroyImmediate(_gridGo);

            _gridGo = new GameObject($"{nameof(WorldGridBuilder)}_NewZone");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();
            var newCollision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var newGridTransform = _grid.Grid != null ? _grid.Grid.transform : _grid.transform;

            // Verify the NEW tilemap's collider STARTS enabled (proves it's a
            // fresh creation, not the disabled one carried over).
            var newSrcCollider = newCollision.GetComponent<TilemapCollider2D>();
            Assert.IsTrue(newSrcCollider.enabled,
                "Precondition: a freshly-built Collision tilemap's TilemapCollider2D is enabled.");

            _baker.Initialize(newGridTransform, newCollision, _tagMap);

            Assert.AreSame(newCollision, GetSourceCollision(_baker),
                "Initialize must rebind _sourceCollision to the NEW zone's Collision tilemap.");
            Assert.IsFalse(newSrcCollider.enabled,
                "The NEW Collision tilemap's TilemapCollider2D must be disabled by the rebind — " +
                "otherwise every painted cell silently blocks the player on every visual layer.");
        }

        [Test]
        public void RebuildAll_LazyBindsTagMap_WhenInitialisedWithNullTagMap()
        {
            // The production failure that this guards against:
            // WorldCollisionBaker boots from AfterSceneLoad — BEFORE
            // GameplaySceneSetup creates TileEditorManager. EnsureExists then
            // calls Initialize with tagMap = null. The first RebuildAll runs
            // with _tagMap = null, so EVERY painted Collision cell is treated
            // as Wildcard "*" and stamped into the WorldAll sub-tilemap —
            // which every entity's includeLayers opts into → the player is
            // blocked on every visual layer regardless of the painted tag.
            //
            // Fix: RebuildAll lazy-binds _tagMap from
            // TileEditorManager.Instance.CollisionTags when it becomes
            // available, so the NEXT rebake (triggered by any subsequent
            // paint / erase / overlay load) correctly routes cells by tag.
            //
            // We can't spin up a real TileEditorManager in EditMode (its
            // dependency graph is deep), so this test verifies the rebake
            // behaviour directly: install null tagMap, paint a tagged cell,
            // populate the tag map after the fact via reflection, rebake,
            // and verify the cell landed in the per-layer sub-tilemap, NOT
            // in WorldAll.

            // Re-initialise with a null tagMap to reproduce the boot race.
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var gridTransform = _grid.Grid != null ? _grid.Grid.transform : _grid.transform;
            var newTagMap = new CollisionTagMap();
            _baker.Initialize(gridTransform, collision, null);

            // Paint a cell with collision tag "7" — but with _tagMap = null,
            // the Get() call falls back to Wildcard.
            var wallTile = ScriptableObject.CreateInstance<Tile>();
            try
            {
                wallTile.name = "test_wall";
                var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
                wallTile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);

                var cell = new Vector3Int(3, 3, 0);
                collision.SetTile(cell, wallTile);
                newTagMap.Set(new Vector2Int(3, 3), "7");

                // Simulate "TileEditorManager came online later, its tag map
                // is now populated, and we triggered a rebake". We can't
                // observe TileEditorManager.HasInstance directly because the
                // baker's lazy-bind goes through that singleton — instead,
                // call _tagMap-aware path directly by injecting the populated
                // tag map via reflection (mirrors what the production code
                // does when TileEditorManager.HasInstance flips true).
                var tagMapField = typeof(WorldCollisionBaker).GetField(
                    "_tagMap", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(tagMapField, "_tagMap field must exist for the lazy-bind to work.");
                tagMapField.SetValue(_baker, newTagMap);

                _baker.RebuildAll();

                // Verify: cell (3,3) is stamped in sub-tilemap[7] (the
                // per-layer slot for tag "7"), NOT in the WorldAll slot.
                var subTilemapsField = typeof(WorldCollisionBaker).GetField(
                    "_subTilemaps", BindingFlags.Instance | BindingFlags.NonPublic);
                var subs = (Tilemap[])subTilemapsField.GetValue(_baker);

                Assert.IsNotNull(subs[7].GetTile(cell),
                    "After rebake with populated tag map, cell tagged '7' must be in sub-tilemap[7].");
                Assert.IsNull(subs[WorldCollisionBaker.WorldAllCompositeIndex].GetTile(cell),
                    "After rebake, cell tagged '7' must NOT be in the WorldAll sub-tilemap — " +
                    "if it is, the M2 filter is bypassed and the player is blocked on every layer.");
            }
            finally
            {
                Object.DestroyImmediate(wallTile);
            }
        }

        [Test]
        public void Initialize_RebindAfterDestroy_BuildsFreshSubTilemaps()
        {
            // After zone change, the baker's _subTilemaps[] entries reference
            // destroyed Tilemaps (they lived under the old Grid). A rebind
            // call must build new sub-tilemaps under the new grid, not try to
            // re-use the destroyed ones (which would NRE on ClearAllTiles).
            var subTilemapsField = typeof(WorldCollisionBaker).GetField(
                "_subTilemaps", BindingFlags.Instance | BindingFlags.NonPublic);
            var subTilemapsBefore = (Tilemap[])subTilemapsField.GetValue(_baker);
            Assert.IsNotNull(subTilemapsBefore[0],
                "Precondition: sub-tilemap 0 should be alive after the SetUp Initialize.");

            Object.DestroyImmediate(_gridGo);

            // After grid destruction, the sub-tilemap GameObjects (children of
            // the grid) are destroyed too. Their C# refs are pseudo-null.
            var subTilemapsAfterDestroy = (Tilemap[])subTilemapsField.GetValue(_baker);
            Assert.IsTrue(subTilemapsAfterDestroy[0] == null,
                "After grid destruction, sub-tilemaps must compare == null (pseudo-null).");

            _gridGo = new GameObject($"{nameof(WorldGridBuilder)}_NewZone");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();
            var newCollision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var newGridTransform = _grid.Grid != null ? _grid.Grid.transform : _grid.transform;

            Assert.DoesNotThrow(() =>
                _baker.Initialize(newGridTransform, newCollision, _tagMap),
                "Initialize on a stale baker must not throw — the loop has to detect " +
                "pseudo-null entries and rebuild the sub-tilemaps fresh.");

            var subTilemapsAfterRebind = (Tilemap[])subTilemapsField.GetValue(_baker);
            Assert.IsNotNull(subTilemapsAfterRebind[0],
                "After rebind, sub-tilemap 0 must be a fresh non-null reference.");
            Assert.IsTrue(subTilemapsAfterRebind[0] != null,
                "After rebind, sub-tilemap 0 must not be Unity-pseudo-null either.");
        }
    }
}
