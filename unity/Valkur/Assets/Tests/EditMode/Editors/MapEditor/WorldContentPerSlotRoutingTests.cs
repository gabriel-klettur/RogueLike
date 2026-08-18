using System;
using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Per-slot data isolation for the world-content domains that are NOT
    /// buildings — spawners, lights, particles and authored item drops.
    ///
    /// Buildings got slot routing first (see <c>BuildingsPerSlotRoutingTests</c>);
    /// every other domain kept writing through a hardcoded
    /// <c>StreamingAssets/&lt;Subdir&gt;/</c> path, so placing a spawner or a lamp on
    /// a custom map silently overwrote the default map's file — the same
    /// data-loss shape the buildings fix closed.
    ///
    /// The routing now lives in <see cref="WorldStreamingFileRepositoryBase"/>
    /// behind an opt-in flag, so these tests pin three things at once:
    ///   • default slot keeps the legacy StreamingAssets layout (byte-compat);
    ///   • custom slots route under persistentDataPath/Maps/&lt;slot&gt;/&lt;Subdir&gt;/;
    ///   • shared catalog data (zones_database.json) does NOT follow the slot.
    /// </summary>
    [TestFixture]
    public class WorldContentPerSlotRoutingTests
    {
        private string _tempStreamingRoot;
        private string _tempPersistentRoot;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            string baseTemp = Path.Combine(Path.GetTempPath(),
                "Valkur_WorldContentPerSlotRoutingTests_" + Guid.NewGuid().ToString("N"));
            _tempStreamingRoot  = Path.Combine(baseTemp, "Streaming");
            _tempPersistentRoot = Path.Combine(baseTemp, "Persistent");
            Directory.CreateDirectory(_tempStreamingRoot);
            Directory.CreateDirectory(_tempPersistentRoot);

            MapEditorActiveSlot.SetStreamingRootOverrideForTests(_tempStreamingRoot);
            MapEditorActiveSlot.SetPersistentRootOverrideForTests(_tempPersistentRoot);
        }

        [TearDown]
        public void TearDown()
        {
            MapEditorActiveSlot.SetOverrideForTests(null);
            MapEditorActiveSlot.SetStreamingRootOverrideForTests(null);
            MapEditorActiveSlot.SetPersistentRootOverrideForTests(null);

            try
            {
                string parent = Path.GetDirectoryName(_tempStreamingRoot);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    Directory.Delete(parent, recursive: true);
            }
            catch { /* best-effort cleanup */ }

            LogAssert.ignoreFailingMessages = false;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONTRACT 1 — MapEditorActiveSlot.DirFor generalises BuildingsDir
        // ═════════════════════════════════════════════════════════════════════

        [TestCase("Spawners")]
        [TestCase("Lights")]
        [TestCase("Particles")]
        [TestCase("Items")]
        public void DirFor_DefaultSlot_RoutesToStreaming(string subdir)
        {
            Assert.AreEqual(Path.Combine(_tempStreamingRoot, subdir),
                MapEditorActiveSlot.DirFor(subdir, MapEditorActiveSlot.DEFAULT_SLOT),
                "Default slot must keep the legacy StreamingAssets layout so shipping builds load unchanged.");
        }

        [TestCase("Spawners")]
        [TestCase("Lights")]
        [TestCase("Particles")]
        [TestCase("Items")]
        public void DirFor_CustomSlot_RoutesToPerSlotPersistentPath(string subdir)
        {
            Assert.AreEqual(Path.Combine(_tempPersistentRoot, "Maps", "MiMapa", subdir),
                MapEditorActiveSlot.DirFor(subdir, "MiMapa"),
                "Custom slots must route to persistentDataPath, which is writable on every Unity target.");
        }

        [Test]
        public void DirFor_MatchesBuildingsDir_ForTheBuildingsSubdir()
        {
            // BuildingsDir is now a thin wrapper; a divergence would mean the
            // buildings pipeline silently stopped sharing the slot layout.
            Assert.AreEqual(MapEditorActiveSlot.BuildingsDir("MiMapa"),
                            MapEditorActiveSlot.DirFor("Buildings", "MiMapa"));
            Assert.AreEqual(MapEditorActiveSlot.BuildingsDir(MapEditorActiveSlot.DEFAULT_SLOT),
                            MapEditorActiveSlot.DirFor("Buildings", MapEditorActiveSlot.DEFAULT_SLOT));
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONTRACT 2 — slot-aware repositories follow the active slot
        // ═════════════════════════════════════════════════════════════════════

        private static string PathForDomain(string domain, WorldId world)
        {
            switch (domain)
            {
                case "Spawners":  return new JsonFileSpawnerInstanceRepository().PathFor(world);
                case "Lights":    return new JsonFileLightInstanceRepository().PathFor(world);
                case "Particles": return new JsonFileParticleInstanceRepository().PathFor(world);
                case "Items":     return new JsonFileItemDropRepository().PathFor(world);
                default: throw new ArgumentOutOfRangeException(nameof(domain), domain, null);
            }
        }

        [TestCase("Spawners")]
        [TestCase("Lights")]
        [TestCase("Particles")]
        [TestCase("Items")]
        public void Repository_DefaultSlot_LandsInStreaming(string domain)
        {
            MapEditorActiveSlot.SetOverrideForTests(MapEditorActiveSlot.DEFAULT_SLOT);
            StringAssert.StartsWith(Path.Combine(_tempStreamingRoot, domain),
                PathForDomain(domain, WorldId.Base));
        }

        [TestCase("Spawners")]
        [TestCase("Lights")]
        [TestCase("Particles")]
        [TestCase("Items")]
        public void Repository_CustomSlot_LandsInPerSlotPersistentDir(string domain)
        {
            MapEditorActiveSlot.SetOverrideForTests("MiMapa");
            StringAssert.StartsWith(Path.Combine(_tempPersistentRoot, "Maps", "MiMapa", domain),
                PathForDomain(domain, WorldId.Base));
        }

        [TestCase("Spawners")]
        [TestCase("Lights")]
        [TestCase("Particles")]
        [TestCase("Items")]
        public void Repository_WriteOnCustomSlot_LeavesDefaultSlotUntouched(string domain)
        {
            // The canonical regression: author content on a custom map, switch
            // back to default, find default's file overwritten.
            MapEditorActiveSlot.SetOverrideForTests(MapEditorActiveSlot.DEFAULT_SLOT);
            string defaultPath = PathForDomain(domain, WorldId.Base);
            Directory.CreateDirectory(Path.GetDirectoryName(defaultPath));
            File.WriteAllText(defaultPath, "default-payload");

            MapEditorActiveSlot.SetOverrideForTests("MiMapa");
            string customPath = PathForDomain(domain, WorldId.Base);
            Directory.CreateDirectory(Path.GetDirectoryName(customPath));
            File.WriteAllText(customPath, "custom-payload");

            Assert.AreNotEqual(defaultPath, customPath,
                domain + " must resolve to a different file per slot.");
            StringAssert.Contains("default-payload", File.ReadAllText(defaultPath),
                "Writing " + domain + " on a custom slot must not touch the default slot's file.");
            StringAssert.Contains("custom-payload", File.ReadAllText(customPath));
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONTRACT 3 — what must NOT follow the slot
        // ═════════════════════════════════════════════════════════════════════

        [Test]
        public void ZoneDatabase_IgnoresActiveSlot()
        {
            // zones_database.json is the shipped zone catalog shared by every
            // map. Routing it per slot would fork the catalog and break zone
            // lookups the moment a user creates a custom map.
            MapEditorActiveSlot.SetOverrideForTests(MapEditorActiveSlot.DEFAULT_SLOT);
            string onDefault = new JsonFileZoneDatabaseRepository(_tempStreamingRoot).PathFor(WorldId.Base);

            MapEditorActiveSlot.SetOverrideForTests("MiMapa");
            string onCustom = new JsonFileZoneDatabaseRepository(_tempStreamingRoot).PathFor(WorldId.Base);

            Assert.AreEqual(onDefault, onCustom,
                "The zone database is shared catalog data — it must not follow the map slot.");
        }

        [Test]
        public void PinnedRoot_BeatsSlotRouting()
        {
            // The run-scoped item-drop store is constructed with an explicit
            // root (Saves/<runId>). Slot routing must never relocate a player's
            // in-progress run drops into Maps/<slot>/.
            MapEditorActiveSlot.SetOverrideForTests("MiMapa");
            string runRoot = Path.Combine(_tempPersistentRoot, "Saves", "run42");
            var runRepo = new JsonFileItemDropRepository(runRoot, "WorldDrops", "world_drops.json");

            StringAssert.StartsWith(Path.Combine(runRoot, "WorldDrops"),
                runRepo.PathFor(WorldId.Base),
                "A pinned root is an explicit instruction — slot routing must not override it.");
        }

        [Test]
        public void NonBaseWorld_IgnoresActiveSlot()
        {
            // Worlds and map slots are orthogonal axes: a designed dimension
            // keeps its own StreamingAssets/Worlds/<slug>/ layout no matter
            // which user-authored map happens to be open.
            var altWorld = new WorldId(Guid.NewGuid(), "alt");
            MapEditorActiveSlot.SetOverrideForTests("MiMapa");

            string path = new JsonFileSpawnerInstanceRepository(_tempStreamingRoot).PathFor(altWorld);
            StringAssert.StartsWith(
                Path.Combine(_tempStreamingRoot, "Worlds", "alt", "Spawners"), path);
        }
    }
}
