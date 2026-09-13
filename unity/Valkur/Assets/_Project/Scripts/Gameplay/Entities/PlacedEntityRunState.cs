using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Which hand-placed entities THIS PLAYTHROUGH has killed, and when each one comes back.
    ///
    /// <para><b>Why this is not the placement file.</b> <c>entities_instances.json</c> is authored
    /// world data: an author put a knight in the lobby, and that fact ships to every player and every
    /// save. Killing the knight is a fact about one run. The two used to share the file by accident —
    /// the save path enumerated the LIVE placements, so a kill reached the file only if some later
    /// editor action happened to trigger a write, and then it deleted the placement for good. With
    /// no such action the kill never reached disk and the knight was back on the next Play. One act,
    /// two unrelated outcomes, decided by something the player never did. Same split
    /// <c>IWorldDamageRepository</c> makes for felled trees, for the same reason.</para>
    ///
    /// <para><b>A deadline of 0 means "not this run".</b> A positive one is a Unix timestamp (UTC,
    /// the clock <c>WorldDamageService</c> measures regrowth on), so a respawn keeps counting while
    /// the game is closed, exactly like a felled tree.</para>
    ///
    /// <para>Pure and scene-free on purpose: every rule here is provable in EditMode, and the
    /// serialized form is the one thing a save must never disagree with itself about.</para>
    /// </summary>
    public sealed class PlacedEntityRunState
    {
        /// <summary>Key in the save's metadata bag.</summary>
        public const string MetaKey = "entities.defeated";

        private readonly Dictionary<string, double> _defeated =
            new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>How many placements this run holds as defeated (dead or waiting to respawn).</summary>
        public int Count => _defeated.Count;

        /// <summary>
        /// Record a kill. <paramref name="respawnSeconds"/> at or below zero keeps the placement
        /// dead for the rest of the run.
        /// </summary>
        public void MarkDefeated(string placementId, float respawnSeconds, double nowUnix)
        {
            if (string.IsNullOrEmpty(placementId)) return;
            _defeated[placementId] = respawnSeconds > 0f ? nowUnix + respawnSeconds : 0d;
        }

        /// <summary>
        /// True while the placement must stay off the map: killed with no respawn, or killed and
        /// its deadline has not arrived yet.
        /// </summary>
        public bool IsDefeated(string placementId, double nowUnix)
        {
            if (string.IsNullOrEmpty(placementId)) return false;
            if (!_defeated.TryGetValue(placementId, out double deadline)) return false;
            return deadline <= 0d || nowUnix < deadline;
        }

        /// <summary>True when the run holds any record for this placement, due or not.</summary>
        public bool Contains(string placementId)
            => !string.IsNullOrEmpty(placementId) && _defeated.ContainsKey(placementId);

        /// <summary>The respawn deadline (0 = never this run). False when the placement is not defeated.</summary>
        public bool TryGetRespawnAt(string placementId, out double deadlineUnix)
        {
            deadlineUnix = 0d;
            return !string.IsNullOrEmpty(placementId) &&
                   _defeated.TryGetValue(placementId, out deadlineUnix);
        }

        /// <summary>Drop the record, so the placement is alive again as far as the run knows.</summary>
        public bool Forget(string placementId)
            => !string.IsNullOrEmpty(placementId) && _defeated.Remove(placementId);

        public void Clear() => _defeated.Clear();

        /// <summary>
        /// Replace every record with another state's, deadlines verbatim. Not MarkDefeated, which
        /// turns a duration into "now + seconds": a restored value already IS a deadline.
        /// </summary>
        public void CopyFrom(PlacedEntityRunState other)
        {
            _defeated.Clear();
            if (other == null) return;
            foreach (var kv in other._defeated) _defeated[kv.Key] = kv.Value;
        }

        /// <summary>Every placement whose respawn deadline has arrived. Never-respawn records are not due.</summary>
        public void CollectDue(double nowUnix, List<string> into)
        {
            if (into == null) return;
            foreach (var kv in _defeated)
                if (kv.Value > 0d && nowUnix >= kv.Value) into.Add(kv.Key);
        }

        /// <summary>Snapshot of every record, for probes and the editor.</summary>
        public List<KeyValuePair<string, double>> Entries()
            => new List<KeyValuePair<string, double>>(_defeated);

        /// <summary>
        /// <c>id=deadline;id=deadline</c>, sorted by id so the same run always writes the same
        /// string, and the deadline invariant so a save written under one locale reads under another.
        /// Placement ids are hex GUIDs, so neither separator can appear inside one; an id that
        /// somehow carries one is skipped rather than corrupting its neighbours.
        /// </summary>
        public string Serialize()
        {
            if (_defeated.Count == 0) return "";

            var ids = new List<string>(_defeated.Keys);
            ids.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder(ids.Count * 48);
            foreach (var id in ids)
            {
                if (id.IndexOf('=') >= 0 || id.IndexOf(';') >= 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(id).Append('=')
                  .Append(_defeated[id].ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Read <see cref="Serialize"/>'s form back. Tolerant: a malformed entry is dropped and the
        /// rest survive, because a corrupted metadata value is not worth refusing a whole save over,
        /// and the cost of a dropped entry is one monster standing where it was killed.
        /// </summary>
        public static PlacedEntityRunState Parse(string raw)
        {
            var state = new PlacedEntityRunState();
            if (string.IsNullOrEmpty(raw)) return state;

            foreach (var entry in raw.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;

                string id = entry.Substring(0, eq).Trim();
                if (id.Length == 0) continue;

                if (!double.TryParse(entry.Substring(eq + 1), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out double deadline) ||
                    double.IsNaN(deadline) || double.IsInfinity(deadline))
                    continue;

                state._defeated[id] = deadline < 0d ? 0d : deadline;
            }
            return state;
        }

        /// <summary>Seconds since the Unix epoch, UTC.</summary>
        public static double UnixNow()
            => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }
}
