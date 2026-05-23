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
            importer.mipmapEnabled = false;
            // Per-folder compression policy. The original "everything
            // Uncompressed" caused ~600 MB of VRAM/disk on Building sprites
            // alone (1024×1536 RGBA × 100 buildings) and explained the
            // 10-15s boot freeze + sustained low FPS. Smart compression:
            //   * Tiles + small pixel art (32px-ish) → Uncompressed
            //     (compression artefacts on tiny pixel art are visible).
            //   * Buildings (1024+px sprites, scaled at runtime) →
            //     CompressedHQ (BC7/DXT5 high quality, ~8× VRAM/disk
            //     savings, no visible quality loss at game zoom).
            // The per-platform override block at the bottom of this method
            // honours the same per-folder rule so Standalone/WebGL/Android/
            // iPhone builds don't silently fall back to the wrong format.
            bool useCompressedHQ = ShouldCompressForCategory(assetPath);
            importer.textureCompression = useCompressedHQ
                ? TextureImporterCompression.CompressedHQ
                : TextureImporterCompression.Uncompressed;
            // Clamp wrap mode prevents the GPU from sampling the opposite
            // edge of the texture when a tile mesh's UV reads land a hair
            // outside the [0,1] range due to camera sub-pixel offsets.
            // Repeat (Unity's default for 2D textures) makes the wrong side
            // of the sprite leak through that 1-pixel UV overshoot and
            // shows up as a 1-px coloured seam between adjacent tiles.
            importer.wrapMode = TextureWrapMode.Clamp;

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
                // PPU is derived from the source size so any square tile fills a
                // cell: 16x16 -> PPU=16, 32x32 -> PPU=32, 64x64 -> PPU=64.
                // This lets low-resolution packs (e.g. SNES 16-px castle art)
                // coexist with the canonical 32-px tiles without a per-folder
                // override — each pack stays at native pixel scale.
                //
                // Anything larger than TILE_MAX_ALLOWED_SIZE is REJECTED with an
                // error: oversized tiles cause catastrophic visual bleeding (one
                // cell rendering as N×N units), historically the "sand patch"
                // overlap bug. Run `Valkur > Tiles > Audit Sizes` (or `python
                // tools/atlas/audit_tile_sizes.py --fix`) to downscale offenders.
                Vector2Int srcSize = GetSourceTextureSize(importer);
                int tileMax = Mathf.Max(srcSize.x, srcSize.y);
                if (tileMax > TILE_MAX_ALLOWED_SIZE)
                {
                    Debug.LogError(
                        $"[ValkurAssetPostprocessor] OVERSIZED TILE: '{assetPath}' is " +
                        $"{srcSize.x}x{srcSize.y} px, exceeds the {TILE_MAX_ALLOWED_SIZE}px " +
                        $"limit. Tiles must be ≤{TILE_MAX_ALLOWED_SIZE}x{TILE_MAX_ALLOWED_SIZE} " +
                        $"to render as a single map cell. Run `Valkur > Tiles > Audit Sizes` to fix.");
                    importer.spritePixelsPerUnit = TILE_PPU;
                }
                else if (tileMax > 0)
                {
                    importer.spritePixelsPerUnit = tileMax;
                }
                else
                {
                    // Source size unavailable (reflection call failed) — fall
                    // back to the canonical 32-px policy.
                    importer.spritePixelsPerUnit = TILE_PPU;
                }
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
            // For pixel-art folders (Tiles, Characters, NPC, Items) we force
            // Uncompressed on every platform because compression artefacts on
            // tiny sprites are the highest-impact visual regression in 2D.
            // For large-format folders (Buildings) we let the per-platform
            // setting use CompressedHQ — saves ~80% disk + VRAM with no
            // visible quality loss at the zoom range Buildings are viewed at.
            //
            // Characters also need maxTextureSize lifted to 8192 — their wide
            // spritesheets (5248×128) get crushed to 2048×50 by the per-platform
            // default, killing per-frame detail before the atlas even runs.
            int platformMaxSize = assetPath.Contains("/Characters/") ? 8192 : 0;
            ApplyPlatformOverride(importer, "Standalone", useCompressedHQ, platformMaxSize);
            ApplyPlatformOverride(importer, "WebGL",      useCompressedHQ, platformMaxSize);
            ApplyPlatformOverride(importer, "Android",    useCompressedHQ, platformMaxSize);
            ApplyPlatformOverride(importer, "iPhone",     useCompressedHQ, platformMaxSize);
        }

        /// <summary>
        /// Returns true when the asset at <paramref name="assetPath"/> should
        /// be CompressedHQ (DXT5/BC7) instead of Uncompressed. The split keeps
        /// pixel-art categories raw while compressing large-format sprites
        /// where 8:1 compression is invisible at typical viewing zoom.
        /// </summary>
        private static bool ShouldCompressForCategory(string assetPath)
        {
            // Building sprites are 1024+ px and scaled at runtime. Compression
            // is invisible at game zoom and saves ~600 MB VRAM project-wide
            // (the main cause of the 10-15s boot freeze + sustained low FPS).
            if (assetPath.Contains("/Buildings/")) return true;
            // Everything else (Tiles, Characters, NPC, Items, UI, …) stays
            // Uncompressed because compression artefacts on small pixel art
            // are immediately visible at any zoom.
            return false;
        }

        // Forces the per-platform texture import block to "Override = true"
        // with the requested compression policy. Idempotent: re-runs are no-ops
        // when settings already match. Pass maxTextureSize > 0 to also override
        // the per-platform max size.
        private static void ApplyPlatformOverride(
            TextureImporter importer,
            string platform,
            bool useCompressedHQ,
            int maxTextureSize = 0)
        {
            var ps = importer.GetPlatformTextureSettings(platform);
            if (ps == null) return;

            var desiredCompression = useCompressedHQ
                ? TextureImporterCompression.CompressedHQ
                : TextureImporterCompression.Uncompressed;

            bool needsMaxSizeChange = maxTextureSize > 0 && ps.maxTextureSize != maxTextureSize;

            // Skip work if already in the desired state.
            if (ps.overridden &&
                ps.textureCompression == desiredCompression &&
                !needsMaxSizeChange)
                return;

            ps.overridden          = true;
            ps.textureCompression  = desiredCompression;
            ps.format              = TextureImporterFormat.Automatic;
            if (maxTextureSize > 0) ps.maxTextureSize = maxTextureSize;
            importer.SetPlatformTextureSettings(ps);
        }

        // Pixel-art tile sprites MUST use FullRect (rectangular mesh that
        // spans the entire sprite rect) with non-zero spriteExtrude (1 px of
        // duplicated edge texels around the rect in the atlas). Together
        // these eliminate the Camera-backgroundColor seam that otherwise
        // bleeds through:
        //   * Tight mesh hugs the visible alpha pixels → adjacent tile
        //     meshes don't meet at the cell boundary and a sub-pixel gap
        //     exposes Camera.backgroundColor (Unity's default dark cyan
        //     blue), producing the textbook "blue horizontal line" seam
        //     reported in #seams.
        //   * spriteExtrude=0 means atlas-packed sprites have zero padding
        //     of duplicated edge texels; Point filter then reads the
        //     neighbouring atlas slot whenever the camera lands sub-pixel.
        // FullRect mesh + extrude=1 is the canonical tile-safe configuration
        // recommended by Unity for Tilemap rendering.
        private const int TILE_SPRITE_EXTRUDE = 1;

        private static void SetPivot(TextureImporter importer, Vector2 pivot)
        {
            ApplySpriteSettings(importer, pivot, SpriteMeshType.FullRect, TILE_SPRITE_EXTRUDE);
        }

        private static void ApplySpriteSettings(
            TextureImporter importer,
            Vector2 pivot,
            SpriteMeshType meshType,
            uint extrudePixels)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = meshType;
            settings.spriteExtrude = extrudePixels;
            importer.SetTextureSettings(settings);
        }

        /// <summary>
        /// Returns the source PNG/JPG dimensions as a Vector2Int (width, height).
        /// On first import the TextureImporter's reflective GetWidthAndHeight
        /// returns (0,0) because Unity hasn't ingested the texture yet, so we
        /// also fall back to parsing the PNG IHDR chunk straight from disk —
        /// guaranteed to work even during the very first OnPreprocessTexture call.
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
            if (w == 0 || h == 0)
            {
                var fromDisk = ReadPngDimensionsFromDisk(importer.assetPath);
                if (fromDisk.x > 0 && fromDisk.y > 0)
                    return fromDisk;
            }
            return new Vector2Int(w, h);
        }

        /// <summary>
        /// Parses the PNG IHDR chunk from disk to extract width/height.
        /// Returns Vector2Int.zero on any read failure.
        /// </summary>
        private static Vector2Int ReadPngDimensionsFromDisk(string assetPath)
        {
            try
            {
                using (var stream = System.IO.File.OpenRead(assetPath))
                using (var reader = new System.IO.BinaryReader(stream))
                {
                    byte[] sig = reader.ReadBytes(8);
                    if (sig.Length < 8 || sig[0] != 0x89 || sig[1] != 'P' ||
                        sig[2] != 'N' || sig[3] != 'G')
                        return Vector2Int.zero;
                    reader.ReadBytes(4); // chunk length
                    byte[] chunkType = reader.ReadBytes(4);
                    if (chunkType.Length < 4 || chunkType[0] != 'I' || chunkType[1] != 'H' ||
                        chunkType[2] != 'D' || chunkType[3] != 'R')
                        return Vector2Int.zero;
                    int w = (reader.ReadByte() << 24) | (reader.ReadByte() << 16) |
                            (reader.ReadByte() << 8) | reader.ReadByte();
                    int h = (reader.ReadByte() << 24) | (reader.ReadByte() << 16) |
                            (reader.ReadByte() << 8) | reader.ReadByte();
                    return new Vector2Int(w, h);
                }
            }
            catch
            {
                return Vector2Int.zero;
            }
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
