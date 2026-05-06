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
            // Per-row buttons (Rename / Load / Delete) live inside each row,
            // so RebuildMapsList only needs the selection callback and the
            // "load this slot" callback (used by both the inline Load button
            // and the double-click-to-load shortcut on the name).
            MapEditorUIBuilder.RebuildMapsList(_refs, slots, activeSlot,
                OnSlotRowClicked,
                onRowLoad: slot => _mapSlotCallbacks.OnLoad?.Invoke(slot));
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

        // ── Loading overlay ──────────────────────────────────────────────────

        public void ShowMapsLoadingOverlay(string slotBeingLoaded)
        {
            if (_refs.MapsLoadingOverlay == null) return;
            if (_refs.MapsLoadingLabel != null)
                _refs.MapsLoadingLabel.text = string.IsNullOrEmpty(slotBeingLoaded)
                    ? "Loading map…"
                    : $"Loading map '{slotBeingLoaded}'…";
            _refs.MapsLoadingOverlay.transform.SetAsLastSibling();
            _refs.MapsLoadingOverlay.SetActive(true);
        }

        public void HideMapsLoadingOverlay()
        {
            if (_refs.MapsLoadingOverlay != null)
                _refs.MapsLoadingOverlay.SetActive(false);
        }
    }
}
