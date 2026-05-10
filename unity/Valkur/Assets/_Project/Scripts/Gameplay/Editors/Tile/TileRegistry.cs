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
        private readonly Dictionary<TileBase, string> _namesByTile = new Dictionary<TileBase, string>();

        public TileCatalog Catalog => _catalog;
        public bool IsLoaded => _catalog != null;

        public void Load(TileCatalog catalog)
        {
            _catalog = catalog;
            _tilesByName.Clear();
            _namesByTile.Clear();
            if (catalog == null) return;

            foreach (var entry in catalog.Entries)
            {
                if (entry.tile == null || string.IsNullOrEmpty(entry.tileName)) continue;
                if (!_tilesByName.ContainsKey(entry.tileName))
                    _tilesByName[entry.tileName] = entry.tile;
                if (!_namesByTile.ContainsKey(entry.tile))
                    _namesByTile[entry.tile] = entry.tileName;
            }
            Debug.Log($"[TileRegistry] Loaded {_tilesByName.Count} tiles from catalog.");
        }

        /// <summary>Register a tile instance not present in the static catalog (e.g. tiles created by OverlayLoader at runtime).</summary>
        public void Register(string name, TileBase tile)
        {
            if (string.IsNullOrEmpty(name) || tile == null) return;
            _tilesByName[name] = tile;
            _namesByTile[tile] = name;
        }

        /// <summary>
        /// Drop the cached Tile registered under <paramref name="name"/>, if any.
        /// Used by callers (e.g. <see cref="TerrainTileResolver.ResolveTile"/>)
        /// that detected the cached entry has a destroyed sprite and want a
        /// fresh wrapping created the next time the same name is requested.
        /// </summary>
        public void Unregister(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_tilesByName.TryGetValue(name, out var tile))
            {
                _tilesByName.Remove(name);
                if (tile != null) _namesByTile.Remove(tile);
            }
        }

        /// <summary>
        /// Reset the registry to its uninitialized state. Primarily for tests:
        /// EditMode test fixtures destroy the sprites they create in TearDown,
        /// which would leave Tile instances pointing at destroyed sprites in
        /// the cache. Calling this in TearDown guarantees test isolation.
        /// </summary>
        public void Clear()
        {
            _catalog = null;
            _tilesByName.Clear();
            _namesByTile.Clear();
        }

        public TileBase GetTile(string name)
        {
            _tilesByName.TryGetValue(name, out var tile);
            return tile;
        }

        /// <summary>
        /// Resolve the canonical name for a tile instance. Falls back to <see cref="Object.name"/> and the underlying sprite name
        /// so tiles created outside the catalog (e.g. via <c>OverlayLoader</c>) can still be serialized.
        /// </summary>
        public string GetName(TileBase tile)
        {
            if (tile == null) return null;
            if (_namesByTile.TryGetValue(tile, out var registered) && !string.IsNullOrEmpty(registered))
                return registered;
            if (!string.IsNullOrEmpty(tile.name))
                return tile.name;
            if (tile is Tile t && t.sprite != null && !string.IsNullOrEmpty(t.sprite.name))
                return t.sprite.name;
            return null;
        }
    }
}
