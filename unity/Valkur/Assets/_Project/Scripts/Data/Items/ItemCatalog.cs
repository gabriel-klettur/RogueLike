using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Indexed catalog of every <see cref="ItemDefinition"/> in the project.
    /// Singleton asset at <c>Assets/_Project/Data/Catalogs/Items/ItemCatalog.asset</c>.
    ///
    /// Mirrors Python's <c>items_loader</c> + <c>ItemRegistry</c>: a single point of
    /// truth populated once (by <c>PythonDataMigrator.Items</c>) and queried at
    /// runtime by O(1) id lookup.
    ///
    /// Used by:
    ///   • <see cref="Valkur.Gameplay.Items.ItemsRuntimeEditor"/> — picker grid &amp; instance lookup.
    ///   • Inventory / vendor / loot subsystems — resolve <c>itemId</c> -&gt; <see cref="ItemDefinition"/>.
    ///
    /// Keep this asset in source control: every entry references a deterministic
    /// child .asset under the same Catalogs/Items folder, so the JSON-serialised
    /// list is small and diff-friendly.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "Valkur/Items/Catalog")]
    public class ItemCatalog : ScriptableObject
    {
        [SerializeField] private List<ItemDefinition> _items = new List<ItemDefinition>();

        public IReadOnlyList<ItemDefinition> Items => _items;
        public int Count => _items?.Count ?? 0;

        // Lazy O(1) lookup cache. NonSerialized so domain reload always rebuilds.
        [System.NonSerialized] private Dictionary<string, ItemDefinition> _byId;

        private Dictionary<string, ItemDefinition> ById
        {
            get
            {
                if (_byId == null) RebuildLookup();
                return _byId;
            }
        }

        private void RebuildLookup()
        {
            _byId = new Dictionary<string, ItemDefinition>(
                _items.Count, System.StringComparer.OrdinalIgnoreCase);
            foreach (var it in _items)
            {
                if (it == null || string.IsNullOrEmpty(it.itemId)) continue;
                _byId[it.itemId] = it;
            }
        }

        /// <summary>O(1) lookup by stable item id. Returns null if absent.</summary>
        public ItemDefinition GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return ById.TryGetValue(id, out var it) ? it : null;
        }

        /// <summary>Add a new entry; returns false if the id already exists.</summary>
        public bool Add(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) return false;
            if (ById.ContainsKey(item.itemId)) return false;
            _items.Add(item);
            _byId[item.itemId] = item;
            return true;
        }

        /// <summary>Add or replace by id — used by the importer for re-runs.</summary>
        public void Upsert(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) return;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null
                    && string.Equals(_items[i].itemId, item.itemId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    _items[i] = item;
                    _byId = null;
                    return;
                }
            }
            _items.Add(item);
            _byId = null;
        }

        /// <summary>Remove a stale id; returns true on success.</summary>
        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null
                    && string.Equals(_items[i].itemId, id,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    _items.RemoveAt(i);
                    _byId = null;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Drop nulls and duplicate ids. Called by the importer post-merge.</summary>
        public void Compact()
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (it == null || string.IsNullOrEmpty(it.itemId) || !seen.Add(it.itemId))
                    _items.RemoveAt(i);
            }
            _byId = null;
        }
    }
}
