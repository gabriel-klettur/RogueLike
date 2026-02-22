using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Runtime registry of available tile assets organized by category.
    /// Can be populated from a ScriptableObject asset (editor) or built
    /// at runtime from sprites in Resources/Tiles/ subfolders.
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

        /// <summary>
        /// Build the catalog at runtime from sprites in Resources/Tiles/.
        /// Loads ALL sprites, creates Tile instances on-the-fly.
        /// Category is derived from the sprite name prefix (tileset number).
        /// No pre-built .asset files or SpriteAtlas needed.
        /// </summary>
        public static TileCatalog BuildFromResources()
        {
            var catalog = CreateInstance<TileCatalog>();

            // Known category folders under Resources/Tiles/
            string[] categories = {
                "grass_dirt", "grass_rock", "ocean_grass", "rock_water",
                "sand_grass", "sand_ocean", "sand_ocean_2", "sand_rock"
            };

            var seen = new HashSet<string>();
            int total = 0;

            foreach (string cat in categories)
            {
                string path = $"Tiles/{cat}";
                var sprites = Resources.LoadAll<Sprite>(path);
                if (sprites == null || sprites.Length == 0) continue;

                foreach (var sprite in sprites)
                {
                    // Avoid duplicates (subfolders may overlap)
                    if (!seen.Add(sprite.name)) continue;

                    var tile = CreateInstance<Tile>();
                    tile.sprite = sprite;
                    tile.color = Color.white;
                    tile.colliderType = Tile.ColliderType.None;
                    tile.name = sprite.name;

                    catalog.entries.Add(new TileEntry
                    {
                        category = cat,
                        tileName = sprite.name,
                        tile = tile,
                        preview = sprite
                    });
                    total++;
                }
            }

            if (total == 0)
            {
                // Fallback: try loading everything from Tiles/ root
                var allSprites = Resources.LoadAll<Sprite>("Tiles");
                if (allSprites != null)
                {
                    foreach (var sprite in allSprites)
                    {
                        if (!seen.Add(sprite.name)) continue;

                        var tile = CreateInstance<Tile>();
                        tile.sprite = sprite;
                        tile.color = Color.white;
                        tile.colliderType = Tile.ColliderType.None;
                        tile.name = sprite.name;

                        catalog.entries.Add(new TileEntry
                        {
                            category = "uncategorized",
                            tileName = sprite.name,
                            tile = tile,
                            preview = sprite
                        });
                        total++;
                    }
                }
            }

            Debug.Log($"[TileCatalog] Built runtime catalog: {total} tiles from Resources/Tiles/.");
            return catalog;
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
