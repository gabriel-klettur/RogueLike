using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Component attached to each draggable sprite chip in the wizard's right pane.
    /// Spawns a "ghost" follower image while dragging so the user sees what they
    /// are carrying; the actual assignment is handled by <see cref="TilesetSlotDropTarget.OnDrop"/>.
    /// </summary>
    public class TilesetSpriteDragger : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Sprite Sprite { get; private set; }
        public TilesetConfiguratorPanel Owner { get; private set; }

        private GameObject _ghost;

        public void Bind(TilesetConfiguratorPanel owner, Sprite sprite)
        {
            Owner = owner;
            Sprite = sprite;
        }

        public void OnBeginDrag(PointerEventData ev)
        {
            if (Owner == null || Sprite == null) return;

            _ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _ghost.transform.SetParent(Owner.transform, worldPositionStays: false);
            _ghost.transform.SetAsLastSibling();
            var img = _ghost.GetComponent<Image>();
            img.sprite = Sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var cg = _ghost.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            var rt = _ghost.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(48f, 48f);
            rt.position = ev.position;
        }

        public void OnDrag(PointerEventData ev)
        {
            if (_ghost == null) return;
            _ghost.GetComponent<RectTransform>().position = ev.position;
        }

        public void OnEndDrag(PointerEventData ev)
        {
            if (_ghost != null)
            {
                Destroy(_ghost);
                _ghost = null;
            }
        }
    }
}
