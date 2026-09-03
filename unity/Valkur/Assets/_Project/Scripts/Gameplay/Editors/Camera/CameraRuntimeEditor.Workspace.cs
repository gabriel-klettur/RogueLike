using System;
using UnityEngine;
using Valkur.Core.Editors;
using Valkur.Data.Feel;

namespace Valkur.Gameplay.Editors.CameraFeelEditor
{
    /// <summary>
    /// Camera Editor — what it remembers between sessions.
    ///
    /// No world selection and no picker: the one thing worth keeping is which cue the
    /// author was tuning, which is otherwise re-chosen on every open.
    /// </summary>
    public partial class CameraRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_CUE = "selectedCue";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_CUE, _selectedCue.ToString());
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            if (Enum.TryParse(ws.GetString(WS_CUE, null), out CameraFeelCue cue))
                _selectedCue = cue;
        }
    }
}
