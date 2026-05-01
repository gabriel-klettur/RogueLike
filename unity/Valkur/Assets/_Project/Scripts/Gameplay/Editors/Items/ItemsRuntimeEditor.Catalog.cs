using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor — catalog loading & filtering.
    /// Mirrors Python <c>roguelike_editors/items/services/item_catalog_service.py</c>:
    /// the catalog is populated from <c>items.json</c> via <c>ItemsLoader</c>; here the
    /// equivalent is loading every <see cref="ItemDefinition"/> ScriptableObject from
    /// the project's <c>Resources/Items</c> folder (mirrors InventoryRuntimeEditor).
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        /// <summary>Lazy-load the catalog the first time it's needed and on Activate().</summary>
        private void EnsureCatalog()
        {
            if (_allItems != null) return;
            ReloadCatalog();
        }

        /// <summary>Force a refresh of the catalog from Resources.</summary>
        private void ReloadCatalog()
        {
            _allItems = Resources.LoadAll<ItemDefinition>("Items") ?? System.Array.Empty<ItemDefinition>();
            // Sort by displayName then itemId for a stable picker order.
            System.Array.Sort(_allItems, (a, b) =>
            {
                string an = string.IsNullOrEmpty(a.displayName) ? a.itemId ?? "" : a.displayName;
                string bn = string.IsNullOrEmpty(b.displayName) ? b.itemId ?? "" : b.displayName;
                int c = string.Compare(an, bn, System.StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.itemId ?? "", b.itemId ?? "", System.StringComparison.OrdinalIgnoreCase);
            });
            Debug.Log($"[ItemsEditor] Catalog loaded: {_allItems.Length} items from Resources/Items");
        }

        /// <summary>Apply the search filter to <see cref="_allItems"/> into <see cref="_filtered"/>.</summary>
        private void ApplyFilter()
        {
            _filtered.Clear();
            if (_allItems == null) return;

            string filter = (_searchFilter ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < _allItems.Length; i++)
            {
                var it = _allItems[i];
                if (it == null) continue;
                if (filter.Length == 0)
                {
                    _filtered.Add(it);
                    continue;
                }
                string id = (it.itemId ?? "").ToLowerInvariant();
                string nm = (it.displayName ?? "").ToLowerInvariant();
                if (id.Contains(filter) || nm.Contains(filter))
                    _filtered.Add(it);
            }
        }

        /// <summary>Find an item by its <c>itemId</c> in the catalog (or null).</summary>
        private ItemDefinition FindItemById(string id)
        {
            if (string.IsNullOrEmpty(id) || _allItems == null) return null;
            for (int i = 0; i < _allItems.Length; i++)
            {
                if (_allItems[i] != null && _allItems[i].itemId == id) return _allItems[i];
            }
            return null;
        }

        /// <summary>Search-box callback: update filter and refresh the picker.</summary>
        private void OnSearchChanged(string value)
        {
            _searchFilter = value ?? "";
            RefreshPicker();
        }
    }
}
