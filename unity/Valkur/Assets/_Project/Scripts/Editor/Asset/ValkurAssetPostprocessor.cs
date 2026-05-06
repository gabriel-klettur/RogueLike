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
        private const int TILE_MAX_ALLOWED_SIZE = 64;   // hard upper bound for tile source PNGs (px)
        private const int BUILDING_PPU          = 32;   // 1 Unity unit = 1 game tile = 32 px
        private const int UI_PPU                = 100;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Art/") &&
                !assetPath.StartsWith("Assets/_Project/Resources/Tiles/") &&
                !assetPath.StartsWith("Assets/_Project/Resources/Buildings/"))
                return;

            // Skip backup / source / experimental folders. These hold raw artwork,
            // multi-sprite tilesets, and PSD exports that are NOT consumed at runtime
            // and therefore are exempt from the strict tile size policy.
            if (assetPath.Contains("/_backups/") ||
                assetPath.Contains("/_raw/") ||
                assetPath.Contains("/_source/"))
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
                // Tile sprites must render at exactly 1 world unit (1 cell).
                // Canonical size: 32x32 px @ PPU=32. Sources up to 64x64 are still
                // accepted (will rescale to 1 unit) without warning.
                //
                // Anything larger is REJECTED with an error: oversized tiles cause
                // catastrophic visual bleeding (one cell rendering as N×N units),
                // which historically produced the "sand patch" overlap bug.
                // Run `Valkur > Tiles > Audit Sizes` (or `python tools/atlas/
                // audit_tile_sizes.py --fix`) to downscale offenders to 32x32.
                Vector2Int srcSize = GetSourceTextureSize(importer);
                int tileMax = Mathf.Max(srcSize.x, srcSize.y);
                if (tileMax > TILE_MAX_ALLOWED_SIZE)
                {
                    Debug.LogError(
                        $"[ValkurAssetPostprocessor] OVERSIZED TILE: '{assetPath}' is " +
                        $"{srcSize.x}x{srcSize.y} px, exceeds the {TILE_MAX_ALLOWED_SIZE}px " +
                        $"limit. Tiles must be ≤{TILE_MAX_ALLOWED_SIZE}x{TILE_MAX_ALLOWED_SIZE} " +
                        $"to render as a single map cell. Run `Valkur > Tiles > Audit Sizes` to fix.");
                }
                importer.spritePixelsPerUnit = TILE_PPU;
                SetPivot(importer, new Vector2(0.5f, 0.5f));
            }
            else if (assetPath.Contains("/Characters/"))
            {
                // Keep the default Point filter from line 39 — Bilinear blurs pixels
                // when the camera zooms in (zoom range 2..25 in CameraSetup), which
                // ruins the pixel-art look the rest of the game uses. Tiles, NPCs,
                // and items are all Point-filtered; characters must match.
                importer.spritePixelsPerUnit = PLAYER_CHARACTER_PPU;
                SetPivot(importer, new Vector2(0.5f, 0f));
                // Character spritesheets are wide (e.g. 5248×128 = 41 frames @ 128 px).
                // Standalone's default 2048 max would downsample each 128×128 frame
                // to ~50×50, making the wizard look heavily blocky after zoom even
                // with Point filtering. Force max=8192 across all build targets so
                // the source pixels survive into the SpriteAtlas.
                importer.maxTextureSize = 8192;
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

            // Per-platform overrides: Standalone / WebGL / iOS / Android default
            // to compressed textures regardless of the Default platform setting.
            // Pixel art with compression artefacts is the single highest-impact
            // visual regression in 2D; 12k+ tile sprites on a Standalone build
            // would otherwise ship compressed even though the postprocessor
            // wrote Uncompressed to the Default platform. Forcing the platform
            // settings keeps the policy durable across all build targets.
            //
            // Characters also need maxTextureSize lifted to 8192 — their wide
            // spritesheets (5248×128) get crushed to 2048×50 by the per-platform
            // default, killing per-frame detail before the atlas even runs.
            int platformMaxSize = assetPath.Contains("/Characters/") ? 8192 : 0;
            ApplyUncompressedPlatformOverride(importer, "Standalone", platformMaxSize);
            ApplyUncompressedPlatformOverride(importer, "WebGL",      platformMaxSize);
            ApplyUncompressedPlatformOverride(importer, "Android",    platformMaxSize);
            ApplyUncompressedPlatformOverride(importer, "iPhone",     platformMaxSize);
        }

        // Forces the per-platform texture import block to "Override = true" with
        // Uncompressed format, mirroring the Default-platform setting. Idempotent:
        // re-runs after a manual platform override are silent no-ops.
        // Pass maxTextureSize > 0 to also override the per-platform max size.
        private static void ApplyUncompressedPlatformOverride(TextureImporter importer, string platform, int maxTextureSize = 0)
        {
            var ps = importer.GetPlatformTextureSettings(platform);
            if (ps == null) return;

            bool needsMaxSizeChange = maxTextureSize > 0 && ps.maxTextureSize != maxTextureSize;

            // Skip work if already in the desired state.
            if (ps.overridden &&
                ps.textureCompression == TextureImporterCompression.Uncompressed &&
                !needsMaxSizeChange)
                return;

            ps.overridden          = true;
            ps.textureCompression  = TextureImporterCompression.Uncompressed;
            ps.format              = TextureImporterFormat.Automatic;
            if (maxTextureSize > 0) ps.maxTextureSize = maxTextureSize;
            importer.SetPlatformTextureSettings(ps);
        }

        private static void SetPivot(TextureImporter importer, Vector2 pivot)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            importer.SetTextureSettings(settings);
        }

        /// <summary>
        /// Returns the source PNG/JPG dimensions as a Vector2Int (width, height).
        /// Uses TextureImporter.GetSourceTextureWidthAndHeight via reflection-free API
        /// available since Unity 2019.2.
        /// </summary>
        private static Vector2Int GetSourceTextureSize(TextureImporter importer)
        {
            int w = 0, h = 0;
            var mi = typeof(TextureImporter).GetMethod(
                "GetWidthAndHeight",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi != null)
            {
                object[] args = new object[] { w, h };
                mi.Invoke(importer, args);
                w = (int)args[0];
                h = (int)args[1];
            }
            return new Vector2Int(w, h);
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
