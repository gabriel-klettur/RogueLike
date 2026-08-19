using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// The tile ↔ world round trip for placed spawners.
    ///
    /// Spawners placed with F3 vanished after a restart. Not because they failed to save —
    /// they saved correctly — but because the two halves of the round trip used different
    /// coordinate systems. The file stores tiles zone-relative with the row axis flipped;
    /// <c>SpawnerInstanceLoader</c> converted on the way in, while the editor wrote
    /// <c>RoundToInt(transform.position)</c> straight out. Absolute coordinates went into a
    /// field read as zone-relative, so every reload displaced each spawner by its zone's
    /// origin — Lobby sits at (150, 50), so they marched 150 tiles right per restart until
    /// they were off the map.
    ///
    /// It was invisible because both sides were individually reasonable and nothing ever
    /// compared them. So what is asserted here is the composition, not either half.
    /// </summary>
    [TestFixture]
    public class SpawnerTileMappingTests
    {
        private const int ZONE_H = 50;

        // ── The round trip ───────────────────────────────────────────────────────

        [TestCase(0, 0)]
        [TestCase(12, 20)]
        [TestCase(49, 49)]
        [TestCase(25, 0)]
        [TestCase(0, 49)]
        public void TileSurvivesAWorldRoundTrip(int col, int row)
        {
            foreach (var offset in new[] { new Vector2(0, 0), new Vector2(150, 50), new Vector2(-100, -50) })
            {
                Vector2 world = SpawnerTileMapping.TileToWorld(col, row, offset, ZONE_H);
                Vector2Int back = SpawnerTileMapping.WorldToTile(world, offset, ZONE_H);

                Assert.AreEqual(new Vector2Int(col, row), back,
                    $"offset {offset}: ({col}, {row}) → {world} → {back}. A round trip that does " +
                    "not land where it started moves every spawner a little further on every " +
                    "restart, which is exactly what made them disappear.");
            }
        }

        [Test]
        public void ManyRoundTripsDoNotDrift()
        {
            // One trip being right is not enough — the bug survived precisely because a single
            // save looked fine. It took a restart to move anything, and each restart moved it
            // again.
            var offset = new Vector2(150f, 50f);
            var tile = new Vector2Int(12, 20);

            for (int i = 0; i < 25; i++)
            {
                Vector2 world = SpawnerTileMapping.TileToWorld(tile.x, tile.y, offset, ZONE_H);
                tile = SpawnerTileMapping.WorldToTile(world, offset, ZONE_H);
            }

            Assert.AreEqual(new Vector2Int(12, 20), tile,
                $"After 25 save/load cycles the spawner is at {tile}. The old code moved it " +
                "150 tiles right per cycle.");
        }

        [Test]
        public void TheRowAxisIsFlipped()
        {
            // The file counts rows from the TOP; world y grows upward. Getting this backwards
            // mirrors every spawner about the middle of its zone, which reads as "some of them
            // moved" rather than as a systematic bug.
            var offset = Vector2.zero;

            Vector2 top = SpawnerTileMapping.TileToWorld(0, 0, offset, ZONE_H);
            Vector2 bottom = SpawnerTileMapping.TileToWorld(0, ZONE_H - 1, offset, ZONE_H);

            Assert.Greater(top.y, bottom.y, "Row 0 is the top of the zone.");
            Assert.AreEqual(ZONE_H - 1, top.y, 1e-4f);
            Assert.AreEqual(0f, bottom.y, 1e-4f);
        }

        [Test]
        public void TheZoneOriginIsApplied()
        {
            // The whole failure was this offset being applied on one side only.
            Vector2 atOrigin = SpawnerTileMapping.TileToWorld(10, 10, Vector2.zero, ZONE_H);
            Vector2 offsetZone = SpawnerTileMapping.TileToWorld(10, 10, new Vector2(150f, 50f), ZONE_H);

            Assert.AreEqual(new Vector2(150f, 50f), offsetZone - atOrigin);
        }

        [Test]
        public void AbsoluteCoordinatesAreRecognisableAsOutOfZone()
        {
            // The classifier used to repair the corrupted file, kept because it is also what
            // tells authored data from data that has already drifted.
            Assert.IsTrue(SpawnerTileMapping.IsInsideZone(12, 20, 50, 50));
            Assert.IsFalse(SpawnerTileMapping.IsInsideZone(262, 78, 50, 50));
            Assert.IsFalse(SpawnerTileMapping.IsInsideZone(-1, 20, 50, 50));
            Assert.IsFalse(SpawnerTileMapping.IsInsideZone(12, 50, 50, 50));
        }

        // ── Both sides must go through it ────────────────────────────────────────

        private static string Script(params string[] parts) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts",
                Path.Combine(parts)));

        [Test]
        public void TheLoaderConvertsThroughTheSharedMapping()
        {
            string src = Script("Gameplay", "Spawners", "SpawnerInstanceLoader.cs");

            Assert.IsTrue(src.Contains("SpawnerTileMapping.TileToWorld"),
                "Reading must go through the shared mapping.");
            Assert.IsFalse(src.Contains("zoneDef.gridOffset.x + tileCol"),
                "The open-coded conversion is what drifted from the writer. One definition, " +
                "two callers.");
        }

        [Test]
        public void TheEditorSavesThroughTheSharedMapping()
        {
            string src = Script("Gameplay", "Editors", "Spawners", "SpawnerEditorManager.Modes.cs");

            Assert.IsTrue(src.Contains("SpawnerTileMapping.WorldToTile"),
                "Writing must go through the shared mapping.");
            Assert.IsFalse(src.Contains("int col = Mathf.RoundToInt(pos.x);"),
                "Persisting the raw world position is the original bug: absolute coordinates " +
                "in a field the loader reads as zone-relative.");
        }

        [Test]
        public void TheEditorResolvesTheRealZoneRatherThanAssumingLobby()
        {
            string src = Script("Gameplay", "Editors", "Spawners", "SpawnerEditorManager.Modes.cs");

            Assert.IsTrue(src.Contains("TryGetZoneAtTile"),
                "The zone is no longer just a label: the save converts positions THROUGH that " +
                "zone's origin, so a spawner placed in zone_150_50 and stamped 'Lobby' has its " +
                "tile computed against the wrong offset and comes back 100 tiles away.");
        }
    }
}
