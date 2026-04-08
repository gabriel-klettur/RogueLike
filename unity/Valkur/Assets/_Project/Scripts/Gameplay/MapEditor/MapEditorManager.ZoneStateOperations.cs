using UnityEngine;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Zone rename / move / editable-toggle / restrict operations and
    /// UI-refresh / constraint helpers for <see cref="MapEditorManager"/>.
    /// </summary>
    public partial class MapEditorManager
    {
        private void RenameSelectedZone(string newName)
        {
            if (!_state.HasSelection) { _ui?.SetStatus("Select a zone before renaming."); return; }
            RenameZoneByName(_state.SelectedZone, newName);
        }

        private void RenameZoneByName(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName)) { _ui?.SetStatus("Rename failed: invalid zone."); return; }
            string trimmed = (newName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) { _ui?.SetStatus("Rename failed: empty name."); return; }
            if (!zoneManager.RenameZone(oldName, trimmed)) { _ui?.SetStatus($"Rename failed: '{trimmed}' may already exist."); return; }

            _state.SelectZone(trimmed);
            PersistZonesToDisk();
            _ui?.SetStatus($"Renamed '{oldName}' to '{trimmed}'.");
            RefreshSelectionUIAndOverlay();
        }

        private void ToggleSelectedZoneEditable()
        {
            if (!_state.HasSelection) { _ui?.SetStatus("Select a zone first."); return; }
            ToggleZoneEditableByName(_state.SelectedZone);
        }

        private void ToggleZoneEditableByName(string zoneName)
        {
            if (string.IsNullOrWhiteSpace(zoneName)) { _ui?.SetStatus("Could not update zone editable state: invalid zone."); return; }
            if (!zoneManager.TryGetZone(zoneName, out var zone)) { _ui?.SetStatus($"Zone '{zoneName}' no longer exists."); return; }

            _state.SelectZone(zoneName);
            bool target = !zone.editableInTileEditor;
            if (!zoneManager.SetZoneEditable(zone.zoneName, target)) { _ui?.SetStatus("Could not update zone editable state."); return; }

            PersistZonesToDisk();
            _ui?.SetStatus($"Zone '{zone.zoneName}' editable = {target}");
            RefreshSelectionUIAndOverlay();
        }

        private void MoveSelectedZone(Vector2Int direction)
        {
            if (!_state.HasSelection) { _ui?.SetStatus("Select a zone before moving."); return; }

            int dx = direction.x * Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int dy = direction.y * Mathf.Max(1, zoneManager.ZoneHeightTiles);
            if (!zoneManager.MoveZone(_state.SelectedZone, new Vector2Int(dx, dy))) { _ui?.SetStatus("Failed to move zone."); return; }

            PersistZonesToDisk();
            _ui?.SetStatus($"Zone '{_state.SelectedZone}' moved by [{dx},{dy}].");
            RefreshSelectionUIAndOverlay();
        }

        private void SetRestrictTileEditing(bool restrict)
        {
            _state.RestrictTileEditingToEditableZones = restrict;
            ApplyTileEditorConstraint();
            PersistZonesToDisk();
            _ui?.SetStatus(restrict ? "Tile editor restricted to editable zones." : "Tile editor can edit all cells.");
        }

        // ── Internal refresh helpers ─────────────────────────────────────────

        private void HandleZonesChanged()
        {
            RefreshZoneListUI();
            RebuildZoneOverlays();
            ApplyTileEditorConstraint();

            if (_state.HasSelection && !zoneManager.TryGetZone(_state.SelectedZone, out _))
                _state.ClearSelection();

            if (_isAddZoneFlowActive)
            {
                if (_state.HasSelection && zoneManager.TryGetZone(_state.SelectedZone, out var zone))
                    _ui?.SetAddZoneSource(zone.zoneName, zone.editableInTileEditor);
                else
                    _ui?.SetAddZoneSource("(none)", true);
            }

            RefreshSelectionUIAndOverlay();
        }

        private void RefreshZoneListUI()
        {
            if (_ui == null || zoneManager == null) return;
            _ui.RefreshZones(zoneManager.GetZonesSnapshot());
            _ui.SetRestrictToggle(_state.RestrictTileEditingToEditableZones);
        }

        private void RefreshSelectionUIAndOverlay()
        {
            bool editable = false;
            if (_state.HasSelection && zoneManager.TryGetZone(_state.SelectedZone, out var zone))
                editable = zone.editableInTileEditor;

            _ui?.SetSelectedZone(_state.SelectedZone, editable);
            RecolorZoneOverlays();
        }

        private void ApplyTileEditorConstraint()
        {
            if (tileEditorManager == null)
                tileEditorManager = TileEditorManager.Instance != null
                    ? TileEditorManager.Instance
                    : FindObjectOfType<TileEditorManager>();

            if (tileEditorManager == null) return;

            if (_state.RestrictTileEditingToEditableZones)
                tileEditorManager.SetEditConstraint(zoneManager.IsTileInEditableZone);
            else
                tileEditorManager.ClearEditConstraint();
        }

        private void UpdateAddZonePreviewVisibility()
        {
            if (_addZonePreviewObject != null)
                _addZonePreviewObject.SetActive(_isAddZoneFlowActive && _hasPendingAddTarget && _state.Active);
        }
    }
}
