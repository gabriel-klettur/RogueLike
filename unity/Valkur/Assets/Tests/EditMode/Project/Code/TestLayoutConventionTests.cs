using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.Project.Code
{
    /// <summary>
    /// Where a test lives, what it is called, and what it may see — enforced, not written down.
    ///
    /// <para><b>Why this exists.</b> Audited 2026-09-15: 893 test files had a namespace that
    /// matched their folder 99.9 % of the time (a script fixed namespaces) and a folder that meant
    /// nothing 0 % of the time (nothing checked placement). The tree had grown to 109 folders with
    /// four homes for the same idea — spell tests in seven folders, save tests in two, a
    /// <c>Game/Core</c> the documentation forbade holding 18 files. The convention was a table in
    /// a skill file that listed 31 folders; this fixture replaces the table.</para>
    ///
    /// <para><b>The layout, in one sentence.</b> <c>Tests/&lt;Mode&gt;/&lt;Root&gt;/&lt;Feature&gt;/</c>,
    /// where the ROOT is the highest production layer the test needs (Core, Data, Infrastructure,
    /// UIKit, Gameplay, UI — each its own assembly that can see only the layers below it), plus
    /// <c>Editors</c> for the runtime editors, <c>EditorTools</c> for Valkur.Editor and
    /// <c>Project</c> for rules about the whole project; and the FEATURE mirrors a production
    /// folder (<c>Combat/Death</c>, <c>World/Weather</c>), optionally with one aspect folder below
    /// it (<c>TileEditor/History</c>). Namespace = path.</para>
    ///
    /// <para>Every failure message says what to do; read it before editing the baselines.</para>
    /// </summary>
    [TestFixture]
    [Category(TestCategories.Guard)]
    public sealed class TestLayoutConventionTests
    {
        private const string TESTS_REL = "Tests";
        private const string SCRIPTS_REL = "_Project/Scripts";
        private const string NAMES_BASELINE_REL = "Tests/EditMode/Project/Baselines/test-method-names.txt";
        private const string HELPERS_BASELINE_REL = "Tests/EditMode/Project/Baselines/reflection-helpers.txt";
        private const int MAX_FILE_LINES = 1000;

        /// <summary>Test root -> production assemblies it may reference. The order is the layering.</summary>
        private static readonly Dictionary<string, string[]> EditModeRoots = new Dictionary<string, string[]>
        {
            ["Core"] = new[] { "Valkur.Core" },
            ["Data"] = new[] { "Valkur.Core", "Valkur.Data" },
            ["Infrastructure"] = new[] { "Valkur.Core", "Valkur.Data", "Valkur.Infrastructure" },
            ["UIKit"] = new[] { "Valkur.Core", "Valkur.Data", "Valkur.UIKit" },
            ["Gameplay"] = new[] { "Valkur.Core", "Valkur.Data", "Valkur.Infrastructure", "Valkur.UIKit", "Valkur.Gameplay" },
            ["UI"] = new[] { "Valkur.Core", "Valkur.Data", "Valkur.Infrastructure", "Valkur.UIKit", "Valkur.Gameplay", "Valkur.UI" },
            ["Editors"] = null,
            ["EditorTools"] = null,
            ["Project"] = null,
        };

        private static readonly string[] PlayModeRoots = { "Core", "Data", "Infrastructure", "UIKit", "Gameplay", "UI" };
        private static readonly string[] ProjectFolders = { "Code", "Assets", "Baselines" };

        /// <summary>
        /// Test folder -> the production folder it mirrors, for the few names that cannot be used
        /// as-is. A namespace segment HIDES every type of the same name for all code in the
        /// namespaces below it, so a test folder called <c>Camera</c> turns every <c>Camera</c> in
        /// the sibling tests into CS0118 "is a namespace but is used like a type". The compiler
        /// found each of these on the day the tree was reorganised.
        /// </summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            // keys are feature paths below any root; values are the production feature path
            ["Combat/Vitals"] = "Combat/Resources",                      // UnityEngine.Resources
            ["Combat/Hitboxes"] = "Combat/Collision",                    // UnityEngine.Collision
            ["InventorySystem"] = "Inventory",                           // Valkur's Inventory class
            ["World/CameraRig"] = "World/Camera",                        // UnityEngine.Camera
            ["HUD/DebugHud"] = "HUD/Debug",                              // UnityEngine.Debug
            ["NodeGraphEditor"] = "Editors/DungeonNodeGraph",            // class DungeonNodeGraphEditor
            ["TimeAndWeatherEditor"] = "Editors/TimeWeather",            // class TimeWeatherEditor
        };

        /// <summary>Names that may never be a test folder segment, for the reason <see cref="Aliases"/> gives.</summary>
        private static readonly HashSet<string> ReservedSegments = new HashSet<string>(StringComparer.Ordinal)
        {
            "Camera", "Tile", "Inventory", "Resources", "Collision", "Debug", "Undo", "Tools", "State", "Graph",
            "Animation", "Animator", "Physics", "Time", "Random", "Screen", "Light", "Sprite", "Texture",
        };

        private static readonly Regex TestAttribute = new Regex(@"\[\s*(?:Test|UnityTest|TestCase|TestCaseSource)\b", RegexOptions.Compiled);
        private static readonly Regex NamespaceDecl = new Regex(@"^\s*namespace\s+([\w\.]+)", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex CategoryAttr = new Regex(@"\[\s*(?:NUnit\.Framework\.)?Category\s*\(\s*([^)]*)\)", RegexOptions.Compiled);
        private static readonly Regex TestMethod = new Regex(
            @"\[\s*(?:Test|UnityTest)\b[^\]]*\](?:\s*\[[^\]]*\])*\s*(?:public|private|internal)?\s*(?:async\s+)?(?:void|IEnumerator|Task)\s+(\w+)\s*\(",
            RegexOptions.Compiled);
        private static readonly Regex ConformingName = new Regex(@"^[A-Z][A-Za-z0-9]*(?:_[A-Z0-9][A-Za-z0-9]*){1,2}$", RegexOptions.Compiled);
        private static readonly Regex ReflectionHelper = new Regex(
            @"(?:private|internal)\s+static\s+[\w<>\[\],\.\s]+?\s+(?:Get|Set|Invoke|Call)\w*(?:<\w+>)?\s*\([^)]*\bstring\b[^)]*\)\s*(?:=>|\{)(?<body>[^\n]*\n(?:[^\n]*\n){0,14})",
            RegexOptions.Compiled);

        // ── Placement ───────────────────────────────────────────────────────────────────────

        [Test]
        public void EveryTestFile_LivesUnderAKnownRoot()
        {
            var bad = new List<string>();
            foreach (var f in TestFiles())
            {
                var parts = f.Rel.Split('/');
                if (parts[0] == "Support" || f.Rel == "PlayMode/AssemblyInfo.cs") continue;
                bool ok = parts.Length >= 3 && (parts[0] == "EditMode" && EditModeRoots.ContainsKey(parts[1])
                                                || parts[0] == "PlayMode" && PlayModeRoots.Contains(parts[1]));
                if (!ok) bad.Add("  " + f.Rel);
            }
            Assert.IsEmpty(bad,
                "Test files outside the layout. A test goes in Tests/<EditMode|PlayMode>/<Root>/<Feature>/, where Root is the " +
                "highest production layer it needs (EditMode: " + string.Join(", ", EditModeRoots.Keys) + "; PlayMode: " +
                string.Join(", ", PlayModeRoots) + "). Shared helpers go in Tests/Support.\n" + string.Join("\n", bad));
        }

        [Test]
        public void EveryFeatureFolder_MirrorsAProductionFolder()
        {
            var features = ProductionFeatures(out var editorFolders);
            var bad = new List<string>();
            foreach (var dir in TestFiles().Select(f => Path.GetDirectoryName(f.Rel).Replace('\\', '/')).Distinct())
            {
                var parts = dir.Split('/');
                if (parts[0] == "Support" || parts.Length < 2) continue;
                string root = parts[1];
                string feature = string.Join("/", parts.Skip(2));
                if (feature.Length == 0) continue; // the root itself mirrors files at the root of the assembly
                string why = CheckFeature(root, feature, features, editorFolders);
                if (why != null) bad.Add($"  {dir}: {why}");
            }
            Assert.IsEmpty(bad,
                "Test folders that do not mirror production. Name the folder after the production folder the tests are " +
                "about (Scripts/Gameplay/Combat/Death -> EditMode/Gameplay/Combat/Death); one extra aspect folder below it " +
                "is allowed (EditMode/Editors/TileEditor/History). Editors are Editors/<Name>Editor.\n" + string.Join("\n", bad));
        }

        [Test]
        public void NoFolderSegment_HidesATypeName()
        {
            var bad = TestFiles()
                .SelectMany(f => Path.GetDirectoryName(f.Rel).Replace('\\', '/').Split('/').Select(s => (s, f.Rel)))
                .Where(x => ReservedSegments.Contains(x.s))
                .Select(x => $"  '{x.s}' in {x.Rel}")
                .Distinct().ToList();
            Assert.IsEmpty(bad,
                "A folder is a namespace segment, and a namespace segment hides every type with the same name for the " +
                "tests beside it (CS0118 'X is a namespace but is used like a type'). Use the alias the table in " +
                nameof(TestLayoutConventionTests) + " gives, or add one there with the type it avoids.\n" + string.Join("\n", bad));
        }

        [Test]
        public void EveryNamespace_IsItsPath()
        {
            var bad = new List<string>();
            foreach (var f in TestFiles())
            {
                if (Path.GetFileName(f.Rel) == "AssemblyInfo.cs") continue;
                var m = NamespaceDecl.Match(f.Text);
                string expected = "Valkur.Tests." + Path.GetDirectoryName(f.Rel).Replace('\\', '/').Replace('/', '.');
                if (!m.Success || m.Groups[1].Value != expected)
                    bad.Add($"  {f.Rel}: '{(m.Success ? m.Groups[1].Value : "(none)")}' should be '{expected}'");
            }
            Assert.IsEmpty(bad,
                "The Test Runner builds its tree from namespaces and people find files by folder; the two must agree. " +
                "Run .github/skills/unity-testing/scripts/enforce-namespaces.ps1 to fix them all.\n" + string.Join("\n", bad));
        }

        // ── Files and fixtures ──────────────────────────────────────────────────────────────

        [Test]
        public void EveryFile_DeclaresOneTopLevelType_NamedLikeTheFile()
        {
            var bad = new List<string>();
            foreach (var f in TestFiles())
            {
                if (Path.GetFileName(f.Rel) == "AssemblyInfo.cs") continue;
                string stem = Path.GetFileNameWithoutExtension(f.Rel);
                string typeName = stem.Split('.')[0]; // partials: ChatSystemTests.Greetings.cs
                var types = TopLevelTypes(f.Code);
                if (types.Count != 1 || types[0] != typeName)
                    bad.Add($"  {f.Rel}: declares [{string.Join(", ", types)}]");
            }
            Assert.IsEmpty(bad,
                "One top-level type per file, named like the file (a partial may be <Type>.<Aspect>.cs). A second fixture " +
                "in a file is invisible to anyone searching by file name.\n" + string.Join("\n", bad));
        }

        [Test]
        public void EveryFileWithTests_EndsInTests()
        {
            var bad = TestFiles()
                .Where(f => TestAttribute.IsMatch(f.Code))
                .Where(f => !Path.GetFileNameWithoutExtension(f.Rel).Split('.')[0].EndsWith("Tests", StringComparison.Ordinal))
                .Select(f => "  " + f.Rel).ToList();
            Assert.IsEmpty(bad, "A file with tests is named <Subject>Tests.cs.\n" + string.Join("\n", bad));
        }

        [Test]
        public void NoTwoFixtures_ShareAName()
        {
            var dupes = TestFiles()
                .Where(f => TestAttribute.IsMatch(f.Code))
                .GroupBy(f => Path.GetFileNameWithoutExtension(f.Rel).Split('.')[0])
                .Where(g => g.Select(f => Path.GetDirectoryName(f.Rel)).Distinct().Count() > 1)
                .Select(g => $"  {g.Key}: {string.Join(", ", g.Select(f => f.Rel))}").ToList();
            Assert.IsEmpty(dupes,
                "Two fixtures with one name compile (different namespaces) and read as one in the Test Runner, in a search " +
                "and in a failure report. Name each after what it covers.\n" + string.Join("\n", dupes));
        }

        [Test]
        public void NoFile_IsLongerThanTheCap()
        {
            var bad = TestFiles().Where(f => f.LineCount > MAX_FILE_LINES).Select(f => $"  {f.LineCount}\t{f.Rel}").ToList();
            Assert.IsEmpty(bad,
                $"Test files over {MAX_FILE_LINES} lines stop being navigable by name. Split the fixture into partials by " +
                "behaviour (<Fixture>.<Aspect>.cs) keeping SetUp/TearDown in the main file.\n" + string.Join("\n", bad));
        }

        // ── Assemblies ──────────────────────────────────────────────────────────────────────

        [Test]
        public void EveryEditModeRoot_IsItsOwnAssembly_ThatSeesOnlyItsLayers()
        {
            var bad = new List<string>();
            string editMode = Path.Combine(TestsDir(), "EditMode");
            foreach (var pair in EditModeRoots)
            {
                string dir = Path.Combine(editMode, pair.Key);
                string asmdef = Path.Combine(dir, $"Valkur.Tests.EditMode.{pair.Key}.asmdef");
                if (!File.Exists(asmdef)) { bad.Add($"  {pair.Key}: no Valkur.Tests.EditMode.{pair.Key}.asmdef"); continue; }

                string json = File.ReadAllText(asmdef);
                if (!json.Contains("\"Valkur.Tests.Support\"")) bad.Add($"  {pair.Key}: does not reference Valkur.Tests.Support");
                if (pair.Value != null)
                {
                    var refs = Regex.Matches(json, "\"(Valkur\\.[A-Za-z]+)\"").Cast<Match>().Select(m => m.Groups[1].Value)
                        .Where(r => !r.StartsWith("Valkur.Tests", StringComparison.Ordinal));
                    foreach (var r in refs.Where(r => !pair.Value.Contains(r)))
                        bad.Add($"  {pair.Key}: references {r}, a layer above it");
                }

                string info = Path.Combine(dir, "AssemblyInfo.cs");
                if (!File.Exists(info) || !File.ReadAllText(info).Contains("[assembly: SelectableResetTestAction]"))
                    bad.Add($"  {pair.Key}: AssemblyInfo.cs does not register SelectableResetTestAction");
            }
            Assert.IsEmpty(bad,
                "Each EditMode root is one assembly: it references Valkur.Tests.Support, only the production layers its " +
                "tests may use, and registers the Selectable pre-grow hook (an assembly-level NUnit action reaches only its " +
                "own assembly — without it UI fixtures fail with IndexOutOfRange in Selectable.OnEnable).\n" + string.Join("\n", bad));
        }

        // ── Categories ──────────────────────────────────────────────────────────────────────

        [Test]
        public void EveryCategory_ComesFromTestCategories()
        {
            var bad = new List<string>();
            foreach (var f in TestFiles())
                foreach (Match m in CategoryAttr.Matches(f.Code))
                {
                    string arg = m.Groups[1].Value.Trim();
                    string name = arg.StartsWith("TestCategories.", StringComparison.Ordinal) ? arg.Substring("TestCategories.".Length) : null;
                    if (name == null || !TestCategories.All.Contains(name))
                        bad.Add($"  {f.Rel}: [Category({arg})]");
                }
            Assert.IsEmpty(bad,
                "Categories are filters, and a filter only works if everybody spells it the same way. Use a constant from " +
                "Tests/Support/TestCategories.cs (Guard, ShippedData, Integration, Slow); add one there if a new kind is needed.\n" +
                string.Join("\n", bad));
        }

        [Test]
        public void ProjectGuards_AndShippedData_CarryTheirCategory()
        {
            var bad = new List<string>();
            foreach (var f in TestFiles().Where(f => TestAttribute.IsMatch(f.Code)))
            {
                string stem = Path.GetFileNameWithoutExtension(f.Rel);
                if (f.Rel.StartsWith("EditMode/Project/", StringComparison.Ordinal) && !f.Text.Contains("TestCategories.Guard"))
                    bad.Add($"  {f.Rel}: under Project/ but not [Category(TestCategories.Guard)]");
                if (stem.StartsWith("Shipped", StringComparison.Ordinal) && !f.Text.Contains("TestCategories.ShippedData"))
                    bad.Add($"  {f.Rel}: named Shipped* but not [Category(TestCategories.ShippedData)]");
            }
            Assert.IsEmpty(bad, "Guards and shipped-data fixtures must be runnable as a set.\n" + string.Join("\n", bad));
        }

        // ── Ratchets ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Test names read <c>Subject_Scenario_Outcome</c>: two or three PascalCase parts.
        /// Measured when this was written: 47 % had three parts, 44 % two, 9 % were a sentence
        /// with no separator, and 174 files mixed all three. Renaming eight thousand tests would
        /// break every filter, history entry and note that names one, so existing names are a
        /// per-file allowance that may only fall.
        /// </summary>
        [Test]
        public void TestNames_FollowSubjectScenarioOutcome_OrTheirFilesAllowanceDoesNotGrow()
        {
            RunRatchet(NAMES_BASELINE_REL, CountNonConformingNames,
                "Test methods that are not Subject_Scenario_Outcome (two or three PascalCase parts joined by '_', e.g. " +
                "TakeDamage_WhenInvincible_LeavesHpUnchanged).");
        }

        /// <summary>
        /// Private reflection helpers live in <see cref="TestReflection"/>. The suite had 127 local
        /// copies under seven names; the ones not migrated are an allowance that may only fall.
        /// </summary>
        [Test]
        public void ReflectionHelpers_ComeFromTestReflection_OrTheirFilesAllowanceDoesNotGrow()
        {
            RunRatchet(HELPERS_BASELINE_REL, CountReflectionHelpers,
                "Private reflection helpers (a static Get*/Set*/Invoke* taking a member name). Use " +
                "Valkur.Tests.Support.TestReflection instead of writing another one.");
        }

        [Test]
        public void TheBaselines_DoNotListFilesThatAreGone()
        {
            var missing = new List<string>();
            foreach (var rel in new[] { NAMES_BASELINE_REL, HELPERS_BASELINE_REL })
                foreach (var file in ReadBaseline(rel).Keys)
                    if (!File.Exists(Path.Combine(TestsDir(), file)))
                        missing.Add($"  {rel}: {file}");
            // an allowance for a path that no longer exists is inherited by the next file created there
            Assert.IsEmpty(missing, "Baseline entries for files that no longer exist. Drop these lines:\n" + string.Join("\n", missing));
        }

        // ── Machinery ───────────────────────────────────────────────────────────────────────

        public static int CountNonConformingNames(TestFile f)
            => TestMethod.Matches(f.Code).Cast<Match>().Count(m => !ConformingName.IsMatch(m.Groups[1].Value));

        public static int CountReflectionHelpers(TestFile f)
            => ReflectionHelper.Matches(f.Code).Cast<Match>()
                .Count(m => Regex.IsMatch(m.Groups["body"].Value, @"\b(?:GetField|GetMethod|GetProperty|BindingFlags)\b"));

        private static void RunRatchet(string baselineRel, Func<TestFile, int> count, string what)
        {
            var baseline = ReadBaseline(baselineRel);
            var grew = new List<string>();
            foreach (var f in TestFiles())
            {
                int live = count(f);
                string key = f.Rel;
                baseline.TryGetValue(key, out int allowed);
                if (live > allowed) grew.Add($"  {key}: {allowed} -> {live}");
            }
            if (grew.Count == 0) return;
            string proposed = WriteProposedBaseline(baselineRel, count);
            Assert.Fail(what + "\n\nFix the new ones. Only if an exception is genuinely right, raise the count in " +
                        baselineRel + " in the same commit — a reviewed exception, not a silent one. The live count of " +
                        "every file was written to " + proposed + " for comparison.\n\n" + string.Join("\n", grew));
        }

        /// <summary>A failing ratchet writes what it measured, so the diff against the baseline is one file.</summary>
        private static string WriteProposedBaseline(string baselineRel, Func<TestFile, int> count)
        {
            var sb = new StringBuilder();
            foreach (var f in TestFiles())
            {
                int n = count(f);
                if (n > 0) sb.Append(n).Append('\t').Append(f.Rel).Append('\n');
            }
            string path = "Library/ValkurTestResults/" + Path.GetFileNameWithoutExtension(baselineRel) + ".proposed.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static Dictionary<string, int> ReadBaseline(string rel)
        {
            string path = Path.Combine(Application.dataPath, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(path), $"Baseline not found at '{path}'.");
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var cols = line.Split('\t');
                if (cols.Length == 2 && int.TryParse(cols[0], out int n)) map[cols[1]] = n;
            }
            return map;
        }

        private static string CheckFeature(string root, string feature, HashSet<string> features, HashSet<string> editorFolders)
        {
            if (root == "Project")
                return ProjectFolders.Contains(feature.Split('/')[0]) && !feature.Contains('/') ? null
                    : "Project/ holds only Code, Assets and Baselines";

            foreach (var alias in Aliases)
                if (feature == alias.Key || feature.StartsWith(alias.Key + "/", StringComparison.Ordinal))
                {
                    string mirrored = alias.Value + feature.Substring(alias.Key.Length);
                    return MirrorsWithOneAspect(mirrored, features) ? null : $"alias {alias.Key} does not resolve";
                }

            if (root == "Editors")
            {
                var parts = feature.Split('/');
                if (parts[0] == "_Shared")
                    return MirrorsWithOneAspect(feature, features, editorPrefix: true) ? null : "no production folder Gameplay/Editors/" + feature;
                if (!parts[0].EndsWith("Editor", StringComparison.Ordinal))
                    return "editor folders are named <Name>Editor";
                string name = parts[0].Substring(0, parts[0].Length - "Editor".Length);
                if (!editorFolders.Contains(name)) return $"no production folder Gameplay/Editors/{name}";
                return parts.Length <= 3 && (parts.Length < 3 || features.Contains("Editors/" + name + "/" + parts[1]))
                    ? null : "an editor folder takes at most one aspect folder below a production subfolder";
            }

            return MirrorsWithOneAspect(feature, features) ? null
                : "no production folder with that feature path (checked every assembly, allowing one aspect folder)";
        }

        private static bool MirrorsWithOneAspect(string feature, HashSet<string> features, bool editorPrefix = false)
        {
            string key = editorPrefix ? "Editors/" + feature : feature;
            if (features.Contains(key)) return true;
            int cut = key.LastIndexOf('/');
            return cut > 0 && features.Contains(key.Substring(0, cut));
        }

        private static string StripAssembly(string productionPath)
        {
            foreach (var prefix in new[] { "Gameplay/UIKit/", "Gameplay/", "Core/", "Data/", "Infrastructure/", "UI/", "Editor/" })
                if (productionPath.StartsWith(prefix, StringComparison.Ordinal))
                    return prefix == "Gameplay/" && productionPath.StartsWith("Gameplay/Editors/", StringComparison.Ordinal)
                        ? productionPath.Substring("Gameplay/".Length)
                        : productionPath.Substring(prefix.Length);
            return productionPath;
        }

        /// <summary>Every production folder path below its assembly root ("Combat/Death", "Editors/Tile").</summary>
        private static HashSet<string> ProductionFeatures(out HashSet<string> editorFolders)
        {
            string scripts = Path.Combine(Application.dataPath, SCRIPTS_REL.Replace('/', Path.DirectorySeparatorChar));
            var set = new HashSet<string>(StringComparer.Ordinal);
            editorFolders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dir in Directory.GetDirectories(scripts, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(scripts.Length + 1).Replace('\\', '/');
                string feature = StripAssembly(rel + "/").TrimEnd('/');
                if (feature.Length == 0) continue;
                set.Add(feature);
                if (rel.StartsWith("Gameplay/Editors/", StringComparison.Ordinal) && rel.Count(c => c == '/') == 2)
                    editorFolders.Add(rel.Split('/')[2]);
            }
            return set;
        }

        private static List<string> TopLevelTypes(string code)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(code,
                         @"^(?<ind>[ \t]*)(?:\[[^\]\n]*\]\s*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly)\s+)*(?:class|struct|interface|enum)\s+(?<n>\w+)",
                         RegexOptions.Multiline))
                if (m.Groups["ind"].Value.Replace("\t", "    ").Length <= 4)
                    list.Add(m.Groups["n"].Value);
            return list;
        }

        public sealed class TestFile
        {
            public string Rel;
            public string Text;
            public string Code;
            public int LineCount;
        }

        private static List<TestFile> _cache;

        /// <summary>Every .cs under Assets/Tests, read once per run.</summary>
        public static List<TestFile> TestFiles()
        {
            if (_cache != null) return _cache;
            string root = TestsDir();
            _cache = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(p =>
                {
                    string text = File.ReadAllText(p);
                    return new TestFile
                    {
                        Rel = p.Substring(root.Length + 1).Replace('\\', '/'),
                        Text = text,
                        Code = StripComments(text),
                        LineCount = text.Count(c => c == '\n') + 1,
                    };
                })
                .OrderBy(f => f.Rel, StringComparer.Ordinal)
                .ToList();
            return _cache;
        }

        [OneTimeTearDown]
        public void ReleaseCache() => _cache = null;

        private static string TestsDir()
        {
            string dir = Path.Combine(Application.dataPath, TESTS_REL);
            Assert.IsTrue(Directory.Exists(dir), $"Tests directory not found at '{dir}'.");
            return dir;
        }

        /// <summary>Comments and string contents removed, so a rule is never tripped by its own explanation.</summary>
        public static string StripComments(string s)
        {
            s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
            s = Regex.Replace(s, "@\"(?:[^\"]|\"\")*\"", "\"\"");
            s = Regex.Replace(s, "\"(?:[^\"\\\\\\n]|\\\\.)*\"", "\"\"");
            s = Regex.Replace(s, @"//[^\n]*", "");
            return s;
        }
    }
}
