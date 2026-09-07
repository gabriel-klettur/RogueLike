using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
// The PPU invariants below assert against the same source of truth the postprocessor reads,
// so a stale .meta is a red test rather than a silently resized character.
using Valkur.Editor;

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

        private const string CharactersRoot = "Assets/_Project/Art/Characters";

        /// <summary>
        /// The multi-frame strips still produced by the LEGACY player pipeline
        /// (<c>PlayerCharacterAssetBinder</c>): one 5120x128 PNG per state, sliced into
        /// 8 directions x 5 frames of 128 px.
        ///
        /// dwarf, barbarian and elven used to be here too. They are now built by
        /// <c>tools/atlas/wave3/build_player_frames.py</c> as one tightly-cropped PNG per
        /// frame and bound by <c>PlayerFramesImporter</c>, so they have no strip to name -
        /// the per-file invariants below (single sprite mode, 128 px cells) do not apply to
        /// them, while the folder-wide ones (PPU, filter, no downsampling, atlas membership)
        /// still do and are asserted over <see cref="CharactersRoot"/> instead.
        /// </summary>
        private static readonly string[] CharacterPaths =
        {
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

        /// <summary>
        /// A character sprite imports at the PPU its own character DECLARES, which for every
        /// character but one is the shared 64.
        ///
        /// <para>This asserted a flat 64 and was RIGHT to break. PPU is half of a pair: the
        /// frame builder's baked pixel height buys DETAIL, and the RATIO of the two is the
        /// character's world height. A character who must be taller or sharper than the roster
        /// moves both - the vampire keeps 256 px of her 331-681 px source cells and divides by
        /// 96 to stand 2.667 units against the dwarf's 1.797.</para>
        ///
        /// <para>Reading <see cref="CharacterSpritePpu"/> is STRONGER than the flat constant,
        /// not weaker, and that is the point of doing it this way: the postprocessor and this
        /// test now answer from the same manifest, so a .meta left stale by a rebuild - a
        /// texture still carrying 64 after its character moved to 96, which silently resizes
        /// her and every collider EntityColliderConfigurator derives from renderer.bounds - is
        /// a red test. The old assertion could not see that, because 64 was what it wanted.</para>
        ///
        /// <para>What it deliberately does NOT do is accept any PPU at all: a character with no
        /// entry must still be exactly <see cref="PlayerCharacterPPU"/>.</para>
        /// </summary>
        [TestCaseSource(nameof(CharacterPaths))]
        public void CharacterPNG_PixelsPerUnit_MatchesItsDeclaration(string assetPath)
        {
            var importer = GetImporter(assetPath);
            float declared = CharacterSpritePpu.PpuFor(assetPath);
            float expected = declared > 0f ? declared : PlayerCharacterPPU;
            string source = declared > 0f
                ? "declared for this character under characterPpu in " +
                  "tools/atlas/generated/player_frames_manifest*.json"
                : "PLAYER_CHARACTER_PPU in ValkurAssetPostprocessor - the shared default for a " +
                  "character that declares none";

            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(expected),
                $"'{assetPath}': spritePixelsPerUnit must be {expected}, {source}. " +
                "Baked pixel height is detail and pixelHeight/PPU is world height, so a wrong " +
                "PPU here resizes the character and her collider without anything failing. If " +
                "the manifest was just rebuilt, reimport Art/Characters/ so the postprocessor " +
                "runs over it again.");
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
        public void CharacterTextures_ImportAtTheirFullSourceResolution()
        {
            // What this actually guards is DOWNSAMPLING: the original bug was
            // maxTextureSize capping a 5120x128 walking strip to 2048, which silently
            // dropped 24 of its 40 frames and shrank the rest.
            //
            // It used to be asserted as "every packed sprite is at least 128x128", which
            // worked only while every character sheet was a grid of 128x128 cells. The
            // wave3 characters (dwarf, barbarian, elven) are built by
            // tools/atlas/wave3/build_player_frames.py as one tightly-cropped PNG per
            // frame, so their sizes are whatever the pose needs - 43x115 for the elf's
            // idle, 180x117 for the dwarf's death. Under the old assertion 330 correctly
            // imported sprites failed for being narrow, which is not what the test is
            // about; a 43px-wide idle is not a downsampled 128px one.
            //
            // So compare the imported texture against the source file directly. That is
            // the real invariant, it holds for both sheet layouts, and it fails loudly on
            // exactly the capping bug that motivated the original test.
            var failures = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CharactersRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (importer == null || texture == null) continue;

                importer.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);
                if (srcW <= 0 || srcH <= 0) continue;

                if (texture.width != srcW || texture.height != srcH)
                {
                    failures.Add(
                        $"  '{path}': imported {texture.width}x{texture.height} from a " +
                        $"{srcW}x{srcH} source (maxTextureSize is {importer.maxTextureSize})");
                }
            }

            Assert.That(failures.Count, Is.EqualTo(0),
                $"{failures.Count} character texture(s) were downsampled on import. Raise " +
                "maxTextureSize for them (ValkurAssetPostprocessor forces 8192 under " +
                "Art/Characters/ for this reason):\n" + string.Join("\n", failures));
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 4c - Every packed sprite carries its character's declared PPU
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The atlas-side half of <see cref="CharacterPNG_PixelsPerUnit_MatchesItsDeclaration"/>,
        /// and not redundant with it: the importer setting is what was ASKED FOR, the packed
        /// sprite is what SHIPPED. A sprite packed before its character's PPU changed keeps the
        /// old value until the atlas is rebuilt, and nothing announces that.
        ///
        /// <para>TWO TRAPS were live in the first version of this and both made it VACUOUS,
        /// which is worse than absent because it reports coverage it does not have.</para>
        ///
        /// <para>First, a packed sprite is a CLONE: <c>AssetDatabase.GetAssetPath</c> returns
        /// empty for all 1016 of them, measured. Resolving a character from the path therefore
        /// skipped every sprite. The character is recovered from the sprite's NAME instead,
        /// against the real folder list, longest match first — so `vampire` never claims a
        /// hypothetical `vampire_lord`'s frames.</para>
        ///
        /// <para>Second, this fixture's atlas constant is <c>players.spriteatlas</c>, and the
        /// characters who do not import at the shared PPU are not in it: FOUR character
        /// folders are claimed by that atlas and <c>SpriteAtlasBuilder</c> hands the REMAINDER
        /// to <c>characters.spriteatlas</c>, today the vampire and the mague — the two baked
        /// at 256 px / PPU 96 rather than the shared 115 / 64. Checking one atlas would have
        /// been green for exactly the characters it needed to see. Both are walked, and a
        /// missing one is a failure rather than a skip.</para>
        /// </summary>
        [Test]
        public void SpriteAtlas_AllPackedSprites_HaveTheirDeclaredPixelsPerUnit()
        {
            // Every character folder, longest name first, so a key that is a prefix of another
            // never wins the match.
            var folders = new List<string>();
            foreach (string dir in System.IO.Directory.GetDirectories(
                         System.IO.Path.GetFullPath(System.IO.Path.Combine(
                             Application.dataPath, "_Project", "Art", "Characters"))))
            {
                folders.Add(System.IO.Path.GetFileName(dir));
            }
            folders.Sort((a, b) => b.Length.CompareTo(a.Length));
            Assert.IsNotEmpty(folders, "No character folders under Art/Characters.");

            string[] atlasPaths =
            {
                AtlasPath,
                "Assets/_Project/SpriteAtlases/characters.spriteatlas",
            };

            var failures = new List<string>();
            int inspected = 0, unattributed = 0;

            foreach (string atlasPath in atlasPaths)
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                Assert.IsNotNull(atlas,
                    $"SpriteAtlas not found at '{atlasPath}'. Both character atlases must exist: " +
                    "players.spriteatlas claims the shared-budget character folders and " +
                    "characters.spriteatlas takes the remainder, so checking only one leaves " +
                    "characters uncovered.");

                var sprites = new Sprite[atlas.spriteCount];
                atlas.GetSprites(sprites);

                foreach (var sprite in sprites)
                {
                    if (sprite == null) continue;

                    // "vampire_idle_e0(Clone)" -> "vampire_idle_e0" -> folder "vampire".
                    string name = sprite.name.Replace("(Clone)", string.Empty);
                    string key = null;
                    foreach (string folder in folders)
                    {
                        if (name.StartsWith(folder + "_", System.StringComparison.Ordinal))
                        { key = folder; break; }
                    }
                    if (key == null) { unattributed++; continue; }

                    inspected++;
                    float declared = CharacterSpritePpu.PpuFor(
                        $"{CharactersRoot}/{key}/x/{name}.png");
                    float expected = declared > 0f ? declared : PlayerCharacterPPU;

                    if (!Mathf.Approximately(sprite.pixelsPerUnit, expected))
                    {
                        failures.Add(
                            $"  '{sprite.name}' (character '{key}', in " +
                            $"{System.IO.Path.GetFileName(atlasPath)}): pixelsPerUnit = " +
                            $"{sprite.pixelsPerUnit}, expected {expected}");
                    }
                }
            }

            Assert.That(failures.Count, Is.EqualTo(0),
                $"{failures.Count} packed sprite(s) have the wrong pixelsPerUnit:\n" +
                string.Join("\n", failures) +
                "\n\nEach character sprite must carry the PPU its character declares under " +
                "characterPpu in tools/atlas/generated/player_frames_manifest*.json, or " +
                $"{PlayerCharacterPPU} when it declares none. A mismatch here on top of a correct " +
                "importer setting means the ATLAS is stale, not the import - re-run " +
                "Valkur > Assets > Build Sprite Atlases.");

            // The guard that stops this going quietly vacuous again. Both previous ways of
            // failing left `inspected` at zero and every assertion above trivially true.
            Assert.That(inspected, Is.GreaterThan(0),
                $"Attributed no packed sprite to a character folder ({unattributed} unattributed), " +
                "so this test asserted nothing. Packed sprites are clones with no asset path, so " +
                "they are matched by NAME against the folders under Art/Characters - a rename on " +
                "either side breaks that silently.");
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
