using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Converts placed-monster records to/from the JSON stored in
    /// <c>StreamingAssets/Entities/entities_instances.json</c>.
    ///
    /// Coordinate system: OWNED BY <see cref="SpawnerTileMapping"/>, not re-derived here. That
    /// type exists specifically so two writers of "a position inside a zone" cannot disagree —
    /// see <c>.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md</c>, where a save wrote
    /// absolute world coordinates into a field the loader read as zone-relative and every
    /// placement drifted by its zone's origin on each restart. <see cref="FromWorldPosition"/>
    /// and <see cref="Deserialize"/> are the only two places this file converts a coordinate,
    /// and both go through <c>SpawnerTileMapping.WorldToTile</c> / <c>TileToWorld</c>.
    ///
    /// Deliberately decoupled from <see cref="Valkur.Gameplay.World.ZoneManager"/> and
    /// <see cref="Valkur.Data.MonsterCatalog"/>: this class only knows about tiles and zone
    /// names, not which zones or monster keys currently resolve. The caller
    /// (<c>EntitiesRuntimeEditor</c>) builds the <c>zoneOffsets</c> lookup once from the live
    /// <c>ZoneManager</c> and decides what to do with an unresolved record (carry it through
    /// unchanged rather than drop it — see <see cref="EntityInstanceRecord.ZoneResolved"/>).
    /// This is what keeps the coordinate round trip provable without a scene, the same
    /// consideration that keeps <see cref="SpawnerTileMapping"/> itself pure and static.
    ///
    /// Schema v1: <c>{"version":1,"instances":[{"id","monster_key","zone","tile":[col,row]}]}</c>.
    /// </summary>
    public static class EntityInstanceSerializer
    {
        private const int CURRENT_VERSION = 1;

        /// <summary>
        /// Writes every record verbatim — no scene, no coordinate maths. Used for both the
        /// live-derived records a save just recomputed from the scene (already resolved through
        /// <see cref="FromWorldPosition"/>) and the unresolved leftovers a load could not spawn
        /// (an unknown monster key, a zone that no longer exists). Both are already fully-formed
        /// <see cref="EntityInstanceRecord"/>s by the time they reach this method, so there is
        /// exactly one writer — unlike a design that recomputes on one path and passes through
        /// on the other, which is the shape that let the spawner and particle writers drift.
        /// </summary>
        public static string Serialize(IReadOnlyList<EntityInstanceRecord> records)
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":").Append(CURRENT_VERSION).Append(",\"instances\":[");

            bool first = true;
            foreach (var r in records)
            {
                if (r == null) continue;
                if (!first) sb.Append(',');
                first = false;

                sb.Append('{');
                sb.Append("\"id\":\"").Append(EscapeJson(r.Id)).Append("\",");
                sb.Append("\"monster_key\":\"").Append(EscapeJson(r.MonsterKey)).Append("\",");
                sb.Append("\"zone\":\"").Append(EscapeJson(r.Zone)).Append("\",");
                sb.Append("\"tile\":[").Append(r.TileCol).Append(',').Append(r.TileRow).Append(']');
                sb.Append('}');
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// Parses the stored JSON into records. World position is resolved through
        /// <paramref name="zoneOffsets"/> when the record's zone is present in it;
        /// <see cref="EntityInstanceRecord.ZoneResolved"/> is false otherwise, which is the
        /// caller's signal to carry the record through rather than trying to spawn it.
        /// Tolerates a bare JSON array too (defensive; every writer in this project emits the
        /// wrapped v1 form).
        /// </summary>
        public static List<EntityInstanceRecord> Deserialize(
            string json, IReadOnlyDictionary<string, Vector2> zoneOffsets, int zoneHeightTiles)
        {
            var result = new List<EntityInstanceRecord>();
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                var parsed = MiniJsonRuntime.Deserialize(json);
                List<object> rawList = null;

                if (parsed is List<object> bare)
                {
                    rawList = bare;
                }
                else if (parsed is Dictionary<string, object> obj &&
                         obj.TryGetValue("instances", out var inst) && inst is List<object> list)
                {
                    rawList = list;
                }

                if (rawList == null) return result;

                foreach (var item in rawList)
                {
                    if (item is not Dictionary<string, object> d) continue;

                    string id = GetString(d, "id");
                    if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
                    string monsterKey = GetString(d, "monster_key");
                    string zone       = GetString(d, "zone");

                    int col = 0, row = 0;
                    if (d.TryGetValue("tile", out var tileObj) &&
                        tileObj is List<object> tileList && tileList.Count >= 2)
                    {
                        col = Convert.ToInt32(tileList[0]);
                        row = Convert.ToInt32(tileList[1]);
                    }

                    var record = new EntityInstanceRecord
                    {
                        Id         = id,
                        MonsterKey = monsterKey,
                        Zone       = zone,
                        TileCol    = col,
                        TileRow    = row,
                    };

                    if (zoneOffsets != null && zoneOffsets.TryGetValue(zone ?? "", out var offset))
                    {
                        record.WorldPos     = SpawnerTileMapping.TileToWorld(col, row, offset, zoneHeightTiles);
                        record.ZoneResolved = true;
                    }

                    result.Add(record);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EntityInstanceSerializer] Parse error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Builds a record from a live world position, resolving its zone-relative tile through
        /// <see cref="SpawnerTileMapping.WorldToTile"/> — the single place that direction of the
        /// transform is allowed to happen.
        /// </summary>
        public static EntityInstanceRecord FromWorldPosition(
            string id, string monsterKey, string zone, Vector2 worldPos,
            Vector2 zoneGridOffset, int zoneHeightTiles)
        {
            Vector2Int tile = SpawnerTileMapping.WorldToTile(worldPos, zoneGridOffset, zoneHeightTiles);
            return new EntityInstanceRecord
            {
                Id           = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,
                MonsterKey   = monsterKey,
                Zone         = zone,
                TileCol      = tile.x,
                TileRow      = tile.y,
                WorldPos     = worldPos,
                ZoneResolved = true,
            };
        }

        private static string GetString(Dictionary<string, object> d, string key, string def = "")
            => d.TryGetValue(key, out var v) && v != null ? v.ToString() : def;

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:   sb.Append(c);      break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>One placed-monster record. World position is populated only when the zone it
    /// names resolves — see <see cref="ZoneResolved"/>.</summary>
    public sealed class EntityInstanceRecord
    {
        /// <summary>Stable GUID, minted on first placement and preserved thereafter.</summary>
        public string Id;

        /// <summary>Matches <c>MonsterDefinition.monsterKey</c>.</summary>
        public string MonsterKey;

        /// <summary>Zone name the tile is relative to.</summary>
        public string Zone;

        /// <summary>Zone-relative tile column (row 0 = the zone's top edge). See
        /// <see cref="SpawnerTileMapping"/>.</summary>
        public int TileCol;

        /// <summary>Zone-relative tile row.</summary>
        public int TileRow;

        /// <summary>Resolved absolute world position. Only meaningful when
        /// <see cref="ZoneResolved"/> is true.</summary>
        public Vector2 WorldPos;

        /// <summary>
        /// False when <see cref="Zone"/> did not resolve against the zone offsets passed to
        /// <see cref="EntityInstanceSerializer.Deserialize"/> — a zone that has since been
        /// renamed or removed. The caller carries such a record through unchanged on the next
        /// save rather than dropping it, the same "records the loader could not spawn pass
        /// through unchanged" contract <c>ParticleInstanceSerializer.SerializeRecords</c> uses.
        /// </summary>
        public bool ZoneResolved;
    }
}
