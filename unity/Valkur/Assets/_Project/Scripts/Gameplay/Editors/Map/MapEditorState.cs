using UnityEngine;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Mutable runtime state for the in-game map editor.
    /// Handles selection and whether tile edits are restricted to editable zones.
    /// </summary>
    public class MapEditorState
    {
        public bool Active;
        public string SelectedZone;
        public bool RestrictTileEditingToEditableZones = true;
        public int NextZoneIndex = 1;

        public void ClearSelection()
        {
            SelectedZone = null;
        }

        public void SelectZone(string zoneName)
        {
            SelectedZone = string.IsNullOrWhiteSpace(zoneName) ? null : zoneName;
        }

        public bool HasSelection => !string.IsNullOrWhiteSpace(SelectedZone);
    }
}
