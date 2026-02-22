using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Editor
{
    /// <summary>
    /// Generates Unity Tile assets from sprites in ready/ and creates a Tile Palette.
    /// Menu: Valkur > Atlas > Generate Tile Assets
    /// Menu: Valkur > Atlas > Generate Tile Palette
    ///
    /// Workflow:
    /// 1. Run "Generate Tile Assets" to create .asset Tile files from each sprite in ready/
    /// 2. Run "Generate Tile Palette" to create a paintable palette prefab
    /// 3. Open Window > 2D > Tile Palette to use it
    /// </summary>
    public static class TilePaletteBuilder
    {
        private const string TILES_READY_FOLDER = "Assets/_Project/Art/Tiles/ready";
        private const string TILE_ASSETS_FOLDER = "Assets/_Project/Art/Tiles/TileAssets";
        private const string PALETTE_PATH = "Assets/_Project/Art/Tiles/Palettes";

        [MenuItem("Valkur/Atlas/Generate Tile Assets")]
        public static void GenerateTileAssets()
        {
            // Ensure output folder exists
            if (!AssetDatabase.IsValidFolder(TILE_ASSETS_FOLDER))
            {
                CreateFolderRecursive(TILE_ASSETS_FOLDER);
            }

            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { TILES_READY_FOLDER });
            int created = 0;
            int skipped = 0;

            foreach (string guid in guids)
            {
                string spritePath = AssetDatabase.GUIDToAssetPath(guid);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite == null) continue;

                // Determine subfolder from relative path
                string relativePath = spritePath.Substring(TILES_READY_FOLDER.Length + 1);
                string subDir = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? "";
                string tileName = Path.GetFileNameWithoutExtension(spritePath);

                string targetFolder = string.IsNullOrEmpty(subDir)
                    ? TILE_ASSETS_FOLDER
                    : $"{TILE_ASSETS_FOLDER}/{subDir}";

                if (!AssetDatabase.IsValidFolder(targetFolder))
                    CreateFolderRecursive(targetFolder);

                string tileAssetPath = $"{targetFolder}/{tileName}.asset";

                // Skip if already exists
                if (AssetDatabase.LoadAssetAtPath<Tile>(tileAssetPath) != null)
                {
                    skipped++;
                    continue;
                }

                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.color = Color.white;
                tile.colliderType = Tile.ColliderType.None;

                AssetDatabase.CreateAsset(tile, tileAssetPath);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TilePaletteBuilder] Generated {created} tile assets, skipped {skipped} existing. Total sprites: {guids.Length}");
        }

        [MenuItem("Valkur/Atlas/Generate Tile Palette")]
        public static void GenerateTilePalette()
        {
            if (!AssetDatabase.IsValidFolder(PALETTE_PATH))
                CreateFolderRecursive(PALETTE_PATH);

            // Collect all tile assets organized by category
            string[] guids = AssetDatabase.FindAssets("t:Tile", new[] { TILE_ASSETS_FOLDER });
            if (guids.Length == 0)
            {
                Debug.LogError("[TilePaletteBuilder] No tile assets found. Run 'Generate Tile Assets' first.");
                return;
            }

            // Create palette prefab with a Grid + Tilemap
            string palettePrefabPath = $"{PALETTE_PATH}/ValkurTilePalette.prefab";

            // Delete existing palette to recreate
            if (File.Exists(Path.GetFullPath(palettePrefabPath)))
                AssetDatabase.DeleteAsset(palettePrefabPath);

            var paletteGo = new GameObject("ValkurTilePalette");
            var grid = paletteGo.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);
            grid.cellLayout = GridLayout.CellLayout.Rectangle;

            var tilemapGo = new GameObject("Layer1");
            tilemapGo.transform.SetParent(paletteGo.transform, false);
            var tilemap = tilemapGo.AddComponent<Tilemap>();
            tilemapGo.AddComponent<TilemapRenderer>();

            // Place tiles in a grid layout organized by category
            var tilesByCategory = new Dictionary<string, List<TileBase>>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tile = AssetDatabase.LoadAssetAtPath<TileBase>(path);
                if (tile == null) continue;

                // Extract category from path
                string relative = path.Substring(TILE_ASSETS_FOLDER.Length + 1);
                string category = Path.GetDirectoryName(relative)?.Replace("\\", "/") ?? "uncategorized";
                // Simplify to top-level category
                int slashIdx = category.IndexOf('/');
                if (slashIdx > 0) category = category.Substring(0, slashIdx);

                if (!tilesByCategory.ContainsKey(category))
                    tilesByCategory[category] = new List<TileBase>();
                tilesByCategory[category].Add(tile);
            }

            // Layout: each category gets a row, tiles placed left to right
            int maxCols = 26; // ~26 tiles per row
            int currentRow = 0;
            foreach (var kvp in tilesByCategory)
            {
                int col = 0;
                foreach (var tile in kvp.Value)
                {
                    tilemap.SetTile(new Vector3Int(col, -currentRow, 0), tile);
                    col++;
                    if (col >= maxCols)
                    {
                        col = 0;
                        currentRow++;
                    }
                }
                currentRow++; // Gap between categories
            }

            // Save as prefab
            PrefabUtility.SaveAsPrefabAsset(paletteGo, palettePrefabPath);
            Object.DestroyImmediate(paletteGo);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TilePaletteBuilder] Created palette at {palettePrefabPath} with {guids.Length} tiles in {tilesByCategory.Count} categories.\n" +
                      "Open Window > 2D > Tile Palette and select 'ValkurTilePalette' to use it.");
        }

        [MenuItem("Valkur/Atlas/Generate Tile Catalog (Runtime)")]
        public static void GenerateTileCatalog()
        {
            string[] guids = AssetDatabase.FindAssets("t:Tile", new[] { TILE_ASSETS_FOLDER });
            if (guids.Length == 0)
            {
                Debug.LogError("[TilePaletteBuilder] No tile assets found. Run 'Generate Tile Assets' first.");
                return;
            }

            // Output to Resources/ so TileEditorManager can use Resources.Load at runtime
            const string RESOURCES_FOLDER = "Assets/_Project/Resources";
            string catalogPath = $"{RESOURCES_FOLDER}/TileCatalog.asset";

            // Also clean up old location if it exists
            string oldPath = $"{TILE_ASSETS_FOLDER}/TileCatalog.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(oldPath) != null)
            {
                AssetDatabase.DeleteAsset(oldPath);
                Debug.Log($"[TilePaletteBuilder] Removed old catalog at {oldPath}");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<Valkur.Gameplay.TileEditor.TileCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<Valkur.Gameplay.TileEditor.TileCatalog>();
                if (!AssetDatabase.IsValidFolder(RESOURCES_FOLDER))
                    CreateFolderRecursive(RESOURCES_FOLDER);
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }

            var entries = new List<Valkur.Gameplay.TileEditor.TileCatalog.TileEntry>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (tile == null) continue;

                string relative = path.Substring(TILE_ASSETS_FOLDER.Length + 1);
                string category = Path.GetDirectoryName(relative)?.Replace("\\", "/") ?? "uncategorized";
                int slashIdx = category.IndexOf('/');
                if (slashIdx > 0) category = category.Substring(0, slashIdx);

                entries.Add(new Valkur.Gameplay.TileEditor.TileCatalog.TileEntry
                {
                    category = category,
                    tileName = tile.name,
                    tile = tile,
                    preview = tile.sprite
                });
            }

            catalog.PopulateFromAssets(entries);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TilePaletteBuilder] Generated TileCatalog at {catalogPath} with {entries.Count} entries.\n" +
                      "It will be auto-loaded at runtime via Resources.Load(\"TileCatalog\"). Press F6 in Play mode to use it.");
        }

        private static void CreateFolderRecursive(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
