using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.WorldDrops
{
    /// <summary>
    /// Orchestrates the lifecycle of <i>persistent</i> world drops across two
    /// stores:
    ///
    ///  • <b>Authoring</b> — drops placed in the world by the Items Editor (F7)
    ///    or scripted quests. Versioned with the world content under
    ///    <c>StreamingAssets/Items/item_drops.json</c>; survives between runs.
    ///  • <b>Run</b> — gameplay drops from loot tables, NPC death, or the
    ///    player throwing items out. Belong to a single playthrough; saved into
    ///    the per-run save folder so the run can be restored mid-session.
    ///
    /// One service instance owns both. <see cref="ItemDropSource"/> on each
    /// instance decides which repo it routes to, so callers don't need to know
    /// the storage layout.
    ///
    /// Subscribes to <see cref="WorldPickup.OnDestroyed"/> once. Pickups
    /// (player), TTL expirations, and manual deletes mirror back into whichever
    /// repo owns the drop.
    /// </summary>
    public class ItemDropService : IDisposable
    {
        private readonly IItemDropRepository _authoringRepo;
        private IItemDropRepository _runRepo;
        private readonly ItemCatalog _catalog;
        private readonly WorldId     _worldId;

        private readonly Dictionary<string, ItemDropInstance> _byId
            = new Dictionary<string, ItemDropInstance>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorldPickup> _liveByDropId
            = new Dictionary<string, WorldPickup>(StringComparer.Ordinal);

        private bool _subscribed;
        private bool _flushOnEveryChange = true;

        /// <summary>
        /// When true (default), every mutation flushes the relevant repository
        /// file synchronously. Tests or batch importers can flip this off,
        /// mutate freely, then call <see cref="Flush"/> once.
        /// </summary>
        public bool FlushOnEveryChange
        {
            get => _flushOnEveryChange;
            set => _flushOnEveryChange = value;
        }

        public ItemCatalog Catalog       => _catalog;
        public WorldId     WorldId       => _worldId;
        public IItemDropRepository AuthoringRepository => _authoringRepo;
        public IItemDropRepository RunRepository       => _runRepo;
        public IReadOnlyCollection<ItemDropInstance> All => _byId.Values;
        public int Count => _byId.Count;

        /// <summary>
        /// Domain-Reload-OFF safety net. The previous Play's <see cref="ItemDropService"/>
        /// stays registered with the <see cref="ServiceLocator"/> across Play
        /// stop/start, holding a cache that was loaded *before* that Play wrote
        /// any drops. Without this hook, a fresh Play would short-circuit
        /// <c>GameplaySceneSetup.EnsureItemDropService</c> and read from the
        /// stale cache instead of rehydrating from disk.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            if (ServiceLocator.TryGet<ItemDropService>(out var stale) && stale != null)
            {
                try { stale.Dispose(); }
                catch (Exception ex) { Debug.LogException(ex); }
                ServiceLocator.Unregister<ItemDropService>();
            }
        }

        public ItemDropService(IItemDropRepository authoringRepo, ItemCatalog catalog, WorldId worldId)
            : this(authoringRepo, runRepo: null, catalog, worldId) { }

        public ItemDropService(
            IItemDropRepository authoringRepo,
            IItemDropRepository runRepo,
            ItemCatalog catalog,
            WorldId worldId)
        {
            _authoringRepo = authoringRepo ?? throw new ArgumentNullException(nameof(authoringRepo));
            _runRepo       = runRepo;
            _catalog       = catalog;
            _worldId       = worldId;

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

        /// <summary>
        /// Swap the run-scoped repository (e.g. when the player starts a new run
        /// with a different runId). Caller is responsible for clearing in-memory
        /// run drops first via <see cref="ClearRunDropsInMemory"/> if needed.
        /// </summary>
        public void SetRunRepository(IItemDropRepository repo)
        {
            _runRepo = repo;
        }

        // ── Source / repo routing ─────────────────────────────────────────────

        /// <summary>
        /// Authoring sources persist with the world content (Editor, Quest,
        /// Unknown). Run sources persist with the active save (Loot, PlayerDrop).
        /// </summary>
        public static bool IsAuthoringSource(ItemDropSource source) =>
            source == ItemDropSource.Editor ||
            source == ItemDropSource.Quest ||
            source == ItemDropSource.Unknown;

        private IItemDropRepository RepoFor(ItemDropSource source) =>
            IsAuthoringSource(source) ? _authoringRepo : _runRepo;

        // ── Loading / persistence ─────────────────────────────────────────────

        /// <summary>Load both authoring and run drops from their repos into the
        /// shared cache. Returns the number of records loaded.</summary>
        public int LoadFromRepository()
        {
            _byId.Clear();
            int count = 0;
            count += LoadFromOneRepo(_authoringRepo);
            if (_runRepo != null) count += LoadFromOneRepo(_runRepo);
            return count;
        }

        private int LoadFromOneRepo(IItemDropRepository repo)
        {
            if (repo == null) return 0;
            string json = repo.ReadRawJson(_worldId);
            if (string.IsNullOrWhiteSpace(json)) return 0;

            ItemDropsFile file;
            try { file = JsonUtility.FromJson<ItemDropsFile>(json); }
            catch (Exception ex)
            {
                Debug.LogError($"[ItemDropService] Failed to parse drops JSON: {ex.Message}");
                return 0;
            }
            if (file?.drops == null) return 0;

            int loaded = 0;
            foreach (var d in file.drops)
            {
                if (d == null || string.IsNullOrEmpty(d.dropId) || string.IsNullOrEmpty(d.itemId))
                    continue;
                _byId[d.dropId] = d;
                loaded++;
            }
            return loaded;
        }

        /// <summary>Serialise the cache, splitting authoring vs run drops, and
        /// write each subset through the matching repository.</summary>
        public void Flush()
        {
            FlushAuthoring();
            if (_runRepo != null) FlushRun();
        }

        private void FlushAuthoring()
        {
            var list = new List<ItemDropInstance>();
            foreach (var d in _byId.Values)
                if (IsAuthoringSource(d.Source)) list.Add(d);
            WriteFile(_authoringRepo, list);
        }

        private void FlushRun()
        {
            var list = new List<ItemDropInstance>();
            foreach (var d in _byId.Values)
                if (!IsAuthoringSource(d.Source)) list.Add(d);
            WriteFile(_runRepo, list);
        }

        private void WriteFile(IItemDropRepository repo, List<ItemDropInstance> drops)
        {
            var file = new ItemDropsFile
            {
                schemaVersion = ItemDropsFile.CurrentSchemaVersion,
                drops         = drops.ToArray(),
            };
            string json = JsonUtility.ToJson(file, prettyPrint: true);
            repo.WriteRawJson(_worldId, json);
        }

        /// <summary>Drop only the run-scoped records from memory (used at the
        /// start of a fresh run before <see cref="SetRunRepository"/> swaps in
        /// the new save folder).</summary>
        public void ClearRunDropsInMemory()
        {
            var toRemove = new List<string>();
            foreach (var kv in _byId)
                if (!IsAuthoringSource(kv.Value.Source)) toRemove.Add(kv.Key);
            foreach (var id in toRemove)
            {
                if (_liveByDropId.TryGetValue(id, out var live) && live != null)
                {
                    live.MarkManualDelete();
                    if (Application.isPlaying) UnityEngine.Object.Destroy(live.gameObject);
                    else UnityEngine.Object.DestroyImmediate(live.gameObject);
                }
                _liveByDropId.Remove(id);
                _byId.Remove(id);
            }
        }

        // ── Spawning ──────────────────────────────────────────────────────────

        /// <summary>Authoring spawn — drops survive across runs (Editor / Quest).</summary>
        public ItemDropInstance SpawnPersistent(
            ItemDefinition def, int quantity, Vector3 worldPos,
            float despawnTtlSeconds, string zoneId, ItemDropSource source)
        {
            return SpawnInternal(def, quantity, worldPos, despawnTtlSeconds, zoneId,
                IsAuthoringSource(source) ? source : ItemDropSource.Editor);
        }

        /// <summary>Run-scoped spawn — drops are saved with the active run
        /// (Loot / PlayerDrop). When no run repo is bound the call still spawns
        /// a live pickup but the record won't survive a save / load.</summary>
        public ItemDropInstance SpawnGameplay(
            ItemDefinition def, int quantity, Vector3 worldPos,
            float despawnTtlSeconds, string zoneId, ItemDropSource source)
        {
            return SpawnInternal(def, quantity, worldPos, despawnTtlSeconds, zoneId,
                IsAuthoringSource(source) ? ItemDropSource.Loot : source);
        }

        private ItemDropInstance SpawnInternal(
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
            if (_flushOnEveryChange) FlushFor(instance);
            return instance;
        }

        public void RestorePersistent(ItemDropInstance instance)
        {
            if (instance == null || string.IsNullOrEmpty(instance.dropId)
                || string.IsNullOrEmpty(instance.itemId))
                return;

            _byId[instance.dropId] = instance;
            SpawnPickupFor(instance);
            if (_flushOnEveryChange) FlushFor(instance);
        }

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

        public bool UpdateQuantity(string dropId, int newQuantity)
        {
            if (string.IsNullOrEmpty(dropId)) return false;
            if (!_byId.TryGetValue(dropId, out var inst)) return false;
            inst.quantity = Mathf.Max(1, newQuantity);
            if (_flushOnEveryChange) FlushFor(inst);
            return true;
        }

        /// <summary>
        /// Persist a new world position for an existing drop. Used by the
        /// F7 editor's RMB drag-to-move gesture; mirrors the live pickup's
        /// transform so a save/load round-trip restores the moved location.
        /// </summary>
        public bool UpdatePosition(string dropId, Vector2 worldPos)
        {
            if (string.IsNullOrEmpty(dropId)) return false;
            if (!_byId.TryGetValue(dropId, out var inst)) return false;
            inst.position = worldPos;
            // Live pickup already follows the cursor during the drag — we don't
            // touch transform.position here so the visual stays smooth.
            if (_flushOnEveryChange) FlushFor(inst);
            return true;
        }

        public bool RemoveByDropId(string dropId)
        {
            if (string.IsNullOrEmpty(dropId)) return false;
            if (!_byId.TryGetValue(dropId, out var inst)) return false;

            _byId.Remove(dropId);

            if (_liveByDropId.TryGetValue(dropId, out var live) && live != null)
            {
                live.MarkManualDelete();
                _liveByDropId.Remove(dropId);
                if (Application.isPlaying) UnityEngine.Object.Destroy(live.gameObject);
                else UnityEngine.Object.DestroyImmediate(live.gameObject);
            }

            if (_flushOnEveryChange) FlushFor(inst);
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

        /// <summary>Drop everything from cache and from both repos.</summary>
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

        private void FlushFor(ItemDropInstance inst)
        {
            if (IsAuthoringSource(inst.Source)) FlushAuthoring();
            else if (_runRepo != null)          FlushRun();
        }

        private WorldPickup SpawnPickupFor(ItemDropInstance instance)
        {
            ItemDefinition def = ResolveDefinition(instance.itemId);
            if (def == null)
            {
                Debug.LogWarning($"[ItemDropService] Skipping drop '{instance.dropId}' — itemId '{instance.itemId}' not in catalog.");
                return null;
            }

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
            if (!_liveByDropId.TryGetValue(pickup.DropId, out var tracked) || tracked != pickup)
                return;

            _liveByDropId.Remove(pickup.DropId);

            switch (reason)
            {
                case WorldPickup.DestructionReason.PickedUp:
                case WorldPickup.DestructionReason.Expired:
                case WorldPickup.DestructionReason.Manual:
                    if (_byId.TryGetValue(pickup.DropId, out var inst))
                    {
                        _byId.Remove(pickup.DropId);
                        if (_flushOnEveryChange) FlushFor(inst);
                    }
                    break;
                case WorldPickup.DestructionReason.SceneUnload:
                default:
                    break;
            }
        }

        private static long NowUnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
