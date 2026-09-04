using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Every <c>MonoScript</c> that a shipped <c>.asset</c> points at must resolve to a CLASS,
    /// not merely to a file that exists.
    ///
    /// <para>WHY THIS IS NOT <c>ResourcesScriptIntegrityTests</c>. That fixture asks whether the
    /// <c>m_Script</c> guid resolves to a PATH, which is the right question for a script that was
    /// deleted. It cannot see the failure this one exists for, and the difference cost the game
    /// its entire skill-tree progression: <c>SkillTree.cs</c> was present, its guid matched its
    /// own <c>.meta</c>, the type compiled, it was the only type of that name in the domain, and
    /// <c>System.Type.GetType("Valkur.Data.SkillTree, Valkur.Data")</c> resolved — while
    /// <c>MonoScript.GetClass()</c> for that same file returned NULL. Unity had lost the
    /// file-to-class binding, so every <c>.asset</c> referencing it deserialized to null.</para>
    ///
    /// <para>WHAT IT LOOKED LIKE. Measured: all five <c>*_skill_tree.asset</c> loaded as null
    /// while their 35 <c>SkillNode</c> siblings and all 80 spell-tree assets loaded fine;
    /// <c>FindAssets("t:SkillTree")</c> returned 0; <c>LoadAllAssetsAtPath</c> returned one
    /// object and that object was null; <c>catalog.skillTrees</c> held 5 entries and 5 nulls.
    /// The YAML on disk was perfect throughout — <c>classKey: dwarf</c>, seven nodes, the lot.
    /// Nothing was logged. The remedy is a forced reimport of the <c>.cs</c> FIRST (which
    /// rebuilds the binding) and only then of the <c>.asset</c> files (which reconstructs the
    /// cached nulls); doing them in the other order fixes nothing, which is how the first
    /// attempt failed.</para>
    ///
    /// <para>THE HAZARD IS WHY THIS IS A TEST AND NOT A NOTE. A null in memory beside good data
    /// on disk is one <c>AssetDatabase.SaveAssets()</c> away from becoming a null on disk — the
    /// shape of the 216-deleted-building-templates incident. This must fail loudly, before
    /// anything saves.</para>
    ///
    /// <para>Cheap by construction: 5,783 shipped assets carry only 51 DISTINCT script guids, so
    /// the scan is one 5.8 MB read and 51 lookups.</para>
    /// </summary>
    [TestFixture]
    public class ShippedScriptBindingTests
    {
        // m_Script: {fileID: 11500000, guid: <32 hex>, type: 3}
        private static readonly Regex ScriptRefRe = new Regex(
            @"m_Script:\s*\{fileID:\s*-?\d+,\s*guid:\s*(?<guid>[0-9a-fA-F]{32}),\s*type:\s*\d+\}",
            RegexOptions.Compiled);

        // Unity's built-in MonoScripts use sentinel GUIDs of 16 zeros + one hex digit + 15
        // zeros. AssetDatabase cannot resolve them to a project path and they are always
        // present, so they are not what this guard is looking for.
        private static readonly Regex BuiltinGuidRe = new Regex(
            @"^0{16}[0-9a-fA-F]0{15}$", RegexOptions.Compiled);

        private static IEnumerable<string> ShippedAssetRoots()
        {
            yield return Path.Combine(Application.dataPath, "_Project", "Data");
            yield return Path.Combine(Application.dataPath, "_Project", "Resources");
        }

        /// <summary>
        /// <c>Data/Dungeon/CatacombsSource/</c> is raw third-party ScriptableObject YAML whose
        /// script Valkur never imported, ON PURPOSE — <c>CatacombsImporter</c> reads those files
        /// as TEXT. All 34 of them load as null by design, and they are outside
        /// <c>Resources/</c> precisely so the full-tree scan does not log a missing-script error
        /// per asset. Excluding them by folder rather than by guid keeps the exclusion readable
        /// and keeps a NEW unresolvable guid anywhere else a failure.
        /// </summary>
        private static bool IsDeliberatelyUnimported(string assetPath)
            => assetPath.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/').Contains("/Data/Dungeon/CatacombsSource/");

        [Test]
        public void EveryScriptAShippedAssetReferences_ResolvesToAClass()
        {
            // guid -> one example asset that references it, for a failure message that names
            // something the reader can open.
            var referencedBy = new Dictionary<string, string>();

            foreach (var root in ShippedAssetRoots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (var path in Directory.GetFiles(root, "*.asset", SearchOption.AllDirectories))
                {
                    if (IsDeliberatelyUnimported(path)) continue;

                    foreach (Match m in ScriptRefRe.Matches(File.ReadAllText(path)))
                    {
                        string guid = m.Groups["guid"].Value;
                        if (BuiltinGuidRe.IsMatch(guid)) continue;
                        if (!referencedBy.ContainsKey(guid)) referencedBy[guid] = path;
                    }
                }
            }

            Assert.Greater(referencedBy.Count, 0,
                "No script references found at all — the scan roots are probably wrong.");

            var broken = new List<string>();
            foreach (var pair in referencedBy)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(pair.Key);

                // A guid that resolves to nothing is ResourcesScriptIntegrityTests' business
                // for Resources/, and outside it there is no owner — so report it here rather
                // than let it pass silently.
                if (string.IsNullOrEmpty(scriptPath))
                {
                    broken.Add($"guid {pair.Key} resolves to no path (referenced by {pair.Value})");
                    continue;
                }

                var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (mono == null)
                {
                    broken.Add($"{scriptPath} is not loadable as a MonoScript " +
                               $"(referenced by {pair.Value})");
                    continue;
                }

                if (mono.GetClass() == null)
                {
                    broken.Add($"{scriptPath} — MonoScript.GetClass() is NULL, so every asset " +
                               $"referencing it deserializes to null (e.g. {pair.Value})");
                }
            }

            Assert.IsEmpty(broken,
                "Shipped assets point at scripts Unity cannot bind to a class. Each one is a " +
                "catalog silently full of nulls, and one SaveAssets away from that emptiness " +
                "being written over the good data on disk.\n\n" +
                "Fix: force-reimport the .cs FIRST (rebuilds the file-to-class binding), then " +
                "the .asset files (reconstructs the cached nulls). Never ForceReserializeAssets " +
                "— that flushes the bad memory state onto the good file.\n\n" +
                string.Join("\n", broken));
        }

        /// <summary>
        /// The catalogs the game cannot start without, checked as OBJECTS rather than as text.
        /// The test above proves the bindings resolve; this proves the references actually
        /// arrived, which is the half a designer breaks by clearing a slot in the Inspector.
        /// </summary>
        [Test]
        public void ProgressionCatalog_HasNoNullTreeReferences()
        {
            var catalog = Resources.Load<Valkur.Data.ProgressionCatalog>("Progression/ProgressionCatalog");
            Assert.IsNotNull(catalog, "Shipped ProgressionCatalog missing from Resources.");

            Assert.IsNotNull(catalog.skillTrees, "skillTrees array is null.");
            Assert.IsNotNull(catalog.spellTrees, "spellTrees array is null.");

            int skillNulls = catalog.skillTrees.Count(t => t == null);
            int spellNulls = catalog.spellTrees.Count(t => t == null);

            Assert.AreEqual(0, skillNulls,
                $"{skillNulls} of {catalog.skillTrees.Length} skill trees are null. Every " +
                "playable class loses its talents, and nothing logs it.");
            Assert.AreEqual(0, spellNulls,
                $"{spellNulls} of {catalog.spellTrees.Length} spell trees are null.");
        }
    }
}
