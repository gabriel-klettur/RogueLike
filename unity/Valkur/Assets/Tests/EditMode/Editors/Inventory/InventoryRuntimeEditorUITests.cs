using System.Collections.Generic;
using System.Linq;
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
    /// UI/UX structural tests for the Inventory Editor (F6).
    ///
    /// Verifies that <see cref="InventoryEditorUIBuilder.BuildAll"/> produces the
    /// expected Items / Buildings-style chrome:
    ///   • 30 px menu bar with the four dropdown buttons (Modes / Entities / Slots / Catalog)
    ///   • Four floating panels, each with a DraggablePanel + content area
    ///   • All ScrollRects have a LayoutElement (regression for the Items
    ///     <c>EnsureFlexibleHeight</c> NRE — same widget helpers, same risk)
    ///   • Tutorial overlay is created but starts hidden
    /// </summary>
    [TestFixture]
    public class InventoryRuntimeEditorUITests
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

        private InventoryRuntimeEditor CreateAndActivateEditor()
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
        {
            var raw = GetField(ed, "_uiRefs");
            Assert.IsNotNull(raw, "_uiRefs must be set after BuildUI.");
            return (InventoryEditorUIBuilder.UIRefs)raw;
        }

        // ── ScrollRect / LayoutElement regression ──────────────────────────────

        [Test]
        public void Activate_AllScrollRects_HaveLayoutElement()
        {
            var ed = CreateAndActivateEditor();
            var root = GetField(ed, "_root") as GameObject;
            Assert.IsTrue(root != null, "_root must exist.");

            var scrolls = root.GetComponentsInChildren<ScrollRect>(includeInactive: true);
            Assert.Greater(scrolls.Length, 0,
                "BuildUI must create at least one ScrollRect (entity list / slots / catalog).");

            foreach (var sr in scrolls)
            {
                var le = sr.GetComponent<LayoutElement>();
                Assert.IsTrue(le != null,
                    $"ScrollRect '{sr.name}' must have a LayoutElement (regression — Items Panels.cs NRE fix).");
            }
        }

        // ── Menu bar ───────────────────────────────────────────────────────────

        [Test]
        public void MenuBar_HasFourDropdownButtons()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            Assert.IsTrue(refs.MenuBar != null, "MenuBar must be created.");
            Assert.IsTrue(refs.ModesMenuBtnImg    != null, "Modes menu button must exist.");
            Assert.IsTrue(refs.EntitiesMenuBtnImg != null, "Entities menu button must exist.");
            Assert.IsTrue(refs.SlotsMenuBtnImg    != null, "Slots menu button must exist.");
            Assert.IsTrue(refs.CatalogMenuBtnImg  != null, "Catalog menu button must exist.");
        }

        [Test]
        public void MenuBar_StartsAtTopOfScreen_WithExpectedHeight()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            var rt = refs.MenuBar.GetComponent<RectTransform>();
            Assert.AreEqual(1f, rt.anchorMin.y, 0.001f, "Menu bar must anchor to the top.");
            Assert.AreEqual(1f, rt.anchorMax.y, 0.001f, "Menu bar must anchor to the top.");
            Assert.AreEqual(TileEditorUIHelpers.MENUBAR_HEIGHT, rt.sizeDelta.y, 0.5f,
                "Menu bar height must match shared TileEditorUIHelpers.MENUBAR_HEIGHT (30 px).");
        }

        // ── Floating panels ────────────────────────────────────────────────────

        [Test]
        public void Activate_BuildsFourPanels()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            Assert.IsTrue(refs.ModesDropdown    != null, "Modes panel must exist.");
            Assert.IsTrue(refs.EntitiesDropdown != null, "Entities panel must exist.");
            Assert.IsTrue(refs.SlotsDropdown    != null, "Slots panel must exist.");
            Assert.IsTrue(refs.CatalogDropdown  != null, "Catalog panel must exist.");

            Assert.IsTrue(refs.ModesPanelDrag    != null, "Modes panel must have DraggablePanel.");
            Assert.IsTrue(refs.EntitiesPanelDrag != null, "Entities panel must have DraggablePanel.");
            Assert.IsTrue(refs.SlotsPanelDrag    != null, "Slots panel must have DraggablePanel.");
            Assert.IsTrue(refs.CatalogPanelDrag  != null, "Catalog panel must have DraggablePanel.");
        }

        [Test]
        public void Activate_OpensAllFourDropdowns()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            Assert.IsTrue(refs.ModesDropdown.activeSelf,
                "Modes panel must be visible after Activate (OpenAllPanels).");
            Assert.IsTrue(refs.EntitiesDropdown.activeSelf,
                "Entities panel must be visible after Activate.");
            Assert.IsTrue(refs.SlotsDropdown.activeSelf,
                "Slots panel must be visible after Activate.");
            Assert.IsTrue(refs.CatalogDropdown.activeSelf,
                "Catalog panel must be visible after Activate.");

            var open = GetField(ed, "_openDropdowns") as HashSet<string>;
            Assert.IsNotNull(open);
            Assert.IsTrue(open.SetEquals(new[] { "modes", "entities", "slots", "catalog" }),
                "_openDropdowns must contain all four keys after Activate.");
        }

        [Test]
        public void ToggleDropdown_ClosesAndReopensPanel()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            Invoke(ed, "ToggleDropdown", "modes");
            Assert.IsFalse(refs.ModesDropdown.activeSelf,
                "ToggleDropdown('modes') must close the Modes panel when open.");

            Invoke(ed, "ToggleDropdown", "modes");
            Assert.IsTrue(refs.ModesDropdown.activeSelf,
                "ToggleDropdown('modes') must reopen the Modes panel when closed.");
        }

        [Test]
        public void PanelOnClose_RemovesKeyFromOpenDropdowns()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            // Simulate the X-button on the Modes panel.
            refs.ModesPanelDrag.OnClose?.Invoke();

            var open = GetField(ed, "_openDropdowns") as HashSet<string>;
            Assert.IsNotNull(open);
            Assert.IsFalse(open.Contains("modes"),
                "When a panel header X is clicked, its key must be removed from _openDropdowns.");
        }

        // ── Content presence ───────────────────────────────────────────────────

        [Test]
        public void ModesPanel_HasModeAndSideButtons()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            Assert.IsTrue(refs.ViewBtnImg       != null, "View button must exist.");
            Assert.IsTrue(refs.AddItemBtnImg    != null, "Add button must exist.");
            Assert.IsTrue(refs.DeleteItemBtnImg != null, "Delete button must exist.");
            Assert.IsTrue(refs.SideDefaultImg   != null, "Default-side button must exist.");
            Assert.IsTrue(refs.SideActiveImg    != null, "Active-side button must exist.");
        }

        [Test]
        public void EntitiesPanel_HasCategoryTabsAndSearchAndList()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            Assert.IsTrue(refs.PlayerTabImg      != null, "Player category tab must exist.");
            Assert.IsTrue(refs.MonstersTabImg    != null, "Monsters category tab must exist.");
            Assert.IsTrue(refs.MapTabImg         != null, "Map category tab must exist.");
            Assert.IsTrue(refs.EntitySearchBox   != null, "Entity search box must exist.");
            Assert.IsTrue(refs.EntityListContent != null, "Entity list scroll-content must exist.");
        }

        [Test]
        public void SlotsPanel_HasOwnerHeaderAndGridAndStatus()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            Assert.IsTrue(refs.OwnerText       != null, "Owner header text must exist.");
            Assert.IsTrue(refs.SlotGridContent != null, "Slot grid content must exist.");
            Assert.IsTrue(refs.StatusText      != null, "Status text must exist.");
        }

        [Test]
        public void CatalogPanel_HasTabsSearchGridQtyInput()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            Assert.IsTrue(refs.CatDefaultImg      != null, "Catalog Default tab must exist.");
            Assert.IsTrue(refs.CatGroundImg       != null, "Catalog Ground tab must exist.");
            Assert.IsTrue(refs.CatalogSearchBox   != null, "Catalog search box must exist.");
            Assert.IsTrue(refs.CatalogGridContent != null, "Catalog grid content must exist.");
            Assert.IsTrue(refs.QtyInput           != null, "Quantity input must exist.");
            Assert.AreEqual("1", refs.QtyInput.text,      "Quantity must default to 1.");
        }

        // ── Tutorial overlay ───────────────────────────────────────────────────

        [Test]
        public void Activate_TutorialOverlayExists_ButHidden()
        {
            var ed   = CreateAndActivateEditor();
            var tut  = GetField(ed, "_tutorial") as GameObject;

            Assert.IsTrue(tut != null, "Tutorial overlay must be created.");
            Assert.IsFalse(tut.activeSelf, "Tutorial overlay must start hidden.");
        }

        [Test]
        public void ToggleTutorial_TogglesVisibility()
        {
            var ed  = CreateAndActivateEditor();
            var tut = GetField(ed, "_tutorial") as GameObject;
            Assert.IsTrue(tut != null);

            Invoke(ed, "ToggleTutorial");
            Assert.IsTrue(tut.activeSelf, "ToggleTutorial must show the overlay first time.");

            Invoke(ed, "ToggleTutorial");
            Assert.IsFalse(tut.activeSelf, "Second ToggleTutorial call must hide the overlay.");
        }

        // ── Menu-button highlight (open vs closed) ─────────────────────────────

        [Test]
        public void ApplyMenuBtnStyle_OpenState_UsesAccentText()
        {
            var ed   = CreateAndActivateEditor();
            var refs = GetUIRefs(ed);

            // Sanity: button style helper switches text color between primary and accent.
            InventoryEditorUIBuilder.ApplyMenuBtnStyle(
                refs.ModesMenuBtnImg, refs.ModesMenuBtnTmp, isOpen: true);
            Assert.AreEqual(TileEditorUIHelpers.ACCENT, refs.ModesMenuBtnTmp.color,
                "Open menu-button label must be ACCENT-coloured.");
            Assert.AreEqual(FontStyles.Bold, refs.ModesMenuBtnTmp.fontStyle,
                "Open menu-button label must be bold.");

            InventoryEditorUIBuilder.ApplyMenuBtnStyle(
                refs.ModesMenuBtnImg, refs.ModesMenuBtnTmp, isOpen: false);
            Assert.AreEqual(TileEditorUIHelpers.TEXT_PRIMARY, refs.ModesMenuBtnTmp.color,
                "Closed menu-button label must be TEXT_PRIMARY-coloured.");
        }
    }
}
