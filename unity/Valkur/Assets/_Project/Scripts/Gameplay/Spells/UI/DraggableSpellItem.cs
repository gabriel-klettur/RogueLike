using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Spells.UI
{
    /// <summary>
    /// Makes a spell UI item draggable without disturbing the source layout.
    /// </summary>
    public class DraggableSpellItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField, Tooltip("Reference to the spell definition this item represents.")]
        private SpellDefinition _spellDefinition;

        private CanvasGroup _canvasGroup;
        private Image _previewImage;
        private Canvas _rootCanvas;
        private SpellDragOrigin _origin = SpellDragOrigin.Picker;
        private int _sourceSlotIndex = -1;

        public void Configure(SpellDefinition spell, Image previewImage = null, SpellDragOrigin origin = SpellDragOrigin.Picker, int sourceSlotIndex = -1)
        {
            _spellDefinition = spell;
            _previewImage = previewImage;
            _origin = origin;
            _sourceSlotIndex = sourceSlotIndex;
        }

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            if (_previewImage == null)
                _previewImage = GetComponent<Image>();

            _rootCanvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_spellDefinition == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (_rootCanvas == null) return;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0.55f;
                _canvasGroup.blocksRaycasts = false;
            }

            SpellDragContext.Begin(
                _spellDefinition,
                _previewImage != null ? _previewImage.sprite : _spellDefinition.sprite,
                _origin,
                _sourceSlotIndex,
                _rootCanvas,
                eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_spellDefinition == null || _rootCanvas == null) return;
            SpellDragContext.UpdatePosition(eventData.position, _rootCanvas);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
            }

            SpellDragContext.End();
        }
    }
}
