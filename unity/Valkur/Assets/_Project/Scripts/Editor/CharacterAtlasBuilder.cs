using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

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

        [MenuItem("Valkur/Atlas/Build Character Atlas (Players)")]
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

            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(CharactersFolder);
            if (folder == null)
            {
                Debug.LogError($"[CharacterAtlasBuilder] Folder not found: {CharactersFolder}");
                return;
            }

            atlas.Add(new Object[] { folder });

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string[] sprites = AssetDatabase.FindAssets("t:Sprite", new[] { CharactersFolder });
            string action = isNew ? "Created" : "Updated";
            Debug.Log($"[CharacterAtlasBuilder] {action} atlas at {AtlasPath} — {sprites.Length} sprites from {CharactersFolder}");
        }

        [MenuItem("Valkur/Atlas/Validate Character Atlas (Players)")]
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
            string[] sprites = AssetDatabase.FindAssets("t:Sprite", new[] { CharactersFolder });

            Debug.Log($"[CharacterAtlasBuilder] Atlas: {AtlasPath}\n" +
                      $"  Packable sources: {packableCount}\n" +
                      $"  Sprites in Characters/: {sprites.Length}\n" +
                      $"  Packed sprites: {atlas.spriteCount}");

            if (atlas.spriteCount == 0 && sprites.Length > 0)
                Debug.LogWarning("[CharacterAtlasBuilder] Atlas has 0 packed sprites. Enter Play Mode or build to trigger packing.");
        }
    }
}
