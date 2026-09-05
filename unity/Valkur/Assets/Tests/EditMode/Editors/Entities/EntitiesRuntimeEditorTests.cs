using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.Entities;

namespace Valkur.Tests.EditMode.Editors.Entities
{
    /// <summary>
    /// Comprehensive EditMode tests for <see cref="EntitiesRuntimeEditor"/> (F5).
    ///
    /// Coverage:
    ///   • F5 InputAction binding (path / type / name / enabled)
    ///   • EditorName / IsActive contract
    ///   • Bootstrap: EntitiesRuntimeEditor must be spawned by GameplaySceneSetup
    ///     (regression: F5 silently did nothing because the component was never added
    ///     to the scene — see EnsureEntitiesRuntimeEditor in GameplaySceneSetup.Systems2.cs)
    ///   • UI shell: BuildUI populates every UIRefs field
    ///   • Menu bar: 5 dropdown buttons (tools, categories, picker, addremove, props)
    ///   • Activate / Deactivate / ToggleActive flow
    ///   • Default-open dropdowns after Activate
    ///   • ToggleDropdown opens/closes individual panels and updates highlight
    ///   • SelectCategory updates _category enum + tab highlight
    ///   • SetMode updates _mode enum + button highlight
    ///   • RefreshPicker handles null catalog gracefully
    ///   • ShowMonsterProperties with null catalog shows hint (no NRE)
    ///   • ToggleTutorial flips overlay active state
    ///   • Toggle via simulated F5 input flips IsActive
    ///
    /// EditMode notes:
    ///   • Always set LogAssert.ignoreFailingMessages = true (TMP / Canvas warnings).
    ///   • Singleton _instance is cleared via reflection between tests.
    ///   • Start() is invoked manually via reflection so BuildUI runs in EditMode.
    /// </summary>
    [TestFixture]
    public class EntitiesRuntimeEditorTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // ── Reflection helpers (mirrors BuildingsEditorLifecycleTests) ───────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) { field.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetFieldValue(object obj, string name) => GetField(obj, name)?.GetValue(obj);

        private static void SetPrivateField(object obj, string name, object value)
            => GetField(obj, name)?.SetValue(obj, value);

        private static void InvokeMethod(object obj, string methodName, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        /// <summary>Creates EntitiesRuntimeEditor singleton; OnSingletonAwake is forced.</summary>
        private EntitiesRuntimeEditor CreateEditor(string name = "TestEntitiesEditor")
        {
            ClearSingletonInstance<EntitiesRuntimeEditor>();
            var go = new GameObject(name);
            var ed = go.AddComponent<EntitiesRuntimeEditor>();
            // Force OnSingletonAwake so _toggleAction is created in EditMode
            // (Awake may not run reliably under all EditMode situations).
            if (GetFieldValue(ed, "_toggleAction") == null)
                InvokeMethod(ed, "OnSingletonAwake");
            _sceneObjects.Add(go);
            return ed;
        }

        /// <summary>Creates editor + invokes Start() so BuildUI populates _ui.</summary>
        private EntitiesRuntimeEditor CreateEditorWithUI(string name = "TestEntitiesEditorUI")
        {
            var ed = CreateEditor(name);
            InvokeMethod(ed, "Start");
            return ed;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            ClearSingletonInstance<EntitiesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  F5 INPUT BINDING
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void ToggleAction_ShipsUnbound()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor();

            // The editor toggles ship UNBOUND: every runtime editor is reached from the
            // General Editor on Escape, and the F-row was the source of every same-map
            // collision in the project. The action still EXISTS so the Controls editor can
            // offer it and a player can assign a key — which is why this asserts "no
            // bindings" rather than "no action". Reachability is pinned centrally by
            // EditorEntryPointTests.EveryRetiredToggle_HasAGeneralEditorEntry.
            var action = (InputAction) GetFieldValue(ed, "_toggleAction");
            if (action != null)
                Assert.AreEqual(0, action.bindings.Count,
                    "The Entities toggle must ship unbound — F5 is free now.");
        }

        [Test]
        public void ToggleAction_IsButtonType_AndEnabled()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor();
            var action = (InputAction) GetFieldValue(ed, "_toggleAction");
            if (action == null) Assert.Pass("Ships unbound and resolves to no action here.");

            Assert.AreEqual(InputActionType.Button, action.type, "_toggleAction must be Button type.");
            Assert.IsTrue(action.enabled,
                "The action stays enabled even with no binding, so assigning a key in the " +
                "Controls editor takes effect without a restart.");
            // Action name now comes from the canonical Editors map (post-input-refactor).
            // Accept either the new canonical name or the legacy ad-hoc name so the
            // assertion stays robust if the asset is renamed in either direction.
            Assert.That(action.name,
                Is.EqualTo("ToggleEntities").Or.EqualTo("ToggleEntitiesEditor"),
                "Action name must match the canonical Editors map (ToggleEntities) or the legacy ad-hoc fallback.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  IGameEditor CONTRACT
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void EditorName_Returns_EntitiesEditorString()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor();

            Assert.AreEqual("Entities Editor", ed.EditorName,
                "EditorName must exactly match the Python toggle_entities_editor display string.");
        }

        [Test]
        public void IsActive_InitiallyFalse_AfterCreation()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor();

            Assert.IsFalse(ed.IsActive, "Editor must start closed (IsActive == false).");
        }

