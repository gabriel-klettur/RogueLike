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
    ///   - On <c>OnPlayerDied</c>: closes the active run row with
    ///     duration / killer / total kills, increments
    ///     <c>profile.total_runs</c>, persists to disk.
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
        private IProfileDb _db;
        private RunRecord _activeRun;
        private float _runStartTime;

        public RunRecord ActiveRun => _activeRun;
        public IProfileDb Db => _db;

        public void BindDb(IProfileDb db)
        {
            _db = db;
        }

        public void StartRun(bool permadeath = false)
        {
            if (_db == null)
            {
                Debug.LogWarning("[ProfileTelemetry] StartRun called before BindDb — telemetry disabled this run.");
                return;
            }

            _activeRun = new RunRecord
            {
                runId           = Guid.NewGuid().ToString("N"),
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
        }

        // ── Events ─────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            GameEvents.OnEntityDied   += OnEntityDied;
            GameEvents.OnPlayerDied   += OnPlayerDied;
            GameEvents.OnXpGained     += OnXpGained;
            GameEvents.OnLevelUp      += OnLevelUp;
        }

        private void OnDisable()
        {
            GameEvents.OnEntityDied   -= OnEntityDied;
            GameEvents.OnPlayerDied   -= OnPlayerDied;
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
            if (_db == null || _activeRun == null) return;

            _activeRun.endedAtIso = DateTime.UtcNow.ToString("o");
            _activeRun.durationSeconds = Time.time - _runStartTime;

            // killedBy: best-effort — read the last damager from EntityRegistry
            // if available; otherwise leave empty. Future improvement: thread
            // attacker identity through OnPlayerDied.
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
