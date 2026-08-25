using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    /// <summary>
    /// Regression coverage for two wiring bugs fixed together in
    /// <c>TileEditorUIBuilder.cs</c> / <c>TileEditorUIBuilder.MenuBar.cs</c> /
    /// <c>TileEditorUI.Refresh.cs</c>:
    ///
    /// BUG 1 — <c>UIRefs.StatusText</c> was declared but never assigned (the menu
    /// bar built an empty flexible <c>Spacer</c> instead of a real TMP label), so
    /// all 61 <c>SetStatus(...)</c> call sites across the editor were silent no-ops.
    /// <c>BuildMenuBar</c> now assigns a real <see cref="TextMeshProUGUI"/> to
    /// <c>refs.StatusText</c> in the same flexible slot, and <c>SetStatus</c> falls
    /// back to <c>Debug.Log</c> when the ref is still unassigned so the editor can
    /// never go silently mute again.
    ///
    /// BUG 2 — <c>TileEditorUIBuilder.BuildAll</c> never set
    /// <see cref="DraggablePanel.TopReservedPx"/>, so draggable panels (Tools,
    /// Tiles, Layers, ...) could be dragged up underneath the menu bar. The other
    /// 12 runtime editors all reserve this strip on their first line of BuildAll
    /// (see e.g. ItemsEditorUIBuilder.cs, BuildingsEditorUIBuilder.cs) — Tile
    /// Editor now matches.
    /// </summary>
    [TestFixture]
    public class TileEditorUIBuilderTests
    {
        private GameObject _canvasGo;
        private TileEditorUIBuilder.UIRefs _refs;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Known baseline so the BUG 2 assertion actually proves BuildAll wrote
            // the value, rather than coincidentally matching a leftover from a
            // previous test in the same session.
            DraggablePanel.TopReservedPx = 0f;

            _canvasGo = new GameObject("TestCanvas");
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(_canvasGo.transform, false);

            _refs = TileEditorUIBuilder.BuildAll(root.transform, new TileEditorState(),
                onToolChanged:      _ => { },
                onLayerChanged:     _ => { },
                onBrushSizeChanged: _ => { },
                onDropdownToggle:   _ => { });
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            DraggablePanel.TopReservedPx = 0f;
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helper (only needed for the SetStatus instance tests) ────

        private static void SetRefsField(TileEditorUI ui, TileEditorUIBuilder.UIRefs refs)
        {
            var f = typeof(TileEditorUI).GetField("_refs",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "Reflection: TileEditorUI._refs field not found.");
            f.SetValue(ui, refs);
        }

        // ════════════════════════════════════════════════════════════════════
        // BUG 2 — DraggablePanel.TopReservedPx
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void BuildAll_Sets_TopReservedPx_To_MenuBarHeight()
        {
            Assert.AreEqual(MENUBAR_HEIGHT, DraggablePanel.TopReservedPx, 0.001f,
                "BuildAll must reserve the menu-bar strip so draggable panels can't be dragged " +
                "underneath it — matches the pattern used by the other 12 runtime editors " +
                "(ItemsEditorUIBuilder.cs:118, BuildingsEditorUIBuilder.cs:156, etc.).");
        }

        // ════════════════════════════════════════════════════════════════════
        // BUG 1 — refs.StatusText assignment + styling
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void BuildMenuBar_PopulatesStatusText_AsRealComponent()
        {
            Assert.IsTrue(_refs.StatusText != null,
                "BuildMenuBar must assign refs.StatusText to a real TextMeshProUGUI. Before the fix " +
                "the flexible gap between the dropdown buttons and PANELS/UX/PERF was an empty " +
                "Spacer and UIRefs.StatusText was never assigned, so every SetStatus(...) call in " +
                "the editor (61 call sites) silently did nothing.");
        }

        [Test]
        public void StatusText_KeepsSpacers_FlexibleWidthLayoutElement()
        {
            var le = _refs.StatusText.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "StatusText must carry a LayoutElement — it fills the same flexible slot the " +
                "old empty Spacer occupied.");
            Assert.AreEqual(1f, le.flexibleWidth, 0.001f,
                "flexibleWidth must stay 1f so swapping Spacer→StatusText doesn't resize the menu bar.");
        }

        [Test]
        public void StatusText_UsesThemeMutedColor_NotHardcoded()
        {
            Assert.AreEqual(TEXT_MUTED, _refs.StatusText.color,
                "StatusText must pull its colour from the shared UITheme.TEXT_MUTED alias (via " +
                "TileEditorUIHelpers), matching the rest of the menu bar — not a hardcoded value.");
        }

        [Test]
        public void StatusText_IsCenterAligned_11pt()
        {
            Assert.AreEqual(TextAlignmentOptions.Center, _refs.StatusText.alignment,
                "StatusText must be center-aligned within its flexible slot.");
            Assert.AreEqual(11f, _refs.StatusText.fontSize, 0.001f,
                "StatusText must use the same 11pt size as the other menu-bar buttons.");
        }

        [Test]
        public void StatusText_StartsEmpty()
        {
            Assert.AreEqual(string.Empty, _refs.StatusText.text,
                "StatusText must start blank — the first real SetStatus(...) call populates it " +
                "once the editor opens.");
        }

        // ════════════════════════════════════════════════════════════════════
        // BUG 1 — SetStatus wiring end-to-end (TileEditorUI.Refresh.cs)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void SetStatus_WithAssignedStatusText_WritesTextDirectly()
        {
            var uiGo = new GameObject("TileEditorUI_Assigned");
            try
            {
                var ui = uiGo.AddComponent<TileEditorUI>();
                SetRefsField(ui, _refs);

                ui.SetStatus("Painting layer 3");

                Assert.AreEqual("Painting layer 3", _refs.StatusText.text,
                    "SetStatus must write straight into the real StatusText once it's assigned.");
            }
            finally
            {
                Object.DestroyImmediate(uiGo);
            }
        }

        [Test]
        public void SetStatus_WithUnassignedStatusText_FallsBackToDebugLog()
        {
            var uiGo = new GameObject("TileEditorUI_Unassigned");
            try
            {
                var ui = uiGo.AddComponent<TileEditorUI>();
                // Leave _refs at its default(UIRefs) — StatusText stays null, reproducing the
                // pre-fix state where the editor had no TMP to write into at all.

                LogAssert.Expect(LogType.Log, "[TileEditor] Editor offline");
                ui.SetStatus("Editor offline");
            }
            finally
            {
                Object.DestroyImmediate(uiGo);
            }
        }
    }
}
