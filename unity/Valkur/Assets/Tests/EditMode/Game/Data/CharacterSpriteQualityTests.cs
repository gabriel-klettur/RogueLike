using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Regression guard for the character sprite quality policy.
    ///
    /// Bug history (do not regress):
    ///  - Character spritesheets are ~5248x128 px (up to 41 frames × 128 px).
    ///  - Standalone's per-platform maxTextureSize defaulted to 2048, which
    ///    downsampled each 128x128 frame to approximately 50x50 px before the
    ///    SpriteAtlas even ran. Combined with Point filtering this made the
    ///    wizard (and all other characters) look heavily pixelated/low-detail
    ///    when the camera zoomed in (zoom range 2..25).
    ///  - The fix in ValkurAssetPostprocessor (lines 78-93, 107-145) forces
    ///    maxTextureSize = 8192 and Uncompressed on Default + all four build
    ///    platforms, and keeps FilterMode.Point to match the pixel-art look.
    ///  - These tests assert that policy is still healthy so we never ship
    ///    a build with the downsampling regression again.
    /// </summary>
    public class CharacterSpriteQualityTests
    {
        // ── Constants that must match ValkurAssetPostprocessor ──────────────

        private const int PlayerCharacterPPU   = 64;
        private const int MinMaxTextureSize    = 8192;
        private const string AtlasPath        =
            "Assets/_Project/SpriteAtlases/players.spriteatlas";

        // ── Test-case source ────────────────────────────────────────────────

        private static readonly string[] CharacterPaths =
        {
            "Assets/_Project/Art/Characters/barbarian/barbarian_idle.png",
            "Assets/_Project/Art/Characters/barbarian/barbarian_casting.png",
            "Assets/_Project/Art/Characters/barbarian/barbarian_walking.png",
            "Assets/_Project/Art/Characters/dwarf/dwarf_idle.png",
            "Assets/_Project/Art/Characters/dwarf/dwarf_casting.png",
            "Assets/_Project/Art/Characters/dwarf/dwarf_walking.png",
            "Assets/_Project/Art/Characters/elven/elven_idle.png",
            "Assets/_Project/Art/Characters/elven/elven_casting.png",
            "Assets/_Project/Art/Characters/elven/elven_walking.png",
            "Assets/_Project/Art/Characters/mague/mague_idle.png",
            "Assets/_Project/Art/Characters/mague/mague_casting.png",
            "Assets/_Project/Art/Characters/mague/mague_walking.png",
            "Assets/_Project/Art/Characters/valkyrie/valkyrie_idle.png",
            "Assets/_Project/Art/Characters/valkyrie/valkyrie_casting.png",
            "Assets/_Project/Art/Characters/valkyrie/valkyrie_walking.png",
        };

        private static readonly string[] PlatformTargets =
        {
            "Standalone",
            "WebGL",
            "Android",
            "iPhone",
        };

        // ── Helper: load TextureImporter and assert the asset exists ────────

        private static TextureImporter GetImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.IsNotNull(importer,
                $"TextureImporter not found for '{assetPath}'. " +
                "Ensure the PNG exists at that path and Unity has imported it.");
            return importer;
        }

        /// <summary>
        /// Mirrors ValkurAssetPostprocessor.GetSourceTextureSize — reads the
        /// raw PNG dimensions via private reflection API available since Unity 2019.2.
        /// </summary>
        private static Vector2Int GetSourceTextureSize(TextureImporter importer)
        {
            int w = 0, h = 0;
            var mi = typeof(TextureImporter).GetMethod(
                "GetWidthAndHeight",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi != null)
            {
                object[] args = { w, h };
                mi.Invoke(importer, args);
                w = (int)args[0];
                h = (int)args[1];
            }
            return new Vector2Int(w, h);
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 1 — Default-platform TextureImporter settings
        // ────────────────────────────────────────────────────────────────────

        [TestCaseSource(nameof(CharacterPaths))]
        public void CharacterPNG_FilterMode_IsPoint(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point),
                $"'{assetPath}': filterMode must be Point. " +
                "Bilinear blurs pixels when the camera zooms in (zoom range 2..25), " +
                "ruining the pixel-art look that tiles, NPCs, and items share.");
        }

        [TestCaseSource(nameof(CharacterPaths))]
        public void CharacterPNG_PixelsPerUnit_Is64(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(PlayerCharacterPPU),
                $"'{assetPath}': spritePixelsPerUnit must be {PlayerCharacterPPU} " +
                "(PLAYER_CHARACTER_PPU constant in ValkurAssetPostprocessor). " +
                "128 px native ÷ 64 PPU = 2 world units = 2 game tiles.");
        }

        [TestCaseSource(nameof(CharacterPaths))]
        public void CharacterPNG_DefaultPlatform_IsUncompressed(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Assert.That(importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed),
                $"'{assetPath}': Default platform textureCompression must be Uncompressed. " +
                "Compression artifacts on pixel art are the highest-impact visual regression " +
                "in Valkur 2D.");
        }

        [TestCaseSource(nameof(CharacterPaths))]
        public void CharacterPNG_MipmapEnabled_IsFalse(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Assert.That(importer.mipmapEnabled, Is.False,
                $"'{assetPath}': mipmapEnabled must be false. " +
                "Mipmaps on pixel art produce blurry lower-resolution mips at zoom-out.");
        }

        [TestCaseSource(nameof(CharacterPaths))]
        public void CharacterPNG_DefaultPlatform_MaxTextureSizeAtLeast8192(string assetPath)
        {
            var importer = GetImporter(assetPath);
            // importer.maxTextureSize maps to the Default platform block written by
            // ValkurAssetPostprocessor line 91 (importer.maxTextureSize = 8192).
            Assert.That(importer.maxTextureSize, Is.GreaterThanOrEqualTo(MinMaxTextureSize),
                $"'{assetPath}': Default platform maxTextureSize must be >= {MinMaxTextureSize}. " +
                $"Character spritesheets are up to 5248x128 px; anything smaller causes " +
                $"downsampling that makes each 128x128 frame look blocky at zoom.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 2 — Per-platform overrides (Standalone / WebGL / Android / iPhone)
        // ────────────────────────────────────────────────────────────────────

        private static IEnumerable<TestCaseData> PlatformCases()
        {
            foreach (var path in CharacterPaths)
                foreach (var platform in PlatformTargets)
                    yield return new TestCaseData(path, platform)
                        .SetName($"{System.IO.Path.GetFileNameWithoutExtension(path)}_{platform}");
        }

        [TestCaseSource(nameof(PlatformCases))]
        public void CharacterPNG_PlatformOverride_IsEnabled(string assetPath, string platform)
        {
            var importer = GetImporter(assetPath);
            var ps = importer.GetPlatformTextureSettings(platform);
            Assert.IsNotNull(ps,
                $"'{assetPath}': GetPlatformTextureSettings('{platform}') returned null. " +
                "The platform block must exist.");
            Assert.That(ps.overridden, Is.True,
                $"'{assetPath}' platform '{platform}': overridden must be true. " +
                "Without an explicit override, the platform inherits its own default " +
                "maxTextureSize (often 2048) and textureCompression, ignoring the " +
                "Default-platform setting written by ValkurAssetPostprocessor.");
        }

        [TestCaseSource(nameof(PlatformCases))]
        public void CharacterPNG_PlatformOverride_IsUncompressed(string assetPath, string platform)
        {
            var importer = GetImporter(assetPath);
            var ps = importer.GetPlatformTextureSettings(platform);
            Assert.IsNotNull(ps,
                $"'{assetPath}': GetPlatformTextureSettings('{platform}') returned null.");
            Assert.That(ps.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed),
                $"'{assetPath}' platform '{platform}': textureCompression must be Uncompressed. " +
                "Standalone, WebGL, Android, and iPhone default to compressed formats " +
                "regardless of the Default-platform block.");
        }

        [TestCaseSource(nameof(PlatformCases))]
        public void CharacterPNG_PlatformOverride_MaxTextureSizeAtLeast8192(string assetPath, string platform)
        {
            var importer = GetImporter(assetPath);
            var ps = importer.GetPlatformTextureSettings(platform);
            Assert.IsNotNull(ps,
                $"'{assetPath}': GetPlatformTextureSettings('{platform}') returned null.");
            Assert.That(ps.maxTextureSize, Is.GreaterThanOrEqualTo(MinMaxTextureSize),
                $"'{assetPath}' platform '{platform}': maxTextureSize must be >= {MinMaxTextureSize}. " +
                $"This is the root cause of the original bug: Standalone's per-platform " +
                $"maxTextureSize was 2048, which downsampled the 5248x128 walking strip to " +
                $"~800x50 px, making each 128x128 frame render as ~50x50 — heavily blocky " +
                $"at zoom. Fixed in ValkurAssetPostprocessor line 91.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 3 — Loaded texture dimensions match source PNG (no downsampling)
        // ────────────────────────────────────────────────────────────────────

        [TestCaseSource(nameof(CharacterPaths))]
        public void CharacterPNG_LoadedTexture_MatchesSourceDimensions(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Vector2Int srcSize = GetSourceTextureSize(importer);

            if (srcSize.x == 0 || srcSize.y == 0)
            {
                Assert.Inconclusive(
                    $"'{assetPath}': Could not read source dimensions via GetWidthAndHeight " +
                    "reflection. Skipping loaded-texture size check.");
                return;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            Assert.IsNotNull(texture,
                $"'{assetPath}': Failed to load Texture2D from AssetDatabase.");

            Assert.That(texture.width, Is.EqualTo(srcSize.x),
                $"'{assetPath}': loaded Texture2D.width ({texture.width}) != source PNG width ({srcSize.x}). " +
                "The texture is being downsampled — check maxTextureSize settings on all platforms.");
            Assert.That(texture.height, Is.EqualTo(srcSize.y),
                $"'{assetPath}': loaded Texture2D.height ({texture.height}) != source PNG height ({srcSize.y}). " +
                "The texture is being downsampled — check maxTextureSize settings on all platforms.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 4a — SpriteAtlas editor filterMode is Point
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void SpriteAtlas_EditorTextureSettings_FilterModeIsPoint()
        {
            // SpriteAtlas has no direct C# API for editor texture settings.
            // We read the serialized property via SerializedObject, which maps
            // to the YAML field m_EditorData.textureSettings.filterMode.
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            Assert.IsNotNull(atlas,
                $"SpriteAtlas not found at '{AtlasPath}'. " +
                "Verify the atlas exists and is imported.");

            var so = new SerializedObject(atlas);
            // Path in the serialized representation:
            // m_EditorData -> textureSettings -> filterMode
            var filterModeProp = so.FindProperty("m_EditorData.textureSettings.filterMode");
            Assert.IsNotNull(filterModeProp,
                "Could not find serialized property 'm_EditorData.textureSettings.filterMode' " +
                "on the SpriteAtlas. Unity may have changed the internal YAML layout.");

            // FilterMode.Point == 0 in the Unity enum
            Assert.That(filterModeProp.intValue, Is.EqualTo(0),
                $"SpriteAtlas '{AtlasPath}': editor textureSettings.filterMode must be 0 (Point). " +
                "A non-Point (e.g. Bilinear=1) filter on the atlas texture blurs all packed " +
                "sprites when rendered, undoing the per-importer Point filter setting.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 4b — All packed sprites are 128×128 px (no atlas downsampling)
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void SpriteAtlas_AllPackedSprites_AreAtLeast128x128()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            Assert.IsNotNull(atlas, $"SpriteAtlas not found at '{AtlasPath}'.");

            var sprites = new Sprite[atlas.spriteCount];
            atlas.GetSprites(sprites);

            Assert.That(sprites.Length, Is.GreaterThan(0),
                $"SpriteAtlas '{AtlasPath}' contains no sprites. " +
                "The atlas may not be packed yet — pack it in the Sprite Atlas editor.");

            var failures = new List<string>();
            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                if (sprite.rect.width < 128f || sprite.rect.height < 128f)
                {
                    failures.Add(
                        $"  '{sprite.name}': {sprite.rect.width}x{sprite.rect.height} px " +
                        "(expected >= 128x128)");
                }
            }

            Assert.That(failures.Count, Is.EqualTo(0),
                $"SpriteAtlas '{AtlasPath}': {failures.Count} sprite(s) have dimensions " +
                $"below 128x128, indicating atlas-level downsampling:\n" +
                string.Join("\n", failures));
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 4c — All packed sprites have pixelsPerUnit == 64
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void SpriteAtlas_AllPackedSprites_HavePixelsPerUnit64()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            Assert.IsNotNull(atlas, $"SpriteAtlas not found at '{AtlasPath}'.");

            var sprites = new Sprite[atlas.spriteCount];
            atlas.GetSprites(sprites);

            var failures = new List<string>();
            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                if (!Mathf.Approximately(sprite.pixelsPerUnit, PlayerCharacterPPU))
                {
                    failures.Add(
                        $"  '{sprite.name}': pixelsPerUnit = {sprite.pixelsPerUnit} " +
                        $"(expected {PlayerCharacterPPU})");
                }
            }

            Assert.That(failures.Count, Is.EqualTo(0),
                $"SpriteAtlas '{AtlasPath}': {failures.Count} sprite(s) have wrong pixelsPerUnit:\n" +
                string.Join("\n", failures) +
                $"\nAll character sprites must be {PlayerCharacterPPU} PPU " +
                "(128 px native ÷ 64 PPU = 2 world units = 2 game tiles).");
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 4d — Runtime atlas texture filterMode is Point
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void SpriteAtlas_RuntimeTexture_FilterModeIsPoint()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            Assert.IsNotNull(atlas, $"SpriteAtlas not found at '{AtlasPath}'.");

            var sprites = new Sprite[atlas.spriteCount];
            atlas.GetSprites(sprites);

            // Find the first valid sprite with an accessible texture
            Texture sampleTexture = null;
            string sampleName = null;
            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                // In EditMode the atlas texture may not be baked yet;
                // sprite.texture returns the source texture in that case.
                var tex = sprite.texture;
                if (tex != null)
                {
                    sampleTexture = tex;
                    sampleName = sprite.name;
                    break;
                }
            }

            if (sampleTexture == null)
            {
                Assert.Inconclusive(
                    $"SpriteAtlas '{AtlasPath}': could not sample a runtime texture from any " +
                    "packed sprite (atlas may need to be packed). Skipping filterMode check.");
                return;
            }

            Assert.That(sampleTexture.filterMode, Is.EqualTo(FilterMode.Point),
                $"SpriteAtlas '{AtlasPath}': texture sampled from sprite '{sampleName}' " +
                $"has filterMode={sampleTexture.filterMode}; expected Point. " +
                "A Bilinear/Trilinear texture filter blurs the packed sprites at any zoom level, " +
                "directly undoing the per-PNG Point filter setting.");
        }
    }
}
