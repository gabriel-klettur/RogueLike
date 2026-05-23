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
    /// Robustness tests for <see cref="WorldCollisionBaker"/> — guards against
    /// silent regressions in the M2 per-visual-layer collision pipeline that
    /// the rebind / lazy-tagmap / boot-order fixes uncovered. Each test pins
    /// one structural invariant that, if broken, would silently bypass the
    /// per-layer filter.
    ///
    /// Coverage map (avoid duplication with sibling fixtures):
    ///   <see cref="WorldCollisionLayersTests"/>       — mask correctness.
    ///   <see cref="VisualLayerColliderSyncTests"/>    — entity-side filter.
    ///   <see cref="VisualLayerPhysicsSetupTests"/>    — global Physics2D matrix.
    ///   <see cref="WorldCollisionBakerRebindTests"/>  — stale-source recovery.
    ///   <c>WorldCollisionBakerMultiTagTests</c>       — tag-dispatch logic.
    ///   THIS FIXTURE                                  — sub-tilemap GameObject
    ///                                                   configuration + rebake
    ///                                                   invariants + event lifecycle.
    /// </summary>
    [TestFixture]
    public class WorldCollisionBakerHardeningTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _bakerGo;
        private WorldCollisionBaker _baker;
        private CollisionTagMap _tagMap;

        [SetUp]
        public void SetUp()
        {
            if (WorldCollisionBaker.HasInstance)
                Object.DestroyImmediate(WorldCollisionBaker.Instance.gameObject);

            _gridGo = new GameObject(nameof(WorldGridBuilder));
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _bakerGo = new GameObject(nameof(WorldCollisionBaker));
            _baker = _bakerGo.AddComponent<WorldCollisionBaker>();
            _tagMap = new CollisionTagMap();

            // EditMode does not invoke MonoBehaviour OnEnable on AddComponent
            // reliably (mirrors VisualLayerSortingSyncTests' ForceLifecycle
            // pattern). Without firing OnEnable manually, the baker's
            // Tilemap.tilemapTileChanged subscription is dead and the dirty-
            // flag tests below would silently report false negatives.
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(WorldCollisionBaker).GetMethod("OnEnable", Flags)?.Invoke(_baker, null);

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

        // ── Reflection helpers ----------------------------------------------

        private static Tilemap[] GetSubTilemaps(WorldCollisionBaker baker)
        {
            var field = typeof(WorldCollisionBaker).GetField(
                "_subTilemaps", BindingFlags.Instance | BindingFlags.NonPublic);
            return (Tilemap[])field.GetValue(baker);
        }

        private static bool GetDirty(WorldCollisionBaker baker)
        {
            var field = typeof(WorldCollisionBaker).GetField(
                "_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)field.GetValue(baker);
        }

        private static void SetDirty(WorldCollisionBaker baker, bool value)
        {
            var field = typeof(WorldCollisionBaker).GetField(
                "_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(baker, value);
        }

        private static Tile MakeInvisibleTile()
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "test_wall";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            tile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            return tile;
        }

        // ── Sub-tilemap shape -----------------------------------------------

        [Test]
        public void Initialize_CreatesExactly_TenSubTilemaps()
        {
            // 9 per-visual-layer slots (WorldL0..L8) + 1 wildcard (WorldAll).
            // If this count ever drifts, the loop in DispatchCellToSubmaps
            // would IndexOutOfRange when a high-N tag is painted.
            var subs = GetSubTilemaps(_baker);
            Assert.AreEqual(WorldCollisionBaker.CompositeCount, subs.Length,
                "Sub-tilemap array length must equal CompositeCount.");
            for (int i = 0; i < subs.Length; i++)
                Assert.IsNotNull(subs[i], $"Sub-tilemap slot {i} must be a live Tilemap, not null.");
        }

        [Test]
        public void EachPerLayerSubTilemap_LivesOn_MatchingWorldLPhysicsLayer()
        {
            // THE single most important invariant. If sub-tilemap N is on the
            // wrong physics layer, the player's per-collider includeLayers
            // (which targets WorldL{N}) won't match → either the player can't
            // collide with tag-N cells AT ALL, or it collides with cells from
            // ANOTHER layer. Either way, the M2 filter is silently broken.
            var subs = GetSubTilemaps(_baker);
            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int expectedLayer = WorldCollisionLayers.GetWorldLayerIndex(i);
                int actualLayer = subs[i].gameObject.layer;
                Assert.AreEqual(expectedLayer, actualLayer,
                    $"Sub-tilemap[{i}] must live on physics layer WorldL{i} (index {expectedLayer}); " +
                    $"got physics layer {actualLayer} ('{LayerMask.LayerToName(actualLayer)}').");
            }
        }

        [Test]
        public void WorldAllSubTilemap_LivesOn_WorldAllPhysicsLayer()
        {
            // The wildcard slot must be on the WorldAll layer so every entity's
            // includeLayers (which always opts into WorldAll) collides with its
            // cells. If misplaced, wildcard ("*") colliders silently stop
            // blocking the player.
            var subs = GetSubTilemaps(_baker);
            int expectedLayer = WorldCollisionLayers.GetWorldAllIndex();
            int actualLayer = subs[WorldCollisionBaker.WorldAllCompositeIndex].gameObject.layer;
            Assert.AreEqual(expectedLayer, actualLayer,
                $"WorldAll sub-tilemap must live on physics layer WorldAll (index {expectedLayer}); " +
                $"got physics layer {actualLayer} ('{LayerMask.LayerToName(actualLayer)}').");
        }

        [Test]
        public void EachSubTilemap_HasTilemapCollider2D_UsedByComposite()
        {
            // The TilemapCollider2D feeds tile cells into the CompositeCollider2D.
            // Without usedByComposite=true, the composite generates no geometry
            // and the per-layer filter produces no actual collisions.
            var subs = GetSubTilemaps(_baker);
            for (int i = 0; i < subs.Length; i++)
            {
                var coll = subs[i].GetComponent<TilemapCollider2D>();
                Assert.IsNotNull(coll, $"Sub-tilemap[{i}] must own a TilemapCollider2D.");
                Assert.IsTrue(coll.usedByComposite,
                    $"Sub-tilemap[{i}]'s TilemapCollider2D must have usedByComposite = true.");
            }
        }

        [Test]
        public void EachSubTilemap_HasCompositeCollider2D_WithPolygonsGeometry()
        {
            // Polygons geometry compacts adjacent cells into single edges —
            // critical for performance with hundreds of painted cells per zone.
            // A regression to Outlines would multiply collider count by ~4x.
            var subs = GetSubTilemaps(_baker);
            for (int i = 0; i < subs.Length; i++)
            {
                var comp = subs[i].GetComponent<CompositeCollider2D>();
                Assert.IsNotNull(comp, $"Sub-tilemap[{i}] must own a CompositeCollider2D.");
                Assert.AreEqual(CompositeCollider2D.GeometryType.Polygons, comp.geometryType,
                    $"Sub-tilemap[{i}]'s composite must use Polygons geometry " +
                    $"(perf-critical: Outlines would inflate collider count ~4x).");
            }
        }

        [Test]
        public void EachSubTilemap_HasStaticRigidbody2D()
        {
            // CompositeCollider2D requires a Rigidbody2D. It MUST be Static —
            // a Dynamic body would let the sub-tilemap "fall" under gravity,
            // dragging the world geometry with it.
            var subs = GetSubTilemaps(_baker);
            for (int i = 0; i < subs.Length; i++)
            {
                var rb = subs[i].GetComponent<Rigidbody2D>();
                Assert.IsNotNull(rb,
                    $"Sub-tilemap[{i}] must own a Rigidbody2D (auto-added by CompositeCollider2D).");
                Assert.AreEqual(RigidbodyType2D.Static, rb.bodyType,
                    $"Sub-tilemap[{i}]'s Rigidbody2D MUST be Static — Dynamic would drift the world.");
            }
        }

        [Test]
        public void EachSubTilemap_IsParentedUnderGrid()
        {
            // Sub-tilemaps must share the Grid's cell coordinate system. If
            // parented elsewhere, their colliders would land at the wrong
            // world positions (offset by the parent's transform).
            var subs = GetSubTilemaps(_baker);
            var expectedParent = _grid.Grid.transform;
            for (int i = 0; i < subs.Length; i++)
            {
                Assert.AreSame(expectedParent, subs[i].transform.parent,
                    $"Sub-tilemap[{i}] must be parented under the Grid component " +
                    $"to share cell coordinates with the source Collision tilemap.");
            }
        }

        [Test]
        public void EachSubTilemap_HasNoTilemapRenderer()
        {
            // Sub-tilemaps are physics-only — they exist for the composite,
            // not for visuals. A TilemapRenderer would burn a draw call per
            // zone per layer (~10 extra draw calls) for invisible content.
            var subs = GetSubTilemaps(_baker);
            for (int i = 0; i < subs.Length; i++)
            {
                var renderer = subs[i].GetComponent<TilemapRenderer>();
                Assert.IsNull(renderer,
                    $"Sub-tilemap[{i}] must NOT own a TilemapRenderer — these are physics-only.");
            }
        }

        // ── Rebake invariants -----------------------------------------------

        [Test]
        public void SourceCollider_StaysDisabled_AcrossMultipleRebakes()
        {
            // The most subtle regression scenario: someone adds a "re-enable
            // colliders" cleanup somewhere and the source TilemapCollider2D
            // gets toggled back on after a rebake. The source is on layer
            // 'World' which the player collides with by default — that would
            // silently bypass the entire M2 filter.
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var srcCollider = collision.GetComponent<TilemapCollider2D>();
            Assert.IsNotNull(srcCollider);
            Assert.IsFalse(srcCollider.enabled, "Precondition: disabled after Initialize.");

            for (int i = 0; i < 5; i++)
            {
                _baker.RebuildAll();
                Assert.IsFalse(srcCollider.enabled,
                    $"Source TilemapCollider2D must stay disabled after RebuildAll #{i + 1}.");
            }
        }

        [Test]
        public void RebuildAll_HandlesEmptySource_WithoutThrowing()
        {
            // Edge case: zone load where the Collision tilemap has no painted
            // cells. Old guard-clause bug would NRE on GetTilesBlock with
            // empty bounds.
            Assert.DoesNotThrow(() => _baker.RebuildAll(),
                "RebuildAll must not throw against an empty source Collision tilemap.");

            var subs = GetSubTilemaps(_baker);
            for (int i = 0; i < subs.Length; i++)
                Assert.IsTrue(IsEmpty(subs[i]),
                    $"Sub-tilemap[{i}] must remain empty after RebuildAll on empty source.");
        }

        [Test]
        public void RebuildAll_DoesNotAffect_OtherTilemapLayers()
        {
            // Sanity check: the baker reads from Collision only. Painting on
            // Ground / WallsBottom must not be observed by the baker's
            // sub-tilemaps. If a future refactor accidentally widens the
            // source scope, this test catches it.
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            var grassTile = MakeInvisibleTile();
            try
            {
                ground.SetTile(new Vector3Int(3, 3, 0), grassTile);
                _baker.RebuildAll();

                var subs = GetSubTilemaps(_baker);
                for (int i = 0; i < subs.Length; i++)
                    Assert.IsTrue(IsEmpty(subs[i]),
                        $"Sub-tilemap[{i}] must stay empty — Ground paint is not the baker's responsibility.");
            }
            finally
            {
                Object.DestroyImmediate(grassTile);
            }
        }

        // ── Dirty flag lifecycle -------------------------------------------

        [Test]
        public void Dirty_IsSet_WhenSourceCollisionChanges()
        {
            // The tilemapTileChanged subscription is THE bridge between
            // editor paints and the baker's rebake. If the handler stops
            // setting dirty for source events, the per-layer filter freezes
            // at whatever state existed when the bridge broke.
            SetDirty(_baker, false);
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var wallTile = MakeInvisibleTile();
            try
            {
                collision.SetTile(new Vector3Int(2, 2, 0), wallTile);
                Assert.IsTrue(GetDirty(_baker),
                    "Dirty flag must flip true when the source Collision tilemap changes.");
            }
            finally
            {
                Object.DestroyImmediate(wallTile);
            }
        }

        [Test]
        public void Dirty_NotSet_WhenNonSourceTilemapChanges()
        {
            // Perf invariant: the baker filters its dirty-tracking to the
            // source only. Without this filter, paints on visual layers
            // (Ground, Decorations, WallsTop, etc.) would each trigger a
            // baker rebake — multiplying the cost of a zone-load by ~10x.
            SetDirty(_baker, false);
            var ground = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            var grassTile = MakeInvisibleTile();
            try
            {
                ground.SetTile(new Vector3Int(2, 2, 0), grassTile);
                Assert.IsFalse(GetDirty(_baker),
                    "Dirty flag must stay false when a non-Collision tilemap changes " +
                    "— avoids cascading rebakes on every paint.");
            }
            finally
            {
                Object.DestroyImmediate(grassTile);
            }
        }

        // ── Event subscription lifecycle -----------------------------------

        [Test]
        public void EventSubscription_RestoredAfter_OnEnableOnDisableCycle()
        {
            // The baker survives play→stop→play (singleton with Persist=false).
            // OnDisable on stop unsubscribes from tilemapTileChanged. OnEnable
            // on next play re-subscribes. If a future refactor only subscribes
            // in Awake (or only unsubscribes in OnDestroy), a Stop+Play cycle
            // would leak a dead subscription AND lose the live one → silent
            // failure to detect paints.
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var onDisable = typeof(WorldCollisionBaker).GetMethod("OnDisable", Flags);
            var onEnable = typeof(WorldCollisionBaker).GetMethod("OnEnable", Flags);
            Assert.IsNotNull(onDisable, "OnDisable must exist (event subscription cleanup).");
            Assert.IsNotNull(onEnable, "OnEnable must exist (event subscription setup).");

            // Disable: unsubscribes. SetTile should NOT flip dirty.
            onDisable.Invoke(_baker, null);
            SetDirty(_baker, false);
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var wallTile = MakeInvisibleTile();
            try
            {
                collision.SetTile(new Vector3Int(4, 4, 0), wallTile);
                Assert.IsFalse(GetDirty(_baker),
                    "After OnDisable, the baker must not react to tilemap changes.");

                // Re-enable: re-subscribes. SetTile should flip dirty again.
                onEnable.Invoke(_baker, null);
                SetDirty(_baker, false);
                collision.SetTile(new Vector3Int(5, 5, 0), wallTile);
                Assert.IsTrue(GetDirty(_baker),
                    "After OnEnable, the baker must re-react to tilemap changes — " +
                    "subscription must be restored, not leaked.");
            }
            finally
            {
                Object.DestroyImmediate(wallTile);
            }
        }

        // ── End-to-end: the user-facing contract ---------------------------

        [Test]
        public void PaintTag7Cell_LandsOnly_InWorldL7SubTilemap_NotInWorldAll()
        {
            // The exact production scenario the user reported, asserted from
            // end to end. Paint a cell tagged "7" on the source; rebake;
            // verify it ended in sub-tilemap[7] (physics layer WorldL7) and
            // NOT in WorldAll. If this test ever fails, the player on visual
            // layer 0 will be blocked by tag-7 cells (because WorldAll is
            // in every entity's includeLayers mask).
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            var cell = new Vector3Int(6, 6, 0);
            var wallTile = MakeInvisibleTile();
            try
            {
                collision.SetTile(cell, wallTile);
                _tagMap.Set(new Vector2Int(6, 6), "7");
                _baker.RebuildAll();

                var subs = GetSubTilemaps(_baker);
                Assert.IsNotNull(subs[7].GetTile(cell),
                    "Cell tagged '7' MUST land in sub-tilemap[7] (physics layer WorldL7).");
                Assert.IsNull(subs[WorldCollisionBaker.WorldAllCompositeIndex].GetTile(cell),
                    "Cell tagged '7' must NOT land in WorldAll — that would block the " +
                    "player on every visual layer regardless of tag.");

                // And NOT in any of the other per-layer slots either.
                for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
                {
                    if (i == 7) continue;
                    Assert.IsNull(subs[i].GetTile(cell),
                        $"Cell tagged '7' must NOT land in sub-tilemap[{i}].");
                }
            }
            finally
            {
                Object.DestroyImmediate(wallTile);
            }
        }

        private static bool IsEmpty(Tilemap tm)
        {
            var bounds = tm.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0) return true;
            var tiles = tm.GetTilesBlock(bounds);
            if (tiles == null) return true;
            for (int i = 0; i < tiles.Length; i++)
                if (tiles[i] != null) return false;
            return true;
        }
    }
}
