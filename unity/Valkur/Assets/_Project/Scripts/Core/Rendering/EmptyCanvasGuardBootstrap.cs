using UnityEngine;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// One-time bootstrap that attaches <see cref="EmptyCanvasAutoDisable"/>
    /// to every <see cref="Canvas"/> in the active scene that uses
    /// <see cref="RenderMode.ScreenSpaceOverlay"/>. Runs once after scene load
    /// so editor / menu / toast canvases that ship in the scene file pick up
    /// the guard automatically — no per-canvas wiring or prefab edits needed.
    ///
    /// Only overlay canvases are guarded:
    ///   * <c>ScreenSpaceOverlay</c> cost is a dedicated full-screen render
    ///     pass per canvas, which is what the guard eliminates.
    ///   * <c>ScreenSpaceCamera</c> / <c>WorldSpace</c> canvases batch into
    ///     the normal renderer pipeline, so disabling them doesn't pay back.
    /// </summary>
    public static class EmptyCanvasGuardBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachGuards()
        {
            var canvases = Object.FindObjectsOfType<Canvas>(includeInactive: true);
            int attached = 0;
            for (int i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (canvas == null) continue;
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (canvas.GetComponent<EmptyCanvasAutoDisable>() != null) continue;
                canvas.gameObject.AddComponent<EmptyCanvasAutoDisable>();
                attached++;
            }
            if (attached > 0)
                Debug.Log($"[EmptyCanvasGuardBootstrap] Attached EmptyCanvasAutoDisable to {attached} overlay canvas(es).");
        }
    }
}
