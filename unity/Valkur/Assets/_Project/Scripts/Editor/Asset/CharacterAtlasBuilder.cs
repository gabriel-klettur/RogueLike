using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Creates and validates a SpriteAtlas for player character sprites.
    /// Menu: Valkur > Atlas > Build Character Atlas (Players)
    /// </summary>
    public static class CharacterAtlasBuilder
    {
        private const string AtlasPath = "Assets/_Project/Art/Characters/Atlas_Characters_Players.spriteatlas";
        private const string CharactersFolder = "Assets/_Project/Art/Characters";
        private const string PlayerCatalogRoot = "Assets/_Project/Data/Catalogs/Players";

        [MenuItem("Valkur/Characters/Build Character Atlas (Players)")]
        public static void BuildCharacterAtlas()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            bool isNew = atlas == null;

            if (isNew)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, AtlasPath);
            }

            var packSettings = new SpriteAtlasPackingSettings
            {
                blockOffset = 1,
                padding = 2,
                enableRotation = false,
                enableTightPacking = false,
                enableAlphaDilation = false
            };
            atlas.SetPackingSettings(packSettings);

            var texSettings = new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = false,
                sRGB = true,
                filterMode = FilterMode.Point,
                anisoLevel = 1
            };
            atlas.SetTextureSettings(texSettings);

            var platformSettings = new TextureImporterPlatformSettings
            {
                maxTextureSize = 4096,
                textureCompression = TextureImporterCompression.Uncompressed,
                format = TextureImporterFormat.RGBA32
            };
            atlas.SetPlatformSettings(platformSettings);

            var existing = atlas.GetPackables();
            if (existing != null && existing.Length > 0)
                atlas.Remove(existing);

            var playerFolders = GetPlayerCharacterFolders();
            if (playerFolders.Count == 0)
            {
                Debug.LogError("[CharacterAtlasBuilder] No player character folders found from PlayerDefinition catalog.");
                return;
            }

            atlas.Add(playerFolders.ToArray());

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int spriteCount = CountSpritesInFolders(playerFolders);
            string action = isNew ? "Created" : "Updated";
            Debug.Log($"[CharacterAtlasBuilder] {action} atlas at {AtlasPath} — {spriteCount} sprites from {playerFolders.Count} player folders.");
        }

        [MenuItem("Valkur/Characters/Validate Character Atlas (Players)")]
        public static void ValidateCharacterAtlas()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            if (atlas == null)
            {
                Debug.LogError($"[CharacterAtlasBuilder] Atlas not found at {AtlasPath}. Run Build first.");
                return;
            }

            var packables = atlas.GetPackables();
            int packableCount = packables != null ? packables.Length : 0;
            var playerFolders = GetPlayerCharacterFolders();
            int spriteCount = CountSpritesInFolders(playerFolders);

            Debug.Log($"[CharacterAtlasBuilder] Atlas: {AtlasPath}\n" +
                      $"  Packable sources: {packableCount}\n" +
                      $"  Sprites in player folders: {spriteCount}\n" +
                      $"  Packed sprites: {atlas.spriteCount}");

            if (atlas.spriteCount == 0 && spriteCount > 0)
                Debug.LogWarning("[CharacterAtlasBuilder] Atlas has 0 packed sprites. Enter Play Mode or build to trigger packing.");
        }

        private static int CountSpritesInFolders(System.Collections.Generic.List<Object> folders)
        {
            int total = 0;
            for (int i = 0; i < folders.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(folders[i]);
                string[] sprites = AssetDatabase.FindAssets("t:Sprite", new[] { path });
                total += sprites.Length;
            }

            return total;
        }

        private static System.Collections.Generic.List<Object> GetPlayerCharacterFolders()
        {
            string[] guids = AssetDatabase.FindAssets("t:PlayerDefinition", new[] { PlayerCatalogRoot });
            var folders = new System.Collections.Generic.List<Object>();
            var uniquePaths = new System.Collections.Generic.HashSet<string>();

            for (int i = 0; i < guids.Length; i++)
            {
                string defPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(defPath);
                if (def == null)
                    continue;

                string classKey = ResolveClassKey(def);
                if (string.IsNullOrEmpty(classKey))
                    continue;

                string folderPath = $"{CharactersFolder}/{classKey}";
                var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
                if (folder == null)
                    continue;

                if (uniquePaths.Add(folderPath))
                    folders.Add(folder);
            }

            return folders;
        }

        private static string ResolveClassKey(PlayerDefinition playerDef)
        {
            if (!string.IsNullOrWhiteSpace(playerDef.playerKey))
                return playerDef.playerKey.Trim().ToLowerInvariant();

            return playerDef.name.Trim().ToLowerInvariant();
        }
    }
}
