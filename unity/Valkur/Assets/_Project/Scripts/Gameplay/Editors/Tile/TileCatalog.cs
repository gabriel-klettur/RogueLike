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

            // Tilesheet metadata. -1 means "this tile did not come from a sliced
            // tilesheet" (legacy categories like grass_dirt, sand_ocean…).
            // Populated from Resources/Tiles/<cat>/_manifest.json when present.
            public int gridR;
            public int gridC;
            public int uniqueId;
            public bool transparent;
        }

        [SerializeField] private List<TileEntry> entries = new List<TileEntry>();

        // Category-keyed index built lazily on first lookup. Avoids the O(total)
        // scan of entries every time GetTilesForCategory / IsCurrentCategoryTilesheet
        // is called — a hot path while picking from large tilesheets like
        // castle_pandora (2,688 cells). Invalidated whenever entries mutate.
        [System.NonSerialized] private Dictionary<string, List<TileEntry>> _byCategory;
        [System.NonSerialized] private List<string> _categoriesCache;

        public IReadOnlyList<TileEntry> Entries => entries;

        private void EnsureIndex()
        {
            if (_byCategory != null) return;
            _byCategory = new Dictionary<string, List<TileEntry>>(16);
            _categoriesCache = new List<string>(16);
            foreach (var e in entries)
            {
                if (!_byCategory.TryGetValue(e.category, out var list))
                {
                    list = new List<TileEntry>();
                    _byCategory[e.category] = list;
                    _categoriesCache.Add(e.category);
                }
                list.Add(e);
            }
        }

        private void InvalidateIndex()
        {
            _byCategory = null;
            _categoriesCache = null;
        }

        public List<string> GetCategories()
        {
            EnsureIndex();
            // Defensive copy: callers iterate / mutate the result list.
            return new List<string>(_categoriesCache);
        }

        public List<TileEntry> GetTilesForCategory(string category)
        {
            EnsureIndex();
            if (_byCategory.TryGetValue(category, out var list))
                return new List<TileEntry>(list);
            return new List<TileEntry>();
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

            // Category folders under Resources/Tiles/ are discovered at editor
            // time (Resources/ ships flat in a build — no runtime directory
            // listing API exists) and baked into _categories.json by
            // TileCategoryManifestBuilder, same convention as the per-tilesheet
            // _manifest.json files below. Tilesheet-style categories (with a
            // _manifest.json) live alongside legacy single-tile categories —
            // the loader auto-detects which is which by probing for _manifest.json.
            var categoryManifestAsset = Resources.Load<TextAsset>("Tiles/_categories");
            TileCategoryManifest categoryManifest = categoryManifestAsset != null
                ? JsonUtility.FromJson<TileCategoryManifest>(categoryManifestAsset.text)
                : null;

            string[] categories = (categoryManifest != null && categoryManifest.folderCategories != null)
                ? categoryManifest.folderCategories
                : System.Array.Empty<string>();

            var seen = new HashSet<string>();
            int total = 0;

            foreach (string cat in categories)
            {
                string path = $"Tiles/{cat}";
                var sprites = Resources.LoadAll<Sprite>(path);
                if (sprites == null || sprites.Length == 0) continue;

                // Load tilesheet manifest if present. Categories without a
                // manifest (legacy) get the default -1/-1/-1 metadata.
                var manifestText = Resources.Load<TextAsset>($"Tiles/{cat}/_manifest");
                Dictionary<string, TilesheetManifest.Cell> cellLookup = null;
                if (manifestText != null)
                {
                    var manifest = JsonUtility.FromJson<TilesheetManifest>(manifestText.text);
                    if (manifest != null && manifest.cells != null)
                    {
                        cellLookup = new Dictionary<string, TilesheetManifest.Cell>(manifest.cells.Length);
                        foreach (var cell in manifest.cells)
                            cellLookup[cell.file] = cell;
                    }
                }

                foreach (var sprite in sprites)
                {
                    // Avoid duplicates (subfolders may overlap)
                    if (!seen.Add(sprite.name)) continue;

                    int gR = -1, gC = -1, uId = -1;
                    bool transparent = false;
                    if (cellLookup != null && cellLookup.TryGetValue(sprite.name, out var cell))
                    {
                        gR = cell.r;
                        gC = cell.c;
                        uId = cell.uniqueId;
                        transparent = cell.transparent;
                    }

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
                        preview = sprite,
                        gridR = gR,
                        gridC = gC,
                        uniqueId = uId,
                        transparent = transparent
                    });
                    total++;
                }
            }

            // Loose sprites directly under Resources/Tiles/ (no owning
            // subfolder) — e.g. floor, wall, dungeon_floor. Loaded individually
            // (not LoadAll, which would re-pull every category sprite too) and
            // grouped under the manifest's synthetic category so they become
            // selectable from the picker without moving a single PNG —
            // WorldLoader.cs loads several of them by the literal path
            // "Tiles/wall" / "Tiles/floor", which must not change.
            if (categoryManifest != null && categoryManifest.rootFiles != null)
            {
                string rootCategory = string.IsNullOrEmpty(categoryManifest.syntheticRootCategory)
                    ? "basics"
                    : categoryManifest.syntheticRootCategory;

                foreach (string fileName in categoryManifest.rootFiles)
                {
                    var sprite = Resources.Load<Sprite>("Tiles/" + fileName);
                    if (sprite == null) continue;
                    if (!seen.Add(sprite.name)) continue;

                    var tile = CreateInstance<Tile>();
                    tile.sprite = sprite;
                    tile.color = Color.white;
                    tile.colliderType = Tile.ColliderType.None;
                    tile.name = sprite.name;

                    catalog.entries.Add(new TileEntry
                    {
                        category = rootCategory,
                        tileName = sprite.name,
                        tile = tile,
                        preview = sprite,
                        gridR = -1,
                        gridC = -1,
                        uniqueId = -1,
                        transparent = false
                    });
                    total++;
                }
            }

            if (total == 0)
            {
                // Emergency fallback only: reached when _categories.json is
                // missing/unparseable AND no folder or root file loaded
                // anything (broken checkout). Normal operation never gets here.
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
                            preview = sprite,
                            gridR = -1,
                            gridC = -1,
                            uniqueId = -1,
                            transparent = false
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
            InvalidateIndex();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
