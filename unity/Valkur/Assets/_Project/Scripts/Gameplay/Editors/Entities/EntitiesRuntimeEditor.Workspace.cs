using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Entities
{
    /// <summary>Entities Editor (F5) — what it remembers between sessions.</summary>
    public partial class EntitiesRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_MODE     = "mode";
        private const string WS_CATEGORY = "category";
        private const string WS_SEARCH   = "search";
        private const string WS_ENTITY   = "selectedEntity";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_MODE, _mode.ToString());
            ws.SetString(WS_CATEGORY, _category.ToString());
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);
            ws.SetString(WS_ENTITY, _selectedKey ?? string.Empty);
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // Delete is not restored: reopening straight into it is how an author removes a
            // monster they only meant to inspect. Same rule as Buildings and Tile.
            if (Enum.TryParse(ws.GetString(WS_MODE, null), out EditorMode mode)
                && mode != EditorMode.Delete)
                SetMode(mode);

            if (Enum.TryParse(ws.GetString(WS_CATEGORY, null), out EntityCategory category))
                SelectCategory(category);

            string search = ws.GetString(WS_SEARCH, null);
            if (search != null)
            {
                _searchFilter = search;
                if (_ui.SearchBox != null) _ui.SearchBox.SetTextWithoutNotify(search);
            }

            // Resolved against the live picker: an entity key removed from the catalog
            // leaves nothing selected rather than selecting a neighbour.
            string key = ws.GetString(WS_ENTITY, null);
            if (!string.IsNullOrEmpty(key)) SelectEntity(key);
        }
    }
}
