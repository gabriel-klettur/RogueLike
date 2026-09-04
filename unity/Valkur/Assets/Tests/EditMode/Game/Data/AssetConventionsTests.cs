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

        // Catalog ScriptableObjects loaded by string name via `Resources.Load`.
        // Adding a file here means the runtime depends on it living at the
        // exact path `_Project/Resources/<name>` — moving it under a subfolder
        // would break the load.
        private static readonly HashSet<string> ResourcesRootAllowedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "AudioCatalog.asset",
            "CameraFeelProfile.asset",
            "DayNightProfile.asset",    // day/night ramp; the cycle is AddComponent-ed at
                                        // runtime, so it has no inspector slot to be wired from
            "TileCatalog.asset",
            "TerrainCatalog.asset", // autotile pipeline (rulesets + Blob16 lookup)
            "DestructionResistanceTable.asset", // building durability matrix; loaded by
                                        // BuildingDurability via Resources.Load, so it has
                                        // no inspector slot to be wired from either
        };

        // Subfolders of Resources/ that ship whole into the build. Each entry
        // is justified by a runtime call site that loads from `Resources/<name>/...`.
        private static readonly HashSet<string> ResourcesRootAllowedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Buildings",
            "Catalogs",
            "Chat",        // ChatAssignmentCatalog, loaded by ChatSystem.EnsureCatalog via
                           // Resources.Load("Chat/ChatAssignmentCatalog"). The ChatSystem is
                           // AddComponent-ed by GameplaySceneSetup onto a bare GameObject, so
                           // there is no inspector slot to wire the catalogue from — which is
                           // exactly why its [SerializeField] sat null for the life of the
                           // project and no NPC ever spoke.
            "Dungeon",     // autotile sample tilesheets + catacombs blob assets
            "Input",
            "Placeholders",
            "Progression", // ProgressionCatalog, loaded by PlayerProgression.LoadCatalog via
                           // Resources.Load("Progression/ProgressionCatalog"). Same reason as
                           // Chat above: PlayerProgression is AddComponent-ed onto the player
                           // by EntitySetup, so a [SerializeField] on it could never be
                           // filled. One small asset — the trees and curves it points at live
                           // outside Resources and are pulled in by reference.
            "Spells",
            "Tiles",
            "UI",
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
        private static readonly Regex IterationSuffixRe = new(@"[_-](old|copy|new|final|v\d+|tmp)\.[a-z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ForbiddenCharsRe  = new(@"[(),']", RegexOptions.Compiled);
        private static readonly Regex InitTestSceneRe   = new(@"^InitTestScene\d+\.unity(\.meta)?$", RegexOptions.Compiled);
        private static readonly Regex BackupFolderRe    = new(@"^(_?backups?|OLD|.+_old)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string AssetsPath => Application.dataPath; // .../Valkur/Assets

        private static string Rel(string fullPath) =>
            fullPath.Substring(AssetsPath.Length).TrimStart('/', '\\').Replace('\\', '/');

        private static bool IsInInternalWhitelist(string relPath) =>
            InternalConventionWhitelistPrefixes.Any(p => relPath.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        // Walked ONCE per fixture run. Seven tests each enumerated the whole Assets tree
        // (~40-250 ms apiece, ~1.3 s in total) for a listing that does not change between
        // them. Cached lazily rather than in OneTimeSetUp so a single test can still run alone.
        private static string[] _allFilesCache;
        private static string[] _allFoldersCache;

        private static IEnumerable<string> EnumerateAssets()
        {
            if (_allFilesCache == null)
                _allFilesCache = Directory.GetFiles(AssetsPath, "*", SearchOption.AllDirectories);
            return _allFilesCache;
        }

        private static IEnumerable<string> EnumerateAssetFolders()
        {
            if (_allFoldersCache == null)
                _allFoldersCache = Directory.GetDirectories(AssetsPath, "*", SearchOption.AllDirectories);
            return _allFoldersCache;
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

        /// <summary>
        /// The rule is in the name: these must never be COMMITTED. It used to assert they
        /// were not present on disk, which is a different and unsatisfiable claim — Unity's
        /// Test Runner writes <c>InitTestScene&lt;ticks&gt;.unity</c> into <c>Assets/</c> when a
        /// run starts, so the file exists for the whole duration of the very run that
        /// checks for it. Under the project's documented MCP workflow (CLAUDE.md makes MCP
        /// the preferred way to run tests) the old form failed every single time, which is
        /// how a permanently-red test trains people to ignore the suite.
        ///
        /// So it asks git instead. A transient artifact in a gitignored path is not a
        /// violation; one that someone force-added is, and that is exactly what this now
        /// catches. If git is unavailable the test is inconclusive rather than green —
        /// silently passing would be worse than the failure it replaces.
        /// </summary>
        [Test]
        public void HardRules_NoInitTestScenesCommitted()
        {
            string repoRoot = Directory.GetParent(Application.dataPath)?.Parent?.Parent?.FullName;
            if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(Path.Combine(repoRoot, ".git")))
                Assert.Ignore("Not a git working tree — cannot check what is tracked.");

            string tracked;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "ls-files -- \"*InitTestScene*.unity\"")
                {
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    tracked = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(10000);
                }
            }
            catch (Exception ex)
            {
                Assert.Ignore("git is not on PATH — cannot check what is tracked (" + ex.Message + ").");
                return;
            }

            var offenders = tracked
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            Assert.That(offenders, Is.Empty,
                "InitTestScene*.unity files are Unity Test Runner artifacts; they are already in\n" +
                ".gitignore and must never be committed. Run: git rm --cached <files>.\n" +
                "Tracked:\n  - " + string.Join("\n  - ", offenders));
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

        // ── Hard rules (promoted from soft after the Stage 11 saneamiento) ──

        // The five checks below were tracked as a single soft-rule warning
        // through Stages 11a-11e while the legacy backlog (438 violations)
        // burned down to zero. Now that lint reports OK, every category is
        // a hard rule — any regression must fail the build.

        [Test]
        public void HardRules_NoToolingTempFilenames()
        {
            var offenders = new List<string>();
            foreach (var path in EnumerateAssets())
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = Rel(path);
                if (IsInInternalWhitelist(rel)) continue;
                if (rel.StartsWith("Screenshots/", StringComparison.OrdinalIgnoreCase)) continue;
                if (ToolingTempRe.IsMatch(name)) offenders.Add(rel);
            }
            Assert.That(offenders, Is.Empty,
                "Filenames must not start with ChatGPT*, screenshot*, or untitled* (tooling-temp prefixes).\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        [Test]
        public void HardRules_NoIterationSuffixes()
        {
            var offenders = new List<string>();
            foreach (var path in EnumerateAssets())
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = Rel(path);
                if (IsInInternalWhitelist(rel)) continue;
                if (rel.StartsWith("Screenshots/", StringComparison.OrdinalIgnoreCase)) continue;
                if (IterationSuffixRe.IsMatch(name)) offenders.Add(rel);
            }
            Assert.That(offenders, Is.Empty,
                "Filenames must not end in _old/_copy/_new/_final/_vN/_tmp — git tracks history.\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        [Test]
        public void HardRules_NoForbiddenCharsInFilenames()
        {
            var offenders = new List<string>();
            foreach (var path in EnumerateAssets())
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = Rel(path);
                if (IsInInternalWhitelist(rel)) continue;
                if (ForbiddenCharsRe.IsMatch(name)) offenders.Add(rel);
            }
            Assert.That(offenders, Is.Empty,
                "Filenames must not contain '(),' — these characters break tooling.\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        [Test]
        public void HardRules_NoSpacesInFilenames()
        {
            var offenders = new List<string>();
            foreach (var path in EnumerateAssets())
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = Rel(path);
                if (IsInInternalWhitelist(rel)) continue;
                if (name.Contains(" ")) offenders.Add(rel);
            }
            Assert.That(offenders, Is.Empty,
                "Filenames must use snake_case — no spaces.\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        [Test]
        public void HardRules_NoSpacesInFolderNames()
        {
            var offenders = new List<string>();
            foreach (var dir in EnumerateAssetFolders())
            {
                string rel = Rel(dir);
                if (IsInInternalWhitelist(rel)) continue;
                if (rel.Equals("TextMesh Pro", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(dir).Contains(" ")) offenders.Add(rel);
            }
            Assert.That(offenders, Is.Empty,
                "Folder names must use snake_case — no spaces. (TextMesh Pro is whitelisted as a Unity package.)\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        // ── Legacy soft-rule shim (kept so existing CI dashboards don't break) ──

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
                if (rel.StartsWith("Screenshots/", StringComparison.OrdinalIgnoreCase)) continue;

                if (ToolingTempRe.IsMatch(name)) toolingTemp++;
                if (IterationSuffixRe.IsMatch(name)) iterationSuffix++;
                if (ForbiddenCharsRe.IsMatch(name)) forbiddenChars++;
                if (name.Contains(" ")) filenameSpaces++;
            }

            foreach (var dir in EnumerateAssetFolders())
            {
                string rel = Rel(dir);
                if (IsInInternalWhitelist(rel)) continue;
                if (rel.Equals("TextMesh Pro", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(dir).Contains(" ")) folderSpaces++;
            }

            int total = toolingTemp + iterationSuffix + forbiddenChars + filenameSpaces + folderSpaces;
            if (total == 0)
            {
                Assert.Pass("No soft-rule violations remain — backlog is closed.");
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
