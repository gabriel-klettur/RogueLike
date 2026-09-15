using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.BuildingsEditor
{
    /// <summary>
    /// What a collider stroke is allowed to do per mouse-move sample, and what it must defer
    /// to the release.
    ///
    /// <para>Measured on the shipped world — 302 buildings, 249 of them CG — painting a tree
    /// whose image fourteen instances share, with Show Colliders on: the stroke propagated to
    /// every sibling on EVERY sample, materialising a collider tile and an overlay visual per
    /// newly-solid cell per building. 32 ms/sample at brush 1, 123 ms at brush 4, 149 ms at
    /// brush 8 — 6 to 30 fps while painting. Deferring took the same stroke to 7.5 ms.</para>
    ///
    /// <para>These are source guards. The regression is a one-line reinstatement, and it is
    /// invisible in EditMode where there is no frame to measure and no fourteen siblings.</para>
    /// </summary>
    [TestFixture]
    public class ColliderStrokeDeferralTests
    {
        private static string BuildingsDir =>
            Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Editors", "Buildings");

        private static string Source(string file) => File.ReadAllText(Path.Combine(BuildingsDir, file));

        /// <summary>A method body with its comments stripped: a comment may NAME what the code must not do.</summary>
        private static string BodyOf(string file, string signature)
        {
            string src = Source(file);
            var m = Regex.Match(src, Regex.Escape(signature) + @"(.*?)\n        \}", RegexOptions.Singleline);
            Assert.IsTrue(m.Success, $"{signature} must exist in {file}.");
            return Regex.Replace(m.Groups[1].Value, @"//[^\n]*", string.Empty);
        }

        // ── The per-sample path ──────────────────────────────────────────────────

        [Test]
        public void HandleColliderPaint_DoesNotWalkTheOtherBuildings()
        {
            string body = BodyOf("BuildingsRuntimeEditor.ColliderPaint.cs",
                "private void HandleColliderPaint(Vector3 worldPos)");

            StringAssert.DoesNotContain("GetCachedBuildings", body,
                "A per-sample loop over every building is what cost 123 ms a sample at brush 4.");
            StringAssert.DoesNotContain("RefreshCollidersOverlay", body,
                "The global overlay rebuild walks all 302 buildings; the active one has its own refresh.");
            StringAssert.DoesNotContain("Physics2D.SyncTransforms", body,
                "Nothing queries physics mid-stroke; one sync per stroke is enough.");
            StringAssert.DoesNotContain("SaveInstancesToJson", body);
        }

        [Test]
        public void HandleColliderPaint_StillUpdatesTheBuildingUnderTheCursor()
        {
            string body = BodyOf("BuildingsRuntimeEditor.ColliderPaint.cs",
                "private void HandleColliderPaint(Vector3 worldPos)");

            StringAssert.Contains("ApplyGridCellToBuilding(_activeBuilding", body,
                "Deferring the siblings must not defer the building the author is dragging over.");
            StringAssert.Contains("RefreshActiveBuildingOverlayCells()", body);
            StringAssert.Contains("_colliderStroke.ChangedCells[cell]", body,
                "Every changed cell must be remembered, or the release has no delta to hand the siblings.");
        }

        // ── The release ──────────────────────────────────────────────────────────

        [Test]
        public void EndColliderStroke_RecordsUndo_WithoutReplayingTheWholeGrid()
        {
            string body = BodyOf("BuildingsRuntimeEditor.ColliderData.cs", "private void EndColliderStroke()");

            StringAssert.Contains("_undo.Record(", body,
                "Do() would execute ApplyGridSnapshot, which tears down and rebuilds every tile on " +
                "every sibling — measured at ~3 s on release for fourteen shared trees.");
            Assert.IsFalse(Regex.IsMatch(body, @"_undo\.Do\("),
                "Record, not Do: the active building already reflects the stroke.");
            StringAssert.Contains("ApplyStrokeDeltaToSharedBuildings(", body,
                "The siblings still have to receive the stroke — as a delta.");
            StringAssert.Contains("Physics2D.SyncTransforms()", body,
                "One sync per stroke, since the per-sample one was removed.");
        }

        [Test]
        public void EndColliderStroke_DebouncesTheSave()
        {
            string body = BodyOf("BuildingsRuntimeEditor.ColliderData.cs", "private void EndColliderStroke()");

            StringAssert.Contains("RequestColliderSave()", body,
                "SaveInstancesToJson serialises all 302 instances at 51 ms; a wall is a dozen strokes.");
            StringAssert.DoesNotContain("SaveColliderAuthoring()", body);
        }

        [Test]
        public void EveryExitFlushesThePendingSave()
        {
            // Deferring may only move WHEN the write happens, never WHETHER.
            var exits = new (string file, string signature)[]
            {
                ("BuildingsRuntimeEditor.Lifecycle.cs",   "public void Deactivate()"),
                ("BuildingsRuntimeEditor.cs",             "protected override void OnDestroy()"),
                ("BuildingsRuntimeEditor.ColliderPaint.cs","public void NotifyActiveMapSlotChanged()"),
            };

            foreach (var (file, signature) in exits)
                StringAssert.Contains("FlushPendingColliderSave()", BodyOf(file, signature),
                    $"{signature} in {file} is an exit: a queued collider save must reach disk there.");
        }

        [Test]
        public void TheLiveSiblingPropagation_IsGone()
        {
            foreach (var file in new[] { "BuildingsRuntimeEditor.ColliderPaint.cs", "BuildingsRuntimeEditor.ColliderData.cs" })
                StringAssert.DoesNotContain("PropagateLiveStrokeToSharedTemplates", Source(file),
                    "Reinstating live propagation reinstates 123 ms a sample.");
        }

        [Test]
        public void SiblingsAreNotSeededAtStrokeStart()
        {
            string body = BodyOf("BuildingsRuntimeEditor.ColliderData.cs", "private void BeginColliderStroke()");

            StringAssert.Contains("ApplyGridOverrideToBuilding(_activeBuilding", body,
                "The active building still needs a materialised baseline to diff against.");
            StringAssert.DoesNotContain("SeedSharedTemplatesFromGrid", body,
                "Seeding every sibling at mouse-down rebuilt them for a view that does not change " +
                "during the drag.");
        }

        // ── The tile index ───────────────────────────────────────────────────────

        [Test]
        public void TileLookupsGoThroughTheIndex_NotThroughChildNames()
        {
            string src = Regex.Replace(Source("BuildingsRuntimeEditor.ColliderTiles.cs"), @"//[^\n]*", string.Empty);

            StringAssert.Contains("BuildingCollisionTileIndex.Of(", src);
            Assert.IsFalse(Regex.IsMatch(src, @"transform\.Find\(childName\)"),
                "Transform.name marshals a managed string per child: one scan over a 451-child " +
                "building measured 205 us, once per cell per building per stroke.");
            Assert.IsFalse(src.Contains("TryReusePooledCollTile"),
                "The linear pooled-tile scan is what the index replaces.");
        }

        [Test]
        public void TheIndex_RoundTripsACell()
        {
            var go = new GameObject("Building", typeof(RectTransform));
            try
            {
                var index = BuildingCollisionTileIndex.Of(go.transform);
                Assert.IsNotNull(index);
                Assert.IsNull(index.Find(2, 3), "Nothing registered yet.");

                var tile = new GameObject($"{BuildingCollisionTileIndex.LivePrefix}2_3");
                tile.transform.SetParent(go.transform, false);
                index.Register(2, 3, tile.transform);

                Assert.AreSame(tile.transform, index.Find(2, 3));
                Assert.AreEqual(1, index.LiveCount);

                var retired = index.Retire(2, 3);
                Assert.AreSame(tile.transform, retired);
                Assert.IsNull(index.Find(2, 3), "A retired cell is no longer live.");
                Assert.AreEqual(1, index.PooledCount);
                StringAssert.StartsWith(BuildingCollisionTileIndex.PooledPrefix, tile.name);

                var reused = index.TakePooled(7, 9);
                Assert.AreSame(tile.transform, reused, "The pool must hand the object back, not allocate.");
                Assert.AreEqual($"{BuildingCollisionTileIndex.LivePrefix}7_9", tile.name,
                    "The name is the contract the loader and the tests still read.");
                Assert.AreSame(tile.transform, index.Find(7, 9));
                Assert.AreEqual(0, index.PooledCount);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheIndex_AdoptsTilesItDidNotCreate()
        {
            // The loader materialises a building's grid before the editor ever opens.
            var go = new GameObject("Building", typeof(RectTransform));
            try
            {
                foreach (var name in new[] { "CollTile_0_0", "CollTile_10_4", "_PooledCollTile_123", "Footprint" })
                {
                    var child = new GameObject(name);
                    child.transform.SetParent(go.transform, false);
                }

                var index = BuildingCollisionTileIndex.Of(go.transform);

                Assert.IsNotNull(index.Find(0, 0));
                Assert.IsNotNull(index.Find(10, 4));
                Assert.AreEqual(2, index.LiveCount, "Only the two CollTile children are cells.");
                Assert.AreEqual(1, index.PooledCount);
                Assert.IsNull(index.Find(1, 1));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void CellKey_SeparatesRowsFromColumns()
        {
            Assert.AreNotEqual(BuildingCollisionTileIndex.CellKey(1, 2), BuildingCollisionTileIndex.CellKey(2, 1));
            Assert.AreEqual(BuildingCollisionTileIndex.CellKey(31, 47), BuildingCollisionTileIndex.CellKey(31, 47));
            Assert.AreNotEqual(BuildingCollisionTileIndex.CellKey(0, 1), BuildingCollisionTileIndex.CellKey(1, 0));
        }

        [Test]
        public void TryParseCell_ReadsTheShippedNamingOnly()
        {
            Assert.IsTrue(BuildingCollisionTileIndex.TryParseCell("CollTile_12_7", out int r, out int c));
            Assert.AreEqual(12, r);
            Assert.AreEqual(7, c);

            Assert.IsFalse(BuildingCollisionTileIndex.TryParseCell("_PooledCollTile_9", out _, out _));
            Assert.IsFalse(BuildingCollisionTileIndex.TryParseCell("Footprint", out _, out _));
            Assert.IsFalse(BuildingCollisionTileIndex.TryParseCell("CollTile_bad", out _, out _));
            Assert.IsFalse(BuildingCollisionTileIndex.TryParseCell(null, out _, out _));
        }
    }
}
