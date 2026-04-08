using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Automatically applies import rules to assets placed under _Project/.
    /// Ensures consistent PPU, filter mode, pivot, and compression per category.
    /// </summary>
    public class ValkurAssetPostprocessor : AssetPostprocessor
    {
        private const int DEFAULT_PPU           = 16;
        private const int PLAYER_CHARACTER_PPU  = 64;
        private const int NPC_PPU               = 64;   // 128 px native ÷ 64 PPU = 2 units = 2 tiles (matches Python 0.5× scale)
        private const int TILE_PPU              = 32;
        private const int BUILDING_PPU          = 32;   // 1 Unity unit = 1 game tile = 32 px
        private const int UI_PPU                = 100;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Art/") &&
                !assetPath.StartsWith("Assets/_Project/Resources/Tiles/") &&
                !assetPath.StartsWith("Assets/_Project/Resources/Buildings/"))
                return;

            var importer = (TextureImporter)assetImporter;

            // Common settings for all game art
            importer.textureType = TextureImporterType.Sprite;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;

            // Category-specific PPU and pivot
            if (assetPath.Contains("/Buildings/"))
            {
                // Building sprites are split at runtime using Sprite.Create.
                // PPU=32 matches tile world units so that 32 px = 1 Unity unit = 1 game tile.
                // isReadable is NOT required for Sprite.Create (UV-based crop).
                importer.spritePixelsPerUnit = BUILDING_PPU;
                importer.spritePivot         = new Vector2(0.5f, 0f); // bottom-center
                importer.spriteImportMode    = SpriteImportMode.Single;
                SetPivot(importer, new Vector2(0.5f, 0f));
            }
            else if (assetPath.Contains("/Tiles/"))
            {
                importer.spritePixelsPerUnit = TILE_PPU;
                SetPivot(importer, new Vector2(0.5f, 0.5f));
            }
            else if (assetPath.Contains("/Characters/"))
            {
                importer.spritePixelsPerUnit = PLAYER_CHARACTER_PPU;
                importer.filterMode = FilterMode.Bilinear;
                SetPivot(importer, new Vector2(0.5f, 0f));
            }
            else if (assetPath.Contains("/NPC/"))
            {
                importer.spritePixelsPerUnit = NPC_PPU;
                SetPivot(importer, new Vector2(0.5f, 0f));
            }
            else if (assetPath.Contains("/UI/"))
            {
                importer.spritePixelsPerUnit = UI_PPU;
                importer.filterMode = FilterMode.Bilinear;
            }
            else if (assetPath.Contains("/Items/"))
            {
                importer.spritePixelsPerUnit = DEFAULT_PPU;
                SetPivot(importer, new Vector2(0.5f, 0.5f));
            }
            else
            {
                importer.spritePixelsPerUnit = DEFAULT_PPU;
            }
        }

        private static void SetPivot(TextureImporter importer, Vector2 pivot)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            importer.SetTextureSettings(settings);
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/_Project/Audio/"))
                return;

            var importer = (AudioImporter)assetImporter;

            // SFX: decompress on load for low latency
            if (assetPath.Contains("/SFX/"))
            {
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                importer.defaultSampleSettings = settings;
            }
            // Music: streaming to save memory
            else if (assetPath.Contains("/Music/"))
            {
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.defaultSampleSettings = settings;
            }
        }
    }
}
