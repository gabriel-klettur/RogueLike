using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Multi-map data-isolation contract — pinned after a real-world data-loss bug:
    /// editing buildings on a custom map slot used to silently overwrite the default
    /// map's <c>buildings_instances.json</c> because every save path was hardcoded to
    /// <c>StreamingAssets/Buildings/</c>.
    ///
    /// These tests ensure:
    ///   • The default slot keeps the legacy StreamingAssets layout (byte-compatible
    ///     with shipping builds + the BuildingsDataGuard backup pipeline).
    ///   • Custom slots route under <c>persistentDataPath/Maps/&lt;slot&gt;/Buildings/</c>.
    ///   • A write to slot A never alters slot B's file, and vice versa (the canonical
    ///     "buildings disappeared from default after creating MiMapa" regression).
    ///   • Switching slots produces files that read back exactly what was written.
    /// </summary>
    [TestFixture]
    public class BuildingsPerSlotRoutingTests
    {
        private string _tempStreamingRoot;
        private string _tempPersistentRoot;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Both test roots live under TempPath so a crash mid-test or a
            // concurrent fixture can't leak files into the real project.
            string baseTemp = Path.Combine(Path.GetTempPath(),
                "Valkur_BuildingsPerSlotRoutingTests_" + Guid.NewGuid().ToString("N"));
            _tempStreamingRoot   = Path.Combine(baseTemp, "Streaming");
            _tempPersistentRoot  = Path.Combine(baseTemp, "Persistent");
            Directory.CreateDirectory(_tempStreamingRoot);
            Directory.CreateDirectory(_tempPersistentRoot);

            MapEditorActiveSlot.SetStreamingRootOverrideForTests(_tempStreamingRoot);
            MapEditorActiveSlot.SetPersistentRootOverrideForTests(_tempPersistentRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // Always clear the overrides — leaving them set would make the next
            // fixture's first MapEditorActiveSlot.Read() pick up our temp dir.
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
        //  CONTRACT 1 — MapEditorActiveSlot path resolution
        // ═════════════════════════════════════════════════════════════════════

        [Test]
        public void Read_NoActiveFile_ReturnsDefault()
        {
            // No _active.txt has been written yet → must return the default
            // slot constant (callers can rely on a non-null result).
            string slot = MapEditorActiveSlot.Read();
            Assert.AreEqual(MapEditorActiveSlot.DEFAULT_SLOT, slot,
                "Read() with no _active.txt must fall back to DEFAULT_SLOT.");
        }

        [Test]
        public void Read_WithActiveFile_ReturnsTrimmedContent()
        {
            string mapsDir = Path.Combine(_tempPersistentRoot, "Maps");
            Directory.CreateDirectory(mapsDir);
            File.WriteAllText(Path.Combine(mapsDir, "_active.txt"), "  MiMapa  \n");

            Assert.AreEqual("MiMapa", MapEditorActiveSlot.Read(),
                "Read() must trim whitespace around the persisted slot name.");
        }

        [Test]
        public void IsDefault_NullEmptyOrDefault_ReturnsTrue()
        {
            Assert.IsTrue(MapEditorActiveSlot.IsDefault(null));
            Assert.IsTrue(MapEditorActiveSlot.IsDefault(""));
            Assert.IsTrue(MapEditorActiveSlot.IsDefault("default"));
            Assert.IsTrue(MapEditorActiveSlot.IsDefault("DEFAULT"),
                "IsDefault must be case-insensitive (mirrors the slot store sanitiser).");
        }

        [Test]
        public void IsDefault_CustomSlot_ReturnsFalse()
        {
            Assert.IsFalse(MapEditorActiveSlot.IsDefault("MiMapa"));
            Assert.IsFalse(MapEditorActiveSlot.IsDefault("forest"));
        }

        [Test]
        public void BuildingsDir_DefaultSlot_RoutesToStreaming()
        {
            string dir = MapEditorActiveSlot.BuildingsDir(MapEditorActiveSlot.DEFAULT_SLOT);
            string expected = Path.Combine(_tempStreamingRoot, "Buildings");
            Assert.AreEqual(expected, dir,
                "Default slot must keep the legacy StreamingAssets/Buildings location " +
                "(byte-compatible with shipping builds and BuildingsDataGuard).");
        }

        [Test]
        public void BuildingsDir_CustomSlot_RoutesToPerSlotPersistentPath()
        {
            string dir = MapEditorActiveSlot.BuildingsDir("MiMapa");
            string expected = Path.Combine(_tempPersistentRoot, "Maps", "MiMapa", "Buildings");
            Assert.AreEqual(expected, dir,
                "Custom slot must route under persistentDataPath/Maps/<slot>/Buildings " +
                "so it's runtime-writable on every Unity target and isolated from default.");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONTRACT 2 — JsonFileBuildingInstanceRepository
        // ═════════════════════════════════════════════════════════════════════

        [Test]
        public void Repository_DefaultSlot_PathFor_LandsInStreaming()
        {
            MapEditorActiveSlot.SetOverrideForTests(MapEditorActiveSlot.DEFAULT_SLOT);
            var repo = new JsonFileBuildingInstanceRepository();
            string path = repo.PathFor(WorldId.Base);
            string expectedDir = Path.Combine(_tempStreamingRoot, "Buildings");
            StringAssert.StartsWith(expectedDir, path,
                "Default slot file path must live inside StreamingAssets/Buildings.");
        }

        [Test]
        public void Repository_CustomSlot_PathFor_LandsInPerSlotPersistentDir()
        {
            MapEditorActiveSlot.SetOverrideForTests("MiMapa");
            var repo = new JsonFileBuildingInstanceRepository();
            string path = repo.PathFor(WorldId.Base);
            string expectedDir = Path.Combine(_tempPersistentRoot, "Maps", "MiMapa", "Buildings");
            StringAssert.StartsWith(expectedDir, path,
                "Custom slot file path must live inside persistentDataPath/Maps/<slot>/Buildings.");
        }

        [Test]
        public void Repository_RoundTripsWritePerSlot()
        {
            // Write a payload while slot=default → it lands in streaming.
            // Switch slot=MiMapa → write a different payload → lands in persistent.
            // Switch back to default → reading must return the original payload.
            // This is the canonical regression test for the data-loss bug.
            const string defaultPayload = "[{\"id\":1,\"template_id\":7,\"zone\":\"Lobby\",\"rel_x\":0,\"rel_y\":0}]";
            const string mimapaPayload  = "[{\"id\":2,\"template_id\":8,\"zone\":\"\",\"rel_x\":32,\"rel_y\":32}]";

            var repo = new JsonFileBuildingInstanceRepository();

            MapEditorActiveSlot.SetOverrideForTests(MapEditorActiveSlot.DEFAULT_SLOT);
            repo.WriteRawJson(WorldId.Base, defaultPayload);

            MapEditorActiveSlot.SetOverrideForTests("MiMapa");
            repo.WriteRawJson(WorldId.Base, mimapaPayload);

            // Inspect both files exist on disk with the expected contents.
            string defaultPath = Path.Combine(_tempStreamingRoot, "Buildings", "buildings_instances.json");
            string mimapaPath  = Path.Combine(_tempPersistentRoot, "Maps", "MiMapa", "Buildings", "buildings_instances.json");
            Assert.IsTrue(File.Exists(defaultPath),  "Default slot file must exist on disk.");
            Assert.IsTrue(File.Exists(mimapaPath),   "Custom slot file must exist on disk.");
            Assert.AreEqual(defaultPayload, File.ReadAllText(defaultPath),
                "Default slot's payload must survive the custom-slot write — this is the bug-2 regression assertion.");
            Assert.AreEqual(mimapaPayload, File.ReadAllText(mimapaPath),
                "Custom slot's payload must be exactly what we wrote.");

            // Now flip back through the repo API and confirm reads route correctly.
            MapEditorActiveSlot.SetOverrideForTests(MapEditorActiveSlot.DEFAULT_SLOT);
            Assert.AreEqual(defaultPayload, repo.ReadRawJson(WorldId.Base),
                "Reading on default slot must hit the StreamingAssets file.");

            MapEditorActiveSlot.SetOverrideForTests("MiMapa");
            Assert.AreEqual(mimapaPayload, repo.ReadRawJson(WorldId.Base),
                "Reading on custom slot must hit the per-slot persistent file.");
        }

        [Test]
        public void Repository_BlankSlotFile_ExistsReturnsFalse()
        {
            // Brand-new custom slot with no buildings ever written: Exists must
            // be false (so BuildingLoader correctly logs "no instances" and
            // skips spawning anything from a stale default-slot path).
            MapEditorActiveSlot.SetOverrideForTests("FreshSlot");
            var repo = new JsonFileBuildingInstanceRepository();
            Assert.IsFalse(repo.Exists(WorldId.Base),
                "A custom slot that has never been saved must not surface the default slot's file.");
            Assert.IsNull(repo.ReadRawJson(WorldId.Base),
                "ReadRawJson on a missing per-slot file must return null (matches default-slot semantics).");
        }

        [Test]
        public void Repository_DefaultSlot_DoesNotPickUpCustomSlotFile()
        {
            // Symmetry check: a write to a custom slot must NOT make the file
            // visible from the default slot's path (catches a regression where
            // the path resolver mistakenly used persistent for both).
            MapEditorActiveSlot.SetOverrideForTests("OtherSlot");
            var repo = new JsonFileBuildingInstanceRepository();
            repo.WriteRawJson(WorldId.Base, "[{\"id\":99}]");

            MapEditorActiveSlot.SetOverrideForTests(MapEditorActiveSlot.DEFAULT_SLOT);
            Assert.IsFalse(repo.Exists(WorldId.Base),
                "Default slot must not see files written under a custom slot — they're isolated by design.");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONTRACT 3 — Multi-world routing still works (regression)
        // ═════════════════════════════════════════════════════════════════════

        [Test]
        public void Repository_NonBaseWorld_BypassesSlotRouting()
        {
            // A non-Base WorldId still uses the legacy Worlds/<slug>/Buildings
            // layout regardless of active slot — multi-world is its own axis,
            // separate from the map slot system.
            MapEditorActiveSlot.SetOverrideForTests("MiMapa");
            var repo = new JsonFileBuildingInstanceRepository();
            string path = repo.PathFor(new WorldId(Guid.NewGuid(), "the_abyss"));
            StringAssert.Contains(Path.Combine("Worlds", "the_abyss", "Buildings"), path,
                "Non-Base WorldId must keep its dedicated Worlds/<slug>/Buildings dir.");
            StringAssert.DoesNotContain("Maps", path,
                "Non-Base WorldId paths must not be rerouted under Maps/<slot>.");
        }
    }
}
