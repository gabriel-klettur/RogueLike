using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Editor.Monsters;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Exercises <see cref="MonsterFramesImporter"/> - the "authoring monster #21 costs a day"
    /// audit item's importer half - against a fully sandboxed manifest directory, an in-memory
    /// <see cref="MonsterCatalog"/> and a scratch asset folder. Nothing shipped is ever touched:
    /// no test here reads or writes <c>Data/Catalogs/Monsters/MonsterCatalog.asset</c>, and every
    /// created <see cref="MonsterDefinition"/> lives under <see cref="ScratchTemplateDir"/>,
    /// deleted in <see cref="TearDown"/> whether the test passed or not.
    ///
    /// Sprite references point at a real, already-imported sprite
    /// (<c>Art/NPC/monsters/barbol/barbol_1_down.png</c>) rather than fabricating new PNGs - the
    /// importer only cares that <c>AssetDatabase.LoadAssetAtPath&lt;Sprite&gt;</c> resolves, and
    /// pixel content is <c>tools/atlas/build_monster_frames.py</c>'s concern, not this one's (see
    /// that tool's own smoke test, run separately against a real character sheet).
    /// </summary>
    public class MonsterFramesImporterTests
    {
        private const string ScratchParent = "Assets/Tests/EditMode/Game/Data";
        private const string ScratchFolderName = "_MonsterFramesImporterScratch";
        private const string ScratchTemplateDir = ScratchParent + "/" + ScratchFolderName;

        private const string ExistingSprite = "Assets/_Project/Art/NPC/monsters/barbol/barbol_1_down.png";

        private const string MonsterKey = "zzz_editmode_test_monster";

        private static readonly string[] Directions =
            { "south", "southEast", "east", "northEast", "north", "northWest", "west", "southWest" };

        private string _manifestDir;
        private MonsterCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            // Defensive: a prior interrupted run could have left the scratch folder behind.
            if (AssetDatabase.IsValidFolder(ScratchTemplateDir))
                AssetDatabase.DeleteAsset(ScratchTemplateDir);
            AssetDatabase.CreateFolder(ScratchParent, ScratchFolderName);

            _manifestDir = Path.Combine(Path.GetTempPath(),
                "valkur_monster_frames_importer_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_manifestDir);

            // In-memory only - never CreateAsset'd, so SaveAssets cannot touch the real catalog.
            _catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(ScratchTemplateDir))
                AssetDatabase.DeleteAsset(ScratchTemplateDir);

            if (_manifestDir != null && Directory.Exists(_manifestDir))
                Directory.Delete(_manifestDir, recursive: true);

            if (_catalog != null)
                UnityEngine.Object.DestroyImmediate(_catalog);

            AssetDatabase.Refresh();
        }

        // ── Fixture helpers ──────────────────────────────────────────────────────────

        private void WriteManifest(string json, string fileName = "monster_frames_manifest_test.json")
        {
            File.WriteAllText(Path.Combine(_manifestDir, fileName), json);
        }

        /// <summary>One monster with a full idle turnaround and a one-frame walk cycle - the
        /// smallest manifest that exercises both an <see cref="EntityAssetConfig.idle"/> write
        /// and a <c>*Sheets</c> write.</summary>
        private static string BuildManifestJson(string monsterKey, string displayName = "Test Monster")
        {
            string idleEntries = string.Join(",\n", Directions.Select(d =>
                $"        {{\"direction\":\"{d}\",\"path\":\"{ExistingSprite}\"}}"));
            string walkSprites = string.Join(",", Directions.Select(_ => $"\"{ExistingSprite}\""));

            return $@"{{
  ""generator"": ""test"",
  ""generatedFrom"": ""test"",
  ""monsters"": [
    {{
      ""monsterKey"": ""{monsterKey}"",
      ""displayName"": ""{displayName}"",
      ""idle"": [
{idleEntries}
      ],
      ""states"": [
        {{ ""state"": ""walk"", ""framesPerDirection"": 1, ""sprites"": [{walkSprites}] }}
      ]
    }}
  ]
}}";
        }

        private MonsterFramesImporter.ImportSummary RunImport(bool apply) =>
            MonsterFramesImporter.Import(_manifestDir, _catalog, ScratchTemplateDir, apply,
                refreshAssetDatabase: false);

        // ── Manifest discovery ───────────────────────────────────────────────────────

        [Test]
        public void Import_WithNoManifestFiles_AbortsAndTouchesNothing()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "No monster_frames_manifest.*json"));

            MonsterFramesImporter.ImportSummary summary = RunImport(apply: true);

            Assert.IsTrue(summary.Aborted, "an empty manifest folder must abort, not import nothing");
            Assert.AreEqual(0, _catalog.Definitions.Count);
        }

        // ── Create / update / register ───────────────────────────────────────────────

        [Test]
        public void Import_CreatesANewDefinition_RegisteredInTheCatalog()
        {
            WriteManifest(BuildManifestJson(MonsterKey));

            MonsterFramesImporter.ImportSummary summary = RunImport(apply: true);

            Assert.IsFalse(summary.Aborted);
            CollectionAssert.Contains(summary.Created, MonsterKey);
            Assert.IsEmpty(summary.MissingSprites, string.Join("; ", summary.MissingSprites));

            MonsterDefinition def = _catalog.GetByKey(MonsterKey);
            Assert.IsNotNull(def, "the created definition must resolve through the catalog it was registered on");
            Assert.AreEqual(MonsterKey, def.monsterKey);

            string expectedPath = $"{ScratchTemplateDir}/{MonsterKey}.asset";
            Assert.AreEqual(expectedPath, AssetDatabase.GetAssetPath(def),
                "a new definition is filed under <templateDir>/<monsterKey>.asset, mirroring " +
                "how the existing catalog names every shipped monster");
        }

        [Test]
        public void Rerun_UpdatesTheSameDefinition_RatherThanDuplicating()
        {
            WriteManifest(BuildManifestJson(MonsterKey));

            MonsterFramesImporter.ImportSummary first = RunImport(apply: true);
            MonsterDefinition firstDef = _catalog.GetByKey(MonsterKey);

            MonsterFramesImporter.ImportSummary second = RunImport(apply: true);
            MonsterDefinition secondDef = _catalog.GetByKey(MonsterKey);

            CollectionAssert.Contains(first.Created, MonsterKey);
            CollectionAssert.DoesNotContain(second.Created, MonsterKey);
            CollectionAssert.Contains(second.Updated, MonsterKey);

            Assert.AreSame(firstDef, secondDef,
                "a re-run must refresh the SAME ScriptableObject instance, not mint a second one");

            int assetCount = AssetDatabase.FindAssets("t:" + nameof(MonsterDefinition), new[] { ScratchTemplateDir })
                .Length;
            Assert.AreEqual(1, assetCount, "re-running the importer must not leave a second .asset file behind");
        }

        [Test]
        public void CreatedDefinition_IsRegisteredExactlyOnceInTheCatalog()
        {
            WriteManifest(BuildManifestJson(MonsterKey));

            RunImport(apply: true);
            RunImport(apply: true); // re-run on purpose - UpsertDefinition must stay idempotent

            int occurrences = _catalog.Definitions.Count(d => d != null && d.monsterKey == MonsterKey);
            Assert.AreEqual(1, occurrences,
                "MonsterCatalog.UpsertDefinition must replace the existing entry, never append a second one");
        }

        // ── Sprite wiring ────────────────────────────────────────────────────────────

        [Test]
        public void Import_WiresTheIdleTurnaroundAndTheWalkSheet()
        {
            WriteManifest(BuildManifestJson(MonsterKey));
            RunImport(apply: true);

            MonsterDefinition def = _catalog.GetByKey(MonsterKey);
            Sprite expected = AssetDatabase.LoadAssetAtPath<Sprite>(ExistingSprite);

            DirectionalSprites idle = def.assetConfig.idle;
            Assert.AreEqual(expected, idle.south);
            Assert.AreEqual(expected, idle.southEast);
            Assert.AreEqual(expected, idle.east);
            Assert.AreEqual(expected, idle.northEast);
            Assert.AreEqual(expected, idle.north);
            Assert.AreEqual(expected, idle.northWest);
            Assert.AreEqual(expected, idle.west);
            Assert.AreEqual(expected, idle.southWest);

            Assert.AreEqual(8, def.assetConfig.walkSheets.Count,
                "one frame per direction x 8 directions, S,SE,E,NE,N,NW,W,SW");
            Assert.IsTrue(def.assetConfig.walkSheets.All(s => s == expected));

            // Untouched slots stay untouched - the manifest named idle+walk only. EntityAssetConfig
            // list fields are not initializer-defaulted (ContentValidator.cs null-checks them for
            // the same reason), so "untouched" can mean either null or an empty list.
            Assert.IsTrue(def.assetConfig.chaseSheets == null || def.assetConfig.chaseSheets.Count == 0);
            Assert.IsTrue(def.assetConfig.attackSheets == null || def.assetConfig.attackSheets.Count == 0);
        }

        // ── Dry run ──────────────────────────────────────────────────────────────────

        [Test]
        public void DryRun_ReportsButWritesNothing()
        {
            WriteManifest(BuildManifestJson(MonsterKey));

            MonsterFramesImporter.ImportSummary summary = RunImport(apply: false);

            CollectionAssert.Contains(summary.Created, MonsterKey);
            Assert.IsEmpty(summary.MissingSprites);
            Assert.AreEqual(0, _catalog.Definitions.Count, "a dry run must not register anything");

            int assetCount = AssetDatabase.FindAssets("t:" + nameof(MonsterDefinition), new[] { ScratchTemplateDir })
                .Length;
            Assert.AreEqual(0, assetCount, "a dry run must not create any .asset file");
        }

        [Test]
        public void DryRun_StillReportsAMissingSprite()
        {
            const string badPath = "Assets/_Project/Art/NPC/monsters/does_not_exist.png";
            string json = BuildManifestJson(MonsterKey).Replace(ExistingSprite, badPath);
            WriteManifest(json);

            MonsterFramesImporter.ImportSummary summary = RunImport(apply: false);

            Assert.IsFalse(summary.Aborted);
            Assert.IsNotEmpty(summary.MissingSprites,
                "a dry run should surface a broken sprite path before anyone applies it for real");
        }

        // ── Validation ───────────────────────────────────────────────────────────────

        [Test]
        public void Import_RejectsAStateWithTheWrongFrameCount()
        {
            // framesPerDirection says 2 (16 sprites expected) but only 8 are listed.
            string walkSprites = string.Join(",", Directions.Select(_ => $"\"{ExistingSprite}\""));
            string json = $@"{{
  ""generator"": ""test"", ""generatedFrom"": ""test"",
  ""monsters"": [
    {{ ""monsterKey"": ""{MonsterKey}"", ""displayName"": ""Bad"", ""idle"": [],
       ""states"": [ {{ ""state"": ""walk"", ""framesPerDirection"": 2, ""sprites"": [{walkSprites}] }} ] }}
  ]
}}";
            WriteManifest(json);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[MonsterFramesImporter\] entry '.*': state 'walk'"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[MonsterFramesImporter\] Aborting"));

            MonsterFramesImporter.ImportSummary summary = RunImport(apply: true);

            Assert.IsTrue(summary.Aborted, "a malformed manifest must never half-import");
            Assert.AreEqual(0, _catalog.Definitions.Count);
        }

        [Test]
        public void Import_RejectsAnUppercaseMonsterKey()
        {
            string json = BuildManifestJson("BadKey");
            WriteManifest(json);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[MonsterFramesImporter\] entry 'BadKey': monsterKey 'BadKey' must be lowercase snake_case"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[MonsterFramesImporter\] Aborting"));

            MonsterFramesImporter.ImportSummary summary = RunImport(apply: true);

            Assert.IsTrue(summary.Aborted);
        }
    }
}
