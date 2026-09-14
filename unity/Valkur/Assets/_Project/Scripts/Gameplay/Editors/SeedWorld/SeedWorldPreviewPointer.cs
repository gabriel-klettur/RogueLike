using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// Reports where the pointer is over the Seed World preview, in 0..1 of the image.
    ///
    /// <para>Through uGUI's pointer events rather than a mouse read: the position arrives from
    /// the EventSystem, so this reads no device and needs no exception to the project's input
    /// centralisation rule.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeedWorldPreviewPointer : MonoBehaviour, IPointerMoveHandler, IPointerExitHandler
    {
        private SeedWorldRuntimeEditor _editor;
        private RectTransform _rect;

        public void Bind(SeedWorldRuntimeEditor editor, RectTransform rect)
        {
            _editor = editor;
            _rect = rect;
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (_editor == null || _rect == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rect, eventData.position, eventData.enterEventCamera, out Vector2 local))
                return;

            var r = _rect.rect;
            if (r.width <= 0f || r.height <= 0f) return;
            _editor.OnPreviewHover(new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_editor != null) _editor.OnPreviewHover(new Vector2(-1f, -1f));
        }
    }
}
