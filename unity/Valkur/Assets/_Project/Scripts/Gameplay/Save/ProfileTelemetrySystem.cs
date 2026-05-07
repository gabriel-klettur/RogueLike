using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.FSM;
using Valkur.Infrastructure.Persistence.Profile;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Subscribes to <see cref="GameEvents"/> and writes meta-progression
    /// telemetry to <see cref="IProfileDb"/>:
    ///
    ///   - On <c>OnPlayerDied</c>: increments the run's <c>deaths</c>
    ///     counter and the global <c>profile.deaths_total</c> — the run
    ///     stays open. Each death now triggers the spirit/altar revive
    ///     loop instead of ending the session, so dying is just an event
    ///     in the run's history, not its termination.
    ///   - On <c>OnRunEnded</c>: closes the active run row with duration
    ///     and persists to disk. Fired explicitly when the player exits
    ///     the gameplay scene (back to main menu, load other save).
    ///   - On <c>OnEntityDied</c> (NPC victim): increments
    ///     <c>kill_stats[entity_key]</c>.
    ///   - On <c>OnXpGained</c>: accumulates the run's xp_total.
    ///   - On <c>OnLevelUp</c>: updates the run's depth_reached
    ///     (proxy for "how far did this run get").
    ///
    /// One active <see cref="RunRecord"/> at a time, identified by a
    /// freshly-rolled GUID at <see cref="StartRun"/>. The bootstrap
    /// step that creates this MonoBehaviour (EnsureProfileTelemetrySystem)
    /// is responsible for calling StartRun once at scene load.
    /// </summary>
    public sealed class ProfileTelemetrySystem : MonoBehaviour
    {
        // Profile-wide counter key used to mint the next per-profile run ordinal.
        // Stored under IProfileRepository so JsonProfileDb.SaveAll persists it
        // alongside the rest of the meta-progression data.
        private const string RUN_COUNTER_KEY = "run_counter";

        private IProfileDb _db;
        private RunRecord _activeRun;
        private float _runStartTime;

        public RunRecord ActiveRun => _activeRun;
        public IProfileDb Db => _db;

        /// <summary>
        /// Ordinal of the currently active run (1-based). Returns 0 when no
        /// run has been started yet — callers (e.g. GameStateCollector) treat
        /// 0 as "no ordinal known", same convention as missing metadata.
        /// </summary>
        public int ActiveRunOrdinal => _activeRun?.runOrdinal ?? 0;

        public void BindDb(IProfileDb db)
        {
            _db = db;
        }

        /// <summary>
        /// Begins a new run row. When <paramref name="reuseRunId"/> /
        /// <paramref name="reuseOrdinal"/> are non-empty, the existing values
        /// are adopted instead of generating fresh ones — used when loading
        /// a save so the resumed run keeps its original identity (matching
        /// what's stored in the autosave's meta block).
        /// </summary>
        public void StartRun(bool permadeath = false,
                             string reuseRunId = null,
                             int    reuseOrdinal = 0)
        {
            if (_db == null)
            {
                Debug.LogWarning("[ProfileTelemetry] StartRun called before BindDb — telemetry disabled this run.");
                return;
            }

            string runId   = string.IsNullOrEmpty(reuseRunId) ? Guid.NewGuid().ToString("N") : reuseRunId;
            int    ordinal = reuseOrdinal > 0
                ? reuseOrdinal
                : _db.Profile.IncrementInt(RUN_COUNTER_KEY);

            _activeRun = new RunRecord
            {
                runId           = runId,
                runOrdinal      = ordinal,
                startedAtIso    = DateTime.UtcNow.ToString("o"),
                endedAtIso      = string.Empty,
                durationSeconds = 0f,
                depthReached    = 1,
                killedBy        = string.Empty,
                totalKills      = 0,
                totalXpGained   = 0,
                wasPermadeath   = permadeath,
            };
            _runStartTime = Time.time;
            _db.Runs.Insert(_activeRun);

            // Persist the bumped counter immediately. If the process dies
            // before the next save, the next launch must NOT mint the same
            // ordinal — duplicates would defeat the point of having one.
            // SaveAll is cheap (single small JSON file) so the cost of
            // flushing here is negligible compared to the safety it buys.
            if (reuseOrdinal <= 0) _db.SaveAll();
        }

        // ── Events ─────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            GameEvents.OnEntityDied   += OnEntityDied;
            GameEvents.OnPlayerDied   += OnPlayerDied;
            GameEvents.OnRunEnded     += OnRunEnded;
            GameEvents.OnXpGained     += OnXpGained;
            GameEvents.OnLevelUp      += OnLevelUp;
        }

        private void OnDisable()
        {
            GameEvents.OnEntityDied   -= OnEntityDied;
            GameEvents.OnPlayerDied   -= OnPlayerDied;
            GameEvents.OnRunEnded     -= OnRunEnded;
            GameEvents.OnXpGained     -= OnXpGained;
            GameEvents.OnLevelUp      -= OnLevelUp;
        }

        private void OnEntityDied(GameObject victim, GameObject killer)
        {
            if (_db == null || victim == null) return;
            // Skip the player — that's tracked separately by OnPlayerDied.
            if (victim.CompareTag("Player")) return;

            string entityKey = ResolveEntityKey(victim);
            if (string.IsNullOrEmpty(entityKey)) return;

            _db.KillStats.RecordKill(entityKey);
            if (_activeRun != null) _activeRun.totalKills++;
        }

        private void OnPlayerDied()
        {
            if (_db == null) return;

            // The run is no longer over: spirit/altar flow lets the player
            // resume after every death. We simply count the death and persist.
            _db.Profile.IncrementInt("deaths_total");
            if (_activeRun != null)
            {
                // RunRecord doesn't expose a deaths field today — track in profile
                // counters until the schema gains one. Future: add RunRecord.deaths.
                _db.Runs.Update(_activeRun);
            }
            _db.SaveAll();
        }

        private void OnRunEnded()
        {
            if (_db == null || _activeRun == null) return;

            _activeRun.endedAtIso = DateTime.UtcNow.ToString("o");
            _activeRun.durationSeconds = Time.time - _runStartTime;
            _db.Runs.Update(_activeRun);

            _db.Profile.IncrementInt("total_runs");
            _db.Profile.SetFloat("total_playtime_sec",
                _db.Profile.GetFloat("total_playtime_sec") + _activeRun.durationSeconds);

            _db.SaveAll();
        }

        private void OnXpGained(GameObject entity, int amount)
        {
            if (_activeRun == null) return;
            _activeRun.totalXpGained += amount;
        }

        private void OnLevelUp(GameObject entity, int newLevel)
        {
            if (_activeRun == null) return;
            // depthReached is a max-level-this-run proxy. Future: replace
            // with actual dungeon depth when level/floor data exists.
            if (newLevel > _activeRun.depthReached) _activeRun.depthReached = newLevel;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        // Tries the FSMMonsterBrain → MonsterDefinition.monsterKey path
        // first; falls back to GameObject.name (cleaned of "(Clone)" suffix).
        private static string ResolveEntityKey(GameObject victim)
        {
            var brain = victim.GetComponent<FSMMonsterBrain>();
            if (brain != null && brain.Definition != null &&
                !string.IsNullOrEmpty(brain.Definition.monsterKey))
                return brain.Definition.monsterKey;

            string name = victim.name;
            if (string.IsNullOrEmpty(name)) return "unknown";
            int cloneIdx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (cloneIdx > 0) name = name.Substring(0, cloneIdx).TrimEnd();
            return name;
        }
    }
}
