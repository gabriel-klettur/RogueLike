using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Component attached to each of the 16 Blob16 slot cells in the
    /// <see cref="TilesetConfiguratorPanel"/>. Receives drop events from the
    /// sprite grid via Unity's EventSystem, and forwards them to the panel.
    /// </summary>
    public class TilesetSlotDropTarget : MonoBehaviour, IDropHandler
    {
        public Blob16Slot Slot { get; private set; }
        public TilesetConfiguratorPanel Owner { get; private set; }

        public void Bind(TilesetConfiguratorPanel owner, Blob16Slot slot)
        {
            Owner = owner;
            Slot = slot;
        }

        public void OnDrop(PointerEventData ev)
        {
            if (Owner == null || ev.pointerDrag == null) return;
            var dragger = ev.pointerDrag.GetComponent<TilesetSpriteDragger>();
            if (dragger == null) return;
            Owner.AssignSpriteToSlot(dragger.Sprite, Slot);
        }
    }
}
