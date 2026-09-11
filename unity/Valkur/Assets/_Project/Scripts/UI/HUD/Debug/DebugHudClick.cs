using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// A click target on the debug HUD. Only section headers and the copy button carry one —
    /// every other graphic of the panel is <c>raycastTarget = false</c> (H5), because the
    /// combat poll refuses a click over any UI and a tool must never cost the player a cast by
    /// merely being open.
    /// </summary>
    public sealed class DebugHudClick : MonoBehaviour, IPointerClickHandler
    {
        public Action Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            Clicked?.Invoke();
        }
    }
}
