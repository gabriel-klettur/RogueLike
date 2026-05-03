using System;
using System.Collections.Generic;
using System.Linq;

namespace Valkur.Infrastructure.Persistence.Profile
{
    /// <summary>
    /// Dictionary-backed <see cref="IProfileDb"/>. Used by EditMode
    /// tests (deterministic, no IO) and as the WebGL fallback (no
    /// persistent filesystem on browser builds).
    ///
    /// SaveAll / LoadAll / ResetAll are valid no-ops here — there is
    /// no disk to round-trip to. Tests that need to verify
    /// "what survives across sessions" should use the JsonProfileDb
    /// against a temp directory instead.
    /// </summary>
    public sealed class InMemoryProfileDb : IProfileDb
    {
        public IRunHistoryRepository  Runs         { get; }
        public IKillStatsRepository   KillStats    { get; }
        public IAchievementRepository Achievements { get; }
        public IProfileRepository     Profile      { get; }

        public InMemoryProfileDb()
        {
            Runs         = new InMemoryRunHistory();
            KillStats    = new InMemoryKillStats();
            Achievements = new InMemoryAchievements();
            Profile      = new InMemoryProfile();
        }

        public void SaveAll() { /* no-op */ }
        public void LoadAll() { /* no-op */ }

        public void ResetAll()
        {
            ((InMemoryRunHistory)Runs).Clear();
            ((InMemoryKillStats)KillStats).Clear();
            ((InMemoryAchievements)Achievements).Clear();
            ((InMemoryProfile)Profile).Clear();
        }

        // ── Internal helpers — exposed package-internal so JsonProfileDb
        //    can dump/load the same dictionaries via cast. Keeps the
        //    serialiser independent from this class while letting the
        //    JSON adapter compose around the in-memory state.

        internal sealed class InMemoryRunHistory : IRunHistoryRepository
        {
            internal readonly Dictionary<string, RunRecord> Map = new Dictionary<string, RunRecord>();

            public void Insert(RunRecord run)
            {
                if (run == null || string.IsNullOrEmpty(run.runId)) return;
                Map[run.runId] = run;
            }

            public void Update(RunRecord run) => Insert(run); // upsert semantics

            public RunRecord GetById(string runId)
            {
                if (string.IsNullOrEmpty(runId)) return null;
                Map.TryGetValue(runId, out var r);
                return r;
            }

            public IReadOnlyList<RunRecord> GetAll()
            {
                var list = new List<RunRecord>(Map.Values);
                list.Sort((a, b) => string.Compare(b.startedAtIso, a.startedAtIso, StringComparison.Ordinal));
                return list;
            }

            public int Count() => Map.Count;

            public float AverageDurationSeconds()
            {
                if (Map.Count == 0) return 0f;
                float total = 0f;
                int counted = 0;
                foreach (var r in Map.Values)
                {
                    if (r.durationSeconds <= 0f) continue;
                    total += r.durationSeconds;
                    counted++;
                }
                return counted == 0 ? 0f : total / counted;
            }

            internal void Clear() { Map.Clear(); }
        }

        internal sealed class InMemoryKillStats : IKillStatsRepository
        {
            internal readonly Dictionary<string, KillStat> Map = new Dictionary<string, KillStat>();

            public void RecordKill(string entityKey)
            {
                if (string.IsNullOrEmpty(entityKey)) return;
                if (!Map.TryGetValue(entityKey, out var stat))
                {
                    stat = new KillStat { entityKey = entityKey, totalKills = 0 };
                    Map[entityKey] = stat;
                }
                stat.totalKills++;
                stat.lastKillAtIso = DateTime.UtcNow.ToString("o");
            }

            public KillStat Get(string entityKey)
            {
                if (string.IsNullOrEmpty(entityKey)) return null;
                Map.TryGetValue(entityKey, out var s);
                return s;
            }

            public IReadOnlyList<KillStat> GetTop(int limit)
            {
                var list = new List<KillStat>(Map.Values);
                list.Sort((a, b) => b.totalKills.CompareTo(a.totalKills));
                if (limit > 0 && list.Count > limit) list.RemoveRange(limit, list.Count - limit);
                return list;
            }

            public int TotalAcrossAllEntities()
            {
                int total = 0;
                foreach (var s in Map.Values) total += s.totalKills;
                return total;
            }

            internal void Clear() { Map.Clear(); }
        }

        internal sealed class InMemoryAchievements : IAchievementRepository
        {
            internal readonly Dictionary<string, AchievementRecord> Map = new Dictionary<string, AchievementRecord>();

            public bool Unlock(string achievementId)
            {
                if (string.IsNullOrEmpty(achievementId)) return false;
                if (Map.ContainsKey(achievementId)) return false;
                Map[achievementId] = new AchievementRecord
                {
                    achievementId = achievementId,
                    unlockedAtIso = DateTime.UtcNow.ToString("o"),
                };
                return true;
            }

            public bool IsUnlocked(string achievementId)
                => !string.IsNullOrEmpty(achievementId) && Map.ContainsKey(achievementId);

            public IReadOnlyList<AchievementRecord> GetAll()
            {
                var list = new List<AchievementRecord>(Map.Values);
                list.Sort((a, b) => string.Compare(b.unlockedAtIso, a.unlockedAtIso, StringComparison.Ordinal));
                return list;
            }

            public int UnlockedCount() => Map.Count;

            internal void Clear() { Map.Clear(); }
        }

        internal sealed class InMemoryProfile : IProfileRepository
        {
            internal readonly Dictionary<string, string> Map =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public void SetString(string key, string value)
            {
                if (string.IsNullOrEmpty(key)) return;
                Map[key] = value ?? string.Empty;
            }

            public void SetInt(string key, int value)
                => SetString(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            public void SetFloat(string key, float value)
                => SetString(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            public string GetString(string key, string fallback = "")
            {
                if (string.IsNullOrEmpty(key)) return fallback;
                return Map.TryGetValue(key, out var v) ? v : fallback;
            }

            public int GetInt(string key, int fallback = 0)
            {
                var s = GetString(key, null);
                if (string.IsNullOrEmpty(s)) return fallback;
                return int.TryParse(s, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : fallback;
            }

            public float GetFloat(string key, float fallback = 0f)
            {
                var s = GetString(key, null);
                if (string.IsNullOrEmpty(s)) return fallback;
                return float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : fallback;
            }

            public int IncrementInt(string key, int delta = 1)
            {
                int v = GetInt(key, 0) + delta;
                SetInt(key, v);
                return v;
            }

            public IReadOnlyList<ProfileEntry> GetAll()
            {
                var list = new List<ProfileEntry>(Map.Count);
                foreach (var kv in Map)
                    list.Add(new ProfileEntry { key = kv.Key, value = kv.Value });
                list.Sort((a, b) => string.Compare(a.key, b.key, StringComparison.Ordinal));
                return list;
            }

            internal void Clear() { Map.Clear(); }
        }
    }
}
