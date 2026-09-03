using System;
using UnityEngine;

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
                    : $"Loading map '{slotBeingLoaded}'";
            // Always start at 0 % — a stale displayed-progress from the
            // previous load would jump-start the new bar at whatever was
            // last shown.
            _refs.MapsLoadingBar?.Reset();
            _refs.MapsLoadingBar?.SetStatus("Preparing");
            // Rotate the teleport-art background so each map switch shows
            // a different image. Falls back to a transparent background
            // (the click-blocker Image stays solid black behind) when the
            // provider has no sprites loaded.
            ApplyTeleportBackground();
            _refs.MapsLoadingOverlay.transform.SetAsLastSibling();
            _refs.MapsLoadingOverlay.SetActive(true);
        }

        private void ApplyTeleportBackground()
        {
            if (_refs.MapsLoadingBgImage == null) return;
            var sprite = Valkur.UIKit.TeleportMapBackgroundProvider.NextBackground();
            if (sprite == null)
            {
                // No sprites authored yet — hide the layer so the underlying
                // black click-blocker remains visible without a stale sprite.
                _refs.MapsLoadingBgImage.sprite = null;
                _refs.MapsLoadingBgImage.color  = Color.clear;
                return;
            }
            _refs.MapsLoadingBgImage.sprite = sprite;
            _refs.MapsLoadingBgImage.color  = Color.white; // un-tint
            if (_refs.MapsLoadingBgFitter != null && sprite.texture != null)
            {
                _refs.MapsLoadingBgFitter.aspectRatio = (float)sprite.texture.width
                    / Mathf.Max(1, sprite.texture.height);
            }
        }

        public void HideMapsLoadingOverlay()
        {
            if (_refs.MapsLoadingOverlay != null)
                _refs.MapsLoadingOverlay.SetActive(false);
        }

        /// <summary>
        /// Update the bar's target progress (0..1) and status text. Safe to
        /// call from a coroutine that drives the slot-load phases.
        /// </summary>
        public void ReportMapsLoadingProgress(float progress01, string status)
        {
            _refs.MapsLoadingBar?.SetTargetProgress(progress01);
            if (!string.IsNullOrEmpty(status))
                _refs.MapsLoadingBar?.SetStatus(status);
        }

        /// <summary>True while the loading overlay is visible — used by the
        /// UI's Update to drive the bar's Tick.</summary>
        public bool IsMapsLoadingOverlayVisible
            => _refs.MapsLoadingOverlay != null && _refs.MapsLoadingOverlay.activeSelf;

        /// <summary>Drive the bar's lerp + dot animation while the overlay
        /// is up. Must be called from MapEditorUI.Update.</summary>
        public void TickMapsLoadingBar(float unscaledDeltaTime)
        {
            if (!IsMapsLoadingOverlayVisible) return;
            _refs.MapsLoadingBar?.Tick(unscaledDeltaTime);
        }
    }
}
