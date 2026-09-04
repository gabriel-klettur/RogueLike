using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Remembers what this playthrough has broken and worked, and puts it back on the next
    /// load. The run-scoped half of the durability system: <see cref="BuildingDurability"/>
    /// and <c>HarvestNode</c> decide what happens to a building, this decides what survives
    /// the session.
    ///
    /// <para>IT NEVER SCANS THE SCENE. Every write emits the record table it already holds,
    /// which is only ever added to or updated — a building that is not loaded simply
    /// contributes no update. That is deliberate and it is what makes an anti-wipe guard
    /// unnecessary: the failure this project has already paid for twice is a save that
    /// rebuilds a full snapshot from live objects and runs while the world is torn down
    /// (inside an interior, mid-transition), writing the emptiness over hundreds of real
    /// records. A table that cannot observe absence cannot record it.</para>
    ///
    /// <para>Two independent things are stored per building and they are not
    /// interchangeable: DURABILITY, which is combat damage against
    /// <see cref="BuildingDurability"/>, and CHARGES, which is harvest work against a
    /// Deplete-mode node that never touches durability at all. A mine worked to nothing is
    /// not a mine at zero hit points.</para>
    /// </summary>
    public class WorldDamageService
    {
        private readonly IWorldDamageRepository _repository;
        private readonly WorldId _worldId;
        private readonly Dictionary<string, WorldDamageRecord> _records =
            new Dictionary<string, WorldDamageRecord>(64);

        private bool _dirty;

        /// <summary>How many buildings this run has touched.</summary>
        public int Count => _records.Count;

        /// <summary>True when there are unwritten changes.</summary>
        public bool IsDirty => _dirty;

        /// <summary>
        /// Set false to hold every write — used by tests and by any caller that wants to
        /// drive the table without touching the disk.
        /// </summary>
        public bool WritesEnabled { get; set; } = true;

        public WorldDamageService(IWorldDamageRepository repository, WorldId worldId)
        {
            _repository = repository;
            _worldId = worldId;
        }

        // ── Load ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Read the run's damage file. Returns how many records were loaded. A missing file
        /// is a fresh run, not an error.
        /// </summary>
        public int Load()
        {
            _records.Clear();
            _dirty = false;

            if (_repository == null) return 0;

            string json = _repository.ReadRawJson(_worldId);
            if (string.IsNullOrEmpty(json)) return 0;

            WorldDamageFile file;
            try
            {
                file = JsonUtility.FromJson<WorldDamageFile>(json);
            }
            catch (Exception ex)
            {
                // A corrupt damage file must not cost the player their run. The world simply
                // comes back pristine, which is the same state a fresh run is in.
                Debug.LogWarning($"[WorldDamageService] Damage file unreadable, ignoring it: {ex.Message}");
                return 0;
            }

            if (file == null || file.records == null) return 0;

            foreach (var record in file.records)
            {
                if (record == null) continue;
                _records[KeyOf(record.slot, record.zone, record.instanceId)] = record;
            }
            return _records.Count;
        }

        // ── Adoption ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Put a freshly spawned building back the way this run left it, and start listening
        /// so anything that happens to it from now on is recorded.
        ///
        /// <para>Called by the loader AFTER both components have been initialized, because a
        /// restore has to write through their own clamping entry points — a record written
        /// against a profile that has since been rebalanced downward would otherwise leave a
        /// building above its own maximum.</para>
        /// </summary>
        public void Adopt(BuildingDurability durability, HarvestNode node)
        {
            if (durability == null && node == null) return;

            var building = durability != null ? durability.Building : node.Building;
            if (building == null) return;

            string slot = MapEditorActiveSlot.Read();
            string key = KeyOf(slot, building.ZoneName, building.InstanceId);

            if (_records.TryGetValue(key, out var record))
            {
                if (durability != null)
                {
                    // A negative durability means the record never tracked one — it belongs to
                    // a Deplete-mode node, which has no BuildingDurability at all. Restoring it
                    // as written would hand a live building zero hit points.
                    if (record.destroyed) durability.RestoreDestroyed(record.regrowAtUnix);
                    else if (record.durability >= 0) durability.RestoreDurability(record.durability);
                }
                if (TracksCharges(node) && record.charges >= 0)
                {
                    // RestoreSpent rather than RestoreCharges: dropping a node to zero through
                    // the latter enters the spent state, and entering it computes a FRESH
                    // deadline — so a seam emptied five minutes before the player quit would
                    // come back with its full timer restarted, and restart it again on every
                    // load. The saved deadline is the whole point of saving it.
                    node.RestoreSpent(record.charges, record.nodeRegrowAtUnix);
                }
            }

            Subscribe(durability, node, slot, building);
        }

        private void Subscribe(BuildingDurability durability, HarvestNode node,
            string slot, BuildingObject building)
        {
            // Captured rather than re-read on every callback: a destroyed building's own
            // fields are still valid, but the ACTIVE slot can change under a map swap, and a
            // record has to be filed under the slot the building was loaded for.
            string zone = building.ZoneName;
            int instanceId = building.InstanceId;

            if (durability != null)
            {
                durability.Struck += (dealt, point, damageClass) =>
                    NoteDurability(slot, zone, instanceId, durability.CurrentDurability, false, 0d);

                durability.Destroyed += (point, damageClass) =>
                    NoteDurability(slot, zone, instanceId, 0, true, RegrowDeadlineFor(durability.Profile));

                durability.Regrown += () =>
                    NoteDurability(slot, zone, instanceId, durability.CurrentDurability, false, 0d);
            }

            if (TracksCharges(node))
            {
                node.BlowLanded += (blow, yielded) =>
                    NoteCharges(slot, zone, instanceId, node.ChargesRemaining, node.RegrowAtUnix);

                node.Depleted += () =>
                    NoteCharges(slot, zone, instanceId, 0, node.RegrowAtUnix);

                node.Regrown += () =>
                    NoteCharges(slot, zone, instanceId, node.ChargesRemaining, node.RegrowAtUnix);
            }
        }

        // ── Recording ──────────────────────────────────────────────────────────────

        private void NoteDurability(string slot, string zone, int instanceId,
            int durability, bool destroyed, double regrowAtUnix)
        {
            var record = Touch(slot, zone, instanceId);
            record.durability = durability;
            record.destroyed = destroyed;
            record.regrowAtUnix = regrowAtUnix;
            _dirty = true;
        }

        private void NoteCharges(string slot, string zone, int instanceId,
            int charges, double nodeRegrowAtUnix)
        {
            var record = Touch(slot, zone, instanceId);
            record.charges = charges;
            record.nodeRegrowAtUnix = nodeRegrowAtUnix;
            _dirty = true;
        }

        private WorldDamageRecord Touch(string slot, string zone, int instanceId)
        {
            string key = KeyOf(slot, zone, instanceId);
            if (_records.TryGetValue(key, out var existing)) return existing;

            var record = new WorldDamageRecord
            {
                slot = slot,
                zone = zone,
                instanceId = instanceId,
                charges = -1,
            };
            _records[key] = record;
            return record;
        }

        /// <summary>
        /// When a destroyed building comes back, as a Unix timestamp. Zero for one that never
        /// does — which is every building whose profile leaves <c>regrowSeconds</c> at 0, and
        /// is the right default for a house.
        /// </summary>
        private static double RegrowDeadlineFor(DestructionProfile profile)
        {
            if (profile == null || profile.regrowSeconds <= 0f) return 0d;
            return UnixNow() + profile.regrowSeconds;
        }

        /// <summary>Seconds since the Unix epoch, UTC. The clock a regrow deadline is measured on.</summary>
        public static double UnixNow()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        // ── Flush ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Write the table if anything changed. Returns true when a write actually happened.
        ///
        /// <para>Refused outside Play Mode. An EditMode test that exercises the durability
        /// path would otherwise write into the player's real save folder, which is exactly how
        /// the <c>RUN_TWIN_SAVE</c> incident produced duplicate run folders on a machine
        /// nobody was playing on.</para>
        /// </summary>
        public bool Flush(bool force = false)
        {
            if (!_dirty && !force) return false;
            if (!WritesEnabled || _repository == null) return false;

            if (!Application.isPlaying)
            {
                Debug.LogWarning("[WorldDamageService] Refusing to write outside Play Mode.");
                return false;
            }

            var file = new WorldDamageFile { schema = 1, records = new List<WorldDamageRecord>(_records.Count) };
            foreach (var record in _records.Values) file.records.Add(record);

            _repository.WriteRawJson(_worldId, JsonUtility.ToJson(file, prettyPrint: true));
            _dirty = false;
            return true;
        }

        /// <summary>
        /// Forget everything, for the start of a fresh run. Does NOT write — a new run's
        /// first real change is what creates its file, so an abandoned new-game leaves the
        /// previous run's record alone.
        /// </summary>
        public void ClearInMemory()
        {
            _records.Clear();
            _dirty = false;
        }

        /// <summary>The record for one building, or null. Exposed for tests and diagnostics.</summary>
        public WorldDamageRecord Find(string slot, string zone, int instanceId)
        {
            _records.TryGetValue(KeyOf(slot, zone, instanceId), out var record);
            return record;
        }

        /// <summary>
        /// Whether a node's CHARGES are a real quantity worth persisting.
        ///
        /// <para>Only a Deplete-mode node has them. A Destroy-mode tree carries a HarvestNode
        /// purely so the player can walk up and chop it, and its charge counter is inert — but
        /// it still raises <c>Depleted</c> when the building dies, which wrote <c>charges: 0</c>
        /// into every felled tree's record. Harmless while the tree stays a stump, and not
        /// harmless at all once it regrows: the restore would hand the fresh tree zero charges
        /// and it would come back already spent, refusing to be chopped, with nothing in the
        /// data looking wrong.</para>
        /// </summary>
        private static bool TracksCharges(HarvestNode node)
        {
            return node != null && node.Mode == HarvestMode.Deplete;
        }

        private static string KeyOf(string slot, string zone, int instanceId)
        {
            // Zone names are compared OrdinalIgnoreCase everywhere else in the project, so the
            // key is lowered rather than trusting every caller to pass consistent casing.
            return string.Concat(
                slot == null ? "" : slot.ToLowerInvariant(), "|",
                zone == null ? "" : zone.ToLowerInvariant(), "|",
                instanceId.ToString());
        }
    }
}
