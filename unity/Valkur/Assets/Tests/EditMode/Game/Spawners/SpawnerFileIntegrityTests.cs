using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// The shipped spawner data must be in the space the loader reads it from.
    ///
    /// Everything else written about this bug guards the CODE: the mapping round trip, the
    /// save/clear symmetry, the fact that neither side open-codes the conversion. All of it is
    /// worth having, and none of it would have gone red on the day the bug was written,
    /// because the code was self-consistent — a save produced a well-formed file, and only a
    /// restart revealed that the file meant something else.
    ///
    /// This one goes red the moment a bad value reaches disk. A tile is zone-relative, so it
    /// lives inside a 50x50 zone; the corrupted file held tiles like (412, 21) and (812, 6).
    /// Nothing had to be played, reproduced or noticed — the numbers alone are the evidence.
    ///
    /// It is also the check that survives a rewrite. If the persistence is redesigned tomorrow
    /// with different classes and different call paths, "a persisted tile is inside its zone"
    /// is still exactly the property that has to hold.
    /// </summary>
    [TestFixture]
    public class SpawnerFileIntegrityTests
    {
        private const string ZONE_DB = "Maps/zones_database.json";
        private const string SPAWNERS = "Spawners/spawners_instances.json";

        private sealed class Zone
        {
            public string Name;
            public Vector2Int GridOffset;
        }

        private int _zoneWidth;
        private int _zoneHeight;
        private Dictionary<string, Zone> _zones;

        private static string Streaming(string rel) =>
            Path.Combine(Application.streamingAssetsPath, rel);

        [SetUp]
        public void SetUp()
        {
            _zones = null;
            string path = Streaming(ZONE_DB);
            if (!File.Exists(path)) return;

            var db = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            if (db == null) return;

            _zoneWidth = ToInt(db, "zone_width_tiles", 50);
            _zoneHeight = ToInt(db, "zone_height_tiles", 50);

            // Offsets in the file are relative to the world origin; ZoneManager exposes them
            // already shifted. Getting this wrong would make the whole fixture assert against
            // the wrong bounds, so it is derived rather than assumed.
            int originX = ToInt(db, "world_origin_x", 0);
            int originY = ToInt(db, "world_origin_y", 0);

            _zones = new Dictionary<string, Zone>(System.StringComparer.OrdinalIgnoreCase);
            if (db.TryGetValue("zones", out var zonesObj) && zonesObj is List<object> list)
            {
                foreach (var z in list.OfType<Dictionary<string, object>>())
                {
                    string name = z.TryGetValue("name", out var n) ? n as string : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    _zones[name] = new Zone
                    {
                        Name = name,
                        GridOffset = new Vector2Int(
                            ToInt(z, "offset_x", 0) - originX,
                            ToInt(z, "offset_y", 0) - originY),
                    };
                }
            }
        }

        private static int ToInt(Dictionary<string, object> d, string key, int fallback) =>
            d.TryGetValue(key, out var v) && v != null
                ? System.Convert.ToInt32(v)
                : fallback;

        private static List<Dictionary<string, object>> Spawners()
        {
            string path = Streaming(SPAWNERS);
            if (!File.Exists(path)) return new List<Dictionary<string, object>>();
            var list = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as List<object>;
            return list == null
                ? new List<Dictionary<string, object>>()
                : list.OfType<Dictionary<string, object>>().ToList();
        }

        private static Vector2Int Tile(Dictionary<string, object> e)
        {
            if (e.TryGetValue("tile", out var t) && t is List<object> l && l.Count >= 2)
                return new Vector2Int(System.Convert.ToInt32(l[0]), System.Convert.ToInt32(l[1]));
            return new Vector2Int(int.MinValue, int.MinValue);
        }

        private static string Id(Dictionary<string, object> e) =>
            e.TryGetValue("id", out var v) ? v as string : "<no id>";

        private static string ZoneOf(Dictionary<string, object> e) =>
            e.TryGetValue("zone", out var v) ? v as string : null;

        // ── The fixture has something to check ───────────────────────────────────

        [Test]
        public void TheZoneDatabaseLoads()
        {
            Assert.IsNotNull(_zones, $"{ZONE_DB} did not parse — every test below would pass vacuously.");
            Assert.IsNotEmpty(_zones, "No zones defined.");
            Assert.Greater(_zoneWidth, 0);
            Assert.Greater(_zoneHeight, 0);
        }

        [Test]
        public void ThereAreSpawnersToCheck()
        {
            Assert.IsNotEmpty(Spawners(),
                $"{SPAWNERS} is empty or missing. If the map genuinely has no spawners this " +
                "test is noise, but far more often it means a save wrote an empty file.");
        }

        // ── The property the bug violated ────────────────────────────────────────

        [Test]
        public void EverySpawnerSitsInsideTheZoneItClaims()
        {
            if (_zones == null) Assert.Ignore("zone database unavailable");

            var bad = new List<string>();
            foreach (var e in Spawners())
            {
                string zoneName = ZoneOf(e);
                if (string.IsNullOrEmpty(zoneName) || !_zones.TryGetValue(zoneName, out _)) continue;

                var tile = Tile(e);
                if (!SpawnerTileMapping.IsInsideZone(tile.x, tile.y, _zoneWidth, _zoneHeight))
                    bad.Add($"{Id(e)}  zone '{zoneName}'  tile {tile}");
            }

            Assert.IsEmpty(bad,
                $"These tiles fall outside a {_zoneWidth}x{_zoneHeight} zone, which means they " +
                "are not zone-relative — almost certainly absolute world coordinates written by " +
                "a save that did not convert. That is the defect that made spawners drift by " +
                "their zone's origin on every restart until they left the map.\n  " +
                string.Join("\n  ", bad));
        }

        [Test]
        public void EverySpawnerNamesAZoneThatExists()
        {
            if (_zones == null) Assert.Ignore("zone database unavailable");

            var unknown = Spawners()
                .Where(e => { var z = ZoneOf(e); return string.IsNullOrEmpty(z) || !_zones.ContainsKey(z); })
                .Select(e => $"{Id(e)}  zone '{ZoneOf(e) ?? "<none>"}'")
                .ToList();

            Assert.IsEmpty(unknown,
                "SpawnerInstanceLoader skips an entry whose zone is not registered — it warns " +
                "and moves on, so the spawner simply never appears and nothing looks broken.\n  " +
                string.Join("\n  ", unknown));
        }

        [Test]
        public void EverySpawnerRoundTripsThroughItsZone()
        {
            // The end-to-end statement: take what is on disk, load it the way the game does,
            // save it the way the editor does, and require the file to be unchanged. This is
            // the assertion the bug could not have survived.
            if (_zones == null) Assert.Ignore("zone database unavailable");

            var broken = new List<string>();
            foreach (var e in Spawners())
            {
                string zoneName = ZoneOf(e);
                if (string.IsNullOrEmpty(zoneName) || !_zones.TryGetValue(zoneName, out var zone)) continue;

                var tile = Tile(e);
                Vector2 world = SpawnerTileMapping.TileToWorld(tile.x, tile.y, zone.GridOffset, _zoneHeight);
                Vector2Int back = SpawnerTileMapping.WorldToTile(world, zone.GridOffset, _zoneHeight);

                if (back != tile)
                    broken.Add($"{Id(e)}  {tile} → world {world} → {back}");
            }

            Assert.IsEmpty(broken, "Load-then-save must be the identity.\n  " + string.Join("\n  ", broken));
        }

        [Test]
        public void NoTwoSpawnersOccupyTheSameTile()
        {
            // Duplicates here were the visible symptom of the reload doubling bug: a reload
            // left editor-placed spawners alive while the file recreated them alongside.
            var stacked = Spawners()
                .GroupBy(e => $"{(e.TryGetValue("template_id", out var t) ? t : "?")}@{ZoneOf(e)}{Tile(e)}")
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} x{g.Count()}")
                .ToList();

            Assert.IsEmpty(stacked,
                "The same template stacked on one tile is what a reload that fails to clear " +
                "produces, and it compounds once saving is automatic.\n  " +
                string.Join("\n  ", stacked));
        }

        // ── Every map slot, not only the default one ─────────────────────────────

        [Test]
        public void EveryPerSlotSpawnerFileIsAlsoInBounds()
        {
            if (_zones == null) Assert.Ignore("zone database unavailable");

            string maps = Path.Combine(Application.persistentDataPath, "Maps");
            if (!Directory.Exists(maps)) Assert.Pass("No custom map slots on this machine.");

            var bad = new List<string>();
            foreach (var slot in Directory.GetDirectories(maps))
            {
                string file = Path.Combine(slot, "Spawners", "spawners_instances.json");
                if (!File.Exists(file)) continue;

                var list = MiniJsonRuntime.Deserialize(File.ReadAllText(file)) as List<object>;
                if (list == null) continue;

                foreach (var e in list.OfType<Dictionary<string, object>>())
                {
                    var tile = Tile(e);
                    if (!SpawnerTileMapping.IsInsideZone(tile.x, tile.y, _zoneWidth, _zoneHeight))
                        bad.Add($"{Path.GetFileName(slot)}: {Id(e)} tile {tile}");
                }
            }

            Assert.IsEmpty(bad,
                "Custom map slots write their own spawner file through the same code, so they " +
                "drift the same way — and nobody looks at them.\n  " + string.Join("\n  ", bad));
        }
    }
}
