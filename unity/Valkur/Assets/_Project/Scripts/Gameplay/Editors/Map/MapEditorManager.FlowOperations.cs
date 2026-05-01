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
            _isAddZoneFlowActive   = true;
            _hasPendingAddTarget   = false;
            _pendingAddZoneOffset  = default;
            _addZoneFlowStartedFrame = Time.frameCount;

            int width  = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int height = Mathf.Max(1, zoneManager.ZoneHeightTiles);

            _ui?.SetAddZoneMode(true);
            _ui?.SetStatus($"Add Zone mode: click on the map to place a {width}\u00d7{height} zone.");
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
            _ui?.SetAddZoneMode(false);   // stop blinking — target is now committed

            // Determine optional source zone for the template toggle
            string sourceName    = _state.HasSelection &&
                                   zoneManager.TryGetZone(_state.SelectedZone, out var srcZone)
                                   ? srcZone.zoneName : string.Empty;
            // For blank zones (no source) default editable=true — fresh zones
            // should be editable in the tile editor unless the user opts out.
            // For template zones, mirror the source's editable flag.
            bool sourceEditable  = string.IsNullOrEmpty(sourceName)
                ? true
                : (zoneManager.TryGetZone(sourceName, out var srcZ2) && srcZ2.editableInTileEditor);

            _ui?.ShowAddZoneDialog(GenerateOffsetZoneName(_pendingAddZoneOffset), sourceName, sourceEditable);
            _ui?.SetAddZoneTarget(_pendingAddZoneOffset, width, height, true);
            _ui?.SetStatus($"Add Zone target at [{alignedX},{alignedY}] — fill in details and confirm.");
        }

        private void ConfirmAddZone(string requestedZoneName, bool useSelectedZoneAsTemplate, bool editableInTileEditor)
        {
            if (!_isAddZoneFlowActive) { _ui?.SetStatus("Add Zone flow is not active."); return; }
            if (!_hasPendingAddTarget) { _ui?.SetStatus("Mark a 50x50 target in the world before confirming Add Zone."); return; }

            string zoneName = (requestedZoneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(zoneName)) { _ui?.SetStatus("Add Zone failed: empty name."); return; }

            // Template mode requires a selected source zone. If the user left
            // the toggle ON without a selection (or the dialog defaulted it ON
            // before this guard moved to the UI side), fall back to creating
            // a blank zone instead of failing silently behind the modal.
            bool useTemplate = useSelectedZoneAsTemplate && _state.HasSelection;
            bool created = useTemplate
                ? zoneManager.AddZoneFromTemplate(_state.SelectedZone, zoneName, _pendingAddZoneOffset, editableInTileEditor)
                : zoneManager.AddZone(zoneName, _pendingAddZoneOffset, editableInTileEditor);

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
            _ui?.SetAddZoneMode(false);
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

            // Drop the orphan override file so it doesn't pile up on disk and
            // doesn't trigger "no matching zone" warnings on the next boot.
            Valkur.Gameplay.TileEditor.TileOverlayPersistence.DeleteOverride(zoneName);

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
