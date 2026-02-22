using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
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
