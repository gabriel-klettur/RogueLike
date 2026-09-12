using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
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

            // Only a HOSTILE counts as a kill. Without this the "top monsters killed" board
            // listed the shopkeepers by name — measured, 3 of its 7 rows were vendors — and a
            // player who shot a vendor by accident had that on their record forever. The
            // AUTHORED faction is what answers it, never the derived side: a charmed monster is
            // still a monster, exactly as the loot and coin gates already decide.
            if (EntityFaction.AuthoredSideOf(victim) != FactionSide.Hostile) return;

            string entityKey = ResolveEntityKey(victim);
            if (string.IsNullOrEmpty(entityKey)) return;

            _db.KillStats.RecordKill(entityKey);
            if (_activeRun != null)
            {
                _activeRun.totalKills++;
                // Persisted as it happens. It used to be written only from OnPlayerDied and
                // OnRunEnded, and OnRunEnded never fired — so 249 of 251 shipped run rows read
                // kills=0 while the lifetime table held 39 kills. A run without a death recorded
                // nothing it did.
                _db.Runs.Update(_activeRun);
            }
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

            // Under permadeath the run really is over — there is no altar to walk to and the
            // save is about to be deleted — so it is closed here rather than waiting for the
            // scene to go away. Everywhere else the spirit flow lets the player carry on, which
            // is why an ordinary death does NOT end the run.
            if (GameSettings.Instance != null && GameSettings.Instance.permadeath) EndActiveRun();
        }

        private void OnRunEnded() => EndActiveRun();

        /// <summary>
        /// Closes the open run: stamps its end, its duration, and folds it into the lifetime
        /// counters. Idempotent — a run can only be ended once.
        ///
        /// <para><b>Why this is reached from four places and not from one event.</b>
        /// <c>GameEvents.FireRunEnded()</c> had ZERO production callers for the life of the
        /// project: only a test raised it. So the game opened a run on every boot
        /// (<c>StartRun</c> IS called, from the boot sequence) and closed none — measured on this
        /// machine's profile, <b>251 run rows and every one of them with duration 0</b>, while
        /// the panel that draws them printed "Total runs: 0" above the list.</para>
        ///
        /// <para>The fix is not a fifth event nobody remembers to raise. The system that OWNS
        /// the run closes it when its own scene goes away (<c>OnDestroy</c>), when the
        /// application quits, and when permadeath makes the run over by definition. Those are
        /// conditions, not calls, so a new way of leaving the world cannot forget to end it.</para>
        /// </summary>
        private void EndActiveRun()
        {
            if (_db == null || _activeRun == null || _runEnded) return;
            _runEnded = true;

            _activeRun.endedAtIso = DateTime.UtcNow.ToString("o");
            _activeRun.durationSeconds = Time.time - _runStartTime;
            _db.Runs.Update(_activeRun);

            // The lifetime counters are kept for anything that already reads them, but they are
            // no longer the SOURCE: the panel derives its totals from the run table. Two paths to
            // one number is how the two came to disagree by 251.
            _db.Profile.IncrementInt("total_runs");
            _db.Profile.SetFloat("total_playtime_sec",
                _db.Profile.GetFloat("total_playtime_sec") + _activeRun.durationSeconds);

            _db.SaveAll();
        }

        private bool _runEnded;

        /// <summary>The scene holding the run is going away, so the run is over.</summary>
        private void OnDestroy() => EndActiveRun();

        /// <summary>Quitting from inside a run ends it; otherwise it is lost like every other.</summary>
        private void OnApplicationQuit() => EndActiveRun();

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
