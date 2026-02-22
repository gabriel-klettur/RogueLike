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
}
