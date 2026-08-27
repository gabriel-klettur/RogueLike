using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Editors.Entities
{
    /// <summary>
    /// Pins the coordinate round trip for <see cref="EntityInstanceSerializer"/> — the
    /// serializer half of the F5 placement persistence gap the audit's Dimension 3 flags
    /// ("No repository, no entities_instances.json, nothing").
    ///
    /// No scene, no ZoneManager, no MonoBehaviour: <see cref="EntityInstanceSerializer"/> is
    /// pure, taking a zone-name → grid-offset lookup as plain data. That is what makes this
    /// round trip provable exactly the way
    /// <c>.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md</c> says a persistence round trip
    /// has to be proven: as a composition, not one half in isolation. A single save/load cycle
    /// would have looked fine for the old spawner bug too — it took a SECOND cycle to reveal the
    /// drift — so several of these tests chain the trip more than once on purpose.
    /// </summary>
    [TestFixture]
    public class EntityInstanceSerializerTests
    {
        private const string ZONE = "Lobby";
        private static readonly Vector2 ZoneOffset = new Vector2(150f, 50f);
        private const int ZoneHeightTiles = 50;

        private static Dictionary<string, Vector2> OneZone()
            => new Dictionary<string, Vector2> { [ZONE] = ZoneOffset };

        // ── Serialize → Deserialize is the identity ─────────────────────────────

        [Test]
        public void SerializeThenDeserialize_ReproducesTheSameTileAndWorldPosition()
        {
            var original = EntityInstanceSerializer.FromWorldPosition(
                "id-1", "barbol", ZONE, new Vector2(160f, 30f), ZoneOffset, ZoneHeightTiles);

            string json = EntityInstanceSerializer.Serialize(new[] { original });
            var reloaded = EntityInstanceSerializer.Deserialize(json, OneZone(), ZoneHeightTiles);

            Assert.AreEqual(1, reloaded.Count);
            var r = reloaded[0];
            Assert.AreEqual(original.Id, r.Id);
            Assert.AreEqual(original.MonsterKey, r.MonsterKey);
            Assert.AreEqual(original.Zone, r.Zone);
            Assert.AreEqual(original.TileCol, r.TileCol);
            Assert.AreEqual(original.TileRow, r.TileRow);
            Assert.IsTrue(r.ZoneResolved);
            Assert.AreEqual(original.WorldPos.x, r.WorldPos.x, 0.001f);
            Assert.AreEqual(original.WorldPos.y, r.WorldPos.y, 0.001f);
        }

        [Test]
        public void RoundTrip_SurvivesManyCycles_AcrossAGridOfPositions()
        {
            // Twenty-five cycles, mirroring SpawnerTileMappingTests: a bug that only shows up
            // after a restart (drifting by the zone's origin every time) would still pass a
            // single save/load and only fail on a repeated one.
            for (int gx = -3; gx <= 3; gx++)
            for (int gy = -3; gy <= 3; gy++)
            {
                Vector2 world = ZoneOffset + new Vector2(gx * 4f, gy * 4f);
                var record = EntityInstanceSerializer.FromWorldPosition(
                    "probe", "barbol", ZONE, world, ZoneOffset, ZoneHeightTiles);

                for (int cycle = 0; cycle < 25; cycle++)
                {
                    string json = EntityInstanceSerializer.Serialize(new[] { record });
                    var reloaded = EntityInstanceSerializer.Deserialize(json, OneZone(), ZoneHeightTiles);
                    Assert.AreEqual(1, reloaded.Count);
                    record = reloaded[0];

                    Assert.AreEqual(world.x, record.WorldPos.x, 0.001f,
                        $"drifted on cycle {cycle} at grid ({gx},{gy})");
                    Assert.AreEqual(world.y, record.WorldPos.y, 0.001f,
                        $"drifted on cycle {cycle} at grid ({gx},{gy})");
                }
            }
        }

        [Test]
        public void FromWorldPosition_AgreesWithSpawnerTileMapping_DirectlyComposed()
        {
            // The serializer does not reimplement the transform — it composes
            // SpawnerTileMapping, the single owner the spawner-drift incident produced.
            // Asserting the composition (not just that *a* value round-trips) is the point:
            // a re-implementation that quietly disagreed with SpawnerTileMapping would still
            // pass the tests above.
            Vector2 world = new Vector2(171f, 42f);
            var record = EntityInstanceSerializer.FromWorldPosition(
                "id", "barbol", ZONE, world, ZoneOffset, ZoneHeightTiles);

            Vector2Int expectedTile = SpawnerTileMapping.WorldToTile(world, ZoneOffset, ZoneHeightTiles);
            Assert.AreEqual(expectedTile.x, record.TileCol);
            Assert.AreEqual(expectedTile.y, record.TileRow);

            Vector2 expectedWorld = SpawnerTileMapping.TileToWorld(
                record.TileCol, record.TileRow, ZoneOffset, ZoneHeightTiles);
            Assert.AreEqual(expectedWorld, record.WorldPos);
        }

        // ── Unresolved zones are preserved, not dropped ─────────────────────────

        [Test]
        public void Deserialize_UnknownZone_MarksNotResolved_ButKeepsTheRecord()
        {
            var record = EntityInstanceSerializer.FromWorldPosition(
                "id-2", "barbol", "GoneZone", new Vector2(10f, 10f), Vector2.zero, ZoneHeightTiles);

            string json = EntityInstanceSerializer.Serialize(new[] { record });

            // Empty zone lookup: "GoneZone" no longer exists.
            var reloaded = EntityInstanceSerializer.Deserialize(
                json, new Dictionary<string, Vector2>(), ZoneHeightTiles);

            Assert.AreEqual(1, reloaded.Count, "an unresolved record must still be returned");
            Assert.IsFalse(reloaded[0].ZoneResolved);
            Assert.AreEqual("barbol", reloaded[0].MonsterKey);
            Assert.AreEqual("GoneZone", reloaded[0].Zone);
        }

        // ── Serialize is a straight pass-through (no coordinate maths) ──────────

        [Test]
        public void Serialize_WritesRecordsVerbatim_NoRecomputation()
        {
            // Serialize must never re-derive tile/zone from anything — it is handed
            // already-resolved records (either freshly computed from a live transform, or
            // carried through unresolved) and writes exactly what it is given. This is what
            // lets EntitiesRuntimeEditor merge two different provenances into one file safely.
            var manual = new EntityInstanceRecord
            {
                Id = "manual-id",
                MonsterKey = "knight_red",
                Zone = "Somewhere",
                TileCol = 999,
                TileRow = -12,
            };

            string json = EntityInstanceSerializer.Serialize(new[] { manual });

            StringAssert.Contains("\"id\":\"manual-id\"", json);
            StringAssert.Contains("\"monster_key\":\"knight_red\"", json);
            StringAssert.Contains("\"zone\":\"Somewhere\"", json);
            StringAssert.Contains("\"tile\":[999,-12]", json);
        }

        [Test]
        public void Deserialize_EmptyOrNullJson_ReturnsEmptyList()
        {
            Assert.IsEmpty(EntityInstanceSerializer.Deserialize(null, OneZone(), ZoneHeightTiles));
            Assert.IsEmpty(EntityInstanceSerializer.Deserialize("", OneZone(), ZoneHeightTiles));
        }

        [Test]
        public void Deserialize_UnrecognisedTopLevelShape_ReturnsEmptyList_WithoutThrowing()
        {
            // A bare JSON string/number (no "instances" array, not a bare array either) is not
            // a shape this format ever produces — MiniJsonRuntime's own truncated-input loops
            // are a separate, pre-existing defect in Gameplay/World/_Util (out of this change's
            // scope), so this deliberately stays with WELL-FORMED-but-wrong-shaped JSON rather
            // than truncated JSON.
            Assert.DoesNotThrow(() =>
            {
                var result = EntityInstanceSerializer.Deserialize("\"just a string\"", OneZone(), ZoneHeightTiles);
                Assert.IsEmpty(result);
            });
        }
    }
}
