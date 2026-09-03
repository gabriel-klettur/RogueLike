using UnityEngine;
using Valkur.Core.Editors;
using Valkur.Gameplay.Editors.Workspace;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Map Editor (F11) — what it remembers between sessions.
    ///
    /// The other half of the panel-key collision this layer exists to make impossible:
    /// this editor and the Buildings Editor both build a <c>"PropertiesPanel"</c>, and with
    /// no owner they shared one remembered-closed bit. Adopting the layer namespaces them
    /// (<c>"Map Editor/PropertiesPanel"</c>) without either builder changing.
    ///
    /// Its state is small — this editor's real content is the zone database on disk, which
    /// has its own persistence and is not the workspace's business.
    /// </summary>
    public partial class MapEditorManager : IProvidesWorkspaceState
    {
        private const string WS_RESTRICT_EDITS = "restrictTileEditingToEditableZones";

        private const string WS_SELECTION_ZONE = "zone";

        public Transform WorkspaceRoot => _ui != null ? _ui.CanvasRoot : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null || _state == null) return;

            ws.SetBool(WS_RESTRICT_EDITS, _state.RestrictTileEditingToEditableZones);

            // A zone's stable identity is its NAME — there is no numeric id, and the
            // editor's own rename path rewrites every reference to it. Scoped by map slot
            // because zone names are per slot; not scoped by zone, which would be circular.
            //
            // NextZoneIndex is deliberately NOT captured: it is a counter for minting the
            // next default name, derived from what is already on disk, and restoring a
            // stale value would mint a name that collides with one created since.
            if (_state.HasSelection)
            {
                ws.selection.Set(WS_SELECTION_ZONE, _state.SelectedZone,
                    EditorWorkspaceContext.CurrentMapSlot, currentZone: string.Empty);
            }
            else
            {
                ws.selection.Clear();
            }
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null || _state == null) return;

            _state.RestrictTileEditingToEditableZones =
                ws.GetBool(WS_RESTRICT_EDITS, _state.RestrictTileEditingToEditableZones);

            RestoreSelectedZone(ws);
        }

        private void RestoreSelectedZone(EditorWorkspace ws)
        {
            var record = ws.selection;
            if (record == null || !record.HasValue) return;
            if (record.type != WS_SELECTION_ZONE) return;

            if (!record.AppliesTo(EditorWorkspaceContext.CurrentMapSlot, currentZone: string.Empty))
                return;

            // Resolved against the live database. A zone renamed or deleted between
            // sessions leaves nothing selected — and here that matters more than usual,
            // because several of this editor's operations (rename, move, delete, toggle
            // editable) act on the SELECTION, so restoring the wrong one would point every
            // one of them at a zone the author never picked.
            if (zoneManager == null || !zoneManager.TryGetZone(record.id, out _))
            {
                _ui?.SetStatus("La zona seleccionada antes ya no existe en este slot.");
                return;
            }

            OnZoneSelected(record.id);
        }
    }
}
