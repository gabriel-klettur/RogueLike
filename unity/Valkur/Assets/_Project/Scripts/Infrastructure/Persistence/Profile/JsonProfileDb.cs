using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Valkur.Infrastructure.Persistence.Profile
{
    /// <summary>
    /// JSON-backed <see cref="IProfileDb"/>. Composes the four
    /// <see cref="InMemoryProfileDb"/> repositories for runtime
    /// behaviour and adds disk persistence on top:
    ///
    ///   - One file per database: <c>{persistentDataPath}/profile.json</c>.
    ///   - Atomic write via temp file + <c>File.Replace</c>; sidecar
    ///     <c>profile.json.bak</c> survives a crash mid-write.
    ///   - Schema-versioned payload: bumping <see cref="CurrentSchema"/>
    ///     and adding a migration step in <see cref="MigrateForward"/>
    ///     keeps existing profiles loadable across app updates.
    ///
    /// Why not SQLite (yet): for &lt;~10k rows / no aggregate-query
    /// hot path, JSON is significantly simpler to ship, has zero
    /// native-plugin cost, and matches the existing save layer's
    /// idioms. The repository pattern means swapping in a future
    /// <c>SqliteProfileDb</c> is a constructor change, not a
    /// rewrite — see roadmap note at the bottom of this file.
    ///
    /// Domain Reload OFF: <see cref="LoadAll"/> rehydrates from disk
    /// on each Awake of the bootstrap step that owns this instance,
    /// so static state from a previous Play session is irrelevant
    /// here — instance state is what matters.
    /// </summary>
    public sealed class JsonProfileDb : IProfileDb
    {
        public const int CurrentSchema = 1;

        private readonly string _filePath;
        private readonly string _bakPath;
        private readonly InMemoryProfileDb _inner;

        public IRunHistoryRepository  Runs         => _inner.Runs;
        public IKillStatsRepository   KillStats    => _inner.KillStats;
        public IAchievementRepository Achievements => _inner.Achievements;
        public IProfileRepository     Profile      => _inner.Profile;

        public string FilePath => _filePath;

        /// <summary>
        /// Use the default persistentDataPath/profile.json location.
        /// Public ctor with explicit path is exposed for tests.
        /// </summary>
        public JsonProfileDb() : this(DefaultPath()) { }

        public JsonProfileDb(string filePath)
        {
            _filePath = filePath;
            _bakPath  = filePath + ".bak";
            _inner    = new InMemoryProfileDb();
        }

        private static string DefaultPath()
            => Path.Combine(Application.persistentDataPath, "profile.json");

        // ── IProfileDb ──────────────────────────────────────────────────────────

        public void SaveAll()
        {
            var snap = new Snapshot
            {
                schemaVersion = CurrentSchema,
                runs         = ((InMemoryProfileDb.InMemoryRunHistory)_inner.Runs).Map.Values.ToList(),
                killStats    = ((InMemoryProfileDb.InMemoryKillStats)_inner.KillStats).Map.Values.ToList(),
                achievements = ((InMemoryProfileDb.InMemoryAchievements)_inner.Achievements).Map.Values.ToList(),
                profile      = ((InMemoryProfileDb.InMemoryProfile)_inner.Profile).Map.ToProfileEntries(),
            };

            string json = JsonUtility.ToJson(snap, prettyPrint: true);
            string tempPath = _filePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? string.Empty);
                File.WriteAllText(tempPath, json);

                if (File.Exists(_filePath))
                    File.Replace(tempPath, _filePath, _bakPath);
                else
                    File.Move(tempPath, _filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JsonProfileDb] SaveAll failed for '{_filePath}': {ex.Message}");
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* swallow */ }
                }
            }
        }

        public void LoadAll()
        {
            _inner.ResetAll();
            string source = ResolveSourceFile();
            if (string.IsNullOrEmpty(source)) return;

            try
            {
                string json = File.ReadAllText(source);
                var raw = JsonUtility.FromJson<Snapshot>(json);
                if (raw == null) return;

                Snapshot snap = MigrateForward(raw);
                Apply(snap);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JsonProfileDb] LoadAll failed for '{source}': {ex.Message}. " +
                               "Starting from an empty profile to avoid further corruption.");
                _inner.ResetAll();
            }
        }

        public void ResetAll()
        {
            _inner.ResetAll();
            try
            {
                if (File.Exists(_filePath)) File.Delete(_filePath);
                if (File.Exists(_bakPath))  File.Delete(_bakPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JsonProfileDb] ResetAll could not delete files at '{_filePath}': {ex.Message}");
            }
        }

        // ── Internals ──────────────────────────────────────────────────────────

        // Prefer the primary file; if missing or unreadable, fall back to the
        // sidecar .bak. Returns the path that exists (or empty string).
        private string ResolveSourceFile()
        {
            if (File.Exists(_filePath)) return _filePath;
            if (File.Exists(_bakPath))
            {
                Debug.LogWarning($"[JsonProfileDb] Primary file missing; loading sidecar '{_bakPath}'.");
                return _bakPath;
            }
            return string.Empty;
        }

        // Forward-only migration chain. Each schema bump adds one
        // `if (snap.schemaVersion == N) … snap.schemaVersion = N+1;`
        // step. Same idiom as SaveSchemaMigrator on the gameplay save side.
        private static Snapshot MigrateForward(Snapshot snap)
        {
            if (snap == null) return new Snapshot { schemaVersion = CurrentSchema };
            if (snap.schemaVersion <= 0) snap.schemaVersion = 1; // unset → assume v1

            // Future migrations:
            //   if (snap.schemaVersion == 1) { /* mutate to v2 */ snap.schemaVersion = 2; }

            return snap;
        }

        private void Apply(Snapshot snap)
        {
            if (snap == null) return;
            var runMap   = ((InMemoryProfileDb.InMemoryRunHistory)_inner.Runs).Map;
            var killMap  = ((InMemoryProfileDb.InMemoryKillStats)_inner.KillStats).Map;
            var achMap   = ((InMemoryProfileDb.InMemoryAchievements)_inner.Achievements).Map;
            var profMap  = ((InMemoryProfileDb.InMemoryProfile)_inner.Profile).Map;

            if (snap.runs != null)
                foreach (var r in snap.runs)
                    if (r != null && !string.IsNullOrEmpty(r.runId)) runMap[r.runId] = r;

            if (snap.killStats != null)
                foreach (var k in snap.killStats)
                    if (k != null && !string.IsNullOrEmpty(k.entityKey)) killMap[k.entityKey] = k;

            if (snap.achievements != null)
                foreach (var a in snap.achievements)
                    if (a != null && !string.IsNullOrEmpty(a.achievementId)) achMap[a.achievementId] = a;

            if (snap.profile != null)
                foreach (var p in snap.profile)
                    if (p != null && !string.IsNullOrEmpty(p.key)) profMap[p.key] = p.value ?? string.Empty;
        }

        [Serializable]
        private class Snapshot
        {
            public int schemaVersion;
            public List<RunRecord> runs;
            public List<KillStat> killStats;
            public List<AchievementRecord> achievements;
            public List<ProfileEntry> profile;
        }

        // ── Future: SqliteProfileDb ─────────────────────────────────────────────
        // When row counts cross ~10k or aggregate queries become bottlenecks
        // (e.g. "average run length over last 100 runs filtered by class"),
        // implement IProfileDb against Mono.Data.Sqlite (bundled with Unity)
        // backed by {persistentDataPath}/profile.sqlite3. Schema bootstrap:
        //
        //   CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY);
        //   CREATE TABLE IF NOT EXISTS runs (
        //     run_id TEXT PRIMARY KEY,
        //     started_at_iso TEXT NOT NULL,
        //     ended_at_iso TEXT,
        //     duration_seconds REAL,
        //     depth_reached INTEGER,
        //     killed_by TEXT,
        //     total_kills INTEGER,
        //     total_xp_gained INTEGER,
        //     was_permadeath INTEGER
        //   );
        //   CREATE TABLE IF NOT EXISTS kill_stats (
        //     entity_key TEXT PRIMARY KEY,
        //     total_kills INTEGER NOT NULL,
        //     last_kill_at_iso TEXT
        //   );
        //   CREATE TABLE IF NOT EXISTS achievements (
        //     achievement_id TEXT PRIMARY KEY,
        //     unlocked_at_iso TEXT NOT NULL
        //   );
        //   CREATE TABLE IF NOT EXISTS profile (
        //     key TEXT PRIMARY KEY,
        //     value TEXT
        //   );
        //
        // Migration from JSON → SQLite is a one-shot import via this class's
        // LoadAll() + the new SqliteProfileDb's bulk insert. The repository
        // pattern means callers don't change.
    }

    // Tiny extension to avoid `using System.Linq` in hot paths above.
    internal static class JsonProfileDbExtensions
    {
        public static List<ProfileEntry> ToProfileEntries(this Dictionary<string, string> map)
        {
            var list = new List<ProfileEntry>(map.Count);
            foreach (var kv in map)
                list.Add(new ProfileEntry { key = kv.Key, value = kv.Value });
            return list;
        }
    }
}
