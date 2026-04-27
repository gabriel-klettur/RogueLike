using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.Inventory
{
    /// <summary>
    /// State-mutation tests for the Inventory Editor (F6):
    /// SetMode / SetSide / SetCategory / SetCatalogTab / AdjustQty and the
    /// associated highlight refreshes.
    /// </summary>
    [TestFixture]
    public class InventoryRuntimeEditorStateTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            ClearSingletonInstance<InventoryRuntimeEditor>();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

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
        private static void   SetField(object obj, string name, object value)
            => Field(obj, name)?.SetValue(obj, value);

        private static MethodInfo Method(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        private static void Invoke(object obj, string method, params object[] args)
        {
            var m = Method(obj, method);
            Assert.IsNotNull(m, $"Method '{method}' not found on {obj.GetType().Name}");
            m.Invoke(obj, args);
        }

        private InventoryRuntimeEditor CreateAndActivate()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingletonInstance<InventoryRuntimeEditor>();
            var go = new GameObject("TestInventoryEditor");
            _scene.Add(go);
            var ed = go.AddComponent<InventoryRuntimeEditor>();
            Invoke(ed, "OnSingletonAwake");
            Invoke(ed, "Start");
            ed.Activate();
            return ed;
        }

        private static InventoryEditorUIBuilder.UIRefs GetUIRefs(InventoryRuntimeEditor ed)
            => (InventoryEditorUIBuilder.UIRefs)GetField(ed, "_uiRefs");

        private static object EnumValue(string typeName, string memberName)
        {
            var t = typeof(InventoryRuntimeEditor)
                .GetNestedType(typeName, BindingFlags.NonPublic);
            Assert.IsNotNull(t, $"Nested enum '{typeName}' must exist.");
            return System.Enum.Parse(t, memberName);
        }

        // ── Mode ───────────────────────────────────────────────────────────────

        [Test]
        public void SetMode_AddItem_HighlightsAddButton()
        {
            var ed   = CreateAndActivate();
            var refs = GetUIRefs(ed);

            Invoke(ed, "SetMode", EnumValue("EditorMode", "AddItem"));

            Assert.AreEqual(EditorUIHelpers.BTN_ACTIVE, refs.AddItemBtnImg.color,
                "Add button must use BTN_ACTIVE colour when AddItem mode is selected.");
            Assert.AreEqual(EditorUIHelpers.BTN_NORMAL, refs.ViewBtnImg.color,
                "View button must use BTN_NORMAL colour when AddItem mode is selected.");
            Assert.AreEqual(EditorUIHelpers.BTN_NORMAL, refs.DeleteItemBtnImg.color,
                "Delete button must use BTN_NORMAL colour when AddItem mode is selected.");
        }

        [Test]
        public void SetMode_View_HighlightsViewButton()
        {
            var ed   = CreateAndActivate();
            var refs = GetUIRefs(ed);

            Invoke(ed, "SetMode", EnumValue("EditorMode", "DeleteItem"));
            Invoke(ed, "SetMode", EnumValue("EditorMode", "View"));

            Assert.AreEqual(EditorUIHelpers.BTN_ACTIVE, refs.ViewBtnImg.color);
            Assert.AreEqual(EditorUIHelpers.BTN_NORMAL, refs.AddItemBtnImg.color);
            Assert.AreEqual(EditorUIHelpers.BTN_NORMAL, refs.DeleteItemBtnImg.color);
        }

        // ── Side ───────────────────────────────────────────────────────────────

        [Test]
        public void SetSide_Default_HighlightsDefaultSideButton()
        {
            var ed   = CreateAndActivate();
            var refs = GetUIRefs(ed);

            Invoke(ed, "SetSide", EnumValue("EditorSide", "Default"));

            Assert.AreEqual(EditorUIHelpers.BTN_ACTIVE, refs.SideDefaultImg.color);
            Assert.AreEqual(EditorUIHelpers.BTN_NORMAL, refs.SideActiveImg.color);
        }

        // ── Category ───────────────────────────────────────────────────────────

        [Test]
        public void SetCategory_Monsters_HighlightsMonstersTabAndClearsSelection()
        {
            var ed   = CreateAndActivate();
            var refs = GetUIRefs(ed);

            // Pre-set a fake selection via reflection so we can confirm it gets cleared.
            SetField(ed, "_selectedEntityName", "Player (Active)");

            Invoke(ed, "SetCategory", EnumValue("EditorCategory", "Monsters"));

            Assert.AreEqual(EditorUIHelpers.BTN_ACTIVE, refs.MonstersTabImg.color);
            Assert.AreEqual(EditorUIHelpers.BTN_NORMAL, refs.PlayerTabImg.color);
            Assert.AreEqual(EditorUIHelpers.BTN_NORMAL, refs.MapTabImg.color);

            Assert.IsNull(GetField(ed, "_selectedEntityName"),
                "Switching category must clear the previously-selected entity.");
            Assert.IsNull(GetField(ed, "_selectedInventory"),
                "Switching category must clear the previously-selected inventory.");
        }

        // ── CatalogTab ─────────────────────────────────────────────────────────

        [Test]
        public void SetCatalogTab_Ground_HighlightsGroundTab()
        {
            var ed   = CreateAndActivate();
            var refs = GetUIRefs(ed);

            Invoke(ed, "SetCatalogTab", EnumValue("CatalogTab", "Ground"));

            Assert.AreEqual(EditorUIHelpers.BTN_ACTIVE, refs.CatGroundImg.color);
            Assert.AreEqual(EditorUIHelpers.BTN_NORMAL, refs.CatDefaultImg.color);
        }

        // ── Quantity stepper ──────────────────────────────────────────────────

        [Test]
        public void AdjustQty_PositiveDelta_IncrementsAndUpdatesInput()
        {
            var ed   = CreateAndActivate();
            var refs = GetUIRefs(ed);

            Invoke(ed, "AdjustQty", 5);

            Assert.AreEqual(6, (int)GetField(ed, "_spinnerQty"),
                "Spinner must be 1 + 5 = 6 after a +5 adjustment.");
            Assert.AreEqual("6", refs.QtyInput.text,
                "QtyInput field must mirror the spinner value.");
        }

        [Test]
        public void AdjustQty_NegativeBelowOne_ClampsToOne()
        {
            var ed   = CreateAndActivate();
            var refs = GetUIRefs(ed);

            Invoke(ed, "AdjustQty", -10);

            Assert.AreEqual(1, (int)GetField(ed, "_spinnerQty"),
                "Spinner must clamp to a minimum of 1.");
            Assert.AreEqual("1", refs.QtyInput.text);
        }

        [Test]
        public void AdjustQty_AboveMax_ClampsTo999()
        {
            var ed = CreateAndActivate();

            Invoke(ed, "AdjustQty", 5000);

            Assert.AreEqual(999, (int)GetField(ed, "_spinnerQty"),
                "Spinner must clamp to a maximum of 999.");
        }

        // ── Catalog refresh sanity (no exceptions) ─────────────────────────────

        [Test]
        public void RefreshCatalog_DefaultTab_DoesNotThrow()
        {
            var ed = CreateAndActivate();
            Assert.DoesNotThrow(() => Invoke(ed, "RefreshCatalog"));
        }

        [Test]
        public void RefreshSlotGrid_NoSelection_ShowsHintWithoutThrowing()
        {
            var ed   = CreateAndActivate();
            var refs = GetUIRefs(ed);

            Assert.DoesNotThrow(() => Invoke(ed, "RefreshSlotGrid"));
            StringAssert.Contains("no entity selected", refs.OwnerText.text,
                "Owner header must show the empty-selection hint when no entity is selected.");
        }
    }
}
