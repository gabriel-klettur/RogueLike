using System.Collections.Generic;

namespace Valkur.Infrastructure.Persistence.Profile
{
    /// <summary>
    /// Aggregator façade over the four meta-progression repositories.
    /// Lives in <c>Valkur.Infrastructure</c> so any layer (Gameplay,
    /// UI, Editor) can ask <c>ServiceLocator.Get&lt;IProfileDb&gt;()</c>
    /// without crossing assembly boundaries.
    ///
    /// Implementations:
    ///   - <see cref="InMemoryProfileDb"/>: dictionary-backed, used in
    ///     EditMode tests and as a fallback on platforms without a
    ///     filesystem (WebGL).
    ///   - <see cref="JsonProfileDb"/>: persists to <c>{persistentDataPath}/profile.json</c>
    ///     via atomic File.Replace + sidecar .bak. Default runtime
    ///     implementation — no native plugin required.
    ///   - <c>SqliteProfileDb</c> (future): drop-in replacement when
    ///     row counts exceed ~10k or when complex aggregate queries
    ///     become bottlenecks. Reference outline lives in code comments
    ///     of <see cref="JsonProfileDb"/>.
    ///
    /// Methods route to the per-domain repositories below; consumers
    /// that only need one repository (e.g. the achievement manager)
    /// inject just that interface to keep their dependency surface
    /// narrow.
    /// </summary>
    public interface IProfileDb
    {
        IRunHistoryRepository  Runs         { get; }
        IKillStatsRepository   KillStats    { get; }
        IAchievementRepository Achievements { get; }
        IProfileRepository     Profile      { get; }

        /// <summary>Persist all in-memory state to disk (or no-op for
        /// InMemoryProfileDb). Called on quit, on level-up checkpoint,
        /// and after a permadeath run ends.</summary>
        void SaveAll();

        /// <summary>Reload all state from disk (or reset to empty for
        /// InMemoryProfileDb). Called on boot.</summary>
        void LoadAll();

        /// <summary>Wipe ALL profile data — used by "Reset Statistics"
        /// in settings, and by tests to start clean.</summary>
        void ResetAll();
    }

    /// <summary>
    /// Run-history repository. Captures one row per played run.
    /// </summary>
    public interface IRunHistoryRepository
    {
        void Insert(RunRecord run);
        void Update(RunRecord run); // upsert by runId
        RunRecord GetById(string runId);
        IReadOnlyList<RunRecord> GetAll(); // ordered descending by startedAtIso
        int Count();
        /// <summary>Average duration of completed runs in seconds; 0 if none.</summary>
        float AverageDurationSeconds();
    }

    /// <summary>
    /// Per-entity kill counters across the player's lifetime.
    /// </summary>
    public interface IKillStatsRepository
    {
        /// <summary>Increment the kill counter for the given entity by 1.
        /// Creates a row on first kill.</summary>
        void RecordKill(string entityKey);
        KillStat Get(string entityKey);                 // null when never killed
        IReadOnlyList<KillStat> GetTop(int limit);      // ordered descending by totalKills
        int TotalAcrossAllEntities();
    }

    /// <summary>
    /// Achievement unlock tracker. Sparse storage: locked achievements
    /// have no row, unlocked achievements have one row each.
    /// </summary>
    public interface IAchievementRepository
    {
        /// <summary>True if the call unlocked a new achievement; false
        /// if it was already unlocked (no double-unlock side effects).</summary>
        bool Unlock(string achievementId);
        bool IsUnlocked(string achievementId);
        IReadOnlyList<AchievementRecord> GetAll();
        int UnlockedCount();
    }

    /// <summary>
    /// Single-row key-value store for global counters: total_runs,
    /// total_playtime_sec, currency_meta, etc.
    /// </summary>
    public interface IProfileRepository
    {
        void SetString(string key, string value);
        void SetInt(string key, int value);
        void SetFloat(string key, float value);
        string GetString(string key, string fallback = "");
        int    GetInt(string key, int fallback = 0);
        float  GetFloat(string key, float fallback = 0f);
        /// <summary>Atomically increment an int-typed key (creates
        /// with value <paramref name="delta"/> when missing).</summary>
        int    IncrementInt(string key, int delta = 1);
        IReadOnlyList<ProfileEntry> GetAll();
    }
}
