using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Tests for <see cref="BuildingsRuntimeEditor"/> JSON serialization (Gap 10 – Save).
    ///
    /// Covers:
    ///   • Coordinate conversion formula (relX / relY) matching Python pixel coords
    ///   • InvariantCulture formatting of split_ratio (no locale comma)
    ///   • JSON structure: required keys, overrides key, sequential IDs
    ///   • Inactive buildings are excluded from the output
    ///
    /// Python reference: roguelike_editors/buildings/building_editor.py
    ///   buildings_data.json: { "id", "template_id", "zone", "rel_x", "rel_y" [, "overrides"] }
    ///   rel_x = (building.x - zone.x_off) * PPU - eff_w // 2
    ///   rel_y = (zone.y_off + (zone_h - 1) - building.y) * PPU - eff_h
    /// </summary>
    [TestFixture]
    public class BuildingsSaveFormatTests
    {
        // SaveInstancesToJson writes real StreamingAssets files; preserve them so
        // serialization tests cannot corrupt the project data-integrity fixtures.
        private readonly Dictionary<string, string> _fileBackups = new Dictionary<string, string>();

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Ensure any MapEditorActiveSlot overrides left by this or a prior
            // test fixture are cleared so they cannot pollute subsequent test runs
            // or manual Play mode sessions (Domain Reload is OFF in this project).
            Valkur.Core.MapEditorActiveSlot.SetOverrideForTests(null);
            Valkur.Core.MapEditorActiveSlot.SetStreamingRootOverrideForTests(null);
            Valkur.Core.MapEditorActiveSlot.SetPersistentRootOverrideForTests(null);

            foreach (var kvp in _fileBackups)
            {
                if (kvp.Value != null)
                    File.WriteAllText(kvp.Key, kvp.Value);
                else if (File.Exists(kvp.Key))
                    File.Delete(kvp.Key);
            }
            _fileBackups.Clear();

            // Clean up any BuildingObjects left in the scene.
            foreach (var b in Object.FindObjectsOfType<BuildingObject>())
                if (b != null) Object.DestroyImmediate(b.gameObject);

            foreach (var e in Object.FindObjectsOfType<BuildingsRuntimeEditor>())
                if (e != null) Object.DestroyImmediate(e.gameObject);
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static T CreateSingleton<T>(string name = "TestGO") where T : MonoBehaviour
        {
            ClearSingletonInstance<T>();
            var go   = new GameObject(name);
            var comp = go.AddComponent<T>();
            var fi   = FindInstanceField(comp, "_toggleAction");
            if (fi?.GetValue(comp) == null) InvokeMethod(comp, "OnSingletonAwake");
            return comp;
        }

        private static FieldInfo FindInstanceField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static void SetField(object obj, string name, object value)
            => FindInstanceField(obj, name)?.SetValue(obj, value);

        private static void InvokeMethod(object obj, string name, params object[] args)
        {
            var t = obj.GetType();
            System.Reflection.MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        private static T GetFieldValue<T>(object obj, string name)
        {
            var field = FindInstanceField(obj, name);
            return field != null ? (T)field.GetValue(obj) : default;
        }

        private static BuildingTemplateData MakeTemplate(int id, int origW = 64, int origH = 64)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.templateId   = id;
            t.originalScale = new Vector2Int(origW, origH);
            t.colliderScope = "CG";
            t.splitRatio    = 0.5f;
            return t;
        }

        private static BuildingObject MakeBuildingInScene(
            string name, BuildingTemplateData tmpl,
            int instanceId = 1, string zone = "Lobby",
            float splitOverride = -1f, Vector2Int scaleOverride = default)
        {
            var go   = new GameObject(name);
            var bObj = go.AddComponent<BuildingObject>();
            SetField(bObj, "_template", tmpl);
            bObj.InstanceId         = instanceId;
            bObj.ZoneName           = zone;
            bObj.SplitRatioOverride = splitOverride;
            bObj.ScaleOverride      = scaleOverride;
            return bObj;
        }

        private static ZoneManager MakeZoneManager()
        {
            var go = new GameObject("TestZoneManager");
            var zm = go.AddComponent<ZoneManager>();
            zm.AddZone("Lobby", Vector2Int.zero, true);
            return zm;
        }

        private string InvokeAndReadJson(BuildingsRuntimeEditor editor)
        {
            CaptureSaveOutputs();
            InvokeMethod(editor, "SaveInstancesToJson");
            string dir  = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string path = Path.Combine(dir, "buildings_instances.json");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path);
        }

        private void CaptureSaveOutputs()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Buildings");
            CaptureFile(Path.Combine(dir, "buildings_instances.json"));
            CaptureFile(Path.Combine(dir, "buildings_collisions_by_image.json"));
            CaptureFile(Path.Combine(dir, "buildings_collisions_by_building_instance_id.json"));
        }

        private static string ReadSavedInstancesJson()
        {
            string dir  = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string path = Path.Combine(dir, "buildings_instances.json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private void CaptureFile(string path)
        {
            if (_fileBackups.ContainsKey(path)) return;
            _fileBackups[path] = File.Exists(path) ? File.ReadAllText(path) : null;
        }

        // ── Pure-formula unit tests (no Unity objects needed) ─────────────────────

        // Python:  rel_x = (building.x - zone.x_off) * PPU - eff_w // 2
        // C#:      relX  = Mathf.RoundToInt((wx - zd.gridOffset.x) * PPU - effW * 0.5f)

        [Test]
        public void RelX_Formula_CorrectForKnownValues()
        {
            // wx=5, gridOffset.x=3, PPU=32, effW=64 → (5-3)*32 - 32 = 64 - 32 = 32
            const float wx = 5f, gridX = 3f, PPU = 32f, effW = 64f;
            int expected = Mathf.RoundToInt((wx - gridX) * PPU - effW * 0.5f);
            Assert.AreEqual(32, expected,
                "relX formula: (wx - gridOffset.x) * PPU - effW * 0.5f should equal 32.");
        }

        [Test]
        public void RelX_Formula_AtGridOrigin_OnlySubtractsHalfWidth()
        {
            // wx == gridOffset.x → first term = 0 → relX = -effW/2
            const float wx = 3f, gridX = 3f, PPU = 32f, effW = 64f;
            int expected = Mathf.RoundToInt((wx - gridX) * PPU - effW * 0.5f);
            Assert.AreEqual(-32, expected,
                "At grid origin, relX should equal -effW/2 (building anchored left of origin).");
        }

        [Test]
        public void RelX_Formula_SymmetryAroundCenter()
        {
            // If building is one tile to the RIGHT of center, relX should be positive.
            // PPU=32, effW=32 (1 tile wide), wx=1, gridX=0
            // relX = (1-0)*32 - 16 = 16
            const float wx = 1f, gridX = 0f, PPU = 32f, effW = 32f;
            int relX = Mathf.RoundToInt((wx - gridX) * PPU - effW * 0.5f);
            Assert.Greater(relX, 0,
                "Building one tile right of grid origin should produce positive relX.");
        }

        // Python:  rel_y = (zone.y_off + (zone_h - 1) - building.y) * PPU - eff_h
        // C#:      relY  = Mathf.RoundToInt((zd.gridOffset.y + (zH - 1) - wy) * PPU - effH)

        [Test]
        public void RelY_Formula_CorrectForKnownValues()
        {
            // wy=10, gridOffset.y=0, zH=20, PPU=32, effH=128
            // relY = (0 + 19 - 10)*32 - 128 = 9*32 - 128 = 288 - 128 = 160
            const float wy = 10f, gridY = 0f, PPU = 32f, effH = 128f;
            const int   zH = 20;
            int expected = Mathf.RoundToInt((gridY + (zH - 1) - wy) * PPU - effH);
            Assert.AreEqual(160, expected,
                "relY formula: (gridOffset.y + (zH-1) - wy) * PPU - effH should equal 160.");
        }

        [Test]
        public void RelY_Formula_AtTopRow_ReturnsNegativeHeight()
        {
            // wy = gridOffset.y + (zH-1) → top row → first term = 0 → relY = -effH
            const float gridY = 0f, PPU = 32f, effH = 64f;
            const int   zH = 20;
            float wy = gridY + (zH - 1); // top row in Python grid
            int expected = Mathf.RoundToInt((gridY + (zH - 1) - wy) * PPU - effH);
            Assert.AreEqual(-64, expected,
                "At top row, relY should equal -effH (building anchored above grid).");
        }

        [Test]
        public void RelY_Formula_IncreasesAsWorldYDecreases()
        {
            // Lower wy (further south on screen) → higher relY value
            const float gridY = 0f, PPU = 32f, effH = 32f;
            const int   zH = 20;
            int relYHigh = Mathf.RoundToInt((gridY + (zH - 1) - 5f) * PPU - effH);
            int relYLow  = Mathf.RoundToInt((gridY + (zH - 1) - 10f) * PPU - effH);
            Assert.Greater(relYHigh, relYLow,
                "relY increases as wy decreases — buildings further south have larger row index.");
        }

        // ── InvariantCulture formatting ───────────────────────────────────────────

        [Test]
        public void SplitRatioFormat_UsesDot_NotComma()
        {
            // Some locales (e.g. Spanish, German) format 0.45 as "0,45".
            // Buildings JSON must use invariant culture dot separator.
            string formatted = string.Format(
                CultureInfo.InvariantCulture,
                "\"split_ratio\": {0:F4}", 0.45f);

            Assert.IsFalse(formatted.Contains(','),
                "split_ratio must use a decimal point (.) not a comma (,) regardless of locale.");
            Assert.IsTrue(formatted.Contains('.'),
                "split_ratio must contain a decimal point.");
        }

        [Test]
        public void SplitRatioFormat_HasFourDecimalPlaces()
        {
            string formatted = string.Format(
                CultureInfo.InvariantCulture,
                "\"split_ratio\": {0:F4}", 0.5f);

            // Should produce "0.5000" (4 decimal places).
            Assert.IsTrue(formatted.Contains("0.5000"),
                "split_ratio F4 format should produce exactly 4 decimal places: got: " + formatted);
        }

        [Test]
        public void SplitRatioFormat_DoesNotChangeWith_CurrentCulture()
        {
            // Simulate a locale that uses comma as decimal separator.
            var savedCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                string withDefault   = string.Format("{0:F4}", 0.45f);
                string withInvariant = string.Format(CultureInfo.InvariantCulture, "{0:F4}", 0.45f);

                // German locale formats with comma: "0,4500"
                // Invariant locale must always use dot: "0.4500"
                Assert.IsTrue(withInvariant.Contains('.'),
                    "Invariant culture must use dot even when CurrentCulture is German.");
                Assert.IsFalse(withInvariant.Contains(','),
                    "Invariant culture must never produce a comma separator.");

                // Sanity-check: ensure de-DE actually uses comma (validates the test assumption).
                if (withDefault.Contains(','))
                {
                    // German locale confirmed — our invariant fix is essential.
                    Assert.AreNotEqual(withDefault, withInvariant,
                        "Under German locale, default format differs from invariant format.");
                }
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = savedCulture;
            }
        }

        // ── Integration tests — JSON output written to disk ───────────────────────

        [Test]
        public void SaveJson_SingleBuilding_ProducesValidJsonArray()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 10, origW: 64, origH: 64);
            MakeBuildingInScene("B1", tmpl, instanceId: 99, zone: "Village");

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json, "SaveInstancesToJson must write a file to StreamingAssets/Buildings/.");
            Assert.IsTrue(json.TrimStart().StartsWith("["),  "JSON must start with '['.");
            Assert.IsTrue(json.TrimEnd().EndsWith("]"),     "JSON must end with ']'.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_SingleBuilding_ContainsRequiredKeys()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 7);
            MakeBuildingInScene("B1", tmpl, instanceId: 1, zone: "Lobby");

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.Contains("\"id\"",          json, "JSON entry must have 'id' key.");
            StringAssert.Contains("\"template_id\"",  json, "JSON entry must have 'template_id' key.");
            StringAssert.Contains("\"zone\"",         json, "JSON entry must have 'zone' key.");
            StringAssert.Contains("\"rel_x\"",        json, "JSON entry must have 'rel_x' key.");
            StringAssert.Contains("\"rel_y\"",        json, "JSON entry must have 'rel_y' key.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_TemplateId_MatchesBuildingTemplate()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 42);
            MakeBuildingInScene("B1", tmpl, instanceId: 1);

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.Contains("\"template_id\": 42", json,
                "template_id in JSON must match the assigned BuildingTemplateData.templateId.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_ZoneName_WrittenToOutput()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 1);
            MakeBuildingInScene("B1", tmpl, instanceId: 1, zone: "ForestZone");

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.Contains("\"ForestZone\"", json,
                "The zone field must reflect the BuildingObject.ZoneName value.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_IdsReassigned_SequentiallyFrom1()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 1);

            // Create buildings with scattered IDs — they should be renumbered 1, 2, 3.
            MakeBuildingInScene("B1", tmpl, instanceId: 50);
            MakeBuildingInScene("B2", tmpl, instanceId: 20);
            MakeBuildingInScene("B3", tmpl, instanceId: 100);

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.Contains("\"id\": 1", json, "First building ID must be reassigned to 1.");
            StringAssert.Contains("\"id\": 2", json, "Second building ID must be reassigned to 2.");
            StringAssert.Contains("\"id\": 3", json, "Third building ID must be reassigned to 3.");
            StringAssert.DoesNotContain("\"id\": 50",  json, "Original ID 50 must be replaced.");
            StringAssert.DoesNotContain("\"id\": 100", json, "Original ID 100 must be replaced.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_WithNoOverrides_OmitsOverridesKey()
        {
            // SplitRatioOverride = -1 and ScaleOverride = (0,0) → no overrides block
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 1);
            MakeBuildingInScene("B1", tmpl, instanceId: 1, splitOverride: -1f, scaleOverride: Vector2Int.zero);

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.DoesNotContain("\"overrides\"", json,
                "Buildings with no scale/split overrides must not have an 'overrides' key in JSON.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_WithSplitRatioOverride_WritesDecimalPoint_NotComma()
        {
            // Primary regression guard: split_ratio must use InvariantCulture.
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 1);
            MakeBuildingInScene("B1", tmpl, instanceId: 1, splitOverride: 0.3f);

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.Contains("\"overrides\"", json,
                "SplitRatioOverride >= 0 must produce an overrides block.");
            StringAssert.Contains("split_ratio", json,
                "split_ratio key must appear inside overrides.");

            // The critical assertion: decimal point, not comma.
            StringAssert.DoesNotContain("\"split_ratio\": 0,", json,
                "split_ratio value must never use a locale comma (e.g. '0,3000').");
            StringAssert.Contains("split_ratio\": 0.", json,
                "split_ratio value must use a decimal point.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_WithScaleOverride_WritesScaleArray()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 1);
            MakeBuildingInScene("B1", tmpl, instanceId: 1, scaleOverride: new Vector2Int(128, 256));

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.Contains("\"scale\"", json,
                "Scale override must produce a 'scale' key inside overrides.");
            StringAssert.Contains("[128, 256]", json,
                "Scale override values must match the assigned ScaleOverride.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_InactiveBuildingsExcluded_FromOutput()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 1);

            var activeBuilding   = MakeBuildingInScene("Active",   tmpl, instanceId: 1, zone: "Lobby");
            var inactiveBuilding = MakeBuildingInScene("Inactive", tmpl, instanceId: 2, zone: "DeletedZone");
            inactiveBuilding.gameObject.SetActive(false); // Simulates a deleted building

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.DoesNotContain("DeletedZone", json,
                "Inactive buildings (marked deleted) must not appear in the saved JSON.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void SaveJson_NoBuildingsInScene_WritesEmptyArray()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            // No BuildingObjects created.

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json, "Save should succeed even with an empty scene.");
            // The file should contain a valid JSON array (possibly just "[\n]").
            StringAssert.Contains("[",  json);
            StringAssert.Contains("]",  json);
            StringAssert.DoesNotContain("\"id\"", json, "Empty scene must produce no building entries.");

            Object.DestroyImmediate(editor.gameObject);
        }

        [Test]
        public void SaveJson_ZoneFallback_UsesLobby_WhenZoneNameIsNull()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 1);
            var bObj   = MakeBuildingInScene("B1", tmpl, instanceId: 1);
            bObj.ZoneName = null; // Should fall back to "Lobby"

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json);
            StringAssert.Contains("\"Lobby\"", json,
                "Null ZoneName must fall back to 'Lobby' in the JSON output.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void MoveCommit_PersistsUpdatedPositionImmediately()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var zm     = MakeZoneManager();
            var tmpl   = MakeTemplate(id: 5, origW: 64, origH: 64);
            var bObj   = MakeBuildingInScene("B1", tmpl, instanceId: 1, zone: "Lobby");
            bObj.transform.position = new Vector3(1f, 2f, 0f);
            CaptureSaveOutputs();

            SetField(editor, "_activeBuilding", bObj);
            SetField(editor, "_dragStartWorldPos", bObj.transform.position);

            bObj.transform.position = new Vector3(3f, 2f, 0f);
            InvokeMethod(editor, "FinalizeMoveDrag");

            string json = ReadSavedInstancesJson();

            Assert.IsNotNull(json);
            StringAssert.Contains("\"rel_x\": 64", json,
                "Moving a 64px-wide building from x=1 to x=3 should persist the new rel_x immediately.");
            Assert.IsFalse(GetFieldValue<bool>(editor, "_hasUnsavedInstanceChanges"),
                "Autosave after move commit must clear the dirty flag.");

            Object.DestroyImmediate(zm.gameObject);
            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void ResizeCommit_PersistsScaleOverrideImmediately()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var tmpl   = MakeTemplate(id: 9, origW: 64, origH: 64);
            var bObj   = MakeBuildingInScene("B1", tmpl, instanceId: 1, zone: "Lobby");
            bObj.ScaleOverride = new Vector2Int(128, 128);
            CaptureSaveOutputs();

            SetField(editor, "_activeBuilding", bObj);
            SetField(editor, "_resizeStartScale", new Vector2Int(64, 64));

            InvokeMethod(editor, "FinalizeResizeDrag");

            string json = ReadSavedInstancesJson();

            Assert.IsNotNull(json);
            StringAssert.Contains("\"scale\": [128, 128]", json,
                "Resizing a building must persist the new scale override as soon as the drag commits.");
            Assert.IsFalse(GetFieldValue<bool>(editor, "_hasUnsavedInstanceChanges"),
                "Autosave after resize commit must clear the dirty flag.");

            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void OnApplicationQuit_PersistsDirtyTransformsWithoutManualSave()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var zm     = MakeZoneManager();
            var tmpl   = MakeTemplate(id: 11, origW: 64, origH: 64);
            var bObj   = MakeBuildingInScene("B1", tmpl, instanceId: 1, zone: "Lobby");
            bObj.transform.position = new Vector3(4f, 2f, 0f);
            CaptureSaveOutputs();

            SetField(editor, "_activeBuilding", bObj);
            SetField(editor, "_hasUnsavedInstanceChanges", true);

            InvokeMethod(editor, "OnApplicationQuit");

            string json = ReadSavedInstancesJson();

            Assert.IsNotNull(json);
            StringAssert.Contains("\"rel_x\": 96", json,
                "Closing the game with dirty building edits must flush the latest transform to disk.");
            Assert.IsFalse(GetFieldValue<bool>(editor, "_hasUnsavedInstanceChanges"),
                "Shutdown persistence must clear the dirty flag after a successful write.");

            Object.DestroyImmediate(zm.gameObject);
            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl);
        }

        // ── Regression: each building must retain its own position after save ─────
        //
        // Corruption pattern observed empirically (May 2026): after moving 2
        // buildings and saving, every building inside a given zone received the
        // SAME rel_x/rel_y — collapsing 337 distinct positions to 16 (one per
        // zone). The bug would be caused by the save loop reading position from a
        // shared/cached value (e.g. _activeBuilding.transform.position) instead
        // of from the per-iteration BuildingObject `b`.
        //
        // This test deliberately places N buildings with DIFFERENT world positions
        // in the SAME zone and asserts that each serialises to a DIFFERENT rel_x.
        // It must FAIL if the save loop accidentally uses a shared position, and
        // PASS only when every building's individual transform is read.

        [Test]
        public void SaveJson_MultipleBuildingsInSameZone_EachHasDistinctRelPosition()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestEditor");
            var zm     = MakeZoneManager(); // Lobby at gridOffset=(0,0)

            // Three templates with DIFFERENT widths so effW varies per building.
            var tmpl32  = MakeTemplate(id: 1, origW: 32, origH: 32);
            var tmpl64  = MakeTemplate(id: 2, origW: 64, origH: 64);
            var tmpl128 = MakeTemplate(id: 3, origW: 128, origH: 128);

            // Place buildings at clearly distinct world X positions.
            //   B1 at x=2 → relX = (2-0)*32 - 32/2  = 64 - 16 = 48
            //   B2 at x=5 → relX = (5-0)*32 - 64/2  = 160 - 32 = 128
            //   B3 at x=9 → relX = (9-0)*32 - 128/2 = 288 - 64 = 224
            var bObj1 = MakeBuildingInScene("B1", tmpl32,  instanceId: 1, zone: "Lobby");
            var bObj2 = MakeBuildingInScene("B2", tmpl64,  instanceId: 2, zone: "Lobby");
            var bObj3 = MakeBuildingInScene("B3", tmpl128, instanceId: 3, zone: "Lobby");

            bObj1.transform.position = new Vector3(2f, 3f, 0f);
            bObj2.transform.position = new Vector3(5f, 3f, 0f);
            bObj3.transform.position = new Vector3(9f, 3f, 0f);

            // Simulate having dragged bObj2 (the "active" building) to its current
            // position — exactly the scenario that triggered the empirical corruption.
            SetField(editor, "_activeBuilding", bObj2);

            string json = InvokeAndReadJson(editor);

            Assert.IsNotNull(json, "SaveInstancesToJson must write a file.");

            // Parse all rel_x values and verify they are distinct.
            // A collapse bug would produce the same rel_x for all three entries.
            // The JSON format is compact (all fields on one line), so we find the
            // "rel_x": token and then read digits until the next non-digit character.
            var relXValues = new System.Collections.Generic.List<int>();
            const string RelXToken = "\"rel_x\": ";
            int searchFrom = 0;
            while (true)
            {
                int idx = json.IndexOf(RelXToken, searchFrom, System.StringComparison.Ordinal);
                if (idx < 0) break;
                int numStart = idx + RelXToken.Length;
                // Handle optional leading minus sign.
                int numEnd = numStart;
                if (numEnd < json.Length && json[numEnd] == '-') numEnd++;
                while (numEnd < json.Length && char.IsDigit(json[numEnd])) numEnd++;
                if (int.TryParse(json.Substring(numStart, numEnd - numStart), out int rx))
                    relXValues.Add(rx);
                searchFrom = numEnd;
            }

            Assert.AreEqual(3, relXValues.Count,
                "Exactly 3 rel_x values expected (one per building).");

            // Verify the EXPECTED per-building values — guards against any
            // formula regression as well as the shared-position collapse bug.
            Assert.AreEqual(48,  relXValues[0], "B1 (w=32) at x=2 must produce rel_x=48.");
            Assert.AreEqual(128, relXValues[1], "B2 (w=64) at x=5 must produce rel_x=128.");
            Assert.AreEqual(224, relXValues[2], "B3 (w=128) at x=9 must produce rel_x=224.");

            // Explicit uniqueness assertion — the regression guard.
            Assert.AreEqual(3, new System.Collections.Generic.HashSet<int>(relXValues).Count,
                "All three buildings in the same zone must have DISTINCT rel_x values. " +
                "A shared-position collapse bug (reading _activeBuilding instead of b) " +
                "would cause all three to share the same value.");

            Object.DestroyImmediate(zm.gameObject);
            Object.DestroyImmediate(editor.gameObject);
            Object.DestroyImmediate(tmpl32);
            Object.DestroyImmediate(tmpl64);
            Object.DestroyImmediate(tmpl128);
        }

        // ── ValidatePositionUniqueness — defensive sanity guard ─────────────
        //
        // These tests pin down the disk-protection guard added after a
        // catastrophic data-loss incident: 200+ buildings inside a single
        // zone collapsed onto 16 unique (rel_x, rel_y) tuples, irreversibly
        // corrupting buildings_instances.json. The Save now runs this check
        // BEFORE the disk write and refuses to persist a collapsed state.

        private static bool InvokeValidatePositionUniqueness(
            int totalBuildings,
            Dictionary<(string zone, int relX, int relY), int> positionCounts,
            out string reason)
        {
            var method = typeof(BuildingsRuntimeEditor).GetMethod(
                "ValidatePositionUniqueness",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method, "ValidatePositionUniqueness method not found on BuildingsRuntimeEditor.");
            var args = new object[] { totalBuildings, positionCounts, null };
            bool result = (bool)method.Invoke(null, args);
            reason = (string)args[2];
            return result;
        }

        [Test]
        public void ValidatePositionUniqueness_AcceptsHealthyState()
        {
            // 337 buildings spread across 337 unique tuples — the on-disk
            // shape that survived the user's actual bug session.
            var counts = new Dictionary<(string, int, int), int>();
            for (int i = 0; i < 337; i++)
                counts[("Lobby", i, i)] = 1;

            bool ok = InvokeValidatePositionUniqueness(337, counts, out string reason);

            Assert.IsTrue(ok, $"Healthy state should validate, got reason: {reason}");
            Assert.IsNull(reason);
        }

        [Test]
        public void ValidatePositionUniqueness_RejectsCorruptionSignature()
        {
            // The exact corruption pattern observed: 337 buildings collapsed
            // onto 16 unique positions, with 67 stacked at one of them.
            var counts = new Dictionary<(string, int, int), int>
            {
                {("Lobby",       0, 0), 47},
                {("Forest",      0, 0), 67},
                {("zone_100_50", 0, 0), 29},
                {("zone_100_100",0, 0), 47},
                {("dungeon",     0, 0), 10},
            };
            for (int i = 5; i < 16; i++) counts[($"zone_{i}", 0, 0)] = 12; // fill to 16 zones

            bool ok = InvokeValidatePositionUniqueness(337, counts, out string reason);

            Assert.IsFalse(ok, "Corrupt state must be rejected.");
            StringAssert.Contains("buildings collapsed", reason ?? "",
                "Reason should mention the building collapse.");
        }

        [Test]
        public void ValidatePositionUniqueness_RejectsAbsoluteThreshold()
        {
            // Small map but a single position has 5 buildings stacked —
            // crosses the absolute threshold even though uniqueness is fine.
            var counts = new Dictionary<(string, int, int), int>
            {
                {("Lobby", 100, 200), 5},
                {("Lobby", 300, 400), 1},
                {("Lobby", 500, 600), 1},
            };

            bool ok = InvokeValidatePositionUniqueness(7, counts, out string reason);

            Assert.IsFalse(ok, "5 stacked at one position must trigger absolute guard.");
            StringAssert.Contains("rel=(100,200)", reason ?? "");
        }

        [Test]
        public void ValidatePositionUniqueness_AllowsSmallFixturesWithoutFalsePositive()
        {
            // 4 buildings, 2 unique positions (50% — below the 50% relative
            // threshold) but total < 20, so the relative guard must skip
            // and allow the save. EditMode test fixtures often have <20.
            var counts = new Dictionary<(string, int, int), int>
            {
                {("Lobby", 0, 0), 2},
                {("Lobby", 1, 1), 2},
            };

            bool ok = InvokeValidatePositionUniqueness(4, counts, out string reason);

            Assert.IsTrue(ok, $"Small fixtures must skip the relative guard, got: {reason}");
        }
    }
}
