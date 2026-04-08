using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Screen-space inventory grid UI. Toggle with Tab or I key.
    /// Maps to Python's InventoryUISystem (grid layout, slot rendering, drag support).
    /// 
    /// Displays inventory slots in a grid with item icons, quantities, and tooltips.
    /// Supports click-to-drop functionality.
    /// </summary>
    public partial class InventoryUI : SingletonMonoBehaviour<InventoryUI>
    {
        [Header("Layout")]
        [SerializeField] private int columns = 5;
        [SerializeField] private float slotSize = 64f;
        [SerializeField] private float padding = 8f;

        [Header("Colors")]
        [SerializeField] private Color panelColor = new Color(0.1f, 0.1f, 0.15f, 0.9f);
        [SerializeField] private Color slotColor = new Color(0.2f, 0.2f, 0.25f, 1f);
        [SerializeField] private Color slotHoverColor = new Color(0.3f, 0.3f, 0.4f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.4f, 0.6f, 0.9f, 1f);

        private Canvas _canvas;
        private GameObject _panelGo;
        private RectTransform _panelRect;
        private CanvasGroup _panelGroup;
        private Inventory _playerInventory;
        private InputAction _toggleAction;
        private InputAction _dropAction;
        private bool _visible;
        private int _selectedSlot = -1;

        private GameObject[] _slotObjects;
        private Image[] _slotBackgrounds;
        private Image[] _slotIcons;
        private TextMeshProUGUI[] _slotQuantities;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _tooltipText;

        public bool IsVisible => _visible;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleInventory", InputActionType.Button);
            _toggleAction.AddBinding("<Keyboard>/tab");
            _toggleAction.AddBinding("<Keyboard>/i");
            _toggleAction.Enable();

            _dropAction = new InputAction("DropItem", InputActionType.Button, "<Keyboard>/q");
            _dropAction.Enable();
        }

        private void Start()
        {
            FindPlayerInventory();
            BuildUI();
            SetVisible(false);
        }

        private void Update()
        {
            if (_toggleAction != null && _toggleAction.WasPerformedThisFrame())
                SetVisible(!_visible);

            if (_visible)
            {
                if (_dropAction != null && _dropAction.WasPerformedThisFrame() && _selectedSlot >= 0)
                    DropSelectedItem();

                RefreshSlots();
            }
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_panelGroup != null)
            {
                _panelGroup.alpha = visible ? 1f : 0f;
                _panelGroup.blocksRaycasts = visible;
                _panelGroup.interactable = visible;
            }

            if (visible)
            {
                FindPlayerInventory();
                RefreshSlots();
            }
        }

        public void SelectSlot(int index)
        {
            _selectedSlot = index;
            UpdateSlotHighlights();
            UpdateTooltip();
        }

        private void FindPlayerInventory()
        {
            if (_playerInventory != null) return;

            var player = EntityRegistry.Player;
            if (player != null)
                _playerInventory = player.GetComponent<Inventory>();
        }
    }
}
