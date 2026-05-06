using System;

namespace Valkur.Gameplay.MapEditor
{
    public partial class MapEditorUI
    {
        /// <summary>
        /// Re-render the saved-maps list from the latest manager snapshot.
        /// Called once on Initialize and again whenever the manager raises
        /// <c>OnMapSlotsChanged</c>.
        /// </summary>
        public void RefreshMapsList(string[] slots, string activeSlot)
        {
            MapEditorUIBuilder.RebuildMapsList(_refs, slots, activeSlot, OnSlotRowClicked);
        }

        private void OnSlotRowClicked(string slot)
        {
            if (_refs.MapsState == null) return;
            _refs.MapsState.SelectedSlot = slot;
        }

        public void HideMapsDeleteDialog()
        {
            if (_refs.MapsDeleteDialog != null)
                _refs.MapsDeleteDialog.SetActive(false);
        }

        public void HideMapsNewDialog()
        {
            if (_refs.MapsNewDialog != null)
                _refs.MapsNewDialog.SetActive(false);
        }

        public void HideMapsRenameDialog()
        {
            if (_refs.MapsRenameDialog != null)
                _refs.MapsRenameDialog.SetActive(false);
        }
    }
}
