using System.Collections;
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
        // The "Show Colliders" toggle creates GameObjects (BuildingColliderDebugOverlay
        // visuals + TilemapColliderDebugOverlay LineRenderer per composite path).
        // In a scene with ~140 buildings + a tilemap composite with hundreds of paths
        // the synchronous version froze the editor for several seconds. The
        // progressive version processes a bounded number of items per frame so the
        // first frame returns immediately and the overlays appear over ~0.5–1s
        // instead.
        //
        // Budgets are intentionally on the conservative side — small enough that
        // a single frame stays cheap on a mid-tier laptop, large enough that the
        // overlays finish appearing within 1–2 seconds for the largest scene we
        // ship today (~150 buildings, ~300 composite paths).

        private const int OVERLAY_BUILDING_BUDGET_PER_FRAME = 8;
        private const int OVERLAY_TILEMAP_BUDGET_PER_FRAME  = 4;

        // ── State ───────────────────────────────────────────────────────────────

        private Coroutine _overlayShowCoroutine;

        // ── Public API used by the toggle / brush fast path ─────────────────────

        /// <summary>
        /// Cancel any in-flight progressive Show coroutine, then start a new one.
        /// Idempotent — safe to call back-to-back (e.g. user spamming the toggle).
        /// Emits a "Loading colliders…" toast immediately so the UI feels
        /// responsive, then a final "Colliders visible (N shapes)." toast when
        /// the work completes.
        /// </summary>
        private void StartProgressiveShowOverlay()
        {
            StopProgressiveShowOverlay();
            Toast("Loading colliders…");
            _overlayShowCoroutine = StartCoroutine(ProgressiveShowOverlayCoroutine());
        }

        /// <summary>
        /// Cancels any pending progressive show. Called from the Hide branch and
        /// from <see cref="Deactivate"/> so closing the editor mid-load doesn't
        /// leave a coroutine running on a destroyed UI.
        /// </summary>
        private void StopProgressiveShowOverlay()
        {
            if (_overlayShowCoroutine != null)
            {
                StopCoroutine(_overlayShowCoroutine);
                _overlayShowCoroutine = null;
            }
        }

        // ── Coroutine ───────────────────────────────────────────────────────────

        private IEnumerator ProgressiveShowOverlayCoroutine()
        {
            // 1. Tilemap composites — usually fewer in count but each path produces
            //    a LineRenderer GameObject inside RebuildLines(), so we cap them
            //    even tighter than buildings.
            int tilemapsDone = 0;
            var composites = FindObjectsOfType<CompositeCollider2D>();
            for (int i = 0; i < composites.Length; i++)
            {
                if (!_collidersVisible) { _overlayShowCoroutine = null; yield break; }

                var cc = composites[i];
                if (cc == null) continue;
                if (cc.GetComponent<UnityEngine.Tilemaps.TilemapCollider2D>() == null) continue;

                var overlay = cc.GetComponent<TilemapColliderDebugOverlay>();
                if (overlay == null)
                    overlay = cc.gameObject.AddComponent<TilemapColliderDebugOverlay>();
                overlay.SetVisible(true);

                if (++tilemapsDone % OVERLAY_TILEMAP_BUDGET_PER_FRAME == 0)
                    yield return null;
            }

            // 2. Building overlays. Heaviest by far: each building produces one
            //    visual GameObject per authored solid cell. We:
            //      • only materialise overlays for buildings inside the camera
            //        view rect (culling) — UpdateOverlayCulling brings them in
            //        as the user pans;
            //      • yield every OVERLAY_BUILDING_BUDGET_PER_FRAME items so a
            //        150-building scene spreads cleanly over ~19 frames
            //        (~0.3s @ 60fps) even before culling kicks in.
            int total = 0;
            int done  = 0;
            var all   = GetCachedBuildings();

            // Compute the view rect ONCE per coroutine; the per-frame
            // UpdateOverlayCulling pass keeps it fresh as the camera moves.
            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            bool useCulling = cam != null;
            Rect viewRect = useCulling ? ComputeCameraViewRect(cam) : default;
            if (useCulling)
            {
                _lastCullCamPos          = cam.transform.position;
                _lastCullCamSize         = cam.orthographicSize;
                _cullCheckedAtLeastOnce  = true;
            }

            for (int i = 0; i < all.Length; i++)
            {
                if (!_collidersVisible) { _overlayShowCoroutine = null; yield break; }

                var b = all[i];
                if (b == null) continue;

                if (useCulling && !IsBuildingInRect(b, viewRect))
                {
                    // Off-screen: skip materialisation entirely. The overlay
                    // (if any from a prior session) gets hidden by the
                    // synchronous Hide path or by UpdateOverlayCulling.
                    if (++done % OVERLAY_BUILDING_BUDGET_PER_FRAME == 0)
                        yield return null;
                    continue;
                }

                total += EnsureOverlayForBuildingVisible(b);

                if (++done % OVERLAY_BUILDING_BUDGET_PER_FRAME == 0)
                    yield return null;
            }

            // 3. Final toast — only if we still want them visible (the user
            //    might have toggled OFF a frame before we got here, in which
            //    case the early-return checks above already handled it).
            if (_collidersVisible)
                Toast($"Colliders visible ({total} shapes).");

            _overlayShowCoroutine = null;
        }
    }
}
