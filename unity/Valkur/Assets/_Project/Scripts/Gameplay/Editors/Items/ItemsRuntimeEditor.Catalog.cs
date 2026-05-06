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
    /// Mirrors Python <c>roguelike_editors/items/services/item_catalog_service.py</c>.
    /// Source priority: <see cref="ItemCatalog"/> singleton (populated by the
    /// PythonDataMigrator) -&gt; ServiceLocator-registered catalog -&gt;
    /// <c>Resources/Items</c> fallback (legacy; only matters if the migrator
    /// has never been run).
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        /// <summary>Lazy-load the catalog the first time it's needed and on Activate().</summary>
        private void EnsureCatalog()
        {
            if (_allItems != null) return;
            ReloadCatalog();
        }

        /// <summary>Force a refresh of the catalog. Tries ItemCatalog first, then Resources.</summary>
        private void ReloadCatalog()
        {
            var fromCatalog = TryLoadFromItemCatalog();
            _allItems = fromCatalog ?? Resources.LoadAll<ItemDefinition>("Items")
                                    ?? System.Array.Empty<ItemDefinition>();

            // Sort by displayName then itemId for a stable picker order.
            System.Array.Sort(_allItems, (a, b) =>
            {
                string an = string.IsNullOrEmpty(a.displayName) ? a.itemId ?? "" : a.displayName;
                string bn = string.IsNullOrEmpty(b.displayName) ? b.itemId ?? "" : b.displayName;
                int c = string.Compare(an, bn, System.StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.itemId ?? "", b.itemId ?? "", System.StringComparison.OrdinalIgnoreCase);
            });

            string source = fromCatalog != null ? "ItemCatalog" : "Resources/Items";
            Debug.Log($"[ItemsEditor] Catalog loaded: {_allItems.Length} items from {source}");
        }

        /// <summary>Source the catalog in priority order:
        ///   1. ServiceLocator binding registered by GameplaySceneSetup (build-friendly).
        ///   2. Resources/Catalogs/ItemCatalog (legacy / future Addressables stub).
        ///   3. AssetDatabase load of the canonical Data/Catalogs/Items/ItemCatalog.asset
        ///      (Editor-only — keeps the in-game F7 editor working immediately after
        ///      a fresh PythonDataMigrator run, before any scene wiring).
        /// Returns null when nothing is available so the caller can fall through.</summary>
        private static ItemDefinition[] TryLoadFromItemCatalog()
        {
            ItemCatalog catalog = null;
            if (ServiceLocator.TryGet<ItemCatalog>(out var fromService))
                catalog = fromService;

            if (catalog == null)
                catalog = Resources.Load<ItemCatalog>("Catalogs/ItemCatalog");

#if UNITY_EDITOR
            if (catalog == null)
            {
                catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemCatalog>(
                    "Assets/_Project/Data/Catalogs/Items/ItemCatalog.asset");
            }
#endif

            if (catalog == null || catalog.Count == 0) return null;

            var arr = new ItemDefinition[catalog.Count];
            for (int i = 0; i < catalog.Items.Count; i++) arr[i] = catalog.Items[i];
            return arr;
        }

        /// <summary>Apply the search + category filters to <see cref="_allItems"/>
        /// into <see cref="_filtered"/>. Both filters AND together — an item must
        /// match the search substring AND the active category tab to be shown.</summary>
        private void ApplyFilter()
        {
            _filtered.Clear();
            if (_allItems == null) return;

            string filter = (_searchFilter ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < _allItems.Length; i++)
            {
                var it = _allItems[i];
                if (it == null) continue;

                // Category gate (null = "All" tab, accepts everything).
                if (_categoryFilter.HasValue && !it.MatchesTab(_categoryFilter.Value))
                    continue;

                // Search gate.
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

        /// <summary>
        /// TabStrip callback wired in <c>BuildUI</c>. Maps the tab key to the
        /// canonical <see cref="ItemCategoryUtil"/> tab index and refreshes
        /// both Grid and Table views so the filter is consistent across them.
        /// </summary>
        private void OnGridCategoryTabChanged(int _index, string key)
        {
            switch (key)
            {
                case "equipment":  _categoryFilter = ItemCategoryUtil.TAB_EQUIPMENT;  break;
                case "consumable": _categoryFilter = ItemCategoryUtil.TAB_CONSUMABLE; break;
                case "material":   _categoryFilter = ItemCategoryUtil.TAB_MATERIAL;   break;
                case "quest":      _categoryFilter = ItemCategoryUtil.TAB_QUEST;      break;
                case "other":      _categoryFilter = ItemCategoryUtil.TAB_OTHER;      break;
                default:           _categoryFilter = null;                            break; // "all"
            }
            RefreshPicker();
            RefreshTable();
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

        /// <summary>Search-box callback: update filter and refresh both views.</summary>
        private void OnSearchChanged(string value)
        {
            _searchFilter = value ?? "";
            RefreshPicker();
            RefreshTable();
        }
    }
}
