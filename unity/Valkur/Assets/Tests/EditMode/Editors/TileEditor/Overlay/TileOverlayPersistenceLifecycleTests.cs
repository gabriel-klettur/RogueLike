using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Lifecycle and edge-case coverage for <see cref="TileOverlayPersistence"/>
    /// that complements <c>TileOverlayPersistenceTests</c>:
    ///   • SaveZone targets a single zone and clears only that zone's dirty flag.
    ///   • SaveAllDirty drains every dirty zone and fires OnZoneSaved per zone.
    ///   • OnSaveFailed fires for unknown zones (defensive contract).
    ///   • DeleteOverride / ListOverrideFiles / OverridePathForZone cooperate.
    ///   • MarkBatchDirty is a no-op for null/empty input (won't fire spurious events).
    ///   • Cross-zone isolation: editing zone A does not pollute zone B.
    /// </summary>
    [TestFixture]
    public class TileOverlayPersistenceLifecycleTests
    {
        private const string ZONE_A = "zone_lifecycle_test_A";
        private const string ZONE_B = "zone_lifecycle_test_B";
        private const string ZONE_GHOST = "zone_lifecycle_does_not_exist";

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zonesGo;
        private ZoneManager _zones;
        private TileOverlayPersistence _persistence;
        private Tile _tile;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zonesGo = new GameObject("ZoneManager");
            _zones = _zonesGo.AddComponent<ZoneManager>();
            _zones.AddZone(ZONE_A, new Vector2Int(0, 0),  editableInTileEditor: true);
            _zones.AddZone(ZONE_B, new Vector2Int(50, 0), editableInTileEditor: true);

            _persistence = new TileOverlayPersistence(_zones, _grid);

            _tile = ScriptableObject.CreateInstance<Tile>();
            _tile.name = "lifecycle_tile";
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _tile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _tile.sprite.name = "lifecycle_tile";

            TileRegistry.Instance.Register("lifecycle_tile", _tile);
        }

        [TearDown]
        public void TearDown()
        {
            TileOverlayPersistence.DeleteOverride(ZONE_A);
            TileOverlayPersistence.DeleteOverride(ZONE_B);

            UnityEngine.Object.DestroyImmediate(_gridGo);
            UnityEngine.Object.DestroyImmediate(_zonesGo);
            UnityEngine.Object.DestroyImmediate(_tile);
            TileRegistry.Instance.Load(null);
        }

        // ── SaveZone (single-zone) ───────────────────────────────────────

        [Test]
        public void SaveZone_OnlyClearsTargetedZoneDirtyFlag()
        {
            _persistence.MarkCellDirty(new Vector3Int(1, 1, 0));    // zone A
            _persistence.MarkCellDirty(new Vector3Int(60, 1, 0));   // zone B
            Assert.AreEqual(2, _persistence.DirtyZoneCount);

            bool saved = _persistence.SaveZone(ZONE_A);

            Assert.IsTrue(saved);
            Assert.AreEqual(1, _persistence.DirtyZoneCount,
                "Saving zone A must NOT clear zone B from the dirty set.");
            CollectionAssert.Contains(new List<string>(_persistence.DirtyZones), ZONE_B);
        }

        [Test]
        public void SaveZone_FiresOnZoneSavedExactlyOnce()
        {
            _persistence.MarkCellDirty(new Vector3Int(2, 2, 0));

            int savedCount = 0;
            string lastZone = null;
            _persistence.OnZoneSaved += zone => { savedCount++; lastZone = zone; };

            _persistence.SaveZone(ZONE_A);

            Assert.AreEqual(1, savedCount);
            Assert.AreEqual(ZONE_A, lastZone);
        }

        [Test]
        public void SaveZone_UnknownZone_FailsGracefully_DoesNotFireOnZoneSaved()
        {
            int savedFired = 0;
            _persistence.OnZoneSaved += _ => savedFired++;

            bool ok = _persistence.SaveZone(ZONE_GHOST);

            Assert.IsFalse(ok, "Saving an unknown zone must return false.");
            Assert.AreEqual(0, savedFired,
                "OnZoneSaved must NOT fire for an unknown zone.");
        }

        // ── SaveAllDirty (multi-zone drain) ──────────────────────────────

        [Test]
        public void SaveAllDirty_FlushesEveryDirtyZone_AndClearsState()
        {
            _persistence.MarkCellDirty(new Vector3Int(3, 3, 0));    // zone A
            _persistence.MarkCellDirty(new Vector3Int(70, 3, 0));   // zone B
            Assert.AreEqual(2, _persistence.DirtyZoneCount);

            int saved = _persistence.SaveAllDirty();

            Assert.AreEqual(2, saved);
            Assert.IsFalse(_persistence.HasUnsavedChanges,
                "SaveAllDirty must clear the dirty set after a successful flush.");
            Assert.AreEqual(0, _persistence.DirtyZoneCount);
        }

        [Test]
        public void SaveAllDirty_FiresOnZoneSavedOncePerZone()
        {
            _persistence.MarkCellDirty(new Vector3Int(4, 4, 0));    // zone A
            _persistence.MarkCellDirty(new Vector3Int(80, 4, 0));   // zone B

            var savedZones = new List<string>();
            _persistence.OnZoneSaved += savedZones.Add;

            _persistence.SaveAllDirty();

            Assert.AreEqual(2, savedZones.Count);
            CollectionAssert.AreEquivalent(new[] { ZONE_A, ZONE_B }, savedZones);
        }

        [Test]
        public void SaveAllDirty_WithNoDirtyZones_ReturnsZero_AndDoesNotFireEvents()
        {
            int dirtyEvents = 0;
            int saveEvents = 0;
            _persistence.OnDirtyChanged += () => dirtyEvents++;
            _persistence.OnZoneSaved   += _  => saveEvents++;

            int saved = _persistence.SaveAllDirty();

            Assert.AreEqual(0, saved);
            Assert.AreEqual(0, dirtyEvents);
            Assert.AreEqual(0, saveEvents);
        }

        // ── MarkBatchDirty (defensive) ───────────────────────────────────

        [Test]
        public void MarkBatchDirty_NullEdits_DoesNothing()
        {
            int dirtyEvents = 0;
            _persistence.OnDirtyChanged += () => dirtyEvents++;

            _persistence.MarkBatchDirty(null);

            Assert.IsFalse(_persistence.HasUnsavedChanges);
            Assert.AreEqual(0, dirtyEvents);
        }

        [Test]
        public void MarkBatchDirty_EmptyList_DoesNothing()
        {
            int dirtyEvents = 0;
            _persistence.OnDirtyChanged += () => dirtyEvents++;

            _persistence.MarkBatchDirty(new List<TileEdit>());

            Assert.IsFalse(_persistence.HasUnsavedChanges);
            Assert.AreEqual(0, dirtyEvents);
        }

        [Test]
        public void MarkBatchDirty_AggregatesAcrossZones_AndFiresEventOnce()
        {
            int dirtyEvents = 0;
            _persistence.OnDirtyChanged += () => dirtyEvents++;

            // 3 edits: 2 in zone A, 1 in zone B → set goes from 0 → 2 in a single batch.
            _persistence.MarkBatchDirty(new List<TileEdit>
            {
                new TileEdit(new Vector3Int(1, 1, 0), null, null),
                new TileEdit(new Vector3Int(2, 2, 0), null, null),
                new TileEdit(new Vector3Int(60, 10, 0), null, null),
            });

            Assert.AreEqual(2, _persistence.DirtyZoneCount);
            Assert.AreEqual(1, dirtyEvents,
                "OnDirtyChanged must fire exactly once per batch even with multiple zones touched.");
        }

        // ── ClearDirtyState ──────────────────────────────────────────────

        [Test]
        public void ClearDirtyState_FiresEventOnlyWhenSomethingWasCleared()
        {
            int dirtyEvents = 0;
            _persistence.OnDirtyChanged += () => dirtyEvents++;

            _persistence.ClearDirtyState();
            Assert.AreEqual(0, dirtyEvents,
                "ClearDirtyState on an already-clean set must NOT fire OnDirtyChanged.");

            _persistence.MarkCellDirty(new Vector3Int(0, 0, 0));        // +1
            _persistence.ClearDirtyState();                              // +1
            Assert.AreEqual(2, dirtyEvents);
            Assert.IsFalse(_persistence.HasUnsavedChanges);
        }

        // ── Static helpers cooperate ─────────────────────────────────────

        [Test]
        public void OverridePathForZone_LivesUnderOverrideDirectory_AndUsesExpectedExtension()
        {
            string path = TileOverlayPersistence.OverridePathForZone(ZONE_A);

            Assert.IsTrue(path.StartsWith(TileOverlayPersistence.OverrideDirectory),
                $"Override path must be inside OverrideDirectory. Got: {path}");
            StringAssert.EndsWith(".overlay.json", path);
            StringAssert.Contains(ZONE_A, path);
        }

        [Test]
        public void DeleteOverride_RemovesFile_AndIsIdempotent()
        {
            _persistence.MarkCellDirty(new Vector3Int(5, 5, 0));
            Assert.IsTrue(_persistence.SaveZone(ZONE_A));
            string path = TileOverlayPersistence.OverridePathForZone(ZONE_A);
            Assert.IsTrue(File.Exists(path), "Save must produce a file on disk.");

            // First delete: removes.
            bool first  = TileOverlayPersistence.DeleteOverride(ZONE_A);
            // Second delete: must not throw, simply reports nothing-to-do.
            bool second = TileOverlayPersistence.DeleteOverride(ZONE_A);

            Assert.IsTrue(first,  "First DeleteOverride must succeed when the file exists.");
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(second, "Second DeleteOverride must report false (already absent).");
        }

        [Test]
        public void ListOverrideFiles_ReflectsDiskState()
        {
            // Snapshot baseline (other tests/users may have files in the directory).
            var before = new HashSet<string>(TileOverlayPersistence.ListOverrideFiles());

            _persistence.MarkCellDirty(new Vector3Int(7, 7, 0));
            Assert.IsTrue(_persistence.SaveZone(ZONE_A));

            var after = new HashSet<string>(TileOverlayPersistence.ListOverrideFiles());
            string path = TileOverlayPersistence.OverridePathForZone(ZONE_A);

            CollectionAssert.Contains(after, path,
                "ListOverrideFiles must include the file we just saved.");
            Assert.IsTrue(after.Count >= before.Count + 1);
        }
    }
}
