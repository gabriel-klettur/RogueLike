using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Regression guard for the asset organization conventions documented in
    /// <c>.github/skills/asset-pipeline/SKILL.md</c>. Mirrors the rules in
    /// <c>tools/atlas/audit_asset_conventions.py</c> so violations are caught
    /// from both Python (CI / pre-commit) and Unity (every test pass).
    ///
    /// The "hard" rules below MUST stay at zero violations — those categories
    /// are the ones already clean as of Stage 6 of the asset saneamiento and
    /// any future regression should fail the build immediately.
    ///
    /// The "soft" rules emit a Debug.LogWarning with a count so the legacy
    /// ~280-violation backlog stays visible in CI without spuriously failing
    /// the suite. As Stages 7+ burn that backlog down, the corresponding
    /// soft rules will be promoted to hard ones (move them into the
    /// <see cref="HardRules"/> section).
    /// </summary>
    public class AssetConventionsTests
    {
        // ── Top-level whitelist (mirrors audit_asset_conventions.py) ────────

        private static readonly HashSet<string> AssetsRootAllowed = new(StringComparer.OrdinalIgnoreCase)
        {
            "_Project", "Tests", "Settings", "Scenes", "Screenshots",
            "Resources", "StreamingAssets", "TextMesh Pro",
            "InputSystem_Actions.inputactions",
            "UniversalRenderPipelineGlobalSettings.asset",
        };

        private static readonly HashSet<string> ResourcesRootAllowedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "AudioCatalog.asset", "SlashVfxCatalog.asset", "TileCatalog.asset",
        };

        private static readonly HashSet<string> ResourcesRootAllowedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Buildings", "Catalogs", "Input", "Placeholders", "Spells", "Tiles", "UI",
        };

        // Folders that opt out of the snake_case-below-PascalCase rule:
        // vendor drops (preserve original) and catalog buckets (loaded by
        // string name via Resources.Load).
        private static readonly string[] InternalConventionWhitelistPrefixes =
        {
            "_Project/Art/VFX/Vendor/",
            "_Project/Audio/Vendor/",
            "_Project/Data/Backups/",
            "_Project/Data/Catalogs/",
            "_Project/Data/Vendor/",
            "_Project/Resources/",            // own dedicated rules below
            "TextMesh Pro/",                  // third-party package
            "Tests/",                         // tests sometimes need scaffolds
            "Settings/",                      // Unity-required settings asset names
            "Scenes/",                        // bootstrap scenes
        };

        // ── Patterns ────────────────────────────────────────────────────────

        private static readonly Regex UppercaseExtRe   = new(@"\.(PNG|JPG|JPEG|OGG|WAV|MP3|TIF|TIFF|BMP|GIF)$", RegexOptions.Compiled);
        private static readonly Regex ToolingTempRe    = new(@"^(ChatGPT[\s_]|screenshot[_-]|untitled([._-]|$))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex IterationSuffixRe = new(@"_(old|copy|new|final|v\d+|tmp)\.[a-z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ForbiddenCharsRe  = new(@"[(),']", RegexOptions.Compiled);
        private static readonly Regex InitTestSceneRe   = new(@"^InitTestScene\d+\.unity(\.meta)?$", RegexOptions.Compiled);
        private static readonly Regex BackupFolderRe    = new(@"^(_?backups?|OLD|.+_old)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string AssetsPath => Application.dataPath; // .../Valkur/Assets

        private static string Rel(string fullPath) =>
            fullPath.Substring(AssetsPath.Length).TrimStart('/', '\\').Replace('\\', '/');

        private static bool IsInInternalWhitelist(string relPath) =>
            InternalConventionWhitelistPrefixes.Any(p => relPath.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        private static IEnumerable<string> EnumerateAssets()
        {
            return Directory.EnumerateFiles(AssetsPath, "*", SearchOption.AllDirectories);
        }

        private static IEnumerable<string> EnumerateAssetFolders()
        {
            return Directory.EnumerateDirectories(AssetsPath, "*", SearchOption.AllDirectories);
        }

        // ── Hard rules (must stay at zero) ──────────────────────────────────

        [Test]
        public void HardRules_AssetsRoot_OnlyContainsWhitelistedEntries()
        {
            var offenders = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(AssetsPath))
            {
                string name = Path.GetFileName(entry);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetsRootAllowed.Contains(name)) continue;
                if (InitTestSceneRe.IsMatch(name)) continue; // its own dedicated test
                offenders.Add(name);
            }
            Assert.That(offenders, Is.Empty,
                "Assets/ root must contain only whitelisted entries. Loose entries belong under Assets/_Project/.\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        [Test]
        public void HardRules_NoInitTestScenesCommitted()
        {
            var offenders = Directory.EnumerateFiles(AssetsPath, "InitTestScene*.unity",
                                                     SearchOption.TopDirectoryOnly).ToList();
            Assert.That(offenders, Is.Empty,
                "InitTestScene*.unity files are Unity Test Runner artifacts; they're already in .gitignore\n" +
                "and must never be committed. Run: git rm <files> and delete them from disk.");
        }

        [Test]
        public void HardRules_NoBackupFolders_OutsideWhitelistedDataBackups()
        {
            var offenders = new List<string>();
            foreach (var dir in EnumerateAssetFolders())
            {
                string name = Path.GetFileName(dir);
                if (!BackupFolderRe.IsMatch(name)) continue;
                string rel = Rel(dir);
                if (rel.StartsWith("_Project/Data/Backups", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.StartsWith("_Project/Scripts/", StringComparison.OrdinalIgnoreCase)) continue; // C# code namespace
                offenders.Add(rel);
            }
            Assert.That(offenders, Is.Empty,
                "Backup folders are forbidden inside Assets/ — git is the backup. Whitelist exceptions: _Project/Data/Backups/ and _Project/Scripts/**/Backups/.\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        [Test]
        public void HardRules_ResourcesRoot_OnlyContainsWhitelistedEntries()
        {
            string resRoot = Path.Combine(AssetsPath, "_Project", "Resources");
            if (!Directory.Exists(resRoot))
            {
                Assert.Pass("Resources/ does not exist.");
                return;
            }
            var offenders = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(resRoot))
            {
                string name = Path.GetFileName(entry);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                bool isDir = Directory.Exists(entry);
                bool ok = isDir
                    ? ResourcesRootAllowedFolders.Contains(name)
                    : ResourcesRootAllowedFiles.Contains(name);
                if (!ok) offenders.Add(name + (isDir ? "/" : ""));
            }
            Assert.That(offenders, Is.Empty,
                "Resources/ ships whole into the build — root must contain only whitelisted catalog SOs and known subfolders.\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        [Test]
        public void HardRules_NoUppercaseExtensions()
        {
            var offenders = new List<string>();
            foreach (var path in EnumerateAssets())
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (!UppercaseExtRe.IsMatch(name)) continue;
                string rel = Rel(path);
                // Vendor packs preserve their original drop (frequently mixed case).
                if (IsInInternalWhitelist(rel)) continue;
                offenders.Add(rel);
            }
            Assert.That(offenders, Is.Empty,
                "Uppercase extensions break case-sensitive filesystems (Linux/macOS).\n" +
                "Rename to lowercase. Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        // ── Soft rules (report counts only — promote to hard once cleaned) ──

        [Test]
        public void SoftRules_ReportLegacyBacklog()
        {
            int toolingTemp = 0, iterationSuffix = 0, forbiddenChars = 0, filenameSpaces = 0, folderSpaces = 0;

            foreach (var path in EnumerateAssets())
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = Rel(path);
                if (IsInInternalWhitelist(rel)) continue;

                if (ToolingTempRe.IsMatch(name)) toolingTemp++;
                if (IterationSuffixRe.IsMatch(name)) iterationSuffix++;
                if (ForbiddenCharsRe.IsMatch(name)) forbiddenChars++;
                if (name.Contains(" ")) filenameSpaces++;
            }

            foreach (var dir in EnumerateAssetFolders())
            {
                string rel = Rel(dir);
                if (IsInInternalWhitelist(rel)) continue;
                if (Path.GetFileName(dir).Contains(" ")) folderSpaces++;
            }

            int total = toolingTemp + iterationSuffix + forbiddenChars + filenameSpaces + folderSpaces;
            if (total == 0)
            {
                Assert.Pass("No soft-rule violations remain — promote these checks to hard rules.");
            }
            else
            {
                Debug.LogWarning(
                    "[AssetConventions] Soft-rule backlog (does NOT fail the suite — to be cleaned in Stages 7+):\n" +
                    $"  tooling_temp_filename     x{toolingTemp}\n" +
                    $"  iteration_suffix          x{iterationSuffix}\n" +
                    $"  forbidden_chars (',()') x{forbiddenChars}\n" +
                    $"  filename_has_space        x{filenameSpaces}\n" +
                    $"  folder_has_space          x{folderSpaces}\n" +
                    $"  TOTAL                     x{total}\n" +
                    "Run `python tools/atlas/audit_asset_conventions.py` for the full list.");
                Assert.Pass($"Reported {total} soft-rule violations (see warning above).");
            }
        }
    }
}
