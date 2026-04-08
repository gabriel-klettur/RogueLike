using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Zone add / delete / duplicate flow operations for <see cref="MapEditorManager"/>.
    /// </summary>
    public partial class MapEditorManager
    {
        private void SelectZoneAtCursor()
        {
            if (!TryGetCursorTile(out var tilePos))
            {
                _ui?.SetStatus("No tile under cursor.");
                return;
            }

            if (!zoneManager.TryGetZoneAtTile(tilePos, out var zone))
            {
                _state.ClearSelection();
                _ui?.SetStatus($"No zone at tile {tilePos.x},{tilePos.y}");
                RefreshSelectionUIAndOverlay();
                return;
            }

            _state.SelectZone(zone.zoneName);
            _ui?.SetStatus($"Selected zone {zone.zoneName}");
            RefreshSelectionUIAndOverlay();
        }

        private void BeginAddZoneFlow()
        {
            if (!_state.HasSelection || !zoneManager.TryGetZone(_state.SelectedZone, out var sourceZone))
            {
                _ui?.SetStatus("Select a source zone before Add Zone.");
                return;
            }

            _isAddZoneFlowActive = true;
            _hasPendingAddTarget = false;
            _pendingAddZoneOffset = default;

            int width  = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int height = Mathf.Max(1, zoneManager.ZoneHeightTiles);

            _ui?.ShowAddZoneDialog(GenerateUniqueZoneName(), sourceZone.zoneName, sourceZone.editableInTileEditor);
            _ui?.SetAddZoneTarget(default, width, height, false);
            _ui?.SetStatus("Add Zone mode: click world to mark a 50x50 zone target, then confirm.");
            UpdateAddZonePreviewVisibility();
        }

        private void MarkAddZoneTargetAtCursor()
        {
            if (!_isAddZoneFlowActive) return;

            if (!TryGetCursorTile(out var tilePos))
            {
                _ui?.SetStatus("Cannot mark add target: cursor tile unavailable.");
                return;
            }

            int width  = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int height = Mathf.Max(1, zoneManager.ZoneHeightTiles);

            int alignedX = Mathf.FloorToInt(tilePos.x / (float)width)  * width;
            int alignedY = Mathf.FloorToInt(tilePos.y / (float)height) * height;

            _pendingAddZoneOffset = new Vector2Int(alignedX, alignedY);
            _hasPendingAddTarget  = true;

            UpdateAddZonePreview();
            _ui?.SetAddZoneTarget(_pendingAddZoneOffset, width, height, true);
            _ui?.SetStatus($"Add Zone target marked at [{alignedX},{alignedY}] ({width}x{height}).");
        }

        private void ConfirmAddZone(string requestedZoneName, bool useSelectedZoneAsTemplate, bool editableInTileEditor)
        {
            if (!_isAddZoneFlowActive) { _ui?.SetStatus("Add Zone flow is not active."); return; }
            if (!_hasPendingAddTarget) { _ui?.SetStatus("Mark a 50x50 target in the world before confirming Add Zone."); return; }

            string zoneName = (requestedZoneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(zoneName)) { _ui?.SetStatus("Add Zone failed: empty name."); return; }

            bool created;
            if (useSelectedZoneAsTemplate)
            {
                if (!_state.HasSelection) { _ui?.SetStatus("Add Zone failed: source zone is required for template mode."); return; }
                created = zoneManager.AddZoneFromTemplate(_state.SelectedZone, zoneName, _pendingAddZoneOffset, editableInTileEditor);
            }
            else
            {
                created = zoneManager.AddZone(zoneName, _pendingAddZoneOffset, editableInTileEditor);
            }

            if (!created) { _ui?.SetStatus($"Add Zone failed for '{zoneName}'. Check name uniqueness and source-zone selection."); return; }

            _state.SelectZone(zoneName);
            _state.NextZoneIndex++;
            PersistZonesToDisk();
            CancelAddZoneFlow();
            _ui?.SetStatus($"Zone '{zoneName}' added at [{_pendingAddZoneOffset.x},{_pendingAddZoneOffset.y}].");
            RefreshSelectionUIAndOverlay();
        }

        private void CancelAddZoneFlow()
        {
            _isAddZoneFlowActive = false;
            _hasPendingAddTarget = false;
            _ui?.HideAddZoneDialog();
            UpdateAddZonePreviewVisibility();
        }

        private void RequestDeleteSelectedZone()
        {
            if (!_state.HasSelection) { _ui?.SetStatus("Select a zone before deleting."); return; }
            _pendingDeleteZoneName = _state.SelectedZone;
            _ui?.ShowDeleteZoneDialog(_pendingDeleteZoneName);
        }

        private void ConfirmDeleteSelectedZone()
        {
            if (string.IsNullOrWhiteSpace(_pendingDeleteZoneName)) { _ui?.SetStatus("No pending zone to delete."); return; }
            DeleteZoneByName(_pendingDeleteZoneName);
            _pendingDeleteZoneName = null;
        }

        private void DeleteZoneByName(string zoneName)
        {
            if (string.IsNullOrWhiteSpace(zoneName)) { _ui?.SetStatus("Delete failed: invalid zone."); return; }

            var zones = zoneManager.GetZonesSnapshot();
            if (zones.Length <= 1) { _ui?.SetStatus("Cannot delete the last remaining zone."); return; }
            if (!zoneManager.RemoveZone(zoneName)) { _ui?.SetStatus($"Could not delete zone '{zoneName}'."); return; }

            if (_state.HasSelection && _state.SelectedZone == zoneName) _state.ClearSelection();
            _ui?.HideDeleteZoneDialog();
            PersistZonesToDisk();
            _ui?.SetStatus($"Zone '{zoneName}' deleted.");
            RefreshSelectionUIAndOverlay();
        }

        private void DuplicateSelectedZone()
        {
            if (!_state.HasSelection) { _ui?.SetStatus("Select a zone before duplicating."); return; }

            string sourceZoneName = _state.SelectedZone;
            if (!zoneManager.DuplicateZone(sourceZoneName, out var duplicatedZoneName))
            {
                _ui?.SetStatus($"Could not duplicate zone '{sourceZoneName}'.");
                return;
            }

            _state.SelectZone(duplicatedZoneName);
            int dx = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            zoneManager.MoveZone(duplicatedZoneName, new Vector2Int(dx, 0));
            PersistZonesToDisk();
            _ui?.SetStatus($"Zone '{sourceZoneName}' duplicated to '{duplicatedZoneName}' and shifted by [{dx},0].");
            RefreshSelectionUIAndOverlay();
        }
    }
}
