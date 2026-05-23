using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// Disables the host <see cref="Canvas"/> when it has no active
    /// <see cref="Graphic"/> descendants. URP issues a separate
    /// <c>Canvas.RenderOverlays</c> pass per active overlay canvas every frame,
    /// regardless of whether anything is actually drawn — empty canvases were
    /// costing ~3-5ms in render/CPU on a typical scene (10 always-loaded
    /// container canvases for editors / menus / toasts / vendor / pause that
    /// stay <c>enabled=true</c> long after the panel they used to host is
    /// hidden). This guard polls cheaply and re-enables the canvas the instant
    /// real content appears, so dialog opens are still instantaneous.
    ///
    /// Attached automatically by <see cref="EmptyCanvasGuardBootstrap"/> to
    /// every <c>ScreenSpaceOverlay</c> canvas at scene start; no per-canvas
    /// wiring required.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [DisallowMultipleComponent]
    public sealed class EmptyCanvasAutoDisable : MonoBehaviour
    {
        // 4 polls/sec is fast enough that a popup feels instantaneous (<250ms
        // latency to re-enable) and slow enough that 10 watched canvases cost
        // < 0.05ms/frame averaged.
        private const float PollInterval = 0.25f;

        private Canvas _canvas;
        // Cached raycaster — disabled in lock-step with the canvas. An active
        // GraphicRaycaster on an empty canvas would otherwise still run a
        // raycast every input event (mouse move, touch tap) walking the entire
        // child tree, wasting CPU even with the canvas rendering off.
        private UnityEngine.UI.GraphicRaycaster _raycaster;
        private float _nextPollUnscaled;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _raycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        private void OnEnable()
        {
            // Re-poll immediately when the canvas comes back on so we don't
            // strand a freshly-shown panel in the disabled state for up to
            // PollInterval seconds.
            _nextPollUnscaled = 0f;
        }

        private void Update()
        {
            if (_canvas == null) return;
            if (Time.unscaledTime < _nextPollUnscaled) return;
            _nextPollUnscaled = Time.unscaledTime + PollInterval;

            bool hasActiveGraphic = HasAnyActiveGraphic(transform);
            if (_canvas.enabled != hasActiveGraphic)
                _canvas.enabled = hasActiveGraphic;
            if (_raycaster != null && _raycaster.enabled != hasActiveGraphic)
                _raycaster.enabled = hasActiveGraphic;
        }

        // Hand-rolled descendant walk that short-circuits on the first hit.
        // GetComponentsInChildren<Graphic>(false) would work but allocates a
        // throwaway array every poll across every canvas.
        private static bool HasAnyActiveGraphic(Transform root)
        {
            int childCount = root.childCount;
            // Check direct components on `root` first.
            var graphics = root.GetComponents<Graphic>();
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                if (g != null && g.enabled && g.gameObject.activeInHierarchy)
                    return true;
            }
            for (int i = 0; i < childCount; i++)
            {
                var child = root.GetChild(i);
                if (!child.gameObject.activeSelf) continue; // skip inactive subtrees wholesale
                if (HasAnyActiveGraphic(child)) return true;
            }
            return false;
        }
    }
}
