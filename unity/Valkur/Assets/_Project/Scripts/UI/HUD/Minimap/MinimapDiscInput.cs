using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Pointer handling for a round map: hit only inside the circle, report hover and clicks.
    ///
    /// <para>The raycast filter matters beyond neatness. The HUD canvas carries a
    /// <c>GraphicRaycaster</c> so the pointer over the dial counts as "over UI" — that is how
    /// the combat poll and the camera wheel know to leave a click or a scroll alone. A square
    /// hit box would swallow clicks in the four corners around the disc, where the player can
    /// see the world and expects a click to cast.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinimapDiscInput : MonoBehaviour, ICanvasRaycastFilter,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        /// <summary>Raised on a left click inside the disc.</summary>
        public System.Action Clicked;

        /// <summary>True while the pointer is over the disc.</summary>
        public bool Hovered { get; private set; }

        /// <summary>Fraction of the rect's half-width that counts as inside.</summary>
        public float RadiusFraction = 1f;

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, eventCamera, out var local))
                return false;
            var r = rt.rect;
            Vector2 d = local - r.center;
            float radius = Mathf.Min(r.width, r.height) * 0.5f * RadiusFraction;
            return d.sqrMagnitude <= radius * radius;
        }

        /// <summary>True when a screen point lies inside the disc.</summary>
        public bool Contains(Vector2 screenPoint) => IsRaycastLocationValid(screenPoint, null);

        public void OnPointerEnter(PointerEventData eventData) => Hovered = true;
        public void OnPointerExit(PointerEventData eventData) => Hovered = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke();
        }

        private void OnDisable() => Hovered = false;
    }
}
