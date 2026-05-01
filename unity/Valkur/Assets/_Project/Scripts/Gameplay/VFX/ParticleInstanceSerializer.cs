using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Single point of truth for converting <see cref="PersistedParticleInstance"/> data
    /// to/from the versioned JSON format stored in
    /// <c>StreamingAssets/Particles/particles_instances.json</c>.
    ///
    /// Schema versions:
    ///   v1 — bare JSON array (legacy Python format). No version field.
    ///   v2 — wrapped object: <c>{"version":2,"instances":[...]}</c>. Each instance
    ///        carries a stable string <c>id</c> (GUID), <c>preset_id</c>, <c>zone</c>,
    ///        <c>rel_x</c>, <c>rel_y</c>, <c>scale_multiplier</c>.
    ///
    /// Migration strategy: v1 is detected on <see cref="Deserialize"/> by the absence of
    /// a top-level <c>version</c> key. It is migrated in-memory to v2 (new GUIDs generated
    /// from the numeric id as seed string). The next <see cref="Serialize"/> call writes v2.
    /// One-shot migration occurs transparently on first save after the upgrade.
    ///
    /// Coordinate system (PPU=32, Y-flip):
    ///   world_x = zoneOffset.x * tileSize + rel_x / PPU
    ///   world_y = zoneOffset.y * tileSize + (zoneHeightTiles - 1) * tileSize - rel_y / PPU
    /// </summary>
    public static class ParticleInstanceSerializer
    {
        private const int CURRENT_VERSION = 2;
        private const float PPU = 32f;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Serializes a list of world-positioned <see cref="PersistedParticleInstance"/>s
        /// to a v2 JSON string.
        /// </summary>
        /// <param name="instances">All persisted emitter components (include inactive).</param>
        /// <param name="zm">ZoneManager used to resolve zone-relative coordinates. May be null (positions stored as-is in zone "").</param>
        /// <param name="zoneHeightTiles">Height of each zone in tiles. Read from ZoneManager.</param>
        /// <param name="tileSize">World units per tile.</param>
        public static string Serialize(
            IReadOnlyList<PersistedParticleInstance> instances,
            ZoneManager zm,
            int zoneHeightTiles = 50,
            float tileSize = 1f)
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":2,\"instances\":[");

            bool first = true;
            foreach (var inst in instances)
            {
                if (inst == null) continue;

                string zone = ResolveZoneName(zm, inst.transform.position);
                float wx = inst.transform.position.x;
                float wy = inst.transform.position.y;
                int relX, relY;

                if (zm != null && zm.TryGetZone(zone, out var zd))
                {
                    relX = Mathf.RoundToInt((wx - zd.gridOffset.x * tileSize) * PPU);
                    relY = Mathf.RoundToInt((zd.gridOffset.y * tileSize + (zoneHeightTiles - 1) * tileSize - wy) * PPU);
                }
                else
                {
                    // No ZoneManager or zone not found: treat origin as (0,0), zone as "".
                    zone = "";
                    relX = Mathf.RoundToInt(wx * PPU);
                    relY = Mathf.RoundToInt(((zoneHeightTiles - 1) * tileSize - wy) * PPU);
                }

                if (!first) sb.Append(",");
                first = false;

                sb.Append("{");
                sb.Append($"\"id\":\"{EscapeJson(inst.StableGuid)}\",");
                sb.Append($"\"preset_id\":\"{EscapeJson(inst.PresetId ?? "")}\",");
                sb.Append($"\"zone\":\"{EscapeJson(zone)}\",");
                sb.Append($"\"rel_x\":{relX},");
                sb.Append($"\"rel_y\":{relY},");
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "\"scale_multiplier\":{0:F4}", inst.ScaleMultiplier));
                sb.Append("}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// Deserializes JSON (v1 or v2) into a list of <see cref="ParticleInstanceRecord"/>s
        /// with world positions already computed.
        /// </summary>
        /// <param name="json">Raw JSON string.</param>
        /// <param name="zm">ZoneManager for coordinate resolution.</param>
        /// <param name="zoneHeightTiles">Zone height in tiles.</param>
        /// <param name="tileSize">World units per tile.</param>
        /// <param name="flipY">Apply Y-flip (Pygame→Unity coordinate conversion).</param>
        /// <returns>List of records, or empty list on parse failure.</returns>
        public static List<ParticleInstanceRecord> Deserialize(
            string json,
            ZoneManager zm,
            int zoneHeightTiles = 50,
            float tileSize = 1f,
            bool flipY = true)
        {
            if (string.IsNullOrEmpty(json)) return new List<ParticleInstanceRecord>();

            try
            {
                var parsed = MiniJsonRuntime.Deserialize(json);
                List<object> rawList = null;

                if (parsed is List<object> bareArray)
                {
                    // V1: bare array — migrate in-memory.
                    rawList = bareArray;
                }
                else if (parsed is Dictionary<string, object> obj)
                {
                    // V2: wrapped object.
                    if (obj.TryGetValue("instances", out var inst) && inst is List<object> instList)
                        rawList = instList;
                }

                if (rawList == null) return new List<ParticleInstanceRecord>();

                var zoneOffsets = BuildZoneOffsets(zm);
                var result = new List<ParticleInstanceRecord>(rawList.Count);

                foreach (var item in rawList)
                {
                    if (item is not Dictionary<string, object> d) continue;

                    // Support both v1 (int id) and v2 (string guid).
                    string guid = GetString(d, "id");
                    if (string.IsNullOrEmpty(guid))
                    {
                        // v1 numeric id — generate a deterministic guid from it.
                        int numericId = GetInt(d, "id");
                        guid = $"v1_{numericId:D6}";
                    }

                    string presetId = GetString(d, "preset_id");
                    string zone = GetString(d, "zone");
                    int relX = GetInt(d, "rel_x");
                    int relY = GetInt(d, "rel_y");
                    float scale = GetFloat(d, "scale_multiplier", 1f);

                    Vector2 worldPos = ComputeWorldPos(zone, relX, relY,
                        zoneOffsets, zoneHeightTiles, tileSize, flipY);

                    result.Add(new ParticleInstanceRecord
                    {
                        Guid = guid,
                        PresetId = presetId,
                        Zone = zone,
                        RelX = relX,
                        RelY = relY,
                        ScaleMultiplier = scale,
                        WorldPos = worldPos
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParticleInstanceSerializer] Parse error: {ex.Message}");
                return new List<ParticleInstanceRecord>();
            }
        }

        // ── Coordinate helpers ───────────────────────────────────────────────────

        private static Vector2 ComputeWorldPos(
            string zone,
            int relX, int relY,
            Dictionary<string, Vector2Int> zoneOffsets,
            int zoneHeightTiles,
            float tileSize,
            bool flipY)
        {
            Vector2Int offset = Vector2Int.zero;
            if (!string.IsNullOrEmpty(zone))
                zoneOffsets.TryGetValue(zone, out offset);

            float wx = offset.x * tileSize + relX / PPU;
            float wy = flipY
                ? offset.y * tileSize + (zoneHeightTiles - 1) * tileSize - relY / PPU
                : offset.y * tileSize + relY / PPU;
            return new Vector2(wx, wy);
        }

        private static Dictionary<string, Vector2Int> BuildZoneOffsets(ZoneManager zm)
        {
            var result = new Dictionary<string, Vector2Int>(StringComparer.Ordinal);
            if (zm == null) return result;
            foreach (var zone in zm.GetZonesSnapshot())
                result[zone.zoneName] = zone.gridOffset;
            return result;
        }

        private static string ResolveZoneName(ZoneManager zm, Vector3 worldPos)
        {
            if (zm == null) return "Lobby";
            return zm.DetectZone(new Vector2(worldPos.x, worldPos.y));
        }

        // ── JSON field helpers ───────────────────────────────────────────────────

        private static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
            {
                try { return Convert.ToInt32(v); } catch { }
            }
            return def;
        }

        private static string GetString(Dictionary<string, object> d, string key, string def = "")
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return v.ToString();
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float def = 0f)
        {
            if (d.TryGetValue(key, out var v) && v != null)
            {
                try { return Convert.ToSingle(v, CultureInfo.InvariantCulture); } catch { }
            }
            return def;
        }

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

    /// <summary>
    /// Immutable result record returned by <see cref="ParticleInstanceSerializer.Deserialize"/>.
    /// World position is already resolved (no further coordinate math needed).
    /// </summary>
    public sealed class ParticleInstanceRecord
    {
        /// <summary>Stable GUID string. From v2 JSON or synthesized from v1 numeric id.</summary>
        public string Guid;

        /// <summary>Particle preset id. Matches ParticlePresetDefinition.id.</summary>
        public string PresetId;

        /// <summary>Zone name.</summary>
        public string Zone;

        /// <summary>X pixel offset from zone origin (Python space).</summary>
        public int RelX;

        /// <summary>Y pixel offset from zone origin (Python space).</summary>
        public int RelY;

        /// <summary>Visual scale multiplier. 1 = default.</summary>
        public float ScaleMultiplier;

        /// <summary>Pre-computed Unity world position.</summary>
        public Vector2 WorldPos;
    }
}
