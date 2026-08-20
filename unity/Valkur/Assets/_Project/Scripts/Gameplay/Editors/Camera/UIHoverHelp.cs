using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.Editors.CameraFeelEditor
{
    /// <summary>
    /// Shows a tunable's explanation while the pointer is over its slider.
    ///
    /// Camera feel is the one subsystem where a number's consequence is genuinely
    /// non-obvious — lowering the follow spring makes the camera trail rather than smooth,
    /// and any shake below a screen pixel is erased by the pixel snap. A knob whose effect
    /// cannot be reasoned about gets moved on a hunch and moved back a week later.
    /// </summary>
    internal sealed class UIHoverHelp : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Message;
        public CameraEditorUIBuilder.UIRefs Refs;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Refs?.Help != null) Refs.Help.text = Message;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Refs?.Help != null) Refs.Help.text = string.Empty;
        }
    }
}
