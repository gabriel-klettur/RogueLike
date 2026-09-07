using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// The single owner of both directions of <c>spawners_instances.json</c>.
    ///
    /// <para>Save and load used to be two hand-rolled halves — a StringBuilder in the editor
    /// and a MiniJson walk in the loader — which is the shape that produced
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT</c>: each half internally consistent, the
    /// composition wrong. <c>SpawnerTileMapping</c> already made the coordinate conversion one
    /// implementation for that reason; this does the same for the record itself, now that a
    /// record carries fifteen fields instead of four.</para>
    ///
    /// <para><b>Schema v2</b> adds a <c>config</c> block per row: this placement's own copy of
    /// the preset it was placed with. A row without one is v1 and is frozen by
    /// <see cref="Freeze"/> as it loads.</para>
    ///
    /// <para><b>Defaults are omitted, and measured against the CLASS default</b> — never
    /// against the preset's value. Omitting fields that merely agree with the preset would
    /// make a later preset edit reach back into every placement that happened to match, which
    /// is exactly the coupling copy-on-place removes, coming back in through the file.</para>
    /// </summary>
    public static class SpawnerInstanceSerializer
    {
        /// <summary>Schema version written into every emitted row's config block.</summary>
        public const int SchemaVersion = 2;

        // The reference every "is this value worth writing" question is asked against.
        // Constructed once; never handed out, because a caller mutating it would silently
        // change what the whole file omits.
        private static readonly SpawnerInstanceConfig Defaults = new SpawnerInstanceConfig();

        // ── Read ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses the whole file. Returns an empty list for null/blank input and null for
        /// input that is not a JSON array, so a caller can tell "no spawners" from
        /// "unreadable" — the second must never be written back over.
        /// </summary>
        public static List<SpawnerInstanceRecord> ParseAll(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<SpawnerInstanceRecord>();

            if (!(MiniJsonRuntime.Deserialize(json) is List<object> rawList)) return null;

            var records = new List<SpawnerInstanceRecord>(rawList.Count);
            foreach (var item in rawList)
            {
                if (item is Dictionary<string, object> dict)
                    records.Add(ParseRecord(dict));
            }
            return records;
        }

        private static SpawnerInstanceRecord ParseRecord(Dictionary<string, object> dict)
        {
            var record = new SpawnerInstanceRecord
            {
                TemplateId = Str(dict, "template_id"),
                Zone       = Str(dict, "zone", "Lobby"),
                InstanceId = Str(dict, "id"),
                Tile       = Vector2Int.zero
            };

            if (dict.TryGetValue("tile", out var tileObj) && tileObj is List<object> tile && tile.Count >= 2)
                record.Tile = new Vector2Int(Int(tile[0]), Int(tile[1]));

            if (dict.TryGetValue("config", out var cfgObj) && cfgObj is Dictionary<string, object> cfg)
            {
                record.Config    = ParseConfig(cfg);
                record.HadConfig = true;
            }

            return record;
        }

        private static SpawnerInstanceConfig ParseConfig(Dictionary<string, object> d)
        {
            // Starts from the class defaults, so an omitted key restores exactly what the
            // writer left out. Anything the file does carry overwrites it.
            var c = new SpawnerInstanceConfig();

            c.triggerType                 = Enum(d, "trigger", c.triggerType);
            c.triggerRadius               = Flt(d, "triggerRadius", c.triggerRadius);
            c.autoStart                   = Bln(d, "autoStart", c.autoStart);
            c.proximityRearms             = Bln(d, "proximityRearms", c.proximityRearms);
            c.spawnMode                   = Enum(d, "spawnMode", c.spawnMode);
            c.cooldownSeconds             = Flt(d, "cooldown", c.cooldownSeconds);
            c.betweenWavesCooldownSeconds = Flt(d, "betweenWaves", c.betweenWavesCooldownSeconds);
            c.advanceOn                   = Enum(d, "advanceOn", c.advanceOn);
            c.maxActive                   = Itg(d, "maxActive", c.maxActive);
            c.persistent                  = Bln(d, "persistent", c.persistent);
            c.restartOnDone               = Bln(d, "restartOnDone", c.restartOnDone);
            c.restartCooldownSeconds      = Flt(d, "restartCooldown", c.restartCooldownSeconds);
            c.spawnRadius                 = Itg(d, "spawnRadius", c.spawnRadius);
            c.spawnerShape                = Enum(d, "shape", c.spawnerShape);
            c.levelBonus                  = Itg(d, "levelBonus", c.levelBonus);
            c.scaleWithPlayerLevel        = Flt(d, "scaleWithPlayerLevel", c.scaleWithPlayerLevel);
            c.defendLeashRadius           = Flt(d, "defendLeashRadius", c.defendLeashRadius);
            c.fsmSetOverride              = Str(d, "fsmSet", c.fsmSetOverride);

            c.waves = ParseWaves(d);
            return c;
        }

        /// <summary>
        /// Reads the roster. A missing <c>roster</c> key yields an EMPTY wave list, not the
        /// preset's — a config that exists is complete by construction, and falling back to
        /// the preset here would be the live link copy-on-place removes.
        /// </summary>
        private static List<WaveDefinition> ParseWaves(Dictionary<string, object> d)
        {
            var waves = new List<WaveDefinition>();
            if (!d.TryGetValue("roster", out var rosterObj) || !(rosterObj is List<object> rows))
                return waves;

            foreach (var rowObj in rows)
            {
                if (!(rowObj is Dictionary<string, object> row)) continue;

                int waveIndex = Itg(row, "wave", 0);
                while (waves.Count <= waveIndex) waves.Add(new WaveDefinition());

                waves[waveIndex].spawns.Add(new WaveSpawnEntry
                {
                    kind         = Str(row, "kind", "monster"),
                    entityId     = Str(row, "entityId"),
                    count        = Mathf.Max(0, Itg(row, "count", 1)),
                    spreadRadius = Flt(row, "spread", 3f)
                });
            }
            return waves;
        }

        // ── Freeze (v1 -> v2) ───────────────────────────────────────────────────

        /// <summary>
        /// Gives every v1 row a config copied from its preset, and reports whether anything
        /// changed — which is what tells the caller a write-back is owed.
        ///
        /// <para>A row whose preset is not in the catalogue is left ALONE, config still null.
        /// Freezing it against a blank preset would replace an author's data with defaults,
        /// silently, on exactly the rows a broken catalogue reference already makes hard to
        /// notice. It stays v1 and migrates the day its preset comes back.</para>
        /// </summary>
        public static bool Freeze(IReadOnlyList<SpawnerInstanceRecord> records,
                                  SpawnerTemplateCatalog catalog)
        {
            if (records == null) return false;

            bool changed = false;
            foreach (var record in records)
            {
                if (record == null || record.Config != null) continue;

                var preset = catalog != null ? catalog.GetById(record.TemplateId) : null;
                if (preset == null) continue;

                record.Config = SpawnerInstanceConfig.SnapshotOf(preset);
                changed = true;
            }
            return changed;
        }

        // ── Write ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Emits the records, verbatim and complete — including any the caller could not
        /// spawn. Rows with no config are written in their v1 shape rather than invented.
        /// </summary>
        public static string Serialize(IReadOnlyList<SpawnerInstanceRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");

            int count = records?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var r = records[i];
                if (r == null) continue;

                sb.Append("  {");
                sb.Append($"\"template_id\": {Quote(r.TemplateId)}, ");
                sb.Append($"\"zone\": {Quote(r.Zone)}, ");
                sb.Append($"\"tile\": [{r.Tile.x}, {r.Tile.y}], ");
                sb.Append($"\"id\": {Quote(r.InstanceId)}");

                if (r.Config != null)
                {
                    sb.AppendLine(",");
                    sb.Append("   \"config\": ");
                    AppendConfig(sb, r.Config);
                    sb.AppendLine();
                    sb.Append("  }");
                }
                else
                {
                    sb.Append('}');
                }

                if (i < count - 1) sb.Append(',');
                sb.AppendLine();
            }

            sb.AppendLine("]");
            return sb.ToString();
        }

        private static void AppendConfig(StringBuilder sb, SpawnerInstanceConfig c)
        {
            var fields = new List<string> { $"\"v\": {SchemaVersion}" };

            if (c.triggerType != Defaults.triggerType)
                fields.Add($"\"trigger\": {Quote(c.triggerType.ToString())}");
            if (!Approximately(c.triggerRadius, Defaults.triggerRadius))
                fields.Add($"\"triggerRadius\": {Num(c.triggerRadius)}");
            if (c.autoStart != Defaults.autoStart)
                fields.Add($"\"autoStart\": {Bool(c.autoStart)}");
            if (c.proximityRearms != Defaults.proximityRearms)
                fields.Add($"\"proximityRearms\": {Bool(c.proximityRearms)}");
            if (c.spawnMode != Defaults.spawnMode)
                fields.Add($"\"spawnMode\": {Quote(c.spawnMode.ToString())}");
            if (!Approximately(c.cooldownSeconds, Defaults.cooldownSeconds))
                fields.Add($"\"cooldown\": {Num(c.cooldownSeconds)}");
            if (!Approximately(c.betweenWavesCooldownSeconds, Defaults.betweenWavesCooldownSeconds))
                fields.Add($"\"betweenWaves\": {Num(c.betweenWavesCooldownSeconds)}");
            if (c.advanceOn != Defaults.advanceOn)
                fields.Add($"\"advanceOn\": {Quote(c.advanceOn.ToString())}");
            if (c.maxActive != Defaults.maxActive)
                fields.Add($"\"maxActive\": {c.maxActive}");
            if (c.persistent != Defaults.persistent)
                fields.Add($"\"persistent\": {Bool(c.persistent)}");
            if (c.restartOnDone != Defaults.restartOnDone)
                fields.Add($"\"restartOnDone\": {Bool(c.restartOnDone)}");
            if (!Approximately(c.restartCooldownSeconds, Defaults.restartCooldownSeconds))
                fields.Add($"\"restartCooldown\": {Num(c.restartCooldownSeconds)}");
            if (c.spawnRadius != Defaults.spawnRadius)
                fields.Add($"\"spawnRadius\": {c.spawnRadius}");
            if (c.spawnerShape != Defaults.spawnerShape)
                fields.Add($"\"shape\": {Quote(c.spawnerShape.ToString())}");
            if (c.levelBonus != Defaults.levelBonus)
                fields.Add($"\"levelBonus\": {c.levelBonus}");
            if (!Approximately(c.scaleWithPlayerLevel, Defaults.scaleWithPlayerLevel))
                fields.Add($"\"scaleWithPlayerLevel\": {Num(c.scaleWithPlayerLevel)}");
            if (!Approximately(c.defendLeashRadius, Defaults.defendLeashRadius))
                fields.Add($"\"defendLeashRadius\": {Num(c.defendLeashRadius)}");
            if (!string.IsNullOrEmpty(c.fsmSetOverride))
                fields.Add($"\"fsmSet\": {Quote(c.fsmSetOverride)}");

            sb.Append('{');
            sb.Append(string.Join(", ", fields));

            // The roster is always written, even when empty: an omitted roster reads back as
            // an empty one, so the two agree, and writing it keeps the block self-describing
            // for anyone opening the file.
            sb.AppendLine(",");
            sb.Append("     \"roster\": [");
            AppendRoster(sb, c.waves);
            sb.Append(']');
            sb.Append('}');
        }

        private static void AppendRoster(StringBuilder sb, List<WaveDefinition> waves)
        {
            if (waves == null || waves.Count == 0) return;

            bool first = true;
            for (int w = 0; w < waves.Count; w++)
            {
                var wave = waves[w];
                if (wave?.spawns == null) continue;

                foreach (var entry in wave.spawns)
                {
                    if (entry == null) continue;
                    if (!first) sb.Append(',');
                    sb.AppendLine();
                    sb.Append("       {");
                    sb.Append($"\"wave\": {w}, ");
                    sb.Append($"\"kind\": {Quote(entry.kind)}, ");
                    sb.Append($"\"entityId\": {Quote(entry.entityId)}, ");
                    sb.Append($"\"count\": {entry.count}, ");
                    sb.Append($"\"spread\": {Num(entry.spreadRadius)}");
                    sb.Append('}');
                    first = false;
                }
            }
            if (!first)
            {
                sb.AppendLine();
                sb.Append("     ");
            }
        }

        // ── Primitives ──────────────────────────────────────────────────────────

        /// <summary>
        /// A float comparison with an ABSOLUTE epsilon, not <c>Mathf.Approximately</c>, which
        /// scales with magnitude and would call 0 and 1e-7 different while calling 300 and
        /// 300.00003 the same. What is being decided here is only whether to write a key.
        /// </summary>
        private static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-5f;

        private static string Num(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        private static string Bool(bool v) => v ? "true" : "false";

        /// <summary>
        /// JSON string escaping. Hand-written because the whole persistence layer is, and
        /// because an unescaped quote or backslash in a zone name or an entity id would
        /// produce a file that <c>MiniJsonRuntime</c> then refuses — turning a naming choice
        /// into a map that will not load.
        /// </summary>
        private static string Quote(string s)
        {
            if (s == null) return "\"\"";

            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else          sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string Str(Dictionary<string, object> d, string key, string fallback = "")
            => d != null && d.TryGetValue(key, out var v) && v is string s ? s : fallback;

        private static bool Bln(Dictionary<string, object> d, string key, bool fallback)
            => d != null && d.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        private static int Itg(Dictionary<string, object> d, string key, int fallback)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static float Flt(Dictionary<string, object> d, string key, float fallback)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            try { return Convert.ToSingle(v, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static int Int(object v)
        {
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        /// <summary>
        /// Enums travel as their NAME, not their ordinal. An ordinal in a hand-editable file
        /// silently changes meaning the day a value is inserted into the enum, and this
        /// project has already paid for a stored ordinal once
        /// (<c>SortingConfig.Z_SKY</c> passed as a sorting order). An unrecognised name falls
        /// back to the default rather than throwing, so a file from a newer build degrades
        /// instead of refusing to load.
        /// </summary>
        private static T Enum<T>(Dictionary<string, object> d, string key, T fallback) where T : struct
        {
            string raw = Str(d, key, null);
            if (string.IsNullOrEmpty(raw)) return fallback;
            return System.Enum.TryParse(raw, ignoreCase: true, out T parsed) ? parsed : fallback;
        }
    }
}
