using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.VFX;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Tests for the delete-related buttons in <see cref="ParticlesEditorUIBuilder.BuildAll"/>.
    ///
    /// Regression coverage:
    ///   - Bug 3: DeleteInstance button was wrapped in a row GO instead of being the
    ///     direct output of AddDangerBtn, causing layout issues.
    ///     Fix: DeleteInstanceBtnGo == DeleteInstanceBtnImg.gameObject (no wrapper row).
    /// </summary>
    [TestFixture]
    public class ParticlesUIBuilderDeleteButtonsTests
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
                onToggleGroup:     () => { },
                onToggleSpells:    () => { },
                onToggleTutorial:  () => { },
                onDeleteInZone:    () => { },
                onDeleteInstance:  () => { });
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── DeleteInZone button (Tools panel DANGER ZONE) ─────────────────────────

        [Test]
        public void BuildAll_PopulatesDeleteInZoneBtnImg()
        {
            Assert.IsTrue(_ui.DeleteInZoneBtnImg != null,
                "DeleteInZoneBtnImg must be populated after BuildAll.");
        }

        [Test]
        public void BuildAll_DeleteInZoneBtnImg_IsUnderToolsDropdown()
        {
            Assert.IsTrue(_ui.DeleteInZoneBtnImg != null,
                "DeleteInZoneBtnImg must exist.");
            Assert.IsTrue(_ui.ToolsDropdown != null,
                "ToolsDropdown must exist.");

            // The button must be a descendant of the Tools panel.
            var t = _ui.DeleteInZoneBtnImg.transform;
            bool found = false;
            while (t != null)
            {
                if (t.gameObject == _ui.ToolsDropdown)
                {
                    found = true;
                    break;
                }
                t = t.parent;
            }
            Assert.IsTrue(found,
                "DeleteInZoneBtnImg must be a descendant of ToolsDropdown.");
        }

        // ── DeleteInstance button (Properties panel) ──────────────────────────────

        [Test]
        public void BuildAll_PopulatesDeleteInstanceBtnGo()
        {
            Assert.IsTrue(_ui.DeleteInstanceBtnGo != null,
                "DeleteInstanceBtnGo must be populated after BuildAll.");
        }

        [Test]
        public void BuildAll_DeleteInstanceBtnGo_HiddenByDefault()
        {
            Assert.IsTrue(_ui.DeleteInstanceBtnGo != null,
                "DeleteInstanceBtnGo must exist.");
            Assert.IsFalse(_ui.DeleteInstanceBtnGo.activeSelf,
                "DeleteInstanceBtnGo must be hidden by default (no instance selected at startup).");
        }

        [Test]
        public void BuildAll_DeleteInstanceBtnImg_Populated()
        {
            Assert.IsTrue(_ui.DeleteInstanceBtnImg != null,
                "DeleteInstanceBtnImg must be populated after BuildAll.");
        }

        /// <summary>
        /// REGRESSION TEST — Bug 3: DeleteInstance button must NOT be wrapped in a row GO.
        /// DeleteInstanceBtnGo must be the same GameObject as DeleteInstanceBtnImg.gameObject.
        /// </summary>
        [Test]
        public void DeleteInstanceBtn_HasNoWrapperRow_RegressionBug3()
        {
            Assert.IsTrue(_ui.DeleteInstanceBtnGo  != null, "DeleteInstanceBtnGo must exist.");
            Assert.IsTrue(_ui.DeleteInstanceBtnImg != null, "DeleteInstanceBtnImg must exist.");

            Assert.AreSame(_ui.DeleteInstanceBtnImg.gameObject, _ui.DeleteInstanceBtnGo,
                "DeleteInstanceBtnGo must be the same GameObject as DeleteInstanceBtnImg.gameObject " +
                "(no wrapper row). Regression: previously a 'DeleteInstanceRow' wrapper caused layout issues.");
        }

        [Test]
        public void BuildAll_DeleteInstanceBtnGo_IsUnderPropsDropdown()
        {
            Assert.IsTrue(_ui.DeleteInstanceBtnGo != null, "DeleteInstanceBtnGo must exist.");
            Assert.IsTrue(_ui.PropsDropdown != null, "PropsDropdown must exist.");

            var t = _ui.DeleteInstanceBtnGo.transform;
            bool found = false;
            while (t != null)
            {
                if (t.gameObject == _ui.PropsDropdown)
                {
                    found = true;
                    break;
                }
                t = t.parent;
            }
            Assert.IsTrue(found,
                "DeleteInstanceBtnGo must be a descendant of PropsDropdown (Properties panel).");
        }
    }
}
