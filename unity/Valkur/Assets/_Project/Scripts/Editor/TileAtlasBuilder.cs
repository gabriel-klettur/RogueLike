using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Valkur.Editor
{
    /// <summary>
    /// Creates a SpriteAtlas for map tiles from the ready/ folder.
    /// Menu: Valkur > Atlas > Build Tile Atlas
    /// 
    /// Settings:
    /// - Includes all sprites under Assets/_Project/Art/Tiles/ready/
    /// - FilterMode.Point, no compression, no mipmaps (pixel art)
    /// - Max texture size 4096 (fits ~16k 32x32 tiles)
    /// - Padding 2px to avoid bleeding
    /// - No rotation/tight packing (grid tiles must stay axis-aligned)
    /// </summary>
    public static class TileAtlasBuilder
    {
        private const string ATLAS_PATH = "Assets/_Project/Art/Tiles/Atlas_Tiles.spriteatlas";
        // The historic 'Art/Tiles/ready' folder was a Python-era staging area
        // that no longer exists; the canonical runtime tiles live under
        // Resources/Tiles (loaded by TileCatalog.BuildFromResources at boot).
        // Pointing the atlas builder at Resources/Tiles lets the menu item
        // produce a working atlas of the 389 runtime tiles instead of failing
        // silently with "folder not found".
        private const string TILES_READY_FOLDER = "Assets/_Project/Resources/Tiles";

        [MenuItem("Valkur/Tiles/Build Tile Atlas")]
        public static void BuildTileAtlas()
        {
            // Create or load existing atlas
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(ATLAS_PATH);
            bool isNew = atlas == null;

            if (isNew)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, ATLAS_PATH);
            }

            // Configure packing settings
            var packSettings = new SpriteAtlasPackingSettings
            {
                blockOffset = 1,
                padding = 2,
                enableRotation = false,
                enableTightPacking = false,
                enableAlphaDilation = false
            };
            atlas.SetPackingSettings(packSettings);

            // Configure texture settings (pixel art: Point filter, no compression)
            var texSettings = new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = false,
                sRGB = true,
                filterMode = FilterMode.Point,
                anisoLevel = 1
            };
            atlas.SetTextureSettings(texSettings);

            // Platform settings: uncompressed, max 4096
            var platformSettings = new TextureImporterPlatformSettings
            {
                maxTextureSize = 4096,
                textureCompression = TextureImporterCompression.Uncompressed,
                format = TextureImporterFormat.RGBA32
            };
            atlas.SetPlatformSettings(platformSettings);

            // Clear existing packables and add the ready/ folder
            var existing = atlas.GetPackables();
            if (existing != null && existing.Length > 0)
                atlas.Remove(existing);

            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(TILES_READY_FOLDER);
            if (folder != null)
            {
                atlas.Add(new Object[] { folder });
                Debug.Log($"[TileAtlasBuilder] Added folder: {TILES_READY_FOLDER}");
            }
            else
            {
                Debug.LogError($"[TileAtlasBuilder] Folder not found: {TILES_READY_FOLDER}");
                return;
            }

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Count sprites that will be packed
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { TILES_READY_FOLDER });
            string action = isNew ? "Created" : "Updated";
            Debug.Log($"[TileAtlasBuilder] {action} atlas at {ATLAS_PATH} — {guids.Length} sprites from {TILES_READY_FOLDER}");
        }

        [MenuItem("Valkur/Tiles/Validate Tile Atlas")]
        public static void ValidateTileAtlas()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(ATLAS_PATH);
            if (atlas == null)
            {
                Debug.LogError($"[TileAtlasBuilder] Atlas not found at {ATLAS_PATH}. Run Build first.");
                return;
            }

            var packables = atlas.GetPackables();
            int packableCount = packables != null ? packables.Length : 0;

            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { TILES_READY_FOLDER });

            Debug.Log($"[TileAtlasBuilder] Atlas: {ATLAS_PATH}\n" +
                      $"  Packable sources: {packableCount}\n" +
                      $"  Sprites in ready/: {guids.Length}\n" +
                      $"  Packed sprites: {atlas.spriteCount}");

            if (atlas.spriteCount == 0 && guids.Length > 0)
                Debug.LogWarning("[TileAtlasBuilder] Atlas has 0 packed sprites. Enter Play Mode or build to trigger packing.");
        }
    }
}
