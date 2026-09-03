using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>Inventory Editor (F6) — what it remembers between sessions.</summary>
    public partial class InventoryRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_MODE           = "mode";
        private const string WS_CATEGORY       = "category";
        private const string WS_ENTITY_SEARCH  = "entitySearch";
        private const string WS_CATALOG_SEARCH = "catalogSearch";
        private const string WS_ENTITY         = "selectedEntity";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_MODE, _mode.ToString());
            ws.SetString(WS_CATEGORY, _category.ToString());
            ws.SetString(WS_ENTITY_SEARCH, _entitySearch ?? string.Empty);
            ws.SetString(WS_CATALOG_SEARCH, _catalogSearch ?? string.Empty);
            ws.SetString(WS_ENTITY, _selectedEntityName ?? string.Empty);
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // DeleteItem is not restored — reopening into it is how an author strips an
            // inventory they only meant to look at.
            if (Enum.TryParse(ws.GetString(WS_MODE, null), out EditorMode mode)
                && mode != EditorMode.DeleteItem)
                SetMode(mode);

            if (Enum.TryParse(ws.GetString(WS_CATEGORY, null), out EditorCategory category))
                _category = category;

            string entitySearch = ws.GetString(WS_ENTITY_SEARCH, null);
            if (entitySearch != null)
            {
                _entitySearch = entitySearch;
                if (_entitySearchBox != null) _entitySearchBox.SetTextWithoutNotify(entitySearch);
            }

            string catalogSearch = ws.GetString(WS_CATALOG_SEARCH, null);
            if (catalogSearch != null)
            {
                _catalogSearch = catalogSearch;
                if (_catalogSearchBox != null) _catalogSearchBox.SetTextWithoutNotify(catalogSearch);
            }

            // The selected entity is the NAME of a live scene object. It is only recorded
            // here; the panel refresh resolves it, and a name matching nothing simply
            // leaves the inspector empty.
            string entity = ws.GetString(WS_ENTITY, null);
            if (!string.IsNullOrEmpty(entity)) _selectedEntityName = entity;
        }
    }
}
