using System;

namespace Valkur.Infrastructure.Persistence.Profile
{
    /// <summary>
    /// One completed run. Run = "the player started a new game and either
    /// won, died, or quit". Persistence captures enough to drive the
    /// statistics HUD and any future meta-progression curve.
    ///
    /// All fields are flat primitives so the same POCO works as both
    /// the in-memory record and the JSON-serialised payload — no
    /// custom converter dance required.
    /// </summary>
    [Serializable]
    public class RunRecord
    {
        public string runId;                 // GUID; primary key
        public string startedAtIso;          // ISO-8601 UTC
        public string endedAtIso;            // ISO-8601 UTC, empty when in-progress
        public float  durationSeconds;
        public int    depthReached;          // floor / dungeon depth, future-proof
        public string killedBy;              // entity_key of the killer; empty if alive at quit
        public int    totalKills;
        public int    totalXpGained;
        public bool   wasPermadeath;         // captured at run start so unlocking analytics is honest
    }

    /// <summary>
    /// Per-entity kill counter. Aggregated across runs so the player's
    /// "lifetime kill statistics" survive death.
    /// </summary>
    [Serializable]
    public class KillStat
    {
        public string entityKey;             // primary key (e.g. "barbol", "wolf_alpha")
        public int    totalKills;
        public string lastKillAtIso;
    }

    /// <summary>
    /// Achievement unlock record. One row per unlocked achievement.
    /// Locked achievements have no row (sparse storage).
    /// </summary>
    [Serializable]
    public class AchievementRecord
    {
        public string achievementId;         // primary key
        public string unlockedAtIso;
    }

    /// <summary>
    /// Single-row key-value profile data: aggregate counters that don't
    /// fit in any of the other tables (total_runs, total_playtime_sec,
    /// currency_meta, character_unlocks). Keyed by string for designer
    /// extension without touching the schema.
    /// </summary>
    [Serializable]
    public class ProfileEntry
    {
        public string key;
        public string value;                 // string-typed; callers parse to int / float as needed
    }
}
