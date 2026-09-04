using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// Screen-space vendor shop UI. Opens when the player interacts with a VendorNPC.
    /// Shows vendor stock (buy side) and player inventory (sell side) in a split panel.
    /// Maps to Python's VendorUISystem: item list, buy/sell buttons, stock, prices, gold display.
    /// </summary>
    public partial class VendorShopUI : SingletonMonoBehaviour<VendorShopUI>
    {
        [Header("Layout")]
        [Tooltip("Width of each side panel in pixels.")]
        [SerializeField] private float panelWidth = 320f;
        [Tooltip("Height of the shop window in pixels.")]
        [SerializeField] private float panelHeight = 480f;
        [Tooltip("Height of each item row in pixels.")]
        [SerializeField] private float rowHeight = 56f;

        [Header("Colors")]
        [SerializeField] private Color bgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        [SerializeField] private Color rowColor = new Color(0.14f, 0.14f, 0.20f, 1f);
        [SerializeField] private Color rowHoverColor = new Color(0.22f, 0.22f, 0.30f, 1f);
        [SerializeField] private Color buyButtonColor = new Color(0.2f, 0.55f, 0.2f, 1f);
        [SerializeField] private Color sellButtonColor = new Color(0.55f, 0.35f, 0.2f, 1f);
        [SerializeField] private Color titleColor = new Color(0.85f, 0.75f, 0.4f, 1f);
        [SerializeField] private Color goldColor = new Color(1f, 0.85f, 0.2f, 1f);

        // --- Runtime state ---
        private Canvas _canvas;
        private GameObject _root;
        private VendorNPC _currentVendor;

        /// <summary>Shown when a column has no rows. See CreateEmptyState for why.</summary>
        private TMPro.TextMeshProUGUI _vendorEmptyText;
        private TMPro.TextMeshProUGUI _playerEmptyText;
        private Inventory.Inventory _playerInventory;
        private CurrencyWallet _playerWallet;
        private bool _visible;

        private TextMeshProUGUI _goldText;
        private TextMeshProUGUI _vendorTitleText;
        private Transform _vendorRowsParent;
        private Transform _playerRowsParent;
        private InputAction _closeAction;

        private readonly List<GameObject> _vendorRows = new List<GameObject>();
        private readonly List<GameObject> _playerRows = new List<GameObject>();

        public bool IsVisible => _visible;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            // Escape only. E used to close the shop too, and E is now the interact key
            // that OPENS a conversation — with Unity's Update order undefined between
            // this component and PlayerInteractionController, one press would have closed
            // the shop and re-opened the chat behind it, or not, depending on the frame.
            _closeAction = new InputAction("CloseShop", InputActionType.Button);
            _closeAction.AddBinding("<Keyboard>/escape");
            _closeAction.Enable();
        }

        private void Start()
        {
            BuildUI();
            SetVisible(false);
        }

        // Cached gold count and label so we only rebuild the TMP string when
        // the value actually changes. The original code allocated a new
        // string + triggered a TMP mesh rebuild every Update tick while the
        // shop was open — measurable GC pressure during browsing sessions.
        private int _lastDisplayedGold = int.MinValue;

        private void Update()
        {
            if (!_visible) return;
            if (_closeAction != null && _closeAction.WasPerformedThisFrame())
                SetVisible(false);
            if (_goldText != null && _playerWallet != null)
            {
                int coins = _playerWallet.Coins;
                if (coins != _lastDisplayedGold)
                {
                    _lastDisplayedGold = coins;
                    _goldText.text = "Gold: " + coins;
                }
            }
        }

        protected override void OnDestroy()
        {
            if (_closeAction != null)
            {
                _closeAction.Disable();
                _closeAction.Dispose();
            }
            base.OnDestroy();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Opens the shop for the given vendor and player context.</summary>
        public void OpenShop(VendorNPC vendor, Inventory.Inventory playerInventory, CurrencyWallet wallet)
        {
            _currentVendor = vendor;
            _playerInventory = playerInventory;
            _playerWallet = wallet;

            if (_vendorTitleText != null)
                _vendorTitleText.text = vendor.GetComponent<NPCInteractable>()?.NPCName ?? "Shop";

            RefreshVendorRows();
            RefreshPlayerRows();
            SetVisible(true);
        }

        /// <summary>Closes the shop panel.</summary>
        public void CloseShop() => SetVisible(false);

        // ------------------------------------------------------------------
        // UI Construction
        // ------------------------------------------------------------------

        private partial void BuildUI();
        private partial void RefreshVendorRows();
        private partial void RefreshPlayerRows();

        // ------------------------------------------------------------------
        // Transaction Handlers
        // ------------------------------------------------------------------

        private void HandleBuy(ItemDefinition item)
        {
            if (_currentVendor == null || _playerInventory == null || _playerWallet == null) return;
            _currentVendor.TryBuyItem(item, _playerInventory, _playerWallet);
            RefreshVendorRows();
            RefreshPlayerRows();
        }

        private void HandleSell(ItemDefinition item)
        {
            if (_currentVendor == null || _playerInventory == null || _playerWallet == null) return;
            _currentVendor.TrySellItem(item, _playerInventory, _playerWallet);
            RefreshVendorRows();
            RefreshPlayerRows();
        }

        // ------------------------------------------------------------------
        // Visibility
        // ------------------------------------------------------------------

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null)
                _root.SetActive(visible);
            if (!visible)
            {
                _currentVendor = null;
                _playerInventory = null;
                _playerWallet = null;
            }
        }
    }
}
