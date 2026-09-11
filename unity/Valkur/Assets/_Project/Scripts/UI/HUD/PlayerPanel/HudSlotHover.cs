using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UI.HUD
{
    /// <summary>Forwards pointer enter / exit on a slot's frame to the panel.</summary>
    public sealed class HudSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action Entered;
        public System.Action Exited;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
    }
}
