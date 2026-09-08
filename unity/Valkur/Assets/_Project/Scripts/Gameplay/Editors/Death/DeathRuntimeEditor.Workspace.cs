using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Editors.Death
{
    /// <summary>
    /// Death Editor — what it remembers between sessions.
    ///
    /// <para>Only the tab. There is no picker and no world selection here, and the one piece of
    /// state that looks worth keeping — the live death phase — deliberately is NOT: it belongs to
    /// the RUN, not to the author's workspace, and restoring it would mean an editor document
    /// telling the game somebody is dead. The <c>SelectedCellPos</c> defect the Tile editor
    /// exposed in this layer came from exactly that confusion between session state and world
    /// state.</para>
    ///
    /// <para>No destructive mode is restored either, because this editor has none: every action
    /// with consequences (matar, barrer, valores por defecto) is a button, and a button is not a
    /// mode an editor can silently reopen into.</para>
    /// </summary>
    public partial class DeathRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_TAB = "tab";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_TAB, _tab.ToString());
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            if (Enum.TryParse(ws.GetString(WS_TAB, null), out Tab tab)) _tab = tab;
        }
    }
}
