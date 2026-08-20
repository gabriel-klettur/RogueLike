using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Hard rule: nothing under <c>Resources/</c> may reference a MonoScript that does
    /// not resolve.
    ///
    /// <c>Resources/</c> is special. A single <c>Resources.LoadAll&lt;T&gt;("")</c> walks the
    /// whole tree and deserializes every asset in it, so ONE broken script reference
    /// there becomes "The referenced script (Unknown) on this Behaviour is missing!"
    /// on every call — not once at import. That is exactly how 34 raw Udemy
    /// <c>Room_*_Catacombs_*.asset</c> files (whose <c>m_Script</c> points at the course's
    /// <c>RoomTemplateSO</c>, a class Valkur never imported) put 34 errors in the console
    /// on every Play. They now live in <c>Data/Dungeon/CatacombsSource/</c>, outside
    /// <c>Resources/</c>, and <c>CatacombsImporter</c> reads them as YAML text.
    ///
    /// This guard is about placement, not about the assets being broken: raw third-party
    /// ScriptableObjects are fine to keep, just never under <c>Resources/</c>.
    /// </summary>
    public class ResourcesScriptIntegrityTests
    {
        // m_Script: {fileID: 11500000, guid: <32 hex>, type: 3}
        private static readonly Regex ScriptRefRe = new Regex(
            @"m_Script:\s*\{fileID:\s*-?\d+,\s*guid:\s*(?<guid>[0-9a-fA-F]{32}),\s*type:\s*\d+\}",
            RegexOptions.Compiled);

        // Unity's built-in MonoScripts (Tile, RuleTile, the editor resources, …) use
        // sentinel GUIDs of the form 16 zeros + one hex digit + 15 zeros — e.g.
        // 0000000000000000e000000000000000 is "Resources/unity_builtin_extra".
        // AssetDatabase cannot resolve them to a project path, but they are always
        // present, so they are not what this guard is looking for.
        private static readonly Regex BuiltinGuidRe = new Regex(
            @"^0{16}[0-9a-fA-F]0{15}$", RegexOptions.Compiled);

        private static string ResourcesRoot =>
            Path.Combine(Application.dataPath, "_Project", "Resources");

        [Test]
        public void ResourcesAssets_ReferenceOnlyResolvableScripts()
        {
            if (!Directory.Exists(ResourcesRoot))
            {
                Assert.Pass("Resources/ does not exist.");
                return;
            }

            var offenders = new List<string>();
            var unresolvedGuids = new HashSet<string>();

            foreach (var path in EnumerateSerializedAssets(ResourcesRoot))
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }

                // Binary-serialized assets won't match the YAML pattern; that is fine,
                // Unity resolves those through the same GUID table either way.
                foreach (Match match in ScriptRefRe.Matches(text))
                {
                    string guid = match.Groups["guid"].Value;
                    if (IsResolvable(guid)) continue;

                    unresolvedGuids.Add(guid);
                    offenders.Add($"{Rel(path)}  →  guid {guid}");
                }
            }

            Assert.That(offenders, Is.Empty,
                "Assets under Resources/ must not reference unresolvable MonoScripts — every " +
                "full-tree Resources.LoadAll deserializes them and logs one console error per " +
                "broken reference, per call.\n" +
                $"Unresolved script GUIDs: {string.Join(", ", unresolvedGuids)}\n" +
                "Offenders:\n  - " + string.Join("\n  - ", offenders));
        }

        [Test]
        public void UdemyCatacombsSources_LiveOutsideResources()
        {
            // Named guard for the specific regression: moving these back under
            // Resources/ silently reintroduces 34 errors per Play.
            string sourceDir = Path.Combine(Application.dataPath, "_Project", "Data",
                                            "Dungeon", "CatacombsSource");

            if (!Directory.Exists(ResourcesRoot))
            {
                Assert.Pass("Resources/ does not exist.");
                return;
            }

            var strays = Directory.Exists(ResourcesRoot)
                ? Directory.GetFiles(ResourcesRoot, "Room_*_Catacombs_*.asset",
                                     SearchOption.AllDirectories)
                : new string[0];

            // The importer's Valkur-native output intentionally keeps the same file names
            // under Resources/Dungeon/Catacombs/Valkur — those DO resolve to Valkur's own
            // RoomTemplateSO, so only unresolvable ones are a problem. The broad test above
            // covers that; here we just assert the raw sources are where we moved them.
            Assert.IsTrue(Directory.Exists(sourceDir),
                $"Raw Udemy room templates must live at {sourceDir} (outside Resources/). " +
                "CatacombsImporter.SourceTemplatesDir reads them from there as YAML text.");

            foreach (var stray in strays)
            {
                string text = File.ReadAllText(stray);
                foreach (Match match in ScriptRefRe.Matches(text))
                {
                    Assert.IsTrue(IsResolvable(match.Groups["guid"].Value),
                        $"{Rel(stray)} still references an unresolvable script — raw Udemy " +
                        "sources belong in Data/Dungeon/CatacombsSource/, not under Resources/.");
                }
            }
        }

        private static bool IsResolvable(string guid)
        {
            if (BuiltinGuidRe.IsMatch(guid)) return true;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return false;
            // GUIDToAssetPath happily returns a path for a guid Unity remembers but whose
            // file is gone, so confirm the script asset actually loads.
            return AssetDatabase.LoadAssetAtPath<MonoScript>(path) != null;
        }

        private static IEnumerable<string> EnumerateSerializedAssets(string root)
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.asset", SearchOption.AllDirectories))
                yield return path;
            foreach (var path in Directory.EnumerateFiles(root, "*.prefab", SearchOption.AllDirectories))
                yield return path;
        }

        private static string Rel(string fullPath) =>
            fullPath.Substring(Application.dataPath.Length).TrimStart('/', '\\').Replace('\\', '/');
    }
}
