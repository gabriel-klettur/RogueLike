using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Pins the M2 invariant that the <b>Collision</b> tilemap is the single
    /// authoritative source for what blocks an entity at a given visual layer.
    /// No other tilemap may carry its own <see cref="TilemapCollider2D"/> —
    /// otherwise that collider would block at every visual layer regardless of
    /// the per-layer filter (the very contradiction M2 was built to eliminate).
    ///
    /// Historical bug this guards against: <see cref="WorldGridBuilder"/> used
    /// to add a <see cref="TilemapCollider2D"/> + <see cref="CompositeCollider2D"/>
    /// to the WallsBottom tilemap on the <c>World</c> physics layer. The
    /// resulting collider was independent of the per-layer baker, so the player
    /// on visual layer 0 was blocked by walls painted on WallsBottom even when
    /// the corresponding Collision cell carried a tag (e.g. "7") that should
    /// have made the cell layer-7-only. Removing that block made the tag
    /// authoritative again.
    /// </summary>
    [TestFixture]
    public class WorldGridBuilderNoParallelCollidersTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject(nameof(WorldGridBuilder));
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
        }

        [Test]
        public void CollisionTilemap_HasItsOwnTilemapCollider2D()
        {
            // The Collision tilemap is the M2 baker's INPUT — its source
            // TilemapCollider2D is what gets disabled (not removed) by the
            // baker so that cells aren't double-counted (source + per-layer
            // sub-tilemaps). The component still exists, just disabled.
            var tm = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            Assert.IsNotNull(tm, "Collision tilemap must exist on the grid.");
            var coll = tm.GetComponent<TilemapCollider2D>();
            Assert.IsNotNull(coll,
                "Collision tilemap must own a TilemapCollider2D — the per-layer baker " +
                "reads from it as the input. (The baker disables it at runtime; this " +
                "assert only checks for presence.)");
        }

        [Test]
        public void WallsBottomTilemap_DoesNotHaveOwnTilemapCollider2D()
        {
            // THE invariant. The fix that made this test exist: WorldGridBuilder
            // used to AddComponent<TilemapCollider2D>() on WallsBottom. That
            // collider bypassed the per-layer filter. The fix removed those
            // AddComponent calls; this test makes sure they never come back.
            var tm = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.WallsBottom);
            Assert.IsNotNull(tm, "WallsBottom tilemap must exist on the grid.");
            var coll = tm.GetComponent<TilemapCollider2D>();
            Assert.IsNull(coll,
                "WallsBottom must NOT own a TilemapCollider2D. It would block at every " +
                "visual layer regardless of the per-layer tag map — contradicting M2's " +
                "'Collision tag is authoritative' contract.");

            var composite = tm.GetComponent<CompositeCollider2D>();
            Assert.IsNull(composite,
                "WallsBottom must NOT own a CompositeCollider2D either — same reason.");
        }

        [Test]
        public void NonCollisionTilemaps_DoNotHaveTilemapCollider2D()
        {
            // Belt-and-suspenders: every other visual tilemap (Ground,
            // ObjectsLow, Decorations, WallsTop, ObjectsHigh, OverheadDetails)
            // must also be collider-free. Only Collision is allowed to have
            // its own — and even that one gets disabled by the baker at boot.
            foreach (TilemapLayerSetup.TilemapLayer layer in
                     System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer)))
            {
                if (layer == TilemapLayerSetup.TilemapLayer.Collision) continue;
                var tm = _grid.GetTilemap(layer);
                if (tm == null) continue; // optional layers in some builds
                var coll = tm.GetComponent<TilemapCollider2D>();
                Assert.IsNull(coll,
                    $"Tilemap '{layer}' must NOT own a TilemapCollider2D — only " +
                    $"the Collision tilemap may, and only as the per-layer baker's input.");
            }
        }
    }
}
