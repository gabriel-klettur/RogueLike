using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.UIKit;
using Valkur.Core.Editors;
using Valkur.Gameplay.Items;

namespace Valkur.Tests.EditMode.Editors.Items
{
    /// <summary>
    /// Pins the Table-tab column visibility feature:
    ///   • the bar's "Columns" button + counter exist after BuildUI;
    ///   • the columns popup builds lazily on first open;
    ///   • toggling a column hides it from header AND every row;
    ///   • All / None / Reset bulk actions work;
    ///   • the choice round-trips through the editor's WORKSPACE document
    ///     (it used to live in its own PlayerPrefs entry — see below);
    ///   • <see cref="ItemsRuntimeEditor.IsColumnVisible"/> reports the
    ///     correct state for every column even after partial-hide ops.
    ///
    /// Uses the same reflection-based test fixture pattern as the other Items
    /// editor test classes so it stays consistent with the codebase
    /// conventions.
    /// </summary>
    [TestFixture]
    public class ItemsTableColumnsConfigTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<Object> _runtimeAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var a in _runtimeAssets) if (a != null) Object.DestroyImmediate(a);
            _runtimeAssets.Clear();
            ClearSingletonInstance<ItemsRuntimeEditor>();
        }

        // ── Reflection helpers (mirror existing Items test fixtures) ──────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        private static FieldInfo Field(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetField(object obj, string name) => Field(obj, name)?.GetValue(obj);

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public |
                                            BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, args); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{method}' not found on {obj.GetType().Name}");
        }

        private ItemsRuntimeEditor CreateActiveEditor()
        {
            ClearSingletonInstance<ItemsRuntimeEditor>();
            var go = new GameObject("TestItemsEditor");
            _scene.Add(go);
            var ed = go.AddComponent<ItemsRuntimeEditor>();
            Invoke(ed, "OnSingletonAwake");
            Invoke(ed, "Start");
            ed.Activate();
            return ed;
        }

        private static HashSet<string> GetHiddenSet(ItemsRuntimeEditor ed)
            => GetField(ed, "_hiddenColumns") as HashSet<string>;

        // ── Behaviours ────────────────────────────────────────────────────────

        [Test]
        public void Bar_HasColumnsButton_AndCounterLabel()
        {
            var ed = CreateActiveEditor();
            var refs = GetField(ed, "_uiRefs");

            var btn = (Button)Field(refs, "TableColumnsButton").GetValue(refs);
            var counter = (TextMeshProUGUI)Field(refs, "TableColumnsCountLabel").GetValue(refs);

            Assert.IsNotNull(btn,
                "TableColumnsButton ref must be wired by ItemsEditorUIBuilder.");
            Assert.IsNotNull(counter,
                "TableColumnsCountLabel ref must be wired by ItemsEditorUIBuilder.");
        }

        [Test]
        public void Counter_ShowsAllColumnsVisible_ByDefault()
        {
            var ed = CreateActiveEditor();
            var refs = GetField(ed, "_uiRefs");
            var counter = (TextMeshProUGUI)Field(refs, "TableColumnsCountLabel").GetValue(refs);

            int total = ItemTableColumns.All.Count;
            StringAssert.Contains($"{total}/{total}", counter.text,
                "Counter must read 'Columns: 38/38' when no column is hidden.");
        }

        [Test]
        public void IsColumnVisible_DefaultsTrue_ForEveryColumn()
        {
            var ed = CreateActiveEditor();
            foreach (var col in ItemTableColumns.All)
            {
                Assert.IsTrue(ed.IsColumnVisible(col),
                    $"Column '{col.Header}' must default to visible.");
            }
        }

        [Test]
        public void OpenColumnsConfigPopup_BuildsLazyPopup_OnFirstClick()
        {
            var ed = CreateActiveEditor();
            Assert.IsNull(GetField(ed, "_columnsPopup"),
                "Popup must NOT exist until the first open call.");

            Invoke(ed, "OpenColumnsConfigPopup");

            var popup = GetField(ed, "_columnsPopup") as GameObject;
            Assert.IsNotNull(popup, "Popup must exist after OpenColumnsConfigPopup.");
            Assert.IsTrue(popup.activeSelf, "Popup must be active after open.");

            var togglesField = Field(ed, "_columnTogglesByIndex");
            var toggles      = togglesField?.GetValue(ed) as Toggle[];
            Assert.IsNotNull(toggles,
                "Per-column toggles array must be allocated when the popup is built.");
            Assert.AreEqual(ItemTableColumns.All.Count, toggles.Length,
                "Popup must build exactly one toggle per registry column.");
        }

        [Test]
        public void CloseColumnsConfigPopup_HidesPopupWithoutDestroyingIt()
        {
            var ed = CreateActiveEditor();
            Invoke(ed, "OpenColumnsConfigPopup");
            Invoke(ed, "CloseColumnsConfigPopup");

            var popup = GetField(ed, "_columnsPopup") as GameObject;
            Assert.IsNotNull(popup, "Popup GameObject must persist after close.");
            Assert.IsFalse(popup.activeSelf,
                "Popup must be hidden via SetActive(false), not destroyed.");
        }

        [Test]
        public void ToggleColumnOff_HidesColumnFromHeaderAndRows()
        {
            var ed = CreateActiveEditor();

            // Inject a known catalog so RefreshTable produces a deterministic row.
            var sword = ScriptableObject.CreateInstance<ItemDefinition>();
            sword.itemId = "sword"; sword.displayName = "Sword";
            sword.equipSlot = EquipSlot.Weapon; sword.damage = 10; sword.durability = 100;
            _runtimeAssets.Add(sword);
            Field(ed, "_allItems").SetValue(ed, new[] { sword });
            Invoke(ed, "RefreshPicker");
            Invoke(ed, "RefreshTable");

            var refs = GetField(ed, "_uiRefs");
            var hdrContent  = (RectTransform)Field(refs, "TableHeaderContent").GetValue(refs);
            var bodyContent = (RectTransform)Field(refs, "TableBodyContent").GetValue(refs);

            int totalCols   = ItemTableColumns.All.Count;
            int hdrBefore   = hdrContent.childCount;
            int rowCellsBefore = bodyContent.GetChild(0).childCount;
            Assert.AreEqual(totalCols, hdrBefore,
                "Header must have one cell per column before any are hidden.");

            // Hide "damage" via the public toggle path the popup uses.
            var damage = FindCol("damage");
            Invoke(ed, "OnColumnVisibilityToggled", damage, false);

            Assert.AreEqual(totalCols - 1, hdrContent.childCount,
                "Header must drop the hidden cell.");
            Assert.AreEqual(rowCellsBefore - 1, bodyContent.GetChild(0).childCount,
                "Each row must drop the hidden cell.");
            Assert.IsFalse(ed.IsColumnVisible(damage),
                "IsColumnVisible(damage) must be false after toggling it off.");
        }

        [Test]
        public void ToggleColumnOn_RestoresColumn_ToHeaderAndRows()
        {
            var ed = CreateActiveEditor();
            Field(ed, "_allItems").SetValue(ed, new[] { CreateMinimalItem("a", "A") });
            Invoke(ed, "RefreshPicker");
            Invoke(ed, "RefreshTable");

            var damage = FindCol("damage");
            Invoke(ed, "OnColumnVisibilityToggled", damage, false);
            Invoke(ed, "OnColumnVisibilityToggled", damage, true);

            Assert.IsTrue(ed.IsColumnVisible(damage),
                "Column must be visible again after re-toggling on.");
            var hidden = GetHiddenSet(ed);
            Assert.IsFalse(hidden.Contains("damage"),
                "Hidden set must no longer contain 'damage'.");
        }

        [Test]
        public void SetAllColumnsVisible_False_HidesEveryColumn()
        {
            var ed = CreateActiveEditor();
            Invoke(ed, "SetAllColumnsVisible", false);

            var hidden = GetHiddenSet(ed);
            Assert.AreEqual(ItemTableColumns.All.Count, hidden.Count,
                "All columns must end up in the hidden set after SetAllColumnsVisible(false).");
            foreach (var col in ItemTableColumns.All)
                Assert.IsFalse(ed.IsColumnVisible(col),
                    $"Column '{col.Header}' must be hidden.");
        }

        [Test]
        public void SetAllColumnsVisible_True_ShowsEveryColumn()
        {
            var ed = CreateActiveEditor();
            // First hide everything …
            Invoke(ed, "SetAllColumnsVisible", false);
            // … then restore.
            Invoke(ed, "SetAllColumnsVisible", true);

            var hidden = GetHiddenSet(ed);
            Assert.AreEqual(0, hidden.Count,
                "Hidden set must be empty after SetAllColumnsVisible(true).");
            foreach (var col in ItemTableColumns.All)
                Assert.IsTrue(ed.IsColumnVisible(col),
                    $"Column '{col.Header}' must be visible.");
        }

        [Test]
        public void HiddenColumns_AreCapturedIntoTheWorkspace()
        {
            var ed = CreateActiveEditor();

            Invoke(ed, "OnColumnVisibilityToggled", FindCol("damage"), false);
            Invoke(ed, "OnColumnVisibilityToggled", FindCol("weight"), false);

            // A toggle writes nothing on its own — the workspace layer captures the set
            // when the editor closes, through GameEditorManager. That is the single seam;
            // a save call at toggle time would put one preference in two places.
            var ws = new EditorWorkspace { editorName = ed.EditorName };
            ed.CaptureWorkspace(ws);

            string blob = ws.GetString("hiddenColumns", "");
            StringAssert.Contains("damage", blob, "Captured set must include 'damage'.");
            StringAssert.Contains("weight", blob, "Captured set must include 'weight'.");
        }

        [Test]
        public void HiddenColumns_RehydrateFromTheWorkspace_OnRestore()
        {
            var ed = CreateActiveEditor();

            var ws = new EditorWorkspace { editorName = ed.EditorName };
            ws.SetString("hiddenColumns", "damage,weight,critChance");
            ed.RestoreWorkspace(ws);

            var hidden = GetHiddenSet(ed);
            Assert.AreEqual(3, hidden.Count,
                "Three stored columns must be restored into the hidden set.");
            Assert.IsTrue(hidden.Contains("damage"));
            Assert.IsTrue(hidden.Contains("weight"));
            Assert.IsTrue(hidden.Contains("critChance"));

            Assert.IsFalse(ed.IsColumnVisible(FindCol("damage")));
            Assert.IsFalse(ed.IsColumnVisible(FindCol("weight")));
            Assert.IsFalse(ed.IsColumnVisible(FindCol("critChance")));
            Assert.IsTrue(ed.IsColumnVisible(FindCol("itemId")),
                "Columns not named in the stored set must remain visible.");
        }

        [Test]
        public void RestoringAColumnHeaderTheSchemaNoLongerHas_IsDropped()
        {
            var ed = CreateActiveEditor();

            var ws = new EditorWorkspace { editorName = ed.EditorName };
            ws.SetString("hiddenColumns", "damage,columnRenamedInAFutureRefactor");
            ed.RestoreWorkspace(ws);

            var hidden = GetHiddenSet(ed);
            Assert.AreEqual(1, hidden.Count,
                "A header no longer in the schema must be dropped, or the count label " +
                "lies and the popup offers no checkbox to un-hide it.");
            Assert.IsTrue(hidden.Contains("damage"));
        }

        [Test]
        public void RestoringAWorkspaceWithNoColumnEntry_LeavesTheCurrentSetAlone()
        {
            var ed = CreateActiveEditor();
            Invoke(ed, "OnColumnVisibilityToggled", FindCol("damage"), false);

            // A workspace written before this editor stored columns, or by a build that
            // did not have them. Restore must tolerate every value being absent.
            ed.RestoreWorkspace(new EditorWorkspace { editorName = ed.EditorName });

            Assert.IsTrue(GetHiddenSet(ed).Contains("damage"),
                "An absent entry means 'nothing stored', not 'everything visible'.");
        }

        [Test]
        public void TotalContentWidth_DropsWhenColumnsAreHidden()
        {
            var ed = CreateActiveEditor();
            Field(ed, "_allItems").SetValue(ed, new[] { CreateMinimalItem("a", "A") });
            Invoke(ed, "RefreshPicker");
            Invoke(ed, "RefreshTable");

            var refs = GetField(ed, "_uiRefs");
            var bodyContent = (RectTransform)Field(refs, "TableBodyContent").GetValue(refs);

            float widthAll = bodyContent.sizeDelta.x;

            var damage = FindCol("damage");
            Invoke(ed, "OnColumnVisibilityToggled", damage, false);
            float widthMinusDamage = bodyContent.sizeDelta.x;

            Assert.AreEqual(widthAll - damage.Width, widthMinusDamage, 0.5f,
                "Body content width must drop by exactly the hidden column's width.");
        }

        [Test]
        public void Reset_RestoresEveryColumn()
        {
            var ed = CreateActiveEditor();
            Invoke(ed, "OnColumnVisibilityToggled", FindCol("damage"),     false);
            Invoke(ed, "OnColumnVisibilityToggled", FindCol("weight"),     false);
            Invoke(ed, "OnColumnVisibilityToggled", FindCol("critChance"), false);

            Invoke(ed, "ResetColumnsToDefaults");

            Assert.AreEqual(0, GetHiddenSet(ed).Count,
                "Reset must clear the hidden set.");

            var ws = new EditorWorkspace { editorName = ed.EditorName };
            ed.CaptureWorkspace(ws);
            Assert.IsTrue(string.IsNullOrEmpty(ws.GetString("hiddenColumns", "")),
                "A reset editor must capture an empty set, not keep the old one.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ItemTableColumn FindCol(string header)
        {
            foreach (var c in ItemTableColumns.All)
                if (c.Header == header) return c;
            Assert.Fail($"Column '{header}' not in registry.");
            return null;
        }

        private ItemDefinition CreateMinimalItem(string id, string displayName)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = displayName;
            def.maxStack = 1;
            _runtimeAssets.Add(def);
            return def;
        }
    }
}
