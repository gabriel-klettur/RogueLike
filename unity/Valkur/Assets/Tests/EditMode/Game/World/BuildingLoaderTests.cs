using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode
{
    /// <summary>
    /// Tests for <see cref="BuildingLoader"/> — JSON parsing, data integrity, and
    /// coordinate-conversion formula.
    ///
    /// Purpose: prevent regression of the "buildings invisible" bug caused by
    /// <c>buildings_instances.json</c> being overwritten with an empty array (<c>[]</c>).
    ///
    /// Test groups:
    ///   1. Data-integrity  — file exists + is non-empty at the expected StreamingAssets path.
    ///   2. ParseInstances  — private static parser tested via reflection (unit-level).
    ///   3. Coordinate math — Unity world-space formula matches Python's coordinate system.
    ///
    /// Python reference: roguelike_editors/buildings/building_editor.py
    ///   WorldX = gridOffset.x + (rel_x + eff_w / 2) / PPU
    ///   WorldY = gridOffset.y + (zone_h - 1) - (rel_y + eff_h) / PPU
    /// </summary>
    [TestFixture]
    public class BuildingLoaderTests
    {
        // ── Reflection helpers ────────────────────────────────────────────────────

        /// <summary>Returns BuildingLoader.ParseInstances (private static).</summary>
        private static MethodInfo GetParseMethod()
        {
            var m = typeof(BuildingLoader).GetMethod(
                "ParseInstances",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "Reflection: BuildingLoader.ParseInstances not found. " +
                "Method may have been renamed — update this test.");
            return m;
        }

        /// <summary>
        /// Invokes ParseInstances and returns the result as IList so we can inspect
        /// items without referencing the private BuildingInstanceDto struct type.
        /// </summary>
        private static IList InvokeParse(string json)
        {
            var method = GetParseMethod();
            return method.Invoke(null, new object[] { json }) as IList;
        }

        /// <summary>Gets a named field value from an object via reflection.</summary>
        private static T GetField<T>(object obj, string fieldName)
        {
            var fi = obj.GetType().GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, $"Reflection: field '{fieldName}' not found on {obj.GetType().Name}. " +
                "Field may have been renamed — update this test.");
            return (T)fi.GetValue(obj);
        }

        // ── TearDown ──────────────────────────────────────────────────────────────

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        // ─────────────────────────────────────────────────────────────────────────
        // 1. Data-integrity tests
        // ─────────────────────────────────────────────────────────────────────────

        [Test]
        [Category("DataIntegrity")]
        public void InstancesJson_Exists_AtStreamingAssetsPath()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath, "Buildings", "buildings_instances.json");

            Assert.IsTrue(File.Exists(path),
                $"buildings_instances.json is missing at:\n  {path}\n" +
                "Run: Valkur > Migration > Import World Zones from Python  (or restore from git).");
        }

        [Test]
        [Category("DataIntegrity")]
        public void InstancesJson_ContainsAtLeastOneBuilding()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath, "Buildings", "buildings_instances.json");

            // Skip (not fail) if the file is absent — covered by the Exists test above.
            Assume.That(File.Exists(path), $"File not found: {path}");

            string json = File.ReadAllText(path).Trim();

            // Fast guard against the specific regression: file contains exactly "[]"
            Assert.AreNotEqual("[]", json,
                "buildings_instances.json contains an empty array '[]'.\n" +
                "This was the root cause of the 'buildings invisible' bug.\n" +
                "Restore the real data:  git checkout <commit> -- " +
                "unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json");

            // ParseInstances through the production parser to get an accurate count.
            IList instances = InvokeParse(json);

            Assert.IsNotNull(instances,
                "ParseInstances returned null — JSON may be malformed.");
            Assert.Greater(instances.Count, 0,
                "ParseInstances returned 0 buildings — the file may only contain empty objects.");
        }

        [Test]
        [Category("DataIntegrity")]
        public void InstancesJson_ContainsLobbyZoneEntries()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath, "Buildings", "buildings_instances.json");
            Assume.That(File.Exists(path));

            string json = File.ReadAllText(path);
            IList instances = InvokeParse(json);
            Assume.That(instances != null && instances.Count > 0, "ParseInstances returned no items.");

            int lobbyCount = 0;
            foreach (object dto in instances)
            {
                string zone = GetField<string>(dto, "Zone");
                if (string.Equals(zone, "lobby", System.StringComparison.OrdinalIgnoreCase))
                    lobbyCount++;
            }

            Assert.Greater(lobbyCount, 0,
                "buildings_instances.json has no 'lobby' zone entries. " +
                "Lobby buildings should always be present in the base world.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 2. ParseInstances unit tests (private static via reflection)
        // ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void ParseInstances_SingleEntry_ParsesAllRequiredFields()
        {
            const string json =
                @"[{""id"":42,""template_id"":7,""zone"":""lobby"",""rel_x"":100,""rel_y"":200}]";

            IList items = InvokeParse(json);

            Assert.IsNotNull(items);
            Assert.AreEqual(1, items.Count, "One JSON entry should produce one DTO.");

            object dto = items[0];
            Assert.AreEqual(42,      GetField<int>(dto, "Id"),          "Id mismatch.");
            Assert.AreEqual(7,       GetField<int>(dto, "TemplateId"),  "TemplateId mismatch.");
            Assert.AreEqual("lobby", GetField<string>(dto, "Zone"),     "Zone mismatch.");
            Assert.AreEqual(100,     GetField<int>(dto, "RelX"),        "RelX mismatch.");
            Assert.AreEqual(200,     GetField<int>(dto, "RelY"),        "RelY mismatch.");
        }

        [Test]
        public void ParseInstances_EmptyArray_ReturnsEmptyList()
        {
            IList items = InvokeParse("[]");

            Assert.IsNotNull(items, "Empty array must return an empty list, not null.");
            Assert.AreEqual(0, items.Count, "Empty array should produce zero DTOs.");
        }

        [Test]
        public void ParseInstances_InvalidJson_LogsErrorAndReturnsEmptyList()
        {
            LogAssert.Expect(LogType.Error,
                "[BuildingLoader] Failed to parse instances JSON — expected a JSON array.");

            IList items = InvokeParse("{\"not\":\"an array\"}");

            Assert.IsNotNull(items, "Malformed JSON must return an empty list, not null.");
            Assert.AreEqual(0, items.Count, "Malformed JSON should produce zero DTOs.");
        }

        [Test]
        public void ParseInstances_MultipleEntries_ParsesAllOfThem()
        {
            const string json = @"[
                {""id"":1,""template_id"":1,""zone"":""lobby"",      ""rel_x"":0,  ""rel_y"":0  },
                {""id"":2,""template_id"":2,""zone"":""Forest"",     ""rel_x"":100,""rel_y"":200},
                {""id"":3,""template_id"":3,""zone"":""zone_100_50"",""rel_x"":50, ""rel_y"":50 }
            ]";

            IList items = InvokeParse(json);

            Assert.AreEqual(3, items.Count,
                "Three JSON entries should produce exactly three DTOs.");
        }

        [Test]
        public void ParseInstances_OverridesScale_ParsesScaleOverride()
        {
            const string json =
                @"[{""id"":1,""template_id"":10,""zone"":""lobby"",""rel_x"":0,""rel_y"":0," +
                @"""overrides"":{""scale"":[256,320]}}]";

            IList items = InvokeParse(json);
            Assert.AreEqual(1, items.Count);

            object dto   = items[0];
            var    scale = GetField<Vector2Int>(dto, "ScaleOverride");

            Assert.AreEqual(new Vector2Int(256, 320), scale,
                "ScaleOverride must match the [w, h] array in the 'overrides' block.");
        }

        [Test]
        public void ParseInstances_NoOverrides_ScaleOverrideIsZero()
        {
            // When no 'overrides' block is present, ScaleOverride stays at (0,0),
            // which signals BuildingLoader.SpawnInstance to use template.originalScale.
            const string json =
                @"[{""id"":5,""template_id"":2,""zone"":""lobby"",""rel_x"":50,""rel_y"":50}]";

            IList items = InvokeParse(json);
            Assert.AreEqual(1, items.Count);

            var scale = GetField<Vector2Int>(items[0], "ScaleOverride");
            Assert.AreEqual(Vector2Int.zero, scale,
                "ScaleOverride should default to (0,0) when no overrides block is present.");
        }

        [Test]
        public void ParseInstances_SplitRatioOverride_ParsesFromOverrides()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0," +
                @"""overrides"":{""split_ratio"":0.6}}]";

            IList items = InvokeParse(json);
            Assert.AreEqual(1, items.Count);

            float sr = GetField<float>(items[0], "SplitRatioOverride");
            Assert.AreEqual(0.6f, sr, 0.001f,
                "SplitRatioOverride must equal the value from the 'overrides' block.");
        }

        [Test]
        public void ParseInstances_NoSplitRatioOverride_DefaultsToNegativeOne()
        {
            // Negative SplitRatioOverride means "use template.splitRatio".
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0}]";

            IList items = InvokeParse(json);
            float sr = GetField<float>(items[0], "SplitRatioOverride");

            Assert.Less(sr, 0f,
                "SplitRatioOverride must default to a negative value when absent " +
                "(signals BuildingLoader to use template.splitRatio).");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 3. Coordinate-conversion tests
        // ─────────────────────────────────────────────────────────────────────────

        // Constants matching BuildingLoader (Python→Unity coordinate mapping).
        [Test]
        public void ParseInstances_ColliderScopeOverride_ParsesFromOverrides()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0," +
                @"""overrides"":{""collider_scope"":""CU""}}]";

            IList items = InvokeParse(json);
            Assert.AreEqual(1, items.Count);

            string scope = GetField<string>(items[0], "ColliderScopeOverride");
            Assert.AreEqual("CU", scope,
                "ColliderScopeOverride must be parsed from overrides.collider_scope.");
        }

        private const float PPU = 32f;

        [Test]
        public void CoordinateFormula_XAxis_PlacesBuildingHorizontallyCentered()
        {
            // worldX = gridOffset.x + (rel_x + effW * 0.5f) / PPU
            // The + effW/2 centers the building on its anchor (bottom-center pivot).
            const float gridX = 50f;
            const int   relX  = 0, effW = 64;

            float worldX = gridX + (relX + effW * 0.5f) / PPU;

            Assert.AreEqual(50f + 32f / 32f, worldX, 0.001f,
                "Building with relX=0, effW=64 should be at gridOffset.x + 1.");
        }

        [Test]
        public void CoordinateFormula_YAxis_InvertsAndShiftsCorrectly()
        {
            // worldY = gridOffset.y + (zoneH - 1) - (rel_y + effH) / PPU
            // Python Y is top-down; Unity Y is bottom-up. The inversion ensures
            // buildings at Python Y=0 appear at the top of the zone tile-grid.
            const float gridY  = 50f;
            const int   zoneH  = 50;
            const int   relY   = 0, effH = 32;

            float worldY = gridY + (zoneH - 1) - (relY + effH) / PPU;

            Assert.AreEqual(50f + 49f - 1f, worldY, 0.001f,
                "Building at Python Y=0 should map to gridOffset.y + zoneH - 1 - effH/PPU.");
        }

        [Test]
        public void CoordinateFormula_KnownEntry_MatchesPythonExpected()
        {
            // From buildings_instances.json entry id=8:
            //   template_id=8, zone=lobby, rel_x=542, rel_y=492, scale=[201,258]
            // Zone lobby: gridOffset=(50,50), zoneHeight=50 tiles
            // Expected (Python formula):
            //   worldX = 50 + (542 + 201*0.5) / 32 = 50 + (542+100.5)/32 = 50 + 642.5/32 ≈ 70.078
            //   worldY = 50 + 49 - (492+258)/32     = 99 - 750/32                         ≈ 75.5625
            const float gridX = 50f, gridY = 50f;
            const int   zoneH = 50;
            const int   relX = 542, relY = 492, effW = 201, effH = 258;

            float worldX = gridX + (relX + effW * 0.5f) / PPU;
            float worldY = gridY + (zoneH - 1) - (relY + effH) / PPU;

            Assert.AreEqual(70.078f, worldX, 0.01f, "WorldX for lobby building id=8.");
            Assert.AreEqual(75.5625f, worldY, 0.01f, "WorldY for lobby building id=8.");
        }

        [Test]
        public void CoordinateFormula_LargeRelY_DoesNotProduceNegativeY()
        {
            // A building placed deep in a zone (large relY) must still have a
            // sane Y position (within the zone bounds, not below the grid).
            const float gridY = 50f;
            const int   zoneH = 50;
            // Maximum reasonable relY = zoneHeight * PPU = 50 * 32 = 1600 px
            const int   relY = 1500, effH = 32;

            float worldY = gridY + (zoneH - 1) - (relY + effH) / PPU;

            // Zone spans gridY to gridY + zoneH = 50..100
            // So worldY in [50, 100] is valid (allow a small margin for large buildings).
            Assert.GreaterOrEqual(worldY, gridY - 5f,
                "WorldY must not be far below the zone's bottom edge.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 4. BuildingsDataGuard — backup integrity tests
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Verifies that a .bak copy of buildings_instances.json is maintained under
        /// _Project/Data/Backups/. This backup is used by BuildingsDataGuard to
        /// auto-restore the file if it is accidentally deleted from StreamingAssets.
        /// If this test fails run: Valkur > Migration > Dry-Run All  or just open
        /// the Buildings Runtime Editor (F10) and press Save once.
        /// </summary>
        [Test]
        [Category("DataIntegrity")]
        public void BackupFile_Exists_AtExpectedPath()
        {
            string backupPath = Path.Combine(
                Application.dataPath, "_Project", "Data", "Backups",
                "buildings_instances.json.bak");

            Assert.IsTrue(File.Exists(backupPath),
                $"Backup file is missing at:\n  {backupPath}\n" +
                "The backup is created/refreshed every time you save from BuildingsRuntimeEditor " +
                "or run an importer. Open the Buildings Editor (F10 in Play Mode) and press Save.");
        }

        /// <summary>
        /// Verifies that the .bak count matches the live StreamingAssets count.
        /// If they differ someone ran the importer after the last in-engine save
        /// (or vice-versa) and the backup is stale.
        /// </summary>
        [Test]
        [Category("DataIntegrity")]
        public void BackupFile_EntryCountMatchesLiveFile()
        {
            string livePath = Path.Combine(
                Application.streamingAssetsPath, "Buildings", "buildings_instances.json");
            string backupPath = Path.Combine(
                Application.dataPath, "_Project", "Data", "Backups",
                "buildings_instances.json.bak");

            Assume.That(File.Exists(livePath),   "Live buildings_instances.json not found — covered by InstancesJson_Exists test.");
            Assume.That(File.Exists(backupPath), "Backup file not found — covered by BackupFile_Exists test.");

            IList liveItems   = InvokeParse(File.ReadAllText(livePath));
            IList backupItems = InvokeParse(File.ReadAllText(backupPath));

            Assert.IsNotNull(liveItems,   "Live file could not be parsed.");
            Assert.IsNotNull(backupItems, "Backup file could not be parsed.");
            Assert.AreEqual(liveItems.Count, backupItems.Count,
                $"Backup ({backupItems.Count}) and live ({liveItems.Count}) entry counts differ.\n" +
                "Refresh the backup: open the Buildings Editor (F10) and press Save — OR call " +
                "BuildingsDataGuard.RefreshBackup() from any editor script.");
        }

        /// <summary>
        /// Confirms that the live file has the expected minimum number of entries
        /// (142 is the count at commit dfa57b25a — the baseline after in-engine edits).
        /// If the count drops below 142, data was lost.
        /// </summary>
        [Test]
        [Category("DataIntegrity")]
        public void InstancesJson_HasAtLeast142Entries()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath, "Buildings", "buildings_instances.json");
            Assume.That(File.Exists(path), "File not found — covered by InstancesJson_Exists test.");

            IList items = InvokeParse(File.ReadAllText(path));
            Assume.That(items != null, "Parser returned null.");

            Assert.GreaterOrEqual(items.Count, 142,
                $"Expected at least 142 buildings but found {items.Count}.\n" +
                "Possible causes:\n" +
                "  • WorldZoneImporter overwrote the Unity file with an older Python version.\n" +
                "  • BuildingImporter.CopyInstances ran without the safety guard.\n" +
                "  • The file was restored from a stale backup.\n" +
                "Restore with:  git checkout dfa57b25a -- " +
                "unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json");
        }

        /// <summary>
        /// Verifies that all entries have valid positive IDs and required fields
        /// so BuildingLoader can spawn every building without silent failures.
        /// </summary>
        [Test]
        [Category("DataIntegrity")]
        public void InstancesJson_AllEntries_HavePositiveIdAndRequiredFields()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath, "Buildings", "buildings_instances.json");
            Assume.That(File.Exists(path));

            IList items = InvokeParse(File.ReadAllText(path));
            Assume.That(items != null && items.Count > 0, "No items to validate.");

            var invalidIds    = new System.Collections.Generic.List<int>();
            var emptyZones    = new System.Collections.Generic.List<int>();
            var zeroTemplates = new System.Collections.Generic.List<int>();

            foreach (object dto in items)
            {
                int    id         = GetField<int>(dto, "Id");
                int    templateId = GetField<int>(dto, "TemplateId");
                string zone       = GetField<string>(dto, "Zone");

                if (id <= 0)                                 invalidIds.Add(id);
                if (string.IsNullOrWhiteSpace(zone))         emptyZones.Add(id);
                if (templateId <= 0)                         zeroTemplates.Add(id);
            }

            Assert.IsEmpty(invalidIds,
                $"Entries with id ≤ 0: [{string.Join(", ", invalidIds)}]. All IDs must be positive.");
            Assert.IsEmpty(emptyZones,
                $"Entries with empty zone (IDs): [{string.Join(", ", emptyZones)}]. Zone is required.");
            Assert.IsEmpty(zeroTemplates,
                $"Entries with template_id ≤ 0 (IDs): [{string.Join(", ", zeroTemplates)}]. " +
                "BuildingLoader will skip entries without a valid template.");
        }
    }
}
