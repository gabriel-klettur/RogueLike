using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.WorldDrops;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// World-space item pickup entity. Represents a dropped or spawned item on the ground.
    /// Maps to Python's PhysicalItemComponent + CollectibleComponent + MapLoadDropsSystem.
    ///
    /// Two flavors share this MonoBehaviour:
    ///   • <b>Ephemeral</b> — created via <see cref="Initialize"/>; in-memory only,
    ///     dies with the scene. Used by the legacy <c>DropSystem.SpawnDrop</c> path.
    ///   • <b>Persistent</b> — created via <see cref="InitializePersistent"/>;
    ///     carries a <see cref="DropId"/> + <see cref="DespawnTtlSeconds"/> and
    ///     fires <see cref="OnDestroyed"/> on death so an external service can
    ///     mirror the lifecycle into a repository file.
    ///
    /// When the player enters the pickup radius, the item is added to their inventory.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class WorldPickup : MonoBehaviour
    {
        // ── Why this pickup got destroyed — fed to the persistence service. ──

        public enum DestructionReason
        {
            /// <summary>Picked up by the player; remove from the repo.</summary>
            PickedUp = 0,
            /// <summary>TTL elapsed; remove from the repo.</summary>
            Expired = 1,
            /// <summary>Editor / script destroy; remove from the repo.</summary>
            Manual = 2,
            /// <summary>Scene unload / domain reload; do NOT remove from the repo.</summary>
            SceneUnload = 3,
        }

        [Header("Item")]
        [SerializeField] private ItemDefinition itemDefinition;
        [SerializeField] private int quantity = 1;

        [Header("Pickup")]
        [SerializeField] private float pickupRadius = 1f;
        [SerializeField] private bool autoPickup;
        [SerializeField] private float autoPickupDelay = 0.5f;

        [Header("Visual")]
        [SerializeField] private float bobAmplitude = 0.05f;
        [SerializeField] private float bobFrequency = 2f;

        // ── Persistence metadata (only meaningful when IsPersistent is true) ──

        private string _dropId;
        private bool   _isPersistent;
        private float  _despawnTtlSeconds;     // 0 = infinite
        private long   _createdAtUnixMs;
        private string _zoneId;
        private ItemDropSource _source;
        private DestructionReason _pendingReason = DestructionReason.SceneUnload;

        private float _spawnTime;
        private float _baseY;
        private CircleCollider2D _collider;
        private bool _pickedUp;

        // -- The throw: a short arc from where it was dropped to where it lands --
        private bool    _arcing;
        private float   _arcT;
        private Vector3 _arcFrom, _arcTo;
        private const float ArcSeconds = 0.42f;
        private const float ArcHeight  = 0.55f;

        /// <summary>True while the pickup is still in the air after a throw. Test seam.</summary>
        public bool IsArcing => _arcing;

        // Target footprint in world units (1 tile, since Valkur ground tiles are
        // 1 wu × 1 wu). Multiplied by `ItemDefinition.scaleMap` when present, so
        // a designer can author a wagon at scaleMap=2 and a coin at scaleMap=0.5.
        private const float TARGET_TILE_FOOTPRINT = 1f;

        // Shared Unlit material cache. Items render under URP 2D; if their
        // SpriteRenderer keeps the default Sprite-Lit-Default material and the
        // surrounding area has no Light2D coverage (e.g. caves), they show up
        // pitch-black. Same workaround used by tilemaps + buildings + boss bars.
        private static Material s_unlitMaterial;

        /// <summary>
        /// Static event fired in OnDestroy so an external persistence service
        /// can mirror the lifecycle into its repository file. Static so we don't
        /// force a hard reference from gameplay → editor / save layer.
        /// Reset in <see cref="ResetStatics"/> on every domain reload.
        /// </summary>
        public static event Action<WorldPickup, DestructionReason> OnDestroyed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_unlitMaterial = null;
            OnDestroyed = null;
        }

        public ItemDefinition Item => itemDefinition;
        public int Quantity => quantity;

        public string DropId            => _dropId;
        public bool   IsPersistent      => _isPersistent;
        public float  DespawnTtlSeconds => _despawnTtlSeconds;
        public long   CreatedAtUnixMs   => _createdAtUnixMs;
        public string ZoneId            => _zoneId;
        public ItemDropSource Source    => _source;

        /// <summary>True when the pickup should never expire from age.</summary>
        public bool IsInfiniteTtl => _despawnTtlSeconds <= 0f;

        /// <summary>Seconds remaining before TTL expiry. <c>+∞</c> when infinite.</summary>
        public float SecondsUntilExpiry
        {
            get
            {
                if (IsInfiniteTtl) return float.PositiveInfinity;
                return Mathf.Max(0f, _despawnTtlSeconds - (Time.time - _spawnTime));
            }
        }

        // ── Initialisation ────────────────────────────────────────────────────

        /// <summary>Ephemeral spawn — no persistence metadata; existing callers keep working.</summary>
        public void Initialize(ItemDefinition item, int qty, Vector3 position)
            => InitializeCore(item, qty, position,
                dropId: null, persistent: false, ttl: 0f,
                createdAtUnixMs: 0L, zoneId: "", source: ItemDropSource.Unknown);

        /// <summary>
        /// Persistent spawn — the pickup remembers its <paramref name="dropId"/>
        /// and TTL so the persistence service can mirror create / expire / pickup
        /// events into the repository.
        /// </summary>
        public void InitializePersistent(
            ItemDefinition item, int qty, Vector3 position,
            string dropId, float despawnTtlSeconds, long createdAtUnixMs,
            string zoneId, ItemDropSource source)
            => InitializeCore(item, qty, position,
                dropId, persistent: true, ttl: Mathf.Max(0f, despawnTtlSeconds),
                createdAtUnixMs: createdAtUnixMs, zoneId: zoneId, source: source);

        private void InitializeCore(
            ItemDefinition item, int qty, Vector3 position,
            string dropId, bool persistent, float ttl,
            long createdAtUnixMs, string zoneId, ItemDropSource source)
        {
            itemDefinition = item;
            quantity = qty;
            transform.position = position;
            _baseY = position.y;
            _spawnTime = Time.time;

            _dropId            = dropId;
            _isPersistent      = persistent;
            _despawnTtlSeconds = ttl;
            _createdAtUnixMs   = createdAtUnixMs;
            _zoneId            = zoneId ?? "";
            _source            = source;

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && item != null)
            {
                sr.sprite = item.icon ?? item.iconSmall;
                sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                sr.sortingOrder = SortingConfig.Z_LOW_OBJECT + SortingConfig.YToSortingOrder(position.y);
                ApplyUnlitMaterial(sr);
            }

            float worldScale = ComputeWorldScale(sr != null ? sr.sprite : null, item);
            transform.localScale = new Vector3(worldScale, worldScale, 1f);

            _collider = GetComponent<CircleCollider2D>();
            _collider.isTrigger = true;
            _collider.radius = pickupRadius / Mathf.Max(0.0001f, worldScale);

            var ySort = GetComponent<YSortEntity>();
            if (ySort == null)
                ySort = gameObject.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_LOW_OBJECT;

            gameObject.name = item != null ? $"Pickup_{item.itemId}" : "Pickup_unknown";
        }

        /// <summary>
        /// Throw the pickup from where it is to <paramref name="landing"/> along a short hop.
        /// A drop that appears already lying on the ground reads as having been there; one
        /// that is thrown out of the body reads as having come OUT of it. The hop is drawn on
        /// the sprite's Y and ends exactly on the bob baseline, so the landing is the rest
        /// pose and nothing snaps.
        /// </summary>
        public void Launch(Vector3 landing)
        {
            _arcFrom = transform.position;
            _arcTo   = new Vector3(landing.x, landing.y, 0f);
            _arcT    = 0f;
            _arcing  = true;
            _baseY   = _arcTo.y;
        }

        /// <summary>Advance the throw. Public so a test can run one out.</summary>
        public void AdvanceArc(float dt)
        {
            if (!_arcing) return;
            _arcT += dt / ArcSeconds;
            float u = Mathf.Clamp01(_arcT);
            // Decelerating along the ground, a symmetric hop in the air.
            float eased = 1f - (1f - u) * (1f - u);
            var p = Vector3.Lerp(_arcFrom, _arcTo, eased);
            p.y += Mathf.Sin(u * Mathf.PI) * ArcHeight;
            transform.position = p;
            if (u >= 1f)
            {
                _arcing = false;
                transform.position = _arcTo;
            }
        }

        /// <summary>
        /// Tag this pickup as deliberately removed (editor delete, undo) so the
        /// destruction event fires the right reason. Call before <c>Destroy()</c>.
        /// </summary>
        public void MarkManualDelete()
        {
            _pendingReason = DestructionReason.Manual;
        }

        /// <summary>
        /// Move the pickup to a new world position and re-anchor the bob
        /// baseline. The editor's RMB drag-to-move uses this so the bob
        /// animation in <see cref="Update"/> doesn't keep snapping the Y back
        /// to the original spawn baseline mid-drag.
        /// </summary>
        public void SetWorldPosition(Vector3 worldPos)
        {
            transform.position = worldPos;
            _baseY = worldPos.y;
        }

        // ── Frame loop ────────────────────────────────────────────────────────

        private void Update()
        {
            if (_pickedUp) return;

            if (_arcing)
            {
                AdvanceArc(Time.deltaTime);
                return;
            }

            // Bob animation
            float bob = Mathf.Sin((Time.time - _spawnTime) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            var pos = transform.position;
            pos.y = _baseY + bob;
            transform.position = pos;

            // TTL expiry — only for persistent pickups; ephemeral ones live until
            // the scene unloads them.
            if (_isPersistent && _despawnTtlSeconds > 0f
                && (Time.time - _spawnTime) >= _despawnTtlSeconds)
            {
                _pendingReason = DestructionReason.Expired;
                SafeDestroy.Of(gameObject);
            }
        }

        private void OnDestroy()
        {
            // Skip when the application is quitting or the scene is unloading
            // (gameObject == null / static event already cleared).
            if (OnDestroyed != null && !string.IsNullOrEmpty(_dropId) && _isPersistent)
            {
                try { OnDestroyed.Invoke(this, _pendingReason); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        // ── Triggers / pickup ────────────────────────────────────────────────

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_pickedUp) return;
            if (!autoPickup) return;
            if (Time.time - _spawnTime < autoPickupDelay) return;

            TryPickup(other.gameObject);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_pickedUp) return;
            if (!autoPickup) return;
            if (Time.time - _spawnTime < autoPickupDelay) return;

            TryPickup(other.gameObject);
        }

        /// <summary>
        /// Attempt to pick up this item into the given entity's inventory.
        /// Returns true if successful.
        /// </summary>
        public bool TryPickup(GameObject collector) => TryPickupIntoSlot(collector, -1);

        /// <summary>
        /// Pickup variant that first tries to deposit at an explicit visual
        /// slot index (e.g. the cell the player is hovering with the cursor).
        /// Any leftover from an unhandled / partial slot deposit falls through
        /// to the standard auto-pick path so capacity still takes precedence
        /// over user intent. Pass <c>-1</c> for slot to skip the explicit step.
        /// </summary>
        public bool TryPickupIntoSlot(GameObject collector, int slotIndex)
        {
            if (_pickedUp || itemDefinition == null) return false;
            if (!collector.CompareTag("Player")) return false;

            var inventory = collector.GetComponent<Inventory>();
            if (inventory == null) return false;

            int placedAtSlot = (slotIndex >= 0)
                ? inventory.TryDepositInIndex(slotIndex, itemDefinition, quantity)
                : 0;

            int afterSlot = quantity - placedAtSlot;
            int leftover  = afterSlot > 0 ? inventory.AddItem(itemDefinition, afterSlot) : 0;
            int picked    = quantity - leftover;
            if (picked <= 0) return false;

            quantity = leftover;

            Debug.Log($"[WorldPickup] {collector.name} picked up {picked}x {itemDefinition.displayName} (slot={slotIndex})");
            GameEvents.FireItemPickedUp(collector, itemDefinition.displayName, picked);

            // A pickup produced no pixel before this: the sprite vanished and the bag changed.
            // The flash is in the item's rarity colour, the one fact about it worth a glance at
            // the place the player was already looking.
            if (Valkur.Gameplay.VFX.VFXManager.HasInstance)
                Valkur.Gameplay.VFX.VFXManager.Instance.SpawnImpact(transform.position, RarityFlashColour(itemDefinition.rarity), 0.25f, 0.7f);

            if (quantity <= 0)
            {
                _pickedUp = true;
                _pendingReason = DestructionReason.PickedUp;
                SafeDestroy.Of(gameObject);
            }
            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>The flash a pickup makes on collection, by rarity. Pure.</summary>
        public static Color RarityFlashColour(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon:  return new Color(0.45f, 1f, 0.50f, 1f);
                case ItemRarity.Rare:      return new Color(0.45f, 0.70f, 1f, 1f);
                case ItemRarity.Epic:      return new Color(0.80f, 0.45f, 1f, 1f);
                case ItemRarity.Legendary: return new Color(1f, 0.70f, 0.25f, 1f);
                default:                   return new Color(0.95f, 0.95f, 0.9f, 1f);
            }
        }

        private static float ComputeWorldScale(Sprite sprite, ItemDefinition item)
        {
            // Without a sprite we cannot normalise — fall back to a sane default.
            if (sprite == null) return 1f;

            Vector2 wuSize = sprite.bounds.size;
            float maxAxis = Mathf.Max(wuSize.x, wuSize.y);
            if (maxAxis <= 0f) return 1f;

            float baseScale = TARGET_TILE_FOOTPRINT / maxAxis;

            // ItemDefinition.scaleMap intentionally NOT applied here. The values
            // in SQLite (~0.04) are legacy Python pixel-direct scaling that is
            // already covered by Unity's PPU import. When designers need a 2-tile
            // wagon or a half-tile coin we'll add an explicit `worldScaleOverride`
            // field rather than overload the legacy column.
            return baseScale;
        }

        private static void ApplyUnlitMaterial(SpriteRenderer sr)
        {
            if (sr == null) return;
            if (s_unlitMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) return; // URP missing → keep the default lit material
                s_unlitMaterial = new Material(shader) { name = "WorldPickup_Unlit" };
            }
            sr.sharedMaterial = s_unlitMaterial;
        }
    }
}
