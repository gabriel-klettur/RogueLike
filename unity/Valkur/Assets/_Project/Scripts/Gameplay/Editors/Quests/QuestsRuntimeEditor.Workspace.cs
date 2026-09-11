using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Editors.Quests
{
    /// <summary>
    /// Quests editor — what it remembers between sessions.
    ///
    /// <para>The tab and the selected quest ID. Both are the author's PLACE in the catalogue,
    /// which is what a workspace is for, and neither says anything about the run: restoring an
    /// id whose quest has since been completed or dropped is harmless, because
    /// <c>Selected()</c> resolves against the live catalogue on every refresh and the detail
    /// panel simply reports what that quest's state is now.</para>
    ///
    /// <para><b>No destructive state is restored, because none of it is a MODE.</b> Every
    /// action here is a button behind a two-click arm, and <c>_armedDangerId</c> is deliberately
    /// NOT persisted — coming back days later to a panel already asking "Seguro?" is one click
    /// from dropping a quest nobody meant to touch, which is exactly the rule the conversation
    /// panel's own Abandonar follows.</para>
    /// </summary>
    public partial class QuestsRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_TAB = "tab";
        private const string WS_SELECTED = "selected";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_TAB, _tab.ToString());
            ws.SetString(WS_SELECTED, _selectedId ?? string.Empty);
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            if (Enum.TryParse(ws.GetString(WS_TAB, null), out Tab tab)) _tab = tab;

            string id = ws.GetString(WS_SELECTED, null);
            _selectedId = string.IsNullOrEmpty(id) ? null : id;
        }
    }
}
