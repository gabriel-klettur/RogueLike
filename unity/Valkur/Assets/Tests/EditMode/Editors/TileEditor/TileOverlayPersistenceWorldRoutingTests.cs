using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.TileEditor
{
    /// <summary>
    /// Phase 1 contract: TileOverlayPersistence must route every save
    /// through the WorldId it was constructed with, not a hardcoded
    /// WorldId.Base. Without this, swapping to a non-base world would
    /// silently keep writing to the base world's overlays — invisible
    /// data loss as soon as multi-world ships.
    /// </summary>
    [TestFixture]
    public class TileOverlayPersistenceWorldRoutingTests
    {
        private GameObject _gridGo;
        private GameObject _zonesGo;
        private WorldGridBuilder _grid;
        private ZoneManager _zones;

        [SetUp]
        public void SetUp()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            _gridGo = new GameObject("TilePersistenceGrid");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zonesGo = new GameObject("TilePersistenceZones");
            _zones = _zonesGo.AddComponent<ZoneManager>();
            _zones.AddZone("alpha", Vector2Int.zero, editableInTileEditor: true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo  != null) Object.DestroyImmediate(_gridGo);
            if (_zonesGo != null) Object.DestroyImmediate(_zonesGo);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void DefaultConstructor_RoutesToWorldIdBase()
        {
            var repo = new InMemoryTileOverrideRepository();
            var p = new TileOverlayPersistence(_zones, _grid, repo);
            Assert.AreEqual(WorldId.Base, p.WorldId,
                "Two-arg constructor must default to WorldId.Base so legacy " +
                "single-world callers stay byte-compatible.");
        }

        [Test]
        public void ExplicitConstructor_StoresWorldId()
        {
            var repo = new InMemoryTileOverrideRepository();
            var alt  = new WorldId(System.Guid.NewGuid(), "the_abyss");
            var p = new TileOverlayPersistence(_zones, _grid, repo, alt);
            Assert.AreEqual(alt, p.WorldId);
        }

        [Test]
        public void Save_WritesToConstructedWorld_NotBase()
        {
            // The crucial Phase 1 invariant: a save MUST land in the world
            // the persistence was scoped to. If this slips, multi-world
            // overlays would all collapse onto WorldId.Base and overwrite
            // each other.
            var repo = new InMemoryTileOverrideRepository();
            var alt  = new WorldId(System.Guid.NewGuid(), "the_abyss");
            var p = new TileOverlayPersistence(_zones, _grid, repo, alt);

            p.MarkCellDirty(new Vector3Int(0, 0, 0));
            p.SaveAllDirty();

            Assert.IsTrue(repo.Exists(alt, "alpha"),
                "Save must land in the constructed world.");
            Assert.IsFalse(repo.Exists(WorldId.Base, "alpha"),
                "Save must NOT bleed into WorldId.Base when scoped to another world.");
        }

        [Test]
        public void Save_BaseWorldStillWorks_ForLegacySingleWorldFlow()
        {
            var repo = new InMemoryTileOverrideRepository();
            var p = new TileOverlayPersistence(_zones, _grid, repo);

            p.MarkCellDirty(new Vector3Int(0, 0, 0));
            p.SaveAllDirty();

            Assert.IsTrue(repo.Exists(WorldId.Base, "alpha"),
                "Default-constructed persistence must continue to write to " +
                "WorldId.Base — legacy single-world boot must not regress.");
        }
    }
}
