using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Prepares player character sheets (SpriteMode=Multiple + 128x128 slicing)
    /// and binds resulting sprites into PlayerDefinition.assetConfig sheet lists.
    /// Baseline scope: players with idle + walk animations.
    /// </summary>
    public static class PlayerCharacterAssetBinder
    {
        private const string CharactersRoot = "Assets/_Project/Art/Characters";
        private const string PlayerCatalogRoot = "Assets/_Project/Data/Catalogs/Players";
        private const int FrameSizePx = 128;
        private const int CharacterPpu = 64;

        [MenuItem("Valkur/Characters/Rebuild Player Character Assets")]
        public static void RebuildPlayerCharacterAssets()
        {
            string[] playerGuids = AssetDatabase.FindAssets("t:PlayerDefinition", new[] { PlayerCatalogRoot });
            if (playerGuids == null || playerGuids.Length == 0)
            {
                Debug.LogWarning("[PlayerCharacterAssetBinder] No PlayerDefinition assets found.");
                return;
            }

            int updatedDefs = 0;
            int configuredSheets = 0;

            foreach (string guid in playerGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var playerDef = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(path);
                if (playerDef == null)
                    continue;

                string classKey = ResolveClassKey(playerDef);
                if (string.IsNullOrEmpty(classKey))
                {
                    Debug.LogWarning($"[PlayerCharacterAssetBinder] Skipping '{playerDef.name}' due to empty class key.", playerDef);
                    continue;
                }

                bool changed = BindClassSheets(playerDef, classKey, ref configuredSheets);
                if (changed)
                {
                    EditorUtility.SetDirty(playerDef);
                    updatedDefs++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PlayerCharacterAssetBinder] Updated {updatedDefs} PlayerDefinition assets. Configured/Reimported {configuredSheets} sheets.");
        }

        private static bool BindClassSheets(PlayerDefinition playerDef, string classKey, ref int configuredSheets)
        {
            if (playerDef.assetConfig == null)
                playerDef.assetConfig = new EntityAssetConfig();

            List<Sprite> idleFrames = LoadSheetFrames(classKey, "idle", ref configuredSheets);
            List<Sprite> walkFrames = LoadSheetFrames(classKey, "walking", ref configuredSheets);
            List<Sprite> castFrames = LoadSheetFrames(classKey, "casting", ref configuredSheets);

            bool hasAny = idleFrames.Count > 0 || walkFrames.Count > 0;
            if (!hasAny)
            {
                Debug.LogWarning($"[PlayerCharacterAssetBinder] No idle/walking sheets found for class '{classKey}'.");
                return false;
            }

            if (walkFrames.Count == 0 && idleFrames.Count > 0)
                walkFrames = new List<Sprite>(idleFrames);
            if (castFrames.Count == 0)
                castFrames = new List<Sprite>(walkFrames);

            playerDef.assetConfig.idleSheets = idleFrames;
            playerDef.assetConfig.walkSheets = walkFrames;
            playerDef.assetConfig.chaseSheets = walkFrames.Count > 0 ? new List<Sprite>(walkFrames) : new List<Sprite>();
            playerDef.assetConfig.castSheets = castFrames;
            playerDef.assetConfig.attackSheets = castFrames.Count > 0 ? new List<Sprite>(castFrames) : new List<Sprite>();
            playerDef.assetConfig.damageSheets = idleFrames.Count > 0 ? new List<Sprite>(idleFrames) : new List<Sprite>();
            playerDef.assetConfig.deathSheets = idleFrames.Count > 0 ? new List<Sprite>(idleFrames) : new List<Sprite>();
            return true;
        }

        private static List<Sprite> LoadSheetFrames(string classKey, string stateSuffix, ref int configuredSheets)
        {
            string texturePath = $"{CharactersRoot}/{classKey}/{classKey}_{stateSuffix}.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
                return new List<Sprite>();

            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
                return new List<Sprite>();

            bool needsReimport = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                needsReimport = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                importer.spriteImportMode = SpriteImportMode.Multiple;
                needsReimport = true;
            }

            if (Math.Abs(importer.spritePixelsPerUnit - CharacterPpu) > 0.001f)
            {
                importer.spritePixelsPerUnit = CharacterPpu;
                needsReimport = true;
            }

            if (importer.filterMode != FilterMode.Bilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                needsReimport = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                needsReimport = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                needsReimport = true;
            }

            // Read actual source dimensions to avoid maxTextureSize capping (e.g. 5120px sheets
            // were truncated to 2048px, losing 24 of 40 frames).
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            int requiredMaxSize = Mathf.NextPowerOfTwo(Mathf.Max(sourceWidth, sourceHeight));
            if (importer.maxTextureSize < requiredMaxSize)
            {
                importer.maxTextureSize = requiredMaxSize;
                needsReimport = true;
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (settings.spriteAlignment != (int)SpriteAlignment.Custom || settings.spritePivot != new Vector2(0.5f, 0f))
            {
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = new Vector2(0.5f, 0f);
                importer.SetTextureSettings(settings);
                needsReimport = true;
            }

            // Use source width (not texture.width which returns the capped/imported size)
            int frameCount = Mathf.Max(1, sourceWidth / FrameSizePx);
            var metas = new List<SpriteMetaData>(frameCount);
            for (int i = 0; i < frameCount; i++)
            {
                var rect = new Rect(i * FrameSizePx, 0, FrameSizePx, FrameSizePx);
                metas.Add(new SpriteMetaData
                {
                    name = $"{classKey}_{stateSuffix}_{i:D3}",
                    rect = rect,
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = new Vector2(0.5f, 0f),
                    border = Vector4.zero
                });
            }

            // NOTE: This project still targets the importer workflow where spritesheet metadata is available.
            // We keep this path for deterministic slicing from code and can migrate to
            // ISpriteEditorDataProvider in a later refactor.
#pragma warning disable 618
            if (!SpriteMetaEquals(importer.spritesheet, metas))
            {
                importer.spritesheet = metas.ToArray();
                needsReimport = true;
            }
#pragma warning restore 618

            if (needsReimport)
            {
                importer.SaveAndReimport();
                configuredSheets++;
            }

            var sprites = LoadSpritesAtPath(texturePath);
            if (sprites.Count == 0)
                Debug.LogWarning($"[PlayerCharacterAssetBinder] No sliced sprites found at '{texturePath}'.");

            return sprites;
        }

        private static List<Sprite> LoadSpritesAtPath(string texturePath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
            var sprites = new List<Sprite>();
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite sprite)
                    sprites.Add(sprite);
            }

            sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return sprites;
        }

        private static bool SpriteMetaEquals(SpriteMetaData[] existing, List<SpriteMetaData> target)
        {
            if (existing == null || existing.Length != target.Count)
                return false;

            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].name != target[i].name)
                    return false;

                if (Mathf.Abs(existing[i].rect.x - target[i].rect.x) > 0.01f ||
                    Mathf.Abs(existing[i].rect.y - target[i].rect.y) > 0.01f ||
                    Mathf.Abs(existing[i].rect.width - target[i].rect.width) > 0.01f ||
                    Mathf.Abs(existing[i].rect.height - target[i].rect.height) > 0.01f)
                    return false;
            }

            return true;
        }

        private static string ResolveClassKey(PlayerDefinition playerDef)
        {
            if (!string.IsNullOrWhiteSpace(playerDef.playerKey))
                return playerDef.playerKey.Trim().ToLowerInvariant();

            return playerDef.name.Trim().ToLowerInvariant();
        }
    }
}
