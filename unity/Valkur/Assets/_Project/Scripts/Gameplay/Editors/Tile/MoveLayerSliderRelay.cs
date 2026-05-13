using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Attached to the Move-To-Layer slider GameObject inside the SelectModes panel.
    /// Fires <see cref="OnReleased"/> exactly once per pointer interaction (click-only
    /// on the track OR click-and-drag of the thumb), letting the editor commit the
    /// Move-To-Layer action when the user finishes choosing the destination instead
    /// of every intermediate frame while the slider is being dragged.
    ///
    /// Coexists with the Slider component on the same GameObject — both
    /// <see cref="IPointerUpHandler"/> and <see cref="IEndDragHandler"/> simply
    /// observe the events the Selectable already publishes; nothing here consumes
    /// or mutates them. PointerUp + EndDrag can both fire after a drag, so the
    /// relay debounces using a short unscaled-time window.
    /// </summary>
    public class MoveLayerSliderRelay : MonoBehaviour, IPointerUpHandler, IEndDragHandler
    {
        public System.Action OnReleased;

        // Most input frames are 8–16 ms; 50 ms is comfortably larger than the
        // PointerUp/EndDrag dispatch gap inside a single Unity frame yet far
        // shorter than the fastest plausible "intentional second click".
        private const float DEBOUNCE_SECONDS = 0.05f;
        private float _lastFireTime = -1f;

        public void OnPointerUp(PointerEventData _) => Fire();
        public void OnEndDrag(PointerEventData _) => Fire();

        private void Fire()
        {
            if (_lastFireTime >= 0f && Time.unscaledTime - _lastFireTime < DEBOUNCE_SECONDS)
                return;
            _lastFireTime = Time.unscaledTime;
            OnReleased?.Invoke();
        }
    }
}
