using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.UI.InventoryUI
{
    /// <summary>
    /// Screen-space inventory grid UI. Toggle with Tab or I key.
    /// Maps to Python's InventoryUISystem (grid layout, slot rendering, drag support).
    /// 
    /// Displays inventory slots in a grid with item icons, quantities, and tooltips.
    /// Supports click-to-drop functionality.
    /// </summary>
    public class InventoryUI : MonoBehaviour
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

        private static InventoryUI _instance;
        public static InventoryUI Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

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

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                _playerInventory = player.GetComponent<Inventory>();
        }

        private void BuildUI()
        {
            // Canvas
            var canvasGo = new GameObject("InventoryCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Panel
            int maxSlots = _playerInventory != null ? _playerInventory.Capacity : 20;
            int rows = Mathf.CeilToInt((float)maxSlots / columns);
            float panelWidth = padding * 2 + columns * (slotSize + padding);
            float panelHeight = padding * 2 + 40f + rows * (slotSize + padding) + 60f;

            _panelGo = CreateUIObject("InventoryPanel", _canvas.transform);
            _panelRect = _panelGo.GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

            var panelImg = _panelGo.AddComponent<Image>();
            panelImg.color = panelColor;

            _panelGroup = _panelGo.AddComponent<CanvasGroup>();

            // Title
            var titleGo = CreateUIObject("Title", _panelGo.transform);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -padding);
            titleRect.sizeDelta = new Vector2(0, 32f);

            _titleText = titleGo.AddComponent<TextMeshProUGUI>();
            _titleText.text = "Inventario";
            _titleText.fontSize = 22f;
            _titleText.alignment = TextAlignmentOptions.Center;
            _titleText.color = Color.white;

            // Slots
            _slotObjects = new GameObject[maxSlots];
            _slotBackgrounds = new Image[maxSlots];
            _slotIcons = new Image[maxSlots];
            _slotQuantities = new TextMeshProUGUI[maxSlots];

            float startX = padding;
            float startY = -(padding + 40f);

            for (int i = 0; i < maxSlots; i++)
            {
                int col = i % columns;
                int row = i / columns;

                float x = startX + col * (slotSize + padding);
                float y = startY - row * (slotSize + padding);

                var slotGo = CreateUIObject($"Slot_{i}", _panelGo.transform);
                var slotRect = slotGo.GetComponent<RectTransform>();
                slotRect.anchorMin = new Vector2(0, 1);
                slotRect.anchorMax = new Vector2(0, 1);
                slotRect.pivot = new Vector2(0, 1);
                slotRect.anchoredPosition = new Vector2(x, y);
                slotRect.sizeDelta = new Vector2(slotSize, slotSize);

                var slotBg = slotGo.AddComponent<Image>();
                slotBg.color = slotColor;

                // Click handler
                var btn = slotGo.AddComponent<Button>();
                int slotIndex = i;
                btn.onClick.AddListener(() => SelectSlot(slotIndex));

                // Icon
                var iconGo = CreateUIObject("Icon", slotGo.transform);
                var iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                iconRect.sizeDelta = new Vector2(-8, -8);
                iconRect.anchoredPosition = Vector2.zero;

                var iconImg = iconGo.AddComponent<Image>();
                iconImg.preserveAspect = true;
                iconImg.enabled = false;

                // Quantity text
                var qtyGo = CreateUIObject("Qty", slotGo.transform);
                var qtyRect = qtyGo.GetComponent<RectTransform>();
                qtyRect.anchorMin = new Vector2(1, 0);
                qtyRect.anchorMax = new Vector2(1, 0);
                qtyRect.pivot = new Vector2(1, 0);
                qtyRect.anchoredPosition = new Vector2(-2, 2);
                qtyRect.sizeDelta = new Vector2(40, 18);

                var qtyText = qtyGo.AddComponent<TextMeshProUGUI>();
                qtyText.text = "";
                qtyText.fontSize = 14f;
                qtyText.alignment = TextAlignmentOptions.BottomRight;
                qtyText.color = new Color(1f, 1f, 0.7f, 1f);

                _slotObjects[i] = slotGo;
                _slotBackgrounds[i] = slotBg;
                _slotIcons[i] = iconImg;
                _slotQuantities[i] = qtyText;
            }

            // Tooltip area
            var tooltipGo = CreateUIObject("Tooltip", _panelGo.transform);
            var tooltipRect = tooltipGo.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(0, 0);
            tooltipRect.anchorMax = new Vector2(1, 0);
            tooltipRect.pivot = new Vector2(0.5f, 0);
            tooltipRect.anchoredPosition = new Vector2(0, padding);
            tooltipRect.sizeDelta = new Vector2(0, 50f);

            _tooltipText = tooltipGo.AddComponent<TextMeshProUGUI>();
            _tooltipText.text = "Tab/I: cerrar | Q: soltar item seleccionado";
            _tooltipText.fontSize = 14f;
            _tooltipText.alignment = TextAlignmentOptions.Center;
            _tooltipText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        }

        private void RefreshSlots()
        {
            if (_playerInventory == null || _slotObjects == null) return;

            var slots = _playerInventory.Slots;
            int maxSlots = _slotObjects.Length;

            for (int i = 0; i < maxSlots; i++)
            {
                if (i < slots.Count && !slots[i].IsEmpty)
                {
                    var slot = slots[i];
                    _slotIcons[i].enabled = true;
                    _slotIcons[i].sprite = slot.Item.icon ?? slot.Item.iconSmall;

                    _slotQuantities[i].text = slot.Quantity > 1 ? slot.Quantity.ToString() : "";
                }
                else
                {
                    _slotIcons[i].enabled = false;
                    _slotQuantities[i].text = "";
                }
            }

            _titleText.text = $"Inventario ({_playerInventory.UsedSlots}/{_playerInventory.Capacity})";
        }

        private void UpdateSlotHighlights()
        {
            if (_slotBackgrounds == null) return;

            for (int i = 0; i < _slotBackgrounds.Length; i++)
            {
                _slotBackgrounds[i].color = i == _selectedSlot ? selectedColor : slotColor;
            }
        }

        private void UpdateTooltip()
        {
            if (_tooltipText == null || _playerInventory == null) return;

            if (_selectedSlot >= 0 && _selectedSlot < _playerInventory.Slots.Count)
            {
                var slot = _playerInventory.Slots[_selectedSlot];
                if (!slot.IsEmpty)
                {
                    string desc = !string.IsNullOrEmpty(slot.Item.description)
                        ? slot.Item.description
                        : "Sin descripcion";
                    _tooltipText.text = $"<b>{slot.Item.displayName}</b> x{slot.Quantity}\n{desc}";
                    return;
                }
            }

            _tooltipText.text = "Tab/I: cerrar | Q: soltar item seleccionado";
        }

        private void DropSelectedItem()
        {
            if (_playerInventory == null || _selectedSlot < 0) return;
            if (_selectedSlot >= _playerInventory.Slots.Count) return;

            var slot = _playerInventory.Slots[_selectedSlot];
            if (slot.IsEmpty) return;

            var item = slot.Item;
            int qty = slot.Quantity;

            int removed = _playerInventory.RemoveItem(item, qty);
            if (removed <= 0) return;

            // Spawn world pickup at player position + small offset
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                Vector3 dropPos = player.transform.position + (Vector3)(Random.insideUnitCircle.normalized * 1.5f);
                DropSystem.SpawnDrop(item, removed, dropPos);
            }

            _selectedSlot = -1;
            UpdateSlotHighlights();
            UpdateTooltip();

            Debug.Log($"[InventoryUI] Dropped {removed}x {item.displayName}");
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private void OnDisable()
        {
            _toggleAction?.Disable();
            _dropAction?.Disable();
        }

        private void OnDestroy()
        {
            _toggleAction?.Dispose();
            _dropAction?.Dispose();

            if (_instance == this)
                _instance = null;
        }
    }
}
