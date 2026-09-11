using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UI.HUD
{
    /// <summary>Forwards a left click on a bar slot to the bar. The bar decides what it means.</summary>
    public sealed class SpellBarSlotClick : MonoBehaviour, IPointerClickHandler
    {
        public System.Action Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            Clicked?.Invoke();
        }
    }
}
