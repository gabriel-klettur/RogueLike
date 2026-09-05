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
    /// <para>Which context and which keyboard layout the author was looking at, and the search
    /// text. Not the SELECTED KEY: a selection is a question the author was asking a minute
    /// ago, and reopening onto a highlighted key with a detail line about it reads as a state
    /// the editor is in rather than as a leftover.</para>
    ///
    /// <para>Nothing about the BINDINGS is workspace state — those live in
    /// <see cref="Valkur.Core.Input.InputBindingStore"/>, because they belong to the player
    /// and must survive independently of whether this editor was ever opened.</para>
    /// </summary>
    public partial class ControlsRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_CONTEXT = "viewContext";
        private const string WS_LAYOUT = "layout";
        private const string WS_SEARCH = "search";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_CONTEXT, _viewContext ?? InputContexts.War);
            ws.SetString(WS_LAYOUT, _layout.ToString());
            ws.SetString(WS_SEARCH, _search ?? "");
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // Not validated against the live editor registry here: RestoreWorkspace runs
            // before Activate populates the strip, and a context whose editor has since been
            // removed simply paints an empty board rather than throwing.
            var context = ws.GetString(WS_CONTEXT, null);
            if (!string.IsNullOrEmpty(context)) _viewContext = context;
            if (Enum.TryParse(ws.GetString(WS_LAYOUT, null), out KeyboardLayoutKind layout))
                _layout = layout;
            _search = ws.GetString(WS_SEARCH, "") ?? "";

            // Applied to the field, not through SetLayout/SetViewContext: those early-return on
            // an unchanged value and rebuild UI that does not exist yet at restore time.
            _selectedControl = null;
            _selectedMouse = Valkur.Core.Input.MouseControl.None;
        }
    }
}