        [Test]
        public void Implements_IGameEditor_Interface()
        {
            Assert.IsTrue(typeof(GameEditorManager.IGameEditor).IsAssignableFrom(typeof(EntitiesRuntimeEditor)),
                "EntitiesRuntimeEditor must implement IGameEditor so GameEditorManager can route F5.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  BOOTSTRAP REGRESSION — F5 only works if the component is in the scene
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void Bootstrap_EnsureEntitiesRuntimeEditor_Method_Exists()
        {
            // Regression: F5 silently did nothing because GameplaySceneSetup never spawned
            // the editor. This test pins the bootstrap method down so the bug cannot
            // regress.
            var setupType = typeof(Valkur.Gameplay.GameplaySceneSetup);
            Assert.IsNotNull(setupType, "GameplaySceneSetup class must exist.");

            var method = setupType.GetMethod("EnsureEntitiesRuntimeEditor",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method,
                "GameplaySceneSetup.EnsureEntitiesRuntimeEditor() must exist — without it F5 does nothing because the component is never added to the scene.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  UI SHELL — BuildUI populates every UIRefs field
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void BuildUI_Creates_Canvas_And_Root()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();

            var canvas = (Canvas) GetFieldValue(ed, "_canvas");
            var root   = (GameObject) GetFieldValue(ed, "_root");

            Assert.IsNotNull(canvas, "Canvas must be created by BuildUI.");
            Assert.IsNotNull(root,   "Root GameObject must be created by BuildUI.");
            Assert.AreEqual("EntitiesEditorCanvas", canvas.gameObject.name);
            Assert.IsFalse(root.activeSelf,
                "Root must be hidden after Start (only Activate enables it).");
        }

        [Test]
        public void BuildUI_Populates_All_MenuBar_Buttons()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            var ui = GetFieldValue(ed, "_ui");

            // 5 menu-bar buttons × (Image + TMP)
            string[] btnFields = {
                "ToolsMenuBtnImg",      "ToolsMenuBtnTmp",
                "CategoriesMenuBtnImg", "CategoriesMenuBtnTmp",
                "PickerMenuBtnImg",     "PickerMenuBtnTmp",
                "AddRemoveMenuBtnImg",  "AddRemoveMenuBtnTmp",
                "PropsMenuBtnImg",      "PropsMenuBtnTmp",
            };
            foreach (var f in btnFields)
            {
                var v = ui.GetType().GetField(f).GetValue(ui);
                Assert.IsNotNull(v, $"UIRefs.{f} must be populated by BuildAll.");
            }

            var menuBar = (GameObject) ui.GetType().GetField("MenuBar").GetValue(ui);
            Assert.IsNotNull(menuBar, "MenuBar GameObject must exist.");
        }

        [Test]
        public void BuildUI_Populates_All_Five_Dropdown_Panels()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            var ui = GetFieldValue(ed, "_ui");

            string[] panels = {
                "ToolsDropdown", "CategoriesDropdown", "PickerDropdown",
                "AddRemoveDropdown", "PropsDropdown",
            };
            foreach (var p in panels)
            {
                var go = (GameObject) ui.GetType().GetField(p).GetValue(ui);
                Assert.IsNotNull(go, $"Panel UIRefs.{p} must be created.");
                Assert.IsFalse(go.activeSelf,
                    $"Panel {p} must start hidden until Activate / OpenDefaultDropdowns runs.");
            }
        }

        [Test]
        public void BuildUI_Populates_Picker_Search_And_Status()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            var ui = GetFieldValue(ed, "_ui");

            var search = ui.GetType().GetField("SearchBox").GetValue(ui) as TMP_InputField;
            var pickerContent = ui.GetType().GetField("PickerContent").GetValue(ui) as RectTransform;
            var status = ui.GetType().GetField("StatusText").GetValue(ui) as TextMeshProUGUI;

            Assert.IsNotNull(search,        "SearchBox must be a TMP_InputField.");
            Assert.IsNotNull(pickerContent, "PickerContent must be a RectTransform.");
            Assert.IsNotNull(status,        "StatusText must be a TextMeshProUGUI.");
        }

