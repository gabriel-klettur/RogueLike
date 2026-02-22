using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Runtime registry of available tile assets organized by category.
    /// Loads tiles from a ScriptableObject catalog or Resources folder.
    /// Maps to Python's OVERLAY_CODE_MAP + tile picker palette.
    /// </summary>
    [CreateAssetMenu(fileName = "TileCatalog", menuName = "Valkur/Tile Catalog")]
    public class TileCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct TileEntry
        {
            public string category;
            public string tileName;
            public TileBase tile;
            public Sprite preview;
        }

        [SerializeField] private List<TileEntry> entries = new List<TileEntry>();

        public IReadOnlyList<TileEntry> Entries => entries;

        public List<string> GetCategories()
        {
            var cats = new List<string>();
            foreach (var e in entries)
            {
                if (!cats.Contains(e.category))
                    cats.Add(e.category);
            }
            return cats;
        }

        public List<TileEntry> GetTilesForCategory(string category)
        {
            var result = new List<TileEntry>();
            foreach (var e in entries)
            {
                if (e.category == category)
                    result.Add(e);
            }
            return result;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: populate catalog from TileAssets folder.
        /// Called by TilePaletteBuilder.
        /// </summary>
        public void PopulateFromAssets(List<TileEntry> newEntries)
        {
            entries = newEntries;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

    /// <summary>
    /// Runtime tile registry that provides tile lookup by name/category.
    /// Singleton, initialized from TileCatalog ScriptableObject.
    /// </summary>
    public class TileRegistry
    {
        private static TileRegistry _instance;
        public static TileRegistry Instance => _instance ??= new TileRegistry();

        private TileCatalog _catalog;
        private readonly Dictionary<string, TileBase> _tilesByName = new Dictionary<string, TileBase>();

        public TileCatalog Catalog => _catalog;
        public bool IsLoaded => _catalog != null;

        public void Load(TileCatalog catalog)
        {
            _catalog = catalog;
            _tilesByName.Clear();
            if (catalog == null) return;

            foreach (var entry in catalog.Entries)
            {
                if (!_tilesByName.ContainsKey(entry.tileName))
                    _tilesByName[entry.tileName] = entry.tile;
            }
            Debug.Log($"[TileRegistry] Loaded {_tilesByName.Count} tiles from catalog.");
        }

        public TileBase GetTile(string name)
        {
            _tilesByName.TryGetValue(name, out var tile);
            return tile;
        }
    }
}
