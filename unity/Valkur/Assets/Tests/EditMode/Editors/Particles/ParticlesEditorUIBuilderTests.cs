using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.VFX;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Exercises <see cref="ParticlesEditorUIBuilder.BuildAll"/> directly (no MonoBehaviour).
    ///
    /// All tests create a temporary Canvas + root GameObject, call BuildAll with no-op callbacks,
    /// and assert UIRefs fields are populated correctly.
    /// </summary>
    [TestFixture]
    public class ParticlesEditorUIBuilderTests
    {
        private GameObject _canvasGo;
        private ParticlesEditorUIBuilder.UIRefs _ui;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _canvasGo = new GameObject("TestCanvas");
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(_canvasGo.transform, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _ui = ParticlesEditorUIBuilder.BuildAll(
                root.transform,
                onDropdownToggle:  _ => { },
                onUndo:            () => { },
                onRedo:            () => { },
                onSave:            () => { },
                onReload:          () => { },
                onModeSelect:      () => { },
                onModePlace:       () => { },
                onModeDelete:      () => { },
                onAddSystem:       () => { },
                onRemoveSystem:    () => { },
                onSearchChanged:   _ => { },
                onToggleSpells:    () => { },
                onToggleTutorial:  () => { });
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null) UnityEngine.Object.DestroyImmediate(_canvasGo);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── MenuBar ──────────────────────────────────────────────────────────────

        [Test]
        public void BuildAll_MenuBar_IsCreated_WithMenuBarChrome()
        {
            Assert.IsTrue(_ui.MenuBar != null, "MenuBar GameObject must be created.");
            Assert.IsNotNull(_ui.MenuBar.GetComponent<MenuBarChrome>(),
                "MenuBar must have MenuBarChrome component.");
        }

        [Test]
        public void BuildAll_MenuBar_HasAllFiveButtonImagesAndTmps()
        {
            Assert.IsTrue(_ui.ToolsMenuBtnImg   != null, "ToolsMenuBtnImg must be populated.");
            Assert.IsTrue(_ui.ToolsMenuBtnTmp   != null, "ToolsMenuBtnTmp must be populated.");
            Assert.IsTrue(_ui.PresetsMenuBtnImg  != null, "PresetsMenuBtnImg must be populated.");
            Assert.IsTrue(_ui.PresetsMenuBtnTmp  != null, "PresetsMenuBtnTmp must be populated.");
            Assert.IsTrue(_ui.PropsMenuBtnImg    != null, "PropsMenuBtnImg must be populated.");
            Assert.IsTrue(_ui.PropsMenuBtnTmp    != null, "PropsMenuBtnTmp must be populated.");
            Assert.IsTrue(_ui.ViewMenuBtnImg     != null, "ViewMenuBtnImg must be populated.");
            Assert.IsTrue(_ui.ViewMenuBtnTmp     != null, "ViewMenuBtnTmp must be populated.");
            Assert.IsTrue(_ui.SpellsMenuBtnImg   != null, "SpellsMenuBtnImg must be populated.");
            Assert.IsTrue(_ui.SpellsMenuBtnTmp   != null, "SpellsMenuBtnTmp must be populated.");
        }

        // ── Panels ──────────────────────────────────────────────────────────────

        [Test]
        public void BuildAll_AllFivePanels_NonNull_With_DraggablePanel_And_PanelChrome()
        {
            var panels = new[]
            {
                (_ui.ToolsDropdown,   _ui.ToolsPanelDrag,   "Tools"),
                (_ui.PresetsDropdown, _ui.PresetsPanelDrag, "Presets"),
                (_ui.PropsDropdown,   _ui.PropsPanelDrag,   "Properties"),
                (_ui.ViewDropdown,    _ui.ViewPanelDrag,    "View"),
                (_ui.SpellsDropdown,  _ui.SpellsPanelDrag,  "Spells"),
            };

            foreach (var (go, drag, name) in panels)
            {
                Assert.IsTrue(go   != null, $"{name} dropdown GameObject must be non-null.");
                Assert.IsTrue(drag != null, $"{name} DraggablePanel ref must be non-null.");
                Assert.IsNotNull(go.GetComponent<DraggablePanel>(),
                    $"{name} panel must have DraggablePanel component.");
                Assert.IsNotNull(go.GetComponent<PanelChrome>(),
                    $"{name} panel must have PanelChrome component.");
            }
        }

        [Test]
        public void BuildAll_AllDropdownPanels_StartHidden()
        {
            Assert.IsFalse(_ui.ToolsDropdown.activeSelf,   "ToolsDropdown must start hidden.");
            Assert.IsFalse(_ui.PresetsDropdown.activeSelf, "PresetsDropdown must start hidden.");
            Assert.IsFalse(_ui.PropsDropdown.activeSelf,   "PropsDropdown must start hidden.");
            Assert.IsFalse(_ui.ViewDropdown.activeSelf,    "ViewDropdown must start hidden.");
            Assert.IsFalse(_ui.SpellsDropdown.activeSelf,  "SpellsDropdown must start hidden.");
        }

        // ── Presets panel refs ───────────────────────────────────────────────────

        [Test]
        public void BuildAll_Presets_Panel_Refs_AllPopulated()
        {
            Assert.IsTrue(_ui.PickerContent       != null, "PickerContent must be populated.");
            Assert.IsTrue(_ui.StatusText          != null, "StatusText must be populated.");
            Assert.IsTrue(_ui.SearchBox           != null, "SearchBox must be populated.");
            Assert.IsTrue(_ui.PresetsTabStrip     != null, "PresetsTabStrip must be populated.");
            // Table view refs
            Assert.IsTrue(_ui.PresetsTableHeaderScroll  != null, "PresetsTableHeaderScroll must be populated.");
            Assert.IsTrue(_ui.PresetsTableHeaderContent != null, "PresetsTableHeaderContent must be populated.");
            Assert.IsTrue(_ui.PresetsTableBodyScroll    != null, "PresetsTableBodyScroll must be populated.");
            Assert.IsTrue(_ui.PresetsTableBodyContent   != null, "PresetsTableBodyContent must be populated.");
        }

        [Test]
        public void BuildAll_View_Panel_Refs_AllPopulated()
        {
            // LargePreviewImage was removed from the Presets panel and is now ViewRawImage.
            Assert.IsTrue(_ui.ViewRawImage          != null, "ViewRawImage must be populated.");
            Assert.IsTrue(_ui.ViewPresetNameTmp     != null, "ViewPresetNameTmp must be populated.");
            Assert.IsTrue(_ui.ViewStatusTmp         != null, "ViewStatusTmp must be populated.");
            Assert.IsTrue(_ui.ViewPlayPauseBtn      != null, "ViewPlayPauseBtn must be populated.");
            Assert.IsTrue(_ui.ViewSpeed1Btn         != null, "ViewSpeed1Btn must be populated.");
        }

        // ── Properties panel refs ────────────────────────────────────────────────

        [Test]
        public void BuildAll_Properties_Panel_Refs_AllPopulated()
        {
            Assert.IsTrue(_ui.PresetPropsText   != null, "PresetPropsText must be populated.");
            Assert.IsTrue(_ui.InstancePropsText != null, "InstancePropsText must be populated.");
        }

        // ── Tools panel refs ─────────────────────────────────────────────────────

        [Test]
        public void BuildAll_Tools_Panel_UndoRedoLabels_Populated()
        {
            Assert.IsTrue(_ui.UndoBtnLabel != null, "UndoBtnLabel must be populated.");
            Assert.IsTrue(_ui.RedoBtnLabel != null, "RedoBtnLabel must be populated.");
        }

        // ── Spells panel refs ────────────────────────────────────────────────────

        [Test]
        public void BuildAll_Spells_Panel_Refs_AllPopulated()
        {
            Assert.IsTrue(_ui.SpellsContent   != null, "SpellsContent must be populated.");
            Assert.IsTrue(_ui.SpellsHeaderTmp != null, "SpellsHeaderTmp must be populated.");
        }

        // ── DraggablePanel.TopReservedPx ─────────────────────────────────────────

        [Test]
        public void BuildAll_Sets_TopReservedPx_To_MenuBarHeight()
        {
            // MENUBAR_HEIGHT is 30f (private const in builder).
            // After BuildAll the static field must equal that value.
            Assert.AreEqual(30f, DraggablePanel.TopReservedPx, 0.001f,
                "BuildAll must set DraggablePanel.TopReservedPx to MENUBAR_HEIGHT (30f).");
        }

        // ── ApplyMenuBtnStyle ────────────────────────────────────────────────────

        [Test]
        public void ApplyMenuBtnStyle_OpenState_Sets_Bold_And_ChangesImageColor()
        {
            var go = new GameObject("StyleTest");
            go.transform.SetParent(_canvasGo.transform, false);
            var img = go.AddComponent<Image>();

            var tmpGo = new GameObject("TmpChild");
            tmpGo.transform.SetParent(go.transform, false);
            var tmp = tmpGo.AddComponent<TextMeshProUGUI>();

            ParticlesEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, isOpen: false);
            var closedColor = img.color;
            var closedStyle = tmp.fontStyle;

            ParticlesEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, isOpen: true);
            var openColor = img.color;
            var openStyle = tmp.fontStyle;

            Assert.AreNotEqual(closedColor, openColor,
                "ApplyMenuBtnStyle must change Image color between open and closed states.");
            Assert.AreEqual(FontStyles.Bold, openStyle,
                "ApplyMenuBtnStyle(isOpen=true) must set TMP fontStyle to Bold.");
            Assert.AreEqual(FontStyles.Normal, closedStyle,
                "ApplyMenuBtnStyle(isOpen=false) must set TMP fontStyle to Normal.");

            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
