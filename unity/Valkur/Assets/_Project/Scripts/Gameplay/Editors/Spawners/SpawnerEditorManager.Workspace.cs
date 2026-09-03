using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>Spawner Editor (F3) — what it remembers between sessions.</summary>
    public partial class SpawnerEditorManager : IProvidesWorkspaceState
    {
        private const string WS_MODE   = "mode";
        private const string WS_SEARCH = "search";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_MODE, _mode.ToString());
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);

            // The selected INSTANCE is not captured. A spawner placement's coordinates are
            // the subject of a past incident (SPAWNER_COORDINATE_SPACE_DRIFT), where a save
            // and its loader disagreed about which space a position was in; a remembered
            // selection resolved through that same path would point the author's next edit
            // at a spawner far from the one they picked. The filters carry no such risk.
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // This editor has only Select and Place — no destructive mode to guard against,
            // unlike Buildings, Entities, Inventory and Tile.
            if (Enum.TryParse(ws.GetString(WS_MODE, null), out EditorMode mode))
                SetMode(mode);

            string search = ws.GetString(WS_SEARCH, null);
            if (search != null) _searchFilter = search;
        }
    }
}
