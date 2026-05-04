using System;
using UnityEngine;

namespace Valkur.Gameplay.WorldDrops
{
    /// <summary>
    /// Source/origin tag attached to a persisted drop. Informational — does not
    /// gate any behaviour by itself, but lets the editor and analytics tell apart
    /// authoring drops (placed in F7) from gameplay drops (loot, player throw).
    /// </summary>
    public enum ItemDropSource
    {
        Unknown   = 0,
        /// <summary>Placed via the in-game Items Editor (F7) — authoring data.</summary>
        Editor    = 1,
        /// <summary>Dropped by an enemy on death.</summary>
        Loot      = 2,
        /// <summary>Manually thrown out of the player inventory.</summary>
        PlayerDrop = 3,
        /// <summary>Spawned by a quest / scripted event.</summary>
        Quest     = 4,
    }

    /// <summary>
    /// Persisted snapshot of a single world drop. Mirrors Python's
    /// <c>inventory_map.json</c> entry (drop_id, item_id, qty, position, …) but
    /// extends it with first-class <see cref="DespawnTtlSeconds"/> and
    /// <see cref="Source"/> fields so the runtime can express:
    ///
    ///   • Authoring drops (Items Editor F7) — <see cref="DespawnTtlSeconds"/> = 0
    ///     ⇒ infinite, persist forever in <c>StreamingAssets/Items/item_drops.json</c>.
    ///   • Gameplay drops (loot, throws) — <see cref="DespawnTtlSeconds"/> &gt; 0
    ///     ⇒ vanish after that many seconds; saved in the per-run save file.
    ///
    /// **Instance ≠ Definition**: the <see cref="ItemId"/> resolves against the
    /// canonical <c>ItemCatalog</c> at load time. Anything that can vary per
    /// drop (position, qty, dropId, despawn) lives here, not on
    /// <c>ItemDefinition</c>.
    /// </summary>
    [Serializable]
    public class ItemDropInstance
    {
        /// <summary>UUID v4 string. Unique per drop — never reused after pickup.</summary>
        public string dropId;

        /// <summary>FK to <c>ItemDefinition.itemId</c>.</summary>
        public string itemId;

        /// <summary>How many of the item are stacked at this position.</summary>
        public int quantity = 1;

        /// <summary>World-space position in Unity units (PPU 16, 1 unit = 1 tile).</summary>
        public Vector2 position;

        /// <summary>Logical zone the drop belongs to. Empty string = no zone filter.</summary>
        public string zoneId = "";

        /// <summary>Sorting Z layer. 0 = default low-object layer.</summary>
        public int zLayer;

        /// <summary>Unix milliseconds at creation time, for sort/expiry math.</summary>
        public long createdAtUnixMs;

        /// <summary>
        /// Time-to-live in seconds. <b>0 = never expires</b> (treated as infinite).
        /// When &gt; 0, the WorldPickup self-destructs and the repository purges
        /// the entry once <see cref="createdAtUnixMs"/> + ttl ≤ now.
        /// </summary>
        public float despawnTtlSeconds;

        /// <summary>Stored as int for JsonUtility friendliness.</summary>
        public int sourceRaw = (int)ItemDropSource.Unknown;

        public ItemDropSource Source
        {
            get => (ItemDropSource)sourceRaw;
            set => sourceRaw = (int)value;
        }

        /// <summary>True when the drop never expires (despawnTtlSeconds &lt;= 0).</summary>
        public bool IsInfinite => despawnTtlSeconds <= 0f;

        public ItemDropInstance() { }

        public ItemDropInstance(
            string dropId, string itemId, int quantity, Vector2 position,
            string zoneId, int zLayer, long createdAtUnixMs,
            float despawnTtlSeconds, ItemDropSource source)
        {
            this.dropId           = dropId;
            this.itemId           = itemId;
            this.quantity         = quantity;
            this.position         = position;
            this.zoneId           = zoneId ?? "";
            this.zLayer           = zLayer;
            this.createdAtUnixMs  = createdAtUnixMs;
            this.despawnTtlSeconds = Mathf.Max(0f, despawnTtlSeconds);
            this.sourceRaw        = (int)source;
        }

        /// <summary>Generate a fresh UUID v4 string for use as <see cref="dropId"/>.</summary>
        public static string NewDropId() => Guid.NewGuid().ToString("N");

        /// <summary>Deep clone — useful for Undo / Redo.</summary>
        public ItemDropInstance Clone() => new ItemDropInstance(
            dropId, itemId, quantity, position, zoneId, zLayer,
            createdAtUnixMs, despawnTtlSeconds, Source);
    }

    /// <summary>
    /// Root JSON document — what <see cref="JsonUtility"/> serialises to disk.
    /// Wraps a list and a schema version so we can migrate the file format
    /// without breaking saved worlds.
    /// </summary>
    [Serializable]
    public class ItemDropsFile
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public ItemDropInstance[] drops = Array.Empty<ItemDropInstance>();
    }
}
