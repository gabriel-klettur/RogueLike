using System.Collections.Generic;
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
    /// Regression tests for the incremental rebake path added to
    /// <see cref="WorldCollisionBaker"/>: <c>LateUpdate</c> now dispatches only the
    /// cells reported by <c>Tilemap.tilemapTileChanged</c> (accumulated into the
    /// private <c>_pendingCells</c> set) instead of re-sweeping the whole zone via
    /// <see cref="WorldCollisionBaker.RebuildAll"/> on every dirty flush.
    ///
    /// These tests drive the REAL <c>Tilemap.tilemapTileChanged</c> event (via
    /// genuine <c>Tilemap.SetTile</c> calls, exactly like <see cref="WorldCollisionBakerHardeningTests"/>
    /// already proves fires synchronously) rather than hand-constructing a
    /// <c>Tilemap.SyncTile</c>, so they exercise the exact code path production
    /// paints go through.
    ///
    /// Coverage map (avoid duplication with sibling fixtures):
    ///   <see cref="WorldCollisionBakerHardeningTests"/>  — sub-tilemap shape + dirty-flag wiring.
    ///   <see cref="WorldCollisionBakerRebindTests"/>      — stale-source recovery, full-sweep contract.
    ///   <c>WorldCollisionBakerMultiTagTests</c>           — tag-dispatch mask logic via RebuildAll.
    ///   THIS FIXTURE                                      — the incremental add/delete/retag
    ///                                                       contract, and that it never disturbs
    ///                                                       cells outside the changed set.
    /// </summary>
    [TestFixture]
    public class WorldCollisionBakerIncrementalRebakeTests
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

            _gridGo = new GameObject(nameof(WorldGridBuilder));
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _bakerGo = new GameObject(nameof(WorldCollisionBaker));
            _baker = _bakerGo.AddComponent<WorldCollisionBaker>();
            _tagMap = new CollisionTagMap();

            // EditMode does not invoke MonoBehaviour OnEnable on AddComponent
            // reliably — without firing it manually the tilemapTileChanged
            // subscription is dead and every test below would silently no-op.
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(WorldCollisionBaker).GetMethod("OnEnable", Flags)?.Invoke(_baker, null);

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
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            if (_wallTile != null) Object.DestroyImmediate(_wallTile);
        }

        // ── Reflection helpers ----------------------------------------------

        private static Tilemap[] GetSubTilemaps(WorldCollisionBaker baker)
        {
            var field = typeof(WorldCollisionBaker).GetField(
                "_subTilemaps", BindingFlags.Instance | BindingFlags.NonPublic);
            return (Tilemap[])field.GetValue(baker);
        }

        private static HashSet<Vector3Int> GetPendingCells(WorldCollisionBaker baker)
        {
            var field = typeof(WorldCollisionBaker).GetField(
                "_pendingCells", BindingFlags.Instance | BindingFlags.NonPublic);
            return (HashSet<Vector3Int>)field.GetValue(baker);
        }

        private static bool GetDirty(WorldCollisionBaker baker)
        {
            var field = typeof(WorldCollisionBaker).GetField(
                "_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)field.GetValue(baker);
        }

        private static void InvokeLateUpdate(WorldCollisionBaker baker)
        {
            var method = typeof(WorldCollisionBaker).GetMethod(
                "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Reflection: LateUpdate must exist.");
            method.Invoke(baker, null);
        }

        private Tilemap Collision => _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);

        // ── Accumulation ------------------------------------------------------

        [Test]
        public void OnAnyTilemapChanged_AccumulatesChangedPosition_IntoPendingCells()
        {
            // The bridge the whole incremental path depends on: a real SetTile on
            // the source must land the exact cell position in _pendingCells,
            // BEFORE any flush happens.
            var cell = new Vector3Int(3, 3, 0);
            Collision.SetTile(cell, _wallTile);

            var pending = GetPendingCells(_baker);
            Assert.IsTrue(pending.Contains(cell),
                "A real SetTile on the source Collision tilemap must add its cell to _pendingCells.");
        }

        // ── Add -----------------------------------------------------------

        [Test]
        public void Flush_WithOnlyOnePendingCell_StampsCorrectSubmap_ViaIncrementalPath()
        {
            var cell = new Vector3Int(3, 3, 0);
            Collision.SetTile(cell, _wallTile);
            _tagMap.Set(new Vector2Int(3, 3), "4");

            InvokeLateUpdate(_baker);

            var subs = GetSubTilemaps(_baker);
            Assert.IsNotNull(subs[4].GetTile(cell),
                "Newly painted + tagged '4' cell must land in sub-tilemap[4] after an incremental flush.");
            for (int i = 0; i < subs.Length; i++)
            {
                if (i == 4) continue;
                Assert.IsNull(subs[i].GetTile(cell),
                    $"Cell tagged '4' must not appear in sub-tilemap[{i}].");
            }
        }

        [Test]
        public void Flush_ClearsPendingCells_AndDirtyFlag()
        {
            Collision.SetTile(new Vector3Int(2, 2, 0), _wallTile);
            Assert.IsTrue(GetDirty(_baker), "Precondition: dirty after SetTile.");
            Assert.AreNotEqual(0, GetPendingCells(_baker).Count, "Precondition: pending cell recorded.");

            InvokeLateUpdate(_baker);

            Assert.IsFalse(GetDirty(_baker), "Dirty flag must be cleared after a flush.");
            Assert.AreEqual(0, GetPendingCells(_baker).Count,
                "Pending cells must be cleared after they've been dispatched — otherwise the " +
                "next unrelated flush would redundantly re-process stale positions.");
        }

        // ── Delete ----------------------------------------------------------

        [Test]
        public void EraseBakedCell_ThenFlush_RemovesItFrom_EverySubmap()
        {
            var cell = new Vector3Int(5, 5, 0);
            Collision.SetTile(cell, _wallTile);
            _tagMap.Set(new Vector2Int(5, 5), "2");
            _baker.RebuildAll(); // full sweep bakes it in, mirrors an already-persisted cell

            var subs = GetSubTilemaps(_baker);
            Assert.IsNotNull(subs[2].GetTile(cell), "Precondition: cell baked into sub-tilemap[2].");

            Collision.SetTile(cell, null); // erase — real event, real pending accumulation
            InvokeLateUpdate(_baker);

            for (int i = 0; i < subs.Length; i++)
                Assert.IsNull(subs[i].GetTile(cell),
                    $"Erased cell must be removed from sub-tilemap[{i}] — an add-only incremental " +
                    "dispatch that could never retract would leave a phantom collider here.");
        }

        // ── Retag (erase + repaint, the real production shape) --------------

        [Test]
        public void RetagViaEraseAndRepaint_MovesStamp_WithoutDisturbingUnrelatedBakedCell()
        {
            var cellA = new Vector3Int(1, 1, 0);
            var cellB = new Vector3Int(9, 9, 0);
            Collision.SetTile(cellA, _wallTile);
            Collision.SetTile(cellB, _wallTile);
            _tagMap.Set(new Vector2Int(1, 1), "0");
            _tagMap.Set(new Vector2Int(9, 9), "6");
            _baker.RebuildAll();

            var subs = GetSubTilemaps(_baker);
            Assert.IsNotNull(subs[0].GetTile(cellA), "Precondition: cellA baked into sub-tilemap[0].");
            Assert.IsNotNull(subs[6].GetTile(cellB), "Precondition: cellB baked into sub-tilemap[6].");

            // Retag cellA: erase then repaint (the only shape ApplyTagToEdits
            // actually produces in production — a same-tile repaint never calls
            // SetTile at all, so it never reaches this event in the first place).
            Collision.SetTile(cellA, null);
            _tagMap.Set(new Vector2Int(1, 1), "3");
            Collision.SetTile(cellA, _wallTile);

            InvokeLateUpdate(_baker);

            Assert.IsNotNull(subs[3].GetTile(cellA), "cellA must now be stamped into sub-tilemap[3].");
            Assert.IsNull(subs[0].GetTile(cellA), "cellA's old stamp in sub-tilemap[0] must be gone.");

            // The whole point of the incremental path: cellB was never touched by
            // this stroke and must survive the flush exactly as it was.
            Assert.IsNotNull(subs[6].GetTile(cellB),
                "Unrelated previously-baked cellB must remain in sub-tilemap[6] — the incremental " +
                "flush must only touch the cells it was actually told changed.");
        }

        // ── RebuildAll still supersedes any queued incremental work ---------

        [Test]
        public void RebuildAll_CalledDirectly_StillClearsPendingCells()
        {
            Collision.SetTile(new Vector3Int(4, 4, 0), _wallTile);
            Assert.AreNotEqual(0, GetPendingCells(_baker).Count, "Precondition: pending cell recorded.");

            _baker.RebuildAll();

            Assert.AreEqual(0, GetPendingCells(_baker).Count,
                "A full sweep already re-derives every cell from scratch, so it must clear any " +
                "queued incremental work rather than leave it to be redundantly replayed later.");
            Assert.IsFalse(GetDirty(_baker), "RebuildAll must leave the baker in a clean (non-dirty) state.");
        }
    }
}
