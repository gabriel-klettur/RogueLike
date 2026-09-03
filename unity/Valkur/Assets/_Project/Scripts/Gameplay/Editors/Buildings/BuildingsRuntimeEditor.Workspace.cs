using System;
using UnityEngine;
using Valkur.Core.Editors;
using Valkur.Gameplay.Editors.Workspace;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Buildings Editor (F10) — what it remembers between sessions.
    ///
    /// Half of the panel-key collision the workspace layer was built to make impossible:
    /// this editor and the Map Editor both build a panel named <c>"PropertiesPanel"</c>,
    /// and with <c>DraggablePanel.PersistenceKey</c> assigned nowhere in the project both
    /// fell back to the GameObject name and shared one remembered-closed bit. Adopting the
    /// layer namespaces them apart (<c>"Buildings Editor/PropertiesPanel"</c>) with no
    /// change to either builder.
    /// </summary>
    public partial class BuildingsRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_MODE        = "mode";
        private const string WS_TEMPLATE    = "selectedTemplate";
        private const string WS_SEARCH      = "search";
        private const string WS_CATEGORY    = "categoryTab";
        private const string WS_BRUSH_SIZE  = "colliderBrushSize";

        private const string WS_SELECTION_BUILDING = "building";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            ws.SetString(WS_MODE, _mode.ToString());
            ws.SetInt(WS_TEMPLATE, _selectedTemplateId);
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);
            ws.SetString(WS_CATEGORY, _categoryFilter ?? string.Empty);
            ws.SetInt(WS_BRUSH_SIZE, _collBrushSize);

            // A placed building's stable identity is its InstanceId — the same key the
            // buildings file is written with, so the selection lives exactly as long as the
            // placement. Scoped by map slot AND zone: buildings are stored per zone, and an
            // InstanceId is only unique within one.
            if (_activeBuilding != null)
            {
                ws.selection.Set(WS_SELECTION_BUILDING,
                    _activeBuilding.InstanceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    EditorWorkspaceContext.CurrentMapSlot,
                    _activeBuilding.ZoneName ?? EditorWorkspaceContext.CurrentZone);
            }
            else
            {
                ws.selection.Clear();
            }
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // Deliberately NOT restored: Delete and Erase. Reopening straight into a
            // destructive mode is how an author removes a building they only meant to look
            // at — the same reason the Tile Editor refuses to restore its collider paint
            // modes. Select is the safe landing.
            if (Enum.TryParse(ws.GetString(WS_MODE, null), out EditorMode mode)
                && mode != EditorMode.Delete
                && mode != EditorMode.Erase)
            {
                SetMode(mode);
            }

            _collBrushSize = Mathf.Clamp(ws.GetInt(WS_BRUSH_SIZE, _collBrushSize), 1, 9);

            string search = ws.GetString(WS_SEARCH, null);
            if (search != null)
            {
                _searchFilter = search;
                if (_uiRefs.SearchBox != null) _uiRefs.SearchBox.SetTextWithoutNotify(search);
            }

            string category = ws.GetString(WS_CATEGORY, null);
            if (category != null)
            {
                _categoryFilter = category;
                if (_uiRefs.CategoryTabStrip != null) _uiRefs.CategoryTabStrip.SetActive(category);
            }

            RefreshPicker();

            RestoreSelectedTemplate(ws);
            RestoreSelectedBuilding(ws);
        }

        // ── Restore helpers ─────────────────────────────────────────────────────

        private void RestoreSelectedTemplate(EditorWorkspace ws)
        {
            int templateId = ws.GetInt(WS_TEMPLATE, -1);
            if (templateId < 0) return;

            // Resolved against the live catalog: a template removed by a re-import leaves
            // nothing selected rather than selecting whatever now sits at that id.
            if (_catalog == null || _catalog.GetById(templateId) == null) return;

            SelectTemplate(templateId);
        }

        private void RestoreSelectedBuilding(EditorWorkspace ws)
        {
            var record = ws.selection;
            if (record == null || !record.HasValue) return;
            if (record.type != WS_SELECTION_BUILDING) return;

            if (!record.AppliesTo(EditorWorkspaceContext.CurrentMapSlot,
                                  EditorWorkspaceContext.CurrentZone))
                return;

            if (!int.TryParse(record.id, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out int instanceId))
                return;

            var all = FindObjectsOfType<BuildingObject>(includeInactive: true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].InstanceId != instanceId) continue;
                SetActiveBuilding(all[i]);
                return;
            }

            // Deleted, or a different slot loaded — ordinary, and reported where the author
            // is looking rather than in the console.
            Toast("El edificio seleccionado antes ya no está en este mapa.");
        }
    }
}
