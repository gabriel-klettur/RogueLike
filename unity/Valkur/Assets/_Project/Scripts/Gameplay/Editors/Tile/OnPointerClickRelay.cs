using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Tiny click forwarder for cases where we want a clickable UI element WITHOUT
    /// the <see cref="UnityEngine.UI.Selectable"/> + <see cref="UnityEngine.UI.Button"/>
    /// machinery — typically when packing many click targets on a single panel where
    /// Unity UGUI's <c>Selectable.OnDisable</c> static-array bookkeeping (line 555)
    /// has a known race that NREs when many Selectables tear down together.
    ///
    /// Pairs with a sibling <see cref="UnityEngine.UI.Image"/> whose
    /// <c>raycastTarget</c> is left ON so PointerClick events route here. This
    /// component carries no state and never enrols in <c>Selectable.s_Selectables</c>.
    /// </summary>
    public class OnPointerClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnClicked;

        public void OnPointerClick(PointerEventData _) => OnClicked?.Invoke();
    }
}