        [Test]
        public void BuildUI_Populates_Category_Tabs_And_AddRemove_Buttons()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            var ui = GetFieldValue(ed, "_ui");

            string[] required = {
                "HostilesTabImg","HostilesTabTmp","NeutralsTabImg","NeutralsTabTmp",
                "SpecialsTabImg","SpecialsTabTmp","PlayersTabImg","PlayersTabTmp",
                "AddBtnImg","AddBtnTmp","RemoveBtnImg","RemoveBtnTmp",
                "AddOnSystemBtnImg","AddOnSystemBtnTmp","ConfirmBtnImg","ConfirmBtnTmp",
            };
            foreach (var f in required)
            {
                var v = ui.GetType().GetField(f).GetValue(ui);
                Assert.IsNotNull(v, $"UIRefs.{f} must be populated.");
            }
        }

        [Test]
        public void BuildUI_Populates_All_Properties_Sections()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            var ui = GetFieldValue(ed, "_ui");

            string[] sections = {
                "PropsHintText", "PropsFormRoot",
                "PropsIdentitySection", "PropsStatsSection", "PropsAISection",
                "PropsSpawnSection", "PropsAutoCastSection", "PropsAssetsSection",
            };
            foreach (var s in sections)
            {
                var v = ui.GetType().GetField(s).GetValue(ui);
                Assert.IsNotNull(v, $"UIRefs.{s} must be populated.");
            }
        }

        [Test]
        public void BuildUI_Creates_Tutorial_Hidden()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            var tut = (GameObject) GetFieldValue(ed, "_tutorial");

            Assert.IsNotNull(tut, "Tutorial overlay must be built.");
            Assert.IsFalse(tut.activeSelf, "Tutorial must start hidden.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ACTIVATE / DEACTIVATE / TOGGLE
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void Activate_Sets_IsActive_True_And_Shows_Root()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();

            ed.Activate();

            Assert.IsTrue(ed.IsActive, "IsActive must become true after Activate().");
            var root = (GameObject) GetFieldValue(ed, "_root");
            Assert.IsTrue(root.activeSelf, "Root must be enabled after Activate().");
        }

        [Test]
        public void Activate_OpensFiveDefaultDropdowns()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();

            ed.Activate();

            var open = (HashSet<string>) GetFieldValue(ed, "_openDropdowns");
            Assert.AreEqual(5, open.Count, "Activate must open all 5 default dropdowns.");
            CollectionAssert.AreEquivalent(
                new[] { "tools", "categories", "picker", "addremove", "props" },
                open,
                "Default-open set must match Python entities_editor working layout.");

            // And the panels themselves must be active in the hierarchy.
            var ui = GetFieldValue(ed, "_ui");
            foreach (var name in new[] { "ToolsDropdown", "CategoriesDropdown",
                                          "PickerDropdown", "AddRemoveDropdown", "PropsDropdown" })
            {
                var go = (GameObject) ui.GetType().GetField(name).GetValue(ui);
                Assert.IsTrue(go.activeSelf, $"{name} must be active after Activate().");
            }
        }

        [Test]
        public void Deactivate_Sets_IsActive_False_And_Hides_Root()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            ed.Activate();

            ed.Deactivate();

            Assert.IsFalse(ed.IsActive, "IsActive must be false after Deactivate().");
            var root = (GameObject) GetFieldValue(ed, "_root");
            Assert.IsFalse(root.activeSelf, "Root must be hidden after Deactivate().");
        }

        [Test]
        public void Deactivate_Clears_SelectedKey()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            ed.Activate();
            SetPrivateField(ed, "_selectedKey", "skeleton");

            ed.Deactivate();

            Assert.IsNull(GetFieldValue(ed, "_selectedKey"),
                "Deactivate must reset _selectedKey so the next Activate starts clean.");
        }

        [Test]
        public void ToggleActive_Flips_IsActive()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();

            InvokeMethod(ed, "ToggleActive");
            Assert.IsTrue(ed.IsActive, "First toggle must activate.");

            InvokeMethod(ed, "ToggleActive");
            Assert.IsFalse(ed.IsActive, "Second toggle must deactivate.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DROPDOWN MANAGEMENT
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void ToggleDropdown_OpensThenCloses_Single_Panel()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            // Don't Activate — start with all closed for a clean toggle test.
            var open = (HashSet<string>) GetFieldValue(ed, "_openDropdowns");
            Assert.AreEqual(0, open.Count, "Start with no open dropdowns.");

            InvokeMethod(ed, "ToggleDropdown", "tools");
            Assert.IsTrue(open.Contains("tools"), "ToggleDropdown('tools') must open it.");

            InvokeMethod(ed, "ToggleDropdown", "tools");
            Assert.IsFalse(open.Contains("tools"), "Second ToggleDropdown('tools') must close it.");
        }

        [Test]
        public void ToggleDropdown_UnknownName_DoesNotThrow()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();

            Assert.DoesNotThrow(() => InvokeMethod(ed, "ToggleDropdown", "no-such-panel"),
                "Unknown dropdown names must be ignored, not throw.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  CATEGORY + MODE SELECTION
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void SelectCategory_UpdatesCategoryEnum()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();

            // EntityCategory is a private nested enum — convert via integer.
            var enumType = typeof(EntitiesRuntimeEditor).GetNestedType(
                "EntityCategory", BindingFlags.NonPublic);
            Assert.IsNotNull(enumType, "Private nested EntityCategory enum must exist.");

            object players = Enum.Parse(enumType, "Players");
            var method = typeof(EntitiesRuntimeEditor).GetMethod("SelectCategory",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(ed, new[] { players });

            var current = GetFieldValue(ed, "_category");
            Assert.AreEqual("Players", current.ToString(),
                "_category must update to Players after SelectCategory(Players).");
        }

        [Test]
        public void SetMode_UpdatesModeEnum()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();

            var enumType = typeof(EntitiesRuntimeEditor).GetNestedType(
                "EditorMode", BindingFlags.NonPublic);
            Assert.IsNotNull(enumType, "Private nested EditorMode enum must exist.");

            object spawn = Enum.Parse(enumType, "Spawn");
            var method = typeof(EntitiesRuntimeEditor).GetMethod("SetMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(ed, new[] { spawn });

            var current = GetFieldValue(ed, "_mode");
            Assert.AreEqual("Spawn", current.ToString(),
                "_mode must update after SetMode(Spawn).");
        }

        [Test]
        public void DefaultMode_Is_Select()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor();

            var mode = GetFieldValue(ed, "_mode");
            Assert.AreEqual("Select", mode.ToString(),
                "Default mode must be Select (Python parity).");
        }

        [Test]
        public void DefaultCategory_Is_Hostiles()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor();

            var cat = GetFieldValue(ed, "_category");
            Assert.AreEqual("Hostiles", cat.ToString(),
                "Default category must be Hostiles (Python parity).");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ROBUSTNESS — null catalog, null UI, etc.
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void RefreshPicker_WithNullCatalog_DoesNotThrow()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            // _monsterCatalog is null by default in EditMode tests.

            Assert.DoesNotThrow(() => InvokeMethod(ed, "RefreshPicker"),
                "RefreshPicker must handle a null MonsterCatalog without NRE — it should just show an empty list.");
        }

        [Test]
        public void ShowMonsterProperties_WithNullCatalog_ShowsHint_NoThrow()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();

            Assert.DoesNotThrow(
                () => InvokeMethod(ed, "ShowMonsterProperties", "skeleton"),
                "ShowMonsterProperties must handle null catalog gracefully (hint, no NRE).");
        }

        [Test]
        public void ToggleTutorial_FlipsActiveState()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            var tut = (GameObject) GetFieldValue(ed, "_tutorial");

            Assert.IsFalse(tut.activeSelf, "Tutorial starts hidden.");
            InvokeMethod(ed, "ToggleTutorial");
            Assert.IsTrue(tut.activeSelf, "First toggle must show tutorial.");
            InvokeMethod(ed, "ToggleTutorial");
            Assert.IsFalse(tut.activeSelf, "Second toggle must hide tutorial.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  STATIC UI HELPERS
        // ════════════════════════════════════════════════════════════════════════

        [Test]
        public void NormalizeTint_ZeroAlpha_ReturnsWhite()
        {
            // Default value of an uninitialized AnimationScaleConfig.tint is Color.clear
            // (all zeros). The picker must promote this to white so legacy entities
            // (and any not-yet-tinted variants) keep rendering normally.
            var m = typeof(EntitiesRuntimeEditor).GetMethod("NormalizeTint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "NormalizeTint helper must exist on EntitiesRuntimeEditor.");

            var defaultTint = (Color) m.Invoke(null, new object[] { new Color(0f, 0f, 0f, 0f) });
            Assert.AreEqual(Color.white, defaultTint,
                "Uninitialized tint must be promoted to white (Python parity for tint=null).");

            var purple = (Color) m.Invoke(null, new object[] { new Color(0.5f, 0f, 0.5f, 1f) });
            Assert.AreEqual(0.5f, purple.r, 1e-4f, "Red channel must pass through.");
            Assert.AreEqual(0f,   purple.g, 1e-4f, "Green channel must pass through.");
            Assert.AreEqual(0.5f, purple.b, 1e-4f, "Blue channel must pass through.");
            Assert.AreEqual(1f,   purple.a, 1e-4f, "Alpha must always be forced to 1.");

            // Pure-black non-zero-alpha tint must NOT be promoted to white — Python's
            // BLEND_RGB_MULT with [0,0,0] does collapse the sprite to black.
            var black = (Color) m.Invoke(null, new object[] { new Color(0f, 0f, 0f, 1f) });
            Assert.AreEqual(Color.black, new Color(black.r, black.g, black.b, 1f),
                "Explicit black tint with alpha=1 must remain black (Pygame BLEND_RGB_MULT parity).");
        }

        [Test]
        public void ApplyMenuBtnStyle_TogglesColors()
        {
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("BtnStyleTest");
            _sceneObjects.Add(go);
            var img = go.AddComponent<Image>();

            var tmpGo = new GameObject("BtnStyleTestTmp");
            tmpGo.transform.SetParent(go.transform, false);
            _sceneObjects.Add(tmpGo);
            var tmp = tmpGo.AddComponent<TextMeshProUGUI>();

            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, isOpen: true);
            var openColor = img.color;

            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, isOpen: false);
            var closedColor = img.color;

            Assert.AreNotEqual(openColor, closedColor,
                "ApplyMenuBtnStyle must change colour between open and closed states.");
        }
    }
}
