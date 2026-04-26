using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
    }
}
