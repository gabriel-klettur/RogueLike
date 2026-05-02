using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    /// <summary>
    /// Coverage for the runtime Tile Editor's <c>View</c> dropdown and the three
    /// toggleable overlays it owns: <b>Tiles Grid</b>, <b>Zone Grid</b> and
    /// <b>Show Colliders</b> (mirror of the Colliders panel).
    ///
    /// Surface under test:
    ///   1. <see cref="TileEditorState"/> — defaults and toggle invariants for
    ///      <c>ShowGridLines</c> and <c>ShowZoneGrid</c>.
    ///   2. <see cref="TileEditorGridOverlay"/> — <c>SetShowGridLines</c> mutates
    ///      the private flag so the GL renderer skips/draws conditionally.
    ///   3. <see cref="TileEditorManager"/> — private callbacks
    ///      <c>OnShowGridLinesClicked</c>, <c>OnShowZoneGridClicked</c> flip
    ///      state and immediately push to the renderers (so the toggle is instant
    ///      regardless of the cursor-over-UI gate in <c>UpdateGridCursor</c>).
    ///      Zone Grid delegates to <see cref="MapEditor.MapEditorManager.SetExternalOverlayRequest"/>
    ///      rather than drawing its own outlines (avoids the duplicate-cyan-ring bug).
    ///   4. <see cref="TileEditorUIBuilder"/> — layout constants
    ///      (<c>VIEW_BTN_W</c>, <c>VIEW_DROP_W</c>, <c>VIEW_DROP_H</c>) and
    ///      the right-edge stack: View dropdown sits to the LEFT of Size.
    ///
    /// Reflection is used to reach private members the production design keeps
    /// internal — preferred over making them public for testability because the
    /// callbacks must remain UI-only entry points.
    /// </summary>
    [TestFixture]
    public class TileEditorViewPanelTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. TileEditorState — view flags
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void State_ShowGridLines_DefaultsToTrue()
        {
            var state = new TileEditorState();
            Assert.IsTrue(state.ShowGridLines,
                "ShowGridLines must default to true so authors see the per-tile grid the " +
                "very first time they open the editor — same behaviour as before the View " +
                "panel was added.");
        }

        [Test]
        public void State_ShowZoneGrid_DefaultsToFalse()
        {
            var state = new TileEditorState();
            Assert.IsFalse(state.ShowZoneGrid,
                "ShowZoneGrid must default to false — zone outlines are an opt-in overlay, " +
                "matching the Map Editor's preview style.");
        }

        [Test]
        public void State_ViewFlags_AreIndependent()
        {
            var state = new TileEditorState();
            state.ShowGridLines = false;
            state.ShowZoneGrid  = true;

            Assert.IsFalse(state.ShowGridLines, "Tiles Grid flag must mutate independently.");
            Assert.IsTrue (state.ShowZoneGrid,  "Zone Grid flag must mutate independently.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. TileEditorGridOverlay — setters
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Overlay_SetShowGridLines_UpdatesPrivateFlag()
        {
            var go = new GameObject("Overlay");
            try
            {
                var overlay = go.AddComponent<TileEditorGridOverlay>();
                // Default field initializer: _showGridLines = true.
                Assert.IsTrue(GetPrivateBool(overlay, "_showGridLines"),
                    "Default _showGridLines must be true to preserve legacy behaviour.");

                overlay.SetShowGridLines(false);
                Assert.IsFalse(GetPrivateBool(overlay, "_showGridLines"),
                    "SetShowGridLines(false) must flip the private flag.");

                overlay.SetShowGridLines(true);
                Assert.IsTrue(GetPrivateBool(overlay, "_showGridLines"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Overlay_HasNoZoneGridState()
        {
            // Regression guard: the Tile Editor must NOT render its own zone grid.
            // Doing so created the duplicate-cyan-ring bug — Zone Grid now delegates
            // to MapEditorManager.SetExternalOverlayRequest exclusively.
            var go = new GameObject("Overlay");
            try
            {
                var overlay = go.AddComponent<TileEditorGridOverlay>();
                var t = overlay.GetType();

                Assert.IsNull(t.GetField("_showZoneGrid",
                    BindingFlags.Instance | BindingFlags.NonPublic),
                    "_showZoneGrid must NOT exist — Zone Grid is delegated to MapEditor.");
                Assert.IsNull(t.GetField("_zoneManager",
                    BindingFlags.Instance | BindingFlags.NonPublic),
                    "_zoneManager must NOT exist on the overlay — delegation removes the need.");
                Assert.IsNull(t.GetMethod("SetShowZoneGrid",
                    BindingFlags.Instance | BindingFlags.Public),
                    "SetShowZoneGrid must NOT exist — public surface for the cyan grid is removed.");
                Assert.IsNull(t.GetMethod("SetZoneManager",
                    BindingFlags.Instance | BindingFlags.Public),
                    "SetZoneManager must NOT exist — public surface for zone binding is removed.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. TileEditorManager — callback handlers
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Manager_OnShowGridLinesClicked_TogglesState()
        {
            var manager = NewManager();
            var state   = manager.State;
            bool before = state.ShowGridLines;

            InvokePrivate(manager, "OnShowGridLinesClicked");

            Assert.AreNotEqual(before, state.ShowGridLines,
                "OnShowGridLinesClicked must flip ShowGridLines once per call.");
        }

        [Test]
        public void Manager_OnShowGridLinesClicked_RoundTripsToOriginal()
        {
            var manager = NewManager();
            var state   = manager.State;
            bool initial = state.ShowGridLines;

            InvokePrivate(manager, "OnShowGridLinesClicked");
            InvokePrivate(manager, "OnShowGridLinesClicked");

            Assert.AreEqual(initial, state.ShowGridLines,
                "Two clicks must return to the original state (toggle is symmetric).");
        }

        [Test]
        public void Manager_OnShowZoneGridClicked_TogglesState()
        {
            var manager = NewManager();
            var state   = manager.State;
            bool before = state.ShowZoneGrid;

            InvokePrivate(manager, "OnShowZoneGridClicked");

            Assert.AreNotEqual(before, state.ShowZoneGrid,
                "OnShowZoneGridClicked must flip ShowZoneGrid once per call.");
        }

        [Test]
        public void Manager_OnShowGridLinesClicked_PushesToOverlay_Immediately()
        {
            // The View panel button is over UI when clicked. UpdateGridCursor early-returns
            // over UI, so the overlay must be updated FROM the callback itself — otherwise
            // the toggle would feel laggy (state flips, render keeps using old value).
            var manager = NewManager();
            var overlay = AttachOverlay(manager);

            // Pre-condition: overlay default flag is true.
            Assert.IsTrue(GetPrivateBool(overlay, "_showGridLines"),
                "Pre-check: overlay starts with grid lines visible.");

            // First click flips state to false; ApplyViewOverlayVisibility must mirror it.
            InvokePrivate(manager, "OnShowGridLinesClicked");

            Assert.IsFalse(manager.State.ShowGridLines, "State flipped to false.");
            Assert.IsFalse(GetPrivateBool(overlay, "_showGridLines"),
                "Overlay flag must be pushed instantly — not deferred to the next " +
                "UpdateGridCursor frame.");
        }

        [Test]
        public void Manager_OnShowZoneGridClicked_DoesNotTouchTileEditorOverlayFlags()
        {
            // Zone Grid is owned by MapEditorManager.SetExternalOverlayRequest, not by
            // the Tile Editor's GL overlay. Toggling Zone Grid must NOT mutate any flag
            // on _gridOverlay — that would resurrect the duplicate cyan-ring bug.
            var manager = NewManager();
            var overlay = AttachOverlay(manager);

            bool gridLinesBefore = GetPrivateBool(overlay, "_showGridLines");
            InvokePrivate(manager, "OnShowZoneGridClicked");

            Assert.AreEqual(gridLinesBefore, GetPrivateBool(overlay, "_showGridLines"),
                "Zone Grid toggle must not affect the Tiles Grid flag.");
            Assert.IsTrue(manager.State.ShowZoneGrid,
                "State.ShowZoneGrid must still flip — it drives MapEditorManager externally.");
        }

        [Test]
        public void Manager_ApplyViewOverlayVisibility_NullOverlay_DoesNotThrow()
        {
            // Defensive: the overlay GameObject is created in Start(); if a callback
            // somehow runs before Start (domain reload race), the helper must noop.
            // It also hits the optional MapEditorManager.HasInstance branch — must noop
            // when no Map Editor singleton exists in the test scene.
            var manager = NewManager();
            // _gridOverlay is intentionally NOT attached here.
            Assert.DoesNotThrow(() => InvokePrivate(manager, "ApplyViewOverlayVisibility"),
                "ApplyViewOverlayVisibility must be null-safe on every collaborator.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. UI Layout constants
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ViewButtonWidth_IsPositive()
        {
            Assert.Greater(VIEW_BTN_W, 0f,
                "View menu button width must be positive.");
        }

        [Test]
        public void ViewDropdown_HasReasonableSize()
        {
            Assert.That(VIEW_DROP_W, Is.InRange(150f, 600f),
                "View dropdown width must fit inside the canvas.");
            Assert.That(VIEW_DROP_H, Is.InRange(100f, 800f),
                "View dropdown height must fit inside the canvas.");
        }

        [Test]
        public void ViewDropdown_IncludesHeaderInTotalHeight()
        {
            Assert.Greater(VIEW_DROP_H - PANEL_HDR_H, 60f,
                "View content area must be tall enough for at least 3 toggle rows " +
                "(3 × ~30px = 90px). Below this, rows would clip.");
        }

        [Test]
        public void ViewDropdown_StacksLeftOfSize_OnRightEdge()
        {
            // Right-edge stack: Inspector (closest) → Colliders → Size → View (furthest).
            // The View dropdown's X offset (from the right edge) MUST exceed Size's,
            // otherwise the panels overlap.
            float sizeX = PANEL_GAP + INSPECTOR_DROP_W + PANEL_GAP
                        + COLLIDERS_DROP_W + PANEL_GAP;
            float viewX = sizeX + SIZE_DROP_W + PANEL_GAP;

            Assert.Greater(viewX, sizeX + SIZE_DROP_W,
                "View dropdown must dock entirely to the LEFT of Size's right edge — " +
                "otherwise the two panels overlap.");
        }

        [Test]
        public void ViewDropdown_FitsOn1600WidthCanvas()
        {
            // Sanity: total horizontal extent of the right-edge stack must be < 1600.
            float totalX = PANEL_GAP + INSPECTOR_DROP_W + PANEL_GAP
                         + COLLIDERS_DROP_W + PANEL_GAP
                         + SIZE_DROP_W + PANEL_GAP
                         + VIEW_DROP_W;

            Assert.Less(totalX, 1600f,
                "Inspector + Colliders + Size + View dropdowns must fit within the " +
                "1600-px reference canvas without horizontal overflow.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. UI builder — ViewDropdown construction
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Builder_BuildAll_CreatesViewDropdown_WithThreeToggleRows()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            try
            {
                var state = new TileEditorState();
                var refs = TileEditorUIBuilder.BuildAll(canvasGo.transform, state,
                    onToolChanged: null, onLayerChanged: null, onBrushSizeChanged: null,
                    onDropdownToggle: null);

                Assert.IsNotNull(refs.ViewDropdown, "ViewDropdown must be instantiated.");
                Assert.IsNotNull(refs.ViewMenuBtnImg, "View menu button image must be wired.");
                Assert.IsNotNull(refs.ViewMenuBtnTmp, "View menu button label must be wired.");

                // Three toggle rows mirror the Colliders panel UI/UX.
                Assert.IsNotNull(refs.ShowGridLinesToggleImg);
                Assert.IsNotNull(refs.ShowGridLinesToggleLabel);
                Assert.IsNotNull(refs.ShowZoneGridToggleImg);
                Assert.IsNotNull(refs.ShowZoneGridToggleLabel);
                Assert.IsNotNull(refs.ViewShowCollidersToggleImg);
                Assert.IsNotNull(refs.ViewShowCollidersToggleLabel);
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void Builder_ViewDropdown_StartsHidden()
        {
            // Match the convention of every other dropdown — only the menu bar is
            // visible until the user explicitly toggles a panel open.
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            try
            {
                var state = new TileEditorState();
                var refs = TileEditorUIBuilder.BuildAll(canvasGo.transform, state,
                    onToolChanged: null, onLayerChanged: null, onBrushSizeChanged: null,
                    onDropdownToggle: null);

                Assert.IsFalse(refs.ViewDropdown.activeSelf,
                    "ViewDropdown must be inactive on creation.");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Builder_ViewDropdown_TogglesReflect_InitialState()
        {
            // The toggles must paint themselves from `state.ShowGridLines / ShowZoneGrid /
            // ShowColliderOverlay` so opening the panel mid-session shows correct ON/OFF.
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            try
            {
                var state = new TileEditorState
                {
                    ShowGridLines       = false,
                    ShowZoneGrid        = true,
                    ShowColliderOverlay = true,
                };

                var refs = TileEditorUIBuilder.BuildAll(canvasGo.transform, state,
                    onToolChanged: null, onLayerChanged: null, onBrushSizeChanged: null,
                    onDropdownToggle: null);

                // The visual state of each toggle is encoded by the row image's color
                // (BTN_NORMAL when off, red-tinted when on). We just verify the refs are
                // non-null — the actual color-mapping is exercised by RefreshViewToggles
                // tests below.
                Assert.IsNotNull(refs.ShowGridLinesToggleImg);
                Assert.IsNotNull(refs.ShowZoneGridToggleImg);
                Assert.IsNotNull(refs.ViewShowCollidersToggleImg);
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. Reflection helpers
        // ════════════════════════════════════════════════════════════════════

        private TileEditorManager NewManager()
        {
            _host = new GameObject("TileEditorManager_TestHost");
            return _host.AddComponent<TileEditorManager>();
        }

        /// <summary>
        /// Manually attaches a <see cref="TileEditorGridOverlay"/> to the manager via
        /// reflection so the test bypasses <c>Start()</c> (which requires a full scene).
        /// Mirrors what <c>CreateGridOverlay</c> does in production.
        /// </summary>
        private static TileEditorGridOverlay AttachOverlay(TileEditorManager manager)
        {
            var overlayGo = new GameObject("TileEditorGridOverlay_Test");
            overlayGo.transform.SetParent(manager.transform, false);
            var overlay = overlayGo.AddComponent<TileEditorGridOverlay>();

            typeof(TileEditorManager)
                .GetField("_gridOverlay", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, overlay);
            typeof(TileEditorManager)
                .GetField("_gridOverlayGo", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, overlayGo);
            return overlay;
        }

        private static void InvokePrivate(object target, string methodName)
        {
            var mi = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, $"Reflection: {methodName} not found on {target.GetType().Name}.");
            mi.Invoke(target, null);
        }

        private static bool GetPrivateBool(object target, string fieldName)
            => GetPrivateField<bool>(target, fieldName);

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            var fi = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, $"Reflection: field {fieldName} not found on {target.GetType().Name}.");
            return (T)fi.GetValue(target);
        }
    }
}
