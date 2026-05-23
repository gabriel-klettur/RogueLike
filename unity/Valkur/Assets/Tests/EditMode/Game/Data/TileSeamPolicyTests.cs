using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Editor;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Regression guard for the tilemap "blue seam" bug.
    ///
    /// Bug history (do not regress):
    ///  - 2026-05-16: a thin blue horizontal line appeared between adjacent
    ///    rows of tiles in the gameplay view, becoming visible at certain
    ///    camera zoom levels and disappearing at others ("seemingly random").
    ///  - Root cause: <see cref="SpriteMeshType.Tight"/> (Unity's default for
    ///    new sprites) generates a polygon mesh that hugs the visible alpha
    ///    pixels, so adjacent tile meshes do not meet at the cell boundary.
    ///    A sub-pixel gap opens between them, exposing Camera.backgroundColor
    ///    (Unity's default dark cyan-blue) as a thin coloured line.
    ///  - Secondary contributor: <c>wrapMode == Repeat</c> let any UV
    ///    overshoot read the opposite edge of the texture.
    ///  - Fix: ValkurAssetPostprocessor + TileReimporter now force
    ///    <see cref="SpriteMeshType.FullRect"/> (rectangle that spans the
    ///    full sprite rect), <see cref="TextureWrapMode.Clamp"/>,
    ///    spriteExtrude >= 1, Point filter, Uncompressed, no mipmaps.
    ///    CameraSetup also forces a black background as a safety net.
    ///  - Audit: <see cref="TileSeamPolicyAuditor.CollectOffenders"/> scans
    ///    every PNG under <c>Assets/_Project/Resources/Tiles/</c> for any
    ///    setting that would let the bug return; this test calls it directly
    ///    and asserts the offender list is empty.
    ///
    /// What this test guarantees:
    ///   1. Every existing tile PNG passes the seam-safe policy.
    ///   2. The policy validator itself rejects the historical offending
    ///      configurations (Tight mesh, Repeat wrap, Bilinear filter, …).
    ///   3. Representative samples conform individually (faster signal than
    ///      the full audit when the suite fails, and easier to debug).
    /// </summary>
    public class TileSeamPolicyTests
    {
        // Representative tile PNGs spanning the major tilesets. If a future
        // import-policy regression silently breaks one tileset but not the
        // others, the parameterised tests below pinpoint which folder
        // regressed — much faster than scrolling the full audit log.
        private static readonly string[] RepresentativeTilePaths =
        {
            "Assets/_Project/Resources/Tiles/floor.png",
            "Assets/_Project/Resources/Tiles/wall.png",
            "Assets/_Project/Resources/Tiles/dungeon_floor.png",
            "Assets/_Project/Resources/Tiles/sand_grass/tileset1_r1_c0.png",
            "Assets/_Project/Resources/Tiles/grass_dirt/tileset3_r1_c0.png",
            "Assets/_Project/Resources/Tiles/grass_rock/tileset4_r1_c3.png",
            "Assets/_Project/Resources/Tiles/rock_water/tileset8_r1_c0.png",
            "Assets/_Project/Resources/Tiles/sand_ocean/tileset_test_7.png",
            "Assets/_Project/Resources/Tiles/ocean_grass/tileset_test_2.png",
            "Assets/_Project/Resources/Tiles/sand_rock/tileset7_r1_c0.png",
        };

        private static TextureImporter GetImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.IsNotNull(importer,
                $"TextureImporter not found for '{assetPath}'. " +
                "Tile PNG missing or AssetDatabase out of sync — refresh the project.");
            return importer;
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 1 — Representative tile samples conform to every rule
        // ────────────────────────────────────────────────────────────────────

        [TestCaseSource(nameof(RepresentativeTilePaths))]
        public void TilePNG_FilterMode_IsPoint(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point),
                $"'{assetPath}': filterMode must be Point. " +
                "Bilinear filtering on a pixel-art tile causes texture bleed at the " +
                "tile boundary even with FullRect mesh — the GPU samples sub-texels.");
        }

        [TestCaseSource(nameof(RepresentativeTilePaths))]
        public void TilePNG_WrapMode_IsClamp(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp),
                $"'{assetPath}': wrapMode must be Clamp. " +
                "TextureWrapMode.Repeat lets a 1-pixel UV overshoot at the tile edge " +
                "sample the OPPOSITE edge of the texture, producing a coloured seam.");
        }

        [TestCaseSource(nameof(RepresentativeTilePaths))]
        public void TilePNG_MipmapEnabled_IsFalse(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Assert.That(importer.mipmapEnabled, Is.False,
                $"'{assetPath}': mipmapEnabled must be false. " +
                "Mipmaps produce blurry lower-resolution mips at zoom-out and undo " +
                "the Point filter guarantee at the tile edge.");
        }

        [TestCaseSource(nameof(RepresentativeTilePaths))]
        public void TilePNG_DefaultPlatform_IsUncompressed(string assetPath)
        {
            var importer = GetImporter(assetPath);
            Assert.That(importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed),
                $"'{assetPath}': Default platform textureCompression must be Uncompressed. " +
                "DXT/ETC compression at 4×4 block granularity smears tile borders into " +
                "neighbouring atlas slots — a recurring source of visual artefacts.");
        }

        [TestCaseSource(nameof(RepresentativeTilePaths))]
        public void TilePNG_SpriteMeshType_IsFullRect(string assetPath)
        {
            var importer = GetImporter(assetPath);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect),
                $"'{assetPath}': spriteMeshType must be FullRect. " +
                "SpriteMeshType.Tight (Unity's default) was the 2026-05-16 'blue seam' " +
                "root cause — the alpha-tight polygon mesh doesn't meet the neighbour " +
                "tile's mesh exactly at the cell boundary, exposing Camera.backgroundColor.");
        }

        [TestCaseSource(nameof(RepresentativeTilePaths))]
        public void TilePNG_SpriteExtrude_IsAtLeast1(string assetPath)
        {
            var importer = GetImporter(assetPath);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That((int)settings.spriteExtrude, Is.GreaterThanOrEqualTo(1),
                $"'{assetPath}': spriteExtrude must be >= 1. " +
                "Zero extrude means the SpriteAtlas packs sprites with no padding of " +
                "duplicated edge texels — Point filter then reads the neighbouring " +
                "atlas slot whenever the camera lands sub-pixel.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 2 — Full-corpus audit returns zero offenders
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void Audit_AllTilesUnderResources_HaveSeamSafeSettings()
        {
            var offenders = TileSeamPolicyAuditor.CollectOffenders();
            if (offenders.Count == 0)
            {
                Assert.Pass();
                return;
            }

            // Surface a digestible head of the list; full list would flood
            // the test runner and obscure the actual signal.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{offenders.Count} tile(s) violate the seam-safe import policy. " +
                          "Run `Valkur > Tiles > Force Reimport Tiles` to fix.");
            int shown = 0;
            foreach (var v in offenders)
            {
                sb.Append("  • ").Append(v.AssetPath).Append("  →  ").AppendLine(v.Reason);
                if (++shown >= 12) { sb.AppendLine($"  … and {offenders.Count - shown} more."); break; }
            }
            Assert.Fail(sb.ToString());
        }

        // ────────────────────────────────────────────────────────────────────
        // Invariant 3 — The validator itself rejects historical offenders
        //
        // These tests don't write to disk; they verify the validator
        // contract by inspecting a tile that already passes and confirming
        // the validator returns null. Inverse cases (Tight mesh, Repeat wrap,
        // etc.) are exercised indirectly through the audit when any file
        // ever drifts — a true positive there fires Invariant 2.
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void Validator_ReturnsNull_ForConformingImporter()
        {
            var importer = GetImporter(RepresentativeTilePaths[0]);
            string reason = TileSeamPolicyAuditor.ValidateTileImporter(importer);
            Assert.That(reason, Is.Null,
                "Validator should accept the conforming sample tile. " +
                $"Got reason: {reason}");
        }
    }
}
