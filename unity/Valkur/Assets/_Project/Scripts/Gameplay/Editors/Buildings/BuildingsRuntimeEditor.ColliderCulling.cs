using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Tunables ────────────────────────────────────────────────────────────
        //
        // Culling expands the camera's visible rect by this much on every side
        // (in world units, PPU=32 → ~128 px). Buildings inside the expanded
        // rect get an overlay; the rest stay un-built. The margin is small
        // enough that pop-in is invisible during normal panning, large enough
        // that fast panning doesn't show holes.

        private const float OVERLAY_CULL_MARGIN_WORLD = 4f;

        // ── State ───────────────────────────────────────────────────────────────

        private Vector3 _lastCullCamPos;
        private float   _lastCullCamSize;
        private bool    _cullCheckedAtLeastOnce;

        // ── Per-frame culling pass ──────────────────────────────────────────────

        /// <summary>
        /// Called from the editor Update loop while <see cref="_collidersVisible"/>
        /// is true. Cheap when the camera hasn't moved (early-returns on the first
        /// position+size compare); when the camera DOES move it walks the cached
        /// building list once, activating overlays for buildings that just entered
        /// the view rect (lazy-creating them on demand) and hiding overlays on
        /// buildings that just left it. This is what keeps Show Colliders' cost
        /// O(visibles) instead of O(scene) — the dominant factor on big maps.
        /// </summary>
        private void UpdateOverlayCulling()
        {
            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return;

            Vector3 camPos  = cam.transform.position;
            float   camSize = cam.orthographicSize;
            if (_cullCheckedAtLeastOnce && camPos == _lastCullCamPos &&
                Mathf.Approximately(camSize, _lastCullCamSize))
            {
                return;
            }
            _lastCullCamPos          = camPos;
            _lastCullCamSize         = camSize;
            _cullCheckedAtLeastOnce  = true;

            Rect viewRect = ComputeCameraViewRect(cam);
            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || b.Template == null) continue;

                bool inView = IsBuildingInRect(b, viewRect);
                var overlay = b.GetComponent<BuildingColliderDebugOverlay>();

                if (overlay == null)
                {
                    // Lazy creation: only spawn overlay GameObjects for the
                    // buildings that need them right now.
                    if (inView) EnsureOverlayForBuildingVisible(b);
                    continue;
                }

                if (overlay.Visible != inView)
                    overlay.SetVisible(inView);
            }
        }

        /// <summary>
        /// Reset the culling cache so the next call to <see cref="UpdateOverlayCulling"/>
        /// always recomputes (used when the camera is moved by an external system,
        /// e.g. a teleport / zone change, or when a new building is placed).
        /// </summary>
        private void InvalidateOverlayCullingCache()
        {
            _cullCheckedAtLeastOnce = false;
        }

        // ── Helpers shared with the synchronous + progressive build paths ───────

        /// <summary>
        /// Materialise the overlay component on <paramref name="b"/> (lazy
        /// AddComponent), push the authored cells, and turn it on. Returns
        /// <see cref="BuildingColliderDebugOverlay.CurrentVisualCount"/> so
        /// callers can sum totals for the final toast.
        /// </summary>
        private int EnsureOverlayForBuildingVisible(BuildingObject b)
        {
            if (b == null) return 0;
            var overlay = b.GetComponent<BuildingColliderDebugOverlay>();
            if (overlay == null)
                overlay = b.gameObject.AddComponent<BuildingColliderDebugOverlay>();

            int filled = ComputeAuthoringCellsInto(b, _authoringCellsScratch);
            if (filled > 0) overlay.SetAuthoringCells(_authoringCellsScratch);
            else            overlay.ClearAuthoringCells();
            overlay.SetVisible(true);
            return overlay.CurrentVisualCount;
        }

        /// <summary>
        /// Returns the camera's visible rect in world space, expanded by
        /// <see cref="OVERLAY_CULL_MARGIN_WORLD"/> on every side so panning
        /// doesn't show overlay pop-in at the edges.
        /// </summary>
        private static Rect ComputeCameraViewRect(Camera cam)
        {
            // Orthographic 2D: half-height = orthographicSize, half-width = h * aspect.
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Vector3 c = cam.transform.position;
            return new Rect(
                c.x - halfW - OVERLAY_CULL_MARGIN_WORLD,
                c.y - halfH - OVERLAY_CULL_MARGIN_WORLD,
                halfW * 2f + OVERLAY_CULL_MARGIN_WORLD * 2f,
                halfH * 2f + OVERLAY_CULL_MARGIN_WORLD * 2f);
        }

        /// <summary>
        /// True if the building's footprint (TryGetWorldRect) overlaps
        /// <paramref name="viewRect"/>. Buildings whose template can't compute
        /// a rect are treated as out-of-view (they'd render at unknown
        /// coordinates anyway).
        /// </summary>
        private static bool IsBuildingInRect(BuildingObject b, Rect viewRect)
        {
            if (b == null || !b.TryGetWorldRect(out var br)) return false;
            return br.Overlaps(viewRect, allowInverse: true);
        }

        /// <summary>True if the building is currently inside the camera view rect (margin applied).</summary>
        private bool IsBuildingInCameraView(BuildingObject b)
        {
            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return true; // no camera → don't cull, render everything
            return IsBuildingInRect(b, ComputeCameraViewRect(cam));
        }
    }
}
