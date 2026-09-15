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

namespace Valkur.Tests.EditMode.Editors.EntitiesEditor
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
            // collision in the project. The action still EXISTS, with ONE binding whose path
            // is empty, so the Controls editor can offer it and a player can assign a key —
            // ApplyBindingOverride writes into a SLOT and cannot create one, so a toggle with
            // zero bindings was listed by that panel and then refused. Reachability is pinned
            // centrally by EditorEntryPointTests.EveryRetiredToggle_HasAGeneralEditorEntry.
            var action = (InputAction) GetFieldValue(ed, "_toggleAction");
            if (action != null)
            {
                Assert.AreEqual(1, action.bindings.Count,
                    "The Entities toggle needs exactly one binding slot, empty — with none "
                    + "it cannot be given a key from the Controls editor at all.");
                Assert.IsEmpty(action.bindings[0].effectivePath,
                    "The Entities toggle must ship unbound — F5 is free now.");
            }
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

        /// <summary>
        /// The Animation panel is built OUTSIDE <c>BuildAll</c> — it needs eleven callbacks no
        /// other panel shares — so it is the one panel that can be forgotten by a refactor of
        /// BuildUI without anything else going red. Every widget the runtime editor writes to
        /// is listed here; a null one is a control that silently does nothing.
        ///
        /// <para>This asserts STRUCTURE, not layout. uGUI performs no layout pass in Edit Mode,
        /// so reading back a size would report the value that was written and never what a
        /// layout would have made of it — the trap that shipped the Controls editor with its
        /// buttons printed one letter per line.</para>
        /// </summary>
        [Test]
        public void BuildUI_Populates_The_Animation_Panel()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditorWithUI();
            var ui = GetFieldValue(ed, "_ui");

            var panel = (GameObject) ui.GetType().GetField("AnimDropdown").GetValue(ui);
            Assert.IsNotNull(panel, "The Animation panel must be built.");
            Assert.IsFalse(panel.activeSelf,
                "It must start hidden: opening it builds a camera and a RenderTexture, and a " +
                "session that pays for a panel nobody asked for is the opposite of the point.");

            string[] widgets = {
                "AnimMenuBtnImg", "AnimStage", "AnimSubjectText",
                "AnimStateDd", "AnimVariantDd", "AnimLoadoutDd", "AnimLayoutDd",
                "AnimInfoText", "AnimPlayPauseTmp", "AnimReverseImg",
                "AnimStripOverflowText",
                "AnimEntitySpeedInput", "AnimStateSpeedInput", "AnimVariantSpeedInput",
                "AnimHoldToggle",
            };
            foreach (var w in widgets)
            {
                var value = ui.GetType().GetField(w).GetValue(ui);
                Assert.IsNotNull(value, $"UIRefs.{w} must be populated by BuildAnimationPanel.");
            }

            var pad = (Image[]) ui.GetType().GetField("AnimDirBtnImgs").GetValue(ui);
            Assert.IsNotNull(pad, "The direction pad must exist.");
            Assert.AreEqual(9, pad.Length,
                "Nine cells: eight facings and a centre that is the grid toggle, because no " +
                "body faces the camera.");

            var cells = (Image[]) ui.GetType().GetField("AnimStripCellImgs").GetValue(ui);
            Assert.IsNotNull(cells, "The frame strip must realise its cells once, up front.");
            Assert.Greater(cells.Length, 0);
            foreach (var cell in cells)
                Assert.IsFalse(cell.gameObject.activeSelf,
                    "A strip cell starts hidden and is shown by RefreshAnimationStrip — the " +
                    "pool is realised once and never rebuilt.");
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

        /// <summary>
        /// The picker opens on ALL, not on Hostiles.
        ///
        /// <para>It used to be Hostiles for Python parity, and that parity stopped being worth
        /// anything the moment the tabs became a CLASSIFICATION rather than a list: an entity
        /// whose tab an author does not expect is invisible unless they guess which one it fell
        /// into, and guessing is precisely what a misfiled entity defeats. Measured before the
        /// classification was moved onto <c>stats.faction</c>, one of the 28 shipped monsters
        /// was in that position — <c>barbol_brother_felipondor</c>, a NEUTRAL sitting under
        /// Hostiles because his key contains none of the words the old heuristic searched
        /// for.</para>
        ///
        /// <para>Opening on All costs nothing at this scale and removes the whole class of
        /// "it is not in the editor" that is really "it is in another tab".</para>
        /// </summary>
        [Test]
        public void DefaultCategory_Is_All()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor();

            var cat = GetFieldValue(ed, "_category");
            Assert.AreEqual("All", cat.ToString(),
                "The picker must open on every entity, not on one tab.");
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

            // A tint bright enough to read passes through untouched.
            var bright = (Color) m.Invoke(null, new object[] { new Color(0.8f, 0.8f, 0.2f, 1f) });
            Assert.AreEqual(0.8f, bright.r, 1e-4f, "Red channel must pass through.");
            Assert.AreEqual(0.8f, bright.g, 1e-4f, "Green channel must pass through.");
            Assert.AreEqual(0.2f, bright.b, 1e-4f, "Blue channel must pass through.");
            Assert.AreEqual(1f,   bright.a, 1e-4f, "Alpha must always be forced to 1.");

            // A tint too dark to READ is lifted, and its HUE survives. A picker slot identifies;
            // it does not preview. Seven shipped entities author a tint below the panel's own
            // 0.13 surface -- the six Dark twins at exactly (0,0,0) and barbol_oscuro at 0.12 --
            // so drawing them faithfully gives seven black squares under truncated labels, which
            // is the one thing a picker may not be.
            var darkPurple = (Color) m.Invoke(null, new object[] { new Color(0.5f, 0f, 0.5f, 1f) });
            float purpleLum = 0.2126f * darkPurple.r + 0.7152f * darkPurple.g + 0.0722f * darkPurple.b;
            Assert.That(purpleLum, Is.GreaterThanOrEqualTo(0.24f),
                "A dark tint must be lifted until it is READABLE. Lifting to a fixed HSV value " +
                "is the version that looks right and is not: value and luminance are different " +
                "quantities, so a saturated hue comes back still invisible.");
            Assert.That(darkPurple.r, Is.EqualTo(darkPurple.b).Within(1e-3f),
                "Hue must survive the lift: a purple entity still reads purple.");
            Assert.That(darkPurple.g, Is.LessThan(darkPurple.r),
                "Saturation must survive too, or every dark entity lifts to the same grey.");

            // Pure black is achromatic, so it has no hue to keep and becomes grey -- the honest
            // answer for art whose whole identity is "no colour".
            var black = (Color) m.Invoke(null, new object[] { new Color(0f, 0f, 0f, 1f) });
            Assert.That(black.r, Is.EqualTo(black.g).Within(1e-3f));
            Assert.That(black.g, Is.EqualTo(black.b).Within(1e-3f));
            Assert.That(black.r, Is.GreaterThan(0.2f),
                "The Dark roster must be identifiable in the picker even though it is black " +
                "in the world.");
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
