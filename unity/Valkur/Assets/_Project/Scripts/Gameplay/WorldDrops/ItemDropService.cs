using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.WorldDrops
{
    /// <summary>
    /// Orchestrates the lifecycle of <i>persistent</i> world drops:
    ///
    ///  1. Holds the in-memory list backed by an <see cref="IItemDropRepository"/>.
    ///  2. Spawns <see cref="WorldPickup"/> GameObjects from <see cref="ItemDropInstance"/>
    ///     records (rehydration on world load, fresh placements from the F7 editor).
    ///  3. Subscribes to <see cref="WorldPickup.OnDestroyed"/> so pickups (player),
    ///     TTL expirations, and manual deletes mirror back into the repo file.
    ///
    /// One instance is registered with <c>ServiceLocator&lt;ItemDropService&gt;</c>
    /// per running gameplay scene. Tests build their own with an
    /// <see cref="InMemoryItemDropRepository"/> and never touch StreamingAssets.
    ///
    /// Phase A wires the authoring repository (StreamingAssets/Items/item_drops.json).
    /// Phase B will add a parallel run-scoped repository for gameplay drops.
    /// </summary>
    public class ItemDropService : IDisposable
    {
        private readonly IItemDropRepository _repository;
        private readonly ItemCatalog         _catalog;
        private readonly WorldId             _worldId;
        private readonly Dictionary<string, ItemDropInstance> _byId
            = new Dictionary<string, ItemDropInstance>(StringComparer.Ordinal);

        // Keep a back-reference for picking up the right pickup on Undo / replays.
        private readonly Dictionary<string, WorldPickup> _liveByDropId
            = new Dictionary<string, WorldPickup>(StringComparer.Ordinal);

        private bool _subscribed;
        private bool _flushOnEveryChange = true;

        /// <summary>
        /// When true (default), every mutation flushes the repository file
        /// synchronously. Tests or batch importers can flip this off, mutate
        /// freely, then call <see cref="Flush"/> once.
        /// </summary>
        public bool FlushOnEveryChange
        {
            get => _flushOnEveryChange;
            set => _flushOnEveryChange = value;
        }

        public ItemCatalog Catalog => _catalog;
        public WorldId    WorldId  => _worldId;
        public IReadOnlyCollection<ItemDropInstance> All => _byId.Values;
        public int Count => _byId.Count;

        public ItemDropService(IItemDropRepository repository, ItemCatalog catalog, WorldId worldId)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _catalog    = catalog;
            _worldId    = worldId;

            WorldPickup.OnDestroyed += HandlePickupDestroyed;
            _subscribed = true;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                WorldPickup.OnDestroyed -= HandlePickupDestroyed;
                _subscribed = false;
            }
            _byId.Clear();
            _liveByDropId.Clear();
        }

        // ── Loading / persistence ─────────────────────────────────────────────

        /// <summary>
        /// Read every drop record from the repository into the in-memory cache.
        /// Does NOT spawn pickups — call <see cref="Rehydrate"/> for that.
        /// </summary>
        public int LoadFromRepository()
        {
            _byId.Clear();
            string json = _repository.ReadRawJson(_worldId);
            if (string.IsNullOrWhiteSpace(json)) return 0;

            ItemDropsFile file;
            try { file = JsonUtility.FromJson<ItemDropsFile>(json); }
            catch (Exception ex)
            {
                Debug.LogError($"[ItemDropService] Failed to parse drops JSON: {ex.Message}");
                return 0;
            }
            if (file?.drops == null) return 0;

            foreach (var d in file.drops)
            {
                if (d == null || string.IsNullOrEmpty(d.dropId) || string.IsNullOrEmpty(d.itemId))
                    continue;
                _byId[d.dropId] = d;
            }
            return _byId.Count;
        }

        /// <summary>Serialise the current cache and write it through the repo.</summary>
        public void Flush()
        {
            var file = new ItemDropsFile
            {
                schemaVersion = ItemDropsFile.CurrentSchemaVersion,
                drops = new ItemDropInstance[_byId.Count],
            };
            int i = 0;
            foreach (var d in _byId.Values) file.drops[i++] = d;

            string json = JsonUtility.ToJson(file, prettyPrint: true);
            _repository.WriteRawJson(_worldId, json);
        }

        // ── Spawning ──────────────────────────────────────────────────────────

        /// <summary>
        /// Place a brand-new persistent drop in the world. Generates a fresh
        /// <see cref="ItemDropInstance.dropId"/>, stamps the createdAt timestamp,
        /// inserts it into the cache, persists, and spawns the pickup. Returns
        /// the instance metadata so callers can record it for Undo.
        /// </summary>
        public ItemDropInstance SpawnPersistent(
            ItemDefinition def, int quantity, Vector3 worldPos,
            float despawnTtlSeconds, string zoneId, ItemDropSource source)
        {
            if (def == null || quantity <= 0) return null;

            var instance = new ItemDropInstance(
                dropId:           ItemDropInstance.NewDropId(),
                itemId:           def.itemId,
                quantity:         quantity,
                position:         new Vector2(worldPos.x, worldPos.y),
                zoneId:           zoneId ?? "",
                zLayer:           def.zLayer,
                createdAtUnixMs:  NowUnixMs(),
                despawnTtlSeconds: Mathf.Max(0f, despawnTtlSeconds),
                source:           source);

            _byId[instance.dropId] = instance;
            SpawnPickupFor(instance);
            if (_flushOnEveryChange) Flush();
            return instance;
        }

        /// <summary>
        /// Recreate a previously-known drop. Used by:
        ///   • <see cref="ItemDropLoader"/> on world bootstrap (rehydrate from disk).
        ///   • <see cref="ItemsRuntimeEditor"/> Undo (re-insert a deleted drop).
        /// The dropId is taken straight from <paramref name="instance"/>; the
        /// caller owns timestamp / TTL semantics.
        /// </summary>
        public void RestorePersistent(ItemDropInstance instance)
        {
            if (instance == null || string.IsNullOrEmpty(instance.dropId)
                || string.IsNullOrEmpty(instance.itemId))
                return;

            _byId[instance.dropId] = instance;
            SpawnPickupFor(instance);
            if (_flushOnEveryChange) Flush();
        }

        /// <summary>
        /// Spawn a pickup for every cached instance. Called once after
        /// <see cref="LoadFromRepository"/> at scene-bootstrap time. Pre-existing
        /// live pickups for the same dropId are skipped so re-rehydration is safe.
        /// </summary>
        public int Rehydrate()
        {
            int spawned = 0;
            foreach (var d in _byId.Values)
            {
                if (_liveByDropId.ContainsKey(d.dropId)) continue;
                if (SpawnPickupFor(d) != null) spawned++;
            }
            return spawned;
        }

        /// <summary>
        /// Mutate the quantity of an existing drop. Used by F7 Properties Qty±.
        /// Returns false when the drop is unknown.
        /// </summary>
        public bool UpdateQuantity(string dropId, int newQuantity)
        {
            if (string.IsNullOrEmpty(dropId)) return false;
            if (!_byId.TryGetValue(dropId, out var inst)) return false;
            inst.quantity = Mathf.Max(1, newQuantity);
            if (_flushOnEveryChange) Flush();
            return true;
        }

        /// <summary>
        /// Drop a record manually from script (editor delete, debug command).
        /// Destroys the matching live pickup if one exists. Returns true on hit.
        /// </summary>
        public bool RemoveByDropId(string dropId)
        {
            if (string.IsNullOrEmpty(dropId)) return false;
            if (!_byId.Remove(dropId)) return false;

            if (_liveByDropId.TryGetValue(dropId, out var live) && live != null)
            {
                live.MarkManualDelete();
                // Detach from cache before Destroy so the OnDestroyed callback
                // — which would otherwise try to remove the same key — is a no-op.
                _liveByDropId.Remove(dropId);
                if (Application.isPlaying) UnityEngine.Object.Destroy(live.gameObject);
                else UnityEngine.Object.DestroyImmediate(live.gameObject);
            }

            if (_flushOnEveryChange) Flush();
            return true;
        }

        public ItemDropInstance Get(string dropId)
        {
            if (string.IsNullOrEmpty(dropId)) return null;
            _byId.TryGetValue(dropId, out var inst);
            return inst;
        }

        public WorldPickup GetLivePickup(string dropId)
        {
            if (string.IsNullOrEmpty(dropId)) return null;
            _liveByDropId.TryGetValue(dropId, out var live);
            return live;
        }

        /// <summary>Drop everything from cache and from the repo.</summary>
        public void ClearAll()
        {
            foreach (var live in _liveByDropId.Values)
            {
                if (live == null) continue;
                live.MarkManualDelete();
                if (Application.isPlaying) UnityEngine.Object.Destroy(live.gameObject);
                else UnityEngine.Object.DestroyImmediate(live.gameObject);
            }
            _liveByDropId.Clear();
            _byId.Clear();
            if (_flushOnEveryChange) Flush();
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private WorldPickup SpawnPickupFor(ItemDropInstance instance)
        {
            ItemDefinition def = ResolveDefinition(instance.itemId);
            if (def == null)
            {
                Debug.LogWarning($"[ItemDropService] Skipping drop '{instance.dropId}' — itemId '{instance.itemId}' not in catalog.");
                return null;
            }

            // Reuse the canonical builder so material / scale / collider all match
            // the Phase 1 visual rules. We then re-Initialize through the persistent
            // overload to attach the dropId metadata.
            Vector3 pos = new Vector3(instance.position.x, instance.position.y, 0f);
            WorldPickup pickup = DropSystem.BuildPickupShell(def, pos);
            if (pickup == null) return null;

            pickup.InitializePersistent(
                def,
                Mathf.Max(1, instance.quantity),
                pos,
                instance.dropId,
                instance.despawnTtlSeconds,
                instance.createdAtUnixMs,
                instance.zoneId,
                instance.Source);

            _liveByDropId[instance.dropId] = pickup;
            return pickup;
        }

        private ItemDefinition ResolveDefinition(string itemId)
        {
            if (_catalog == null || string.IsNullOrEmpty(itemId)) return null;
            return _catalog.GetById(itemId);
        }

        private void HandlePickupDestroyed(WorldPickup pickup, WorldPickup.DestructionReason reason)
        {
            if (pickup == null || string.IsNullOrEmpty(pickup.DropId)) return;
            // Only react to drops we own — the static event is global.
            if (!_liveByDropId.TryGetValue(pickup.DropId, out var tracked) || tracked != pickup)
                return;

            _liveByDropId.Remove(pickup.DropId);

            switch (reason)
            {
                case WorldPickup.DestructionReason.PickedUp:
                case WorldPickup.DestructionReason.Expired:
                case WorldPickup.DestructionReason.Manual:
                    _byId.Remove(pickup.DropId);
                    if (_flushOnEveryChange) Flush();
                    break;
                case WorldPickup.DestructionReason.SceneUnload:
                default:
                    // Keep the record so the next world load can re-spawn it.
                    break;
            }
        }

        private static long NowUnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
