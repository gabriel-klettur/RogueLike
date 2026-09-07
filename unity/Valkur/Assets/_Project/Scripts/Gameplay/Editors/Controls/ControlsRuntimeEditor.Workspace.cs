using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Editors;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Controls
{
    /// <summary>
    /// Controls editor — what it remembers between sessions.
    ///
    /// <para>Which context and which keyboard layout the author was looking at, the search
    /// text, and which multi-key rows they had opened. Not the SELECTED KEY: a selection is a
    /// question the author was asking a minute ago, and reopening onto a highlighted key with
    /// a detail line about it reads as a state the editor is in rather than as a leftover.</para>
    ///
    /// <para>The expanded rows ARE remembered, and the difference from a selection is worth
    /// stating: opening Move's eight slots is a decision about what this panel SHOWS, the same
    /// kind of thing as the hidden table columns the Items editor persists, and an author who
    /// is halfway through moving four of WASD does not want the row shut on them by a
    /// Play-mode restart.</para>
    ///
    /// <para>Nothing about the BINDINGS is workspace state — those live in
    /// <see cref="Valkur.Core.Input.InputBindingStore"/>, because they belong to the player
    /// and must survive independently of whether this editor was ever opened.</para>
    /// </summary>
    public partial class ControlsRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_CONTEXT  = "viewContext";
        private const string WS_LAYOUT   = "layout";
        private const string WS_SEARCH   = "search";
        private const string WS_EXPANDED = "expanded";
        private const string WS_LAYOUT_VERSION = "layoutVersion";

        /// <summary>Joins the expanded action ids. A semicolon rather than a control character:
        /// the workspace document is JSON a human may open, and an id is <c>Map/Action</c>, so
        /// a semicolon can never appear inside one.</summary>
        private const string Separator = ";";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_CONTEXT, _viewContext ?? InputContexts.War);
            ws.SetString(WS_LAYOUT, _layout.ToString());
            ws.SetString(WS_SEARCH, _search ?? "");
            ws.SetString(WS_EXPANDED, string.Join(Separator, _expanded));
            ws.SetString(WS_LAYOUT_VERSION, ControlsEditorUIBuilder.LAYOUT_VERSION);
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // Both panels are forced back open, and this runs AFTER the service has applied the
            // stored panel states — which is exactly what its own comment promises: "an
            // editor's own Restore may want to act on panels that are, by now, back where they
            // belong."
            //
            // It is a self-heal, not a preference. A panel closed once was stored as closed
            // forever, and this editor has no way to reopen one: the only toolbar lives INSIDE
            // the board panel, so closing that panel removes the keyboard, the mouse, the
            // context tabs, the legend and the status line with no route back. A shipped
            // workspace on this machine was in exactly that state — `"open": false` on
            // ControlsBoardPanel — which is what "the window makes no sense" looked like. The
            // close button is gone now; this puts the already-broken documents right.
            ReopenPanels();

            // THIS EDITOR DOES NOT REMEMBER ITS PANEL GEOMETRY, and that is a deliberate
            // exception rather than an oversight.
            //
            // Measured across three consecutive sessions, the stored geometry DRIFTED and each
            // session saved the drift: a board panel docked at exactly 1092x424 came back as
            // 1089.20x545.34, and the list panel walked from 16 px inside the right edge to
            // flush against the LEFT one. The restore path has several passes that each
            // legitimately adjust geometry — the service's off-screen rescue, the panel's own
            // anchor normalize a frame later, its canvas-resize clamp — and the capture on
            // close records whatever they left, so an error accumulates instead of settling.
            //
            // A two-panel fixed layout has very little to gain from remembering a size and
            // everything to lose from opening wrong, which is what the author actually sees.
            // Resizing and dragging still work for the session; they simply do not persist.
            // The other fifteen editors are untouched — this is one editor declining a feature,
            // not a change to the layer.
            if (isActiveAndEnabled) StartCoroutine(ApplyDefaultGeometryDeferred());

            // Not validated against the live editor registry here: RestoreWorkspace runs
            // before Activate populates the strip, and a context whose editor has since been
            // removed simply paints an empty board rather than throwing.
            var context = ws.GetString(WS_CONTEXT, null);
            if (!string.IsNullOrEmpty(context)) _viewContext = context;
            if (Enum.TryParse(ws.GetString(WS_LAYOUT, null), out KeyboardLayoutKind layout))
                _layout = layout;
            _search = ws.GetString(WS_SEARCH, "") ?? "";

            _expanded.Clear();
            var saved = ws.GetString(WS_EXPANDED, "") ?? "";
            if (saved.Length > 0)
                foreach (var id in saved.Split(new[] { Separator },
                                               StringSplitOptions.RemoveEmptyEntries))
                    _expanded.Add(id);

            // Applied to the field, not through SetLayout/SetViewContext: those early-return on
            // an unchanged value and rebuild UI that does not exist yet at restore time.
            _selectedControl = null;
            _selectedMouse = MouseControl.None;
        }

        /// <summary>Shows both panels and records them as open, so the stored document stops
        /// carrying the closed bit on the next capture.</summary>
        private void ReopenPanels()
        {
            Reopen(_ui?.BoardPanel, _ui?.BoardDrag);
            Reopen(_ui?.ListPanel, _ui?.ListDrag);
        }

        /// <summary>Frames to wait before re-docking. DraggablePanel normalizes on the frame
        /// after it is enabled and rescales on the frame it notices a canvas resize; three is
        /// past both with room to spare, and this runs once per stale document.</summary>
        private const int GEOMETRY_SETTLE_FRAMES = 3;

        private System.Collections.IEnumerator ApplyDefaultGeometryDeferred()
        {
            for (int i = 0; i < GEOMETRY_SETTLE_FRAMES; i++) yield return null;
            if (!_active || _ui?.BoardPanel == null) yield break;

            var before = ((RectTransform)_ui.BoardPanel.transform).sizeDelta;
            ControlsEditorUIBuilder.ApplyDefaultGeometry(_ui);
            var after = ((RectTransform)_ui.BoardPanel.transform).sizeDelta;

            VerboseLog.Log(VerboseLog.Category.Settings,
                () => $"[ControlsEditor] Panels re-docked: board {before} -> {after}.");
        }

        private static void Reopen(GameObject panel, DraggablePanel drag)
        {
            if (panel == null) return;
            if (!panel.activeSelf) panel.SetActive(true);
            drag?.MarkOpened();
        }
    }
}
