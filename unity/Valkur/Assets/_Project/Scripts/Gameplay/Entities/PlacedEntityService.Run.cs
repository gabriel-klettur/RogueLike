using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The run half: kills, respawns, and the save.
    ///
    /// <para><b>It rides the save's metadata bag</b>, the way the market's seed and the death flow
    /// do: it is world state rather than player stats, it is one short string, and a save written
    /// before this layer carries no key — which reads as "this run has killed nothing", the right
    /// answer for every save that predates it. A new game has no save at all, so it starts with
    /// every placement standing. The key is written even when empty, so a later save overwrites an
    /// older one's list instead of inheriting it.</para>
    /// </summary>
    public partial class PlacedEntityService
    {
        /// <summary>How often due respawns are checked. Deadlines are seconds long; per-frame polling buys nothing.</summary>
        private const float RESPAWN_POLL_SECONDS = 0.5f;

        private float _nextRespawnPollAt;
        private readonly List<string> _dueScratch = new List<string>();

        private void TickRespawns()
        {
            if (!_loaded || _run.Count == 0) return;
            if (Time.unscaledTime < _nextRespawnPollAt) return;
            _nextRespawnPollAt = Time.unscaledTime + RESPAWN_POLL_SECONDS;

            SpawnDue(PlacedEntityRunState.UnixNow());
        }

        /// <summary>
        /// Bring back every placement whose deadline has arrived. Returns how many stood up. Internal
        /// so a fixture can pass the clock instead of waiting for it.
        /// </summary>
        internal int SpawnDue(double nowUnix)
        {
            _dueScratch.Clear();
            _run.CollectDue(nowUnix, _dueScratch);
            if (_dueScratch.Count == 0) return 0;

            int spawned = 0;
            var catalog = ResolveCatalog();
            foreach (var id in _dueScratch)
            {
                _run.Forget(id);
                if (!TryGetRecord(id, out var record)) continue;
                var def = catalog != null ? catalog.GetByKey(record.MonsterKey) : null;
                if (def != null && SpawnRecord(record, def) != null) spawned++;
            }

            if (spawned > 0) RaiseChanged();
            return spawned;
        }

        /// <summary>Stand a defeated placement back up now. False when it is unknown or already standing.</summary>
        public bool Revive(string placementId)
        {
            if (!TryGetRecord(placementId, out var record)) return false;
            if (LiveInstance(placementId) != null) return false;

            _run.Forget(placementId);
            var def = ResolveCatalog()?.GetByKey(record.MonsterKey);
            bool ok = def != null && SpawnRecord(record, def) != null;
            RaiseChanged();
            return ok;
        }

        /// <summary>Stand every defeated placement back up. Returns how many stood up.</summary>
        public int ReviveAll()
        {
            int revived = 0;
            foreach (var record in new List<EntityInstanceRecord>(_records))
                if (LiveInstance(record.Id) == null && Revive(record.Id)) revived++;
            return revived;
        }

        // ── Save ─────────────────────────────────────────────────────────────────

        /// <summary>Write the run's kills into a save.</summary>
        public void CollectInto(GameSaveData data)
        {
            if (data == null) return;
            data.SetMeta(PlacedEntityRunState.MetaKey, _run.Serialize());
        }

        /// <summary>
        /// Put a save's kills back. The boot restores the save AFTER the world has spawned, so
        /// placements the save says are dead are taken down (quietly — it is not a new kill), and
        /// placements the save says are alive, but that the previous state had down, are stood up.
        /// </summary>
        public void RestoreFrom(GameSaveData data)
        {
            if (data == null) return;

            _run.CopyFrom(PlacedEntityRunState.Parse(data.GetMeta(PlacedEntityRunState.MetaKey, "")));

            if (!_loaded)
            {
                RaiseChanged();
                return;
            }

            double now = PlacedEntityRunState.UnixNow();
            var catalog = ResolveCatalog();
            foreach (var record in new List<EntityInstanceRecord>(_records))
            {
                if (_run.IsDefeated(record.Id, now))
                {
                    DespawnQuietly(record.Id);
                    continue;
                }

                _run.Forget(record.Id);
                if (LiveInstance(record.Id) != null) continue;
                var def = catalog != null ? catalog.GetByKey(record.MonsterKey) : null;
                if (def != null) SpawnRecord(record, def);
            }

            RaiseChanged();
        }
    }
}
