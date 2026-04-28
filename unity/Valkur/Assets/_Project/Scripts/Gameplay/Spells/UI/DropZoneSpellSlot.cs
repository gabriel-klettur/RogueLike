using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.UI;

namespace Valkur.Gameplay.Spells.UI
{
    /// <summary>
    /// Makes a spell slot in the SpellBarHUD a drop zone that accepts dragged spells.
    /// When a spell is dropped, it assigns the spell to this slot in the player's SpellCaster.
    /// </summary>
    public class DropZoneSpellSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField, Tooltip("The slot index in the player's SpellCaster (0-based).")]
        private int _slotIndex = 0;

        private SpellBarHUD _owner;
        private Image _bgImage;
        private Color _normalBgColor;
        private Color _highlightBgColor;

        private void Awake()
        {
            _bgImage = GetComponent<Image>();
            if (_bgImage != null)
            {
                _normalBgColor = _bgImage.color;
                _highlightBgColor = new Color(1f, 0.83f, 0.18f, 0.95f);
            }
        }

        public void SetSlotIndex(int index)
        {
            _slotIndex = index;
        }

        public void Bind(SpellBarHUD owner, int index)
        {
            _owner = owner;
            _slotIndex = index;
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (!SpellDragContext.IsDragging || _owner == null)
                return;

            if (SpellDragContext.Origin == SpellDragOrigin.HudSlot)
                _owner.TryMoveAssignedSpell(SpellDragContext.SourceSlotIndex, _slotIndex);
            else
                _owner.TryAssignSpellToSlot(_slotIndex, SpellDragContext.DraggedSpell);

            RestoreNormalColor();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_bgImage != null && _owner != null && SpellDragContext.IsDragging && _owner.CanAcceptSpellDrop(_slotIndex))
                _bgImage.color = _highlightBgColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            RestoreNormalColor();
        }

        private void RestoreNormalColor()
        {
            if (_bgImage != null)
                _bgImage.color = _normalBgColor;
        }
    }
}
