using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Buildings;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.BuildingsEditor
{
    /// <summary>
    /// The Buildings panel is resizable, like the four editor panels that already were
    /// (Tile, Items, Particles, Spells). It shipped without a grip, so its width was
    /// whatever the constant said and the author could not trade screen for catalog.
    ///
    /// The size persists for free — <c>DraggablePanel.CaptureState</c> records
    /// <c>sizeDelta</c> and the workspace layer writes it — so what is worth pinning is the
    /// grip itself, the corner it sits in, and the bounds it clamps to.
    /// </summary>
    [TestFixture]
    public class BuildingsPanelResizeTests
    {
        private GameObject _canvasGo;
        private GameObject _rootGo;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _canvasGo = new GameObject("TestCanvas");
            _canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _rootGo = new GameObject("Root", typeof(RectTransform));
            _rootGo.transform.SetParent(_canvasGo.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rootGo != null) UnityEngine.Object.DestroyImmediate(_rootGo);
            if (_canvasGo != null) UnityEngine.Object.DestroyImmediate(_canvasGo);
            LogAssert.ignoreFailingMessages = false;
        }

        private BuildingsEditorUIBuilder.UIRefs BuildUI()
            => BuildingsEditorUIBuilder.BuildAll(
                _rootGo.transform,
                onDropdownToggle: _ => { },
                onUndo: () => { }, onRedo: () => { },
                onSave: () => { }, onReload: () => { },
                onModeSelect: () => { }, onModePlace: () => { },
                onModeResize: () => { }, onModeDelete: () => { },
                onAddBuilding: () => { }, onRemoveBuilding: () => { }, onAddOnSystem: () => { },
                onToggleTutorial: () => { },
                onSearchChanged: _ => { },
                onSplitChanged: _ => { },
                onZBottomMinus: () => { }, onZBottomPlus: () => { },
                onZTopMinus: () => { }, onZTopPlus: () => { },
                onGridColsMinus: () => { }, onGridColsPlus: () => { },
                onGridRowsMinus: () => { }, onGridRowsPlus: () => { },
                onColliderScope: () => { },
                onInteractable: () => { },
                onPaintSolid: () => { }, onPaintWalk: () => { }, onSaveCU: () => { },
                onDeleteBuilding: () => { },
                onResetBuilding: () => { },
                onToggleCollidersVisible: () => { },
                onCollScopeToggle: () => { },
                onBrushPaint: () => { },
                onBrushErase: () => { },
                onCollBrushSizeChanged: _ => { },
                onCollBrushSizeStepDown: () => { },
                onCollBrushSizeStepUp: () => { },
                onToggleBuildingsVisible: () => { });

        private static PanelResizeHandle GripOf(GameObject panel)
            => panel == null ? null : panel.GetComponentInChildren<PanelResizeHandle>(true);

        [Test]
        public void BuildingsPanel_CarriesAResizeGrip()
        {
            var ui = BuildUI();
            Assert.IsNotNull(ui.BuildingsDropdown, "The Buildings panel must exist.");

            var grip = GripOf(ui.BuildingsDropdown);
            Assert.IsNotNull(grip,
                "Without a grip the panel is stuck at its constant, and its 1474-template grid " +
                "cannot be traded for screen space.");
            Assert.AreSame(ui.BuildingsDropdown.GetComponent<RectTransform>(), grip.Target,
                "The grip must resize the PANEL, not itself.");
        }

        [Test]
        public void TheGrip_SitsInTheCornerOppositeThePivot()
        {
            var ui = BuildUI();
            var panelRt = ui.BuildingsDropdown.GetComponent<RectTransform>();
            var gripRt  = (RectTransform)GripOf(ui.BuildingsDropdown).transform;

            // MakeDrop docks top-left, so the panel grows right and DOWN and the grip belongs
            // bottom-right. A grip on the pivot's own corner drags an edge that cannot move.
            Assert.AreEqual(new Vector2(0f, 1f), panelRt.pivot, "Sanity: the panel pivots top-left.");
            Assert.AreEqual(ResizeGripCorner.BottomRight, GripOf(ui.BuildingsDropdown).Corner);
            Assert.AreEqual(new Vector2(1f, 0f), gripRt.anchorMin);
            Assert.AreEqual(new Vector2(1f, 0f), gripRt.anchorMax);
            Assert.AreEqual(new Vector2(1f, 0f), gripRt.pivot,
                "Pivoted into its own corner: a rotated or negatively-scaled grip swings outside the panel.");
        }

        [Test]
        public void TheGrip_ClampsToBoundsThatKeepThePanelUsable()
        {
            var ui = BuildUI();
            var grip = GripOf(ui.BuildingsDropdown);

            // Two picker columns plus the scrollbar is the floor; below it the category tabs
            // wrap their labels and the grid stops being a grid.
            BuildingsRuntimeEditor.ResolvePickerMetrics(
                grip.MinSize.x - 8f * 2f - 12f, out int columnsAtMin, out _);
            Assert.GreaterOrEqual(columnsAtMin, 2,
                $"At the minimum width ({grip.MinSize.x:0}) the picker collapses to {columnsAtMin} column(s).");

            Assert.Greater(grip.MaxSize.x, grip.MinSize.x);
            Assert.Greater(grip.MaxSize.y, grip.MinSize.y);
            Assert.GreaterOrEqual(grip.MinSize.y, 300f,
                "Shorter than this and the search box plus four rows of category tabs leave no grid.");
        }

        [Test]
        public void TheGrip_DoesNotBlockTheStatusLine()
        {
            var ui = BuildUI();
            var gripRt = (RectTransform)GripOf(ui.BuildingsDropdown).transform;

            Assert.LessOrEqual(gripRt.rect.width, 20f,
                "The grip overlays the panel's bottom-right corner; a large one covers the status text.");
            Assert.LessOrEqual(gripRt.rect.height, 20f);
        }
    }
}
