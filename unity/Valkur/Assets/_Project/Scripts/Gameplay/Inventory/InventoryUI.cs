using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Screen-space inventory grid UI. Toggle with Tab or I key.
    /// Maps to Python's InventoryUISystem (header, equipment preview,
    /// 5×5 grid, tabs, gold footer, drag-and-drop).
    /// </summary>
    public partial class InventoryUI : SingletonMonoBehaviour<InventoryUI>
    {
        [Header("Layout (legacy fields, kept for inspector parity)")]
        // CS0414: these [SerializeField] fields are read by Unity serialization, not by C# code.
        // Suppress the "assigned but never used" warning for this small block only.
#pragma warning disable CS0414
        [SerializeField] private int columns = 5;
        [SerializeField] private float slotSize = 64f;
        [SerializeField] private float padding = 8f;
#pragma warning restore CS0414

        [Header("Colors (legacy)")]
        [SerializeField] private Color panelColor      = new Color(0.1f, 0.1f, 0.15f, 0.9f);
        [SerializeField] private Color slotColor       = new Color(0.2f, 0.2f, 0.25f, 1f);
        [SerializeField] private Color slotHoverColor  = new Color(0.3f, 0.3f, 0.4f, 1f);
        [SerializeField] private Color selectedColor   = new Color(0.4f, 0.6f, 0.9f, 1f);

        // ── Runtime UI roots (built in UILogic) ──
        private Canvas         _canvas;
        private GameObject     _panelGo;
        private RectTransform  _panelRect;
        private CanvasGroup    _panelGroup;

        // ── Player references ──
        private Inventory        _playerInventory;
        private CurrencyWallet   _playerWallet;
        private Experience       _playerXp;
        private SpriteRenderer   _playerSprite;
        private GameObject       _playerGo;
        private PlayerDefinition _playerDef;
        private ItemConsumer     _playerConsumer;

        // ── Input ──
        // Resolved from the canonical asset every read rather than held in a field. With
        // Domain Reload off, a serialized private InputAction comes back as a zombie
        // (bindings.Count == 0, actionMap == null) after a mid-Play recompile — the hazard
        // PlayerController carries EnsureInputActionsLive for — and a property that asks the
        // service cannot go stale.
        private InputAction _toggleAction => InputService.Instance?.Gameplay?.Inventory;
        private InputAction _dropAction   => InputService.Instance?.Gameplay?.DropItem;

        private static InputActionDescriptor _descInventory =>
            InputActionCatalog.Find(InputActionCatalog.MapGameplay, "Inventory");

        private bool _visible;
        private int  _selectedSlot = -1;

        // ── Cached UI refs ──
        private GameObject[]      _slotObjects;
        private Image[]           _slotBackgrounds;
        private Outline[]         _slotOutlines;
        private Image[]           _slotIcons;
        private TextMeshProUGUI[] _slotQuantities;
        private TextMeshProUGUI   _tooltipText;

        public bool IsVisible => _visible;

        protected override void OnSingletonAwake()
        {
            // Deliberately empty. This used to build two InputActions in code —
            // "ToggleInventory" on tab AND i, and "DropItem" on q — which put two live
            // bindings outside ValkurInputActions where nothing could audit them. That is
            // how `tab` went on opening the inventory for the whole life of the War/Peace
            // stance, months after a comment in this very file declared it removed, and
            // while StanceGateTests.Tab_IsBoundOnlyToToggleStance passed: the test reads the
            // ASSET, and this action was not in it. Both are asset actions now.
            InputService.Initialize();
        }

        private void Start()
        {
            ResolvePlayerRefs();
            BuildUI();
            SetVisible(false);
            RegisterTrayButton();
        }

        private void Update()
        {
            // Both backends, both derived from whatever the action is bound to right now.
            // InputBindingResolver reads through KeyboardInputManager, so a modal panel's
            // input block still reaches these: raw reads answered while the chat had focus,
            // so typing 'i' opened the inventory mid-sentence and 'q' threw an item on the
            // ground.
            //
            // THE LITERALS WERE THE BUG. `KeyCode.I` and `KeyCode.Q` were hardcoded next to
            // the actions, so rebinding the inventory left `I` opening it anyway — and
            // ValkurInputActions was not the source of truth for the fallback, which is
            // exactly how Tab came within one commit of being a second meaning for the same
            // physical key. This project has bound one key to two features three times
            // (`e` on Interact + SpellSlash, `p` on Pause + SpellMeteorShower, which threw
            // meteors when the player paused); an audit over the asset can only see the ones
            // that are IN the asset.
            //
            // `q` is still the drop key AND SpellTeleport. It survives because the drop is
            // read only while the panel is open, and the panel sets InputBlocker — so the
            // Controls editor reports it as a conflict the player may resolve, rather than
            // this file silently owning half of it.
            if (InputContextPolicy.IsLive(_descInventory) &&
                InputBindingResolver.WasPerformedThisFrame(_toggleAction))
                SetVisible(!_visible);

            if (_visible)
            {
                if (InputBindingResolver.WasPerformedThisFrame(_dropAction) && _selectedSlot >= 0)
                    DropSelectedItem();
            }
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_panelGroup != null)
            {
                _panelGroup.alpha          = visible ? 1f : 0f;
                _panelGroup.blocksRaycasts = visible;
                _panelGroup.interactable   = visible;
            }

            if (visible)
            {
                ResolvePlayerRefs();
                RefreshAll();
                int rendered = 0;
                if (_playerInventory != null)
                    for (int i = 0; i < _playerInventory.Slots.Count; i++)
                        if (!_playerInventory.Slots[i].IsEmpty) rendered++;
                Debug.Log($"[InventoryUI] SetVisible(true): refreshed UI, rendered {rendered} bag item(s)");
            }
        }

        public void SelectSlot(int index)
        {
            _selectedSlot = index;
            UpdateSlotHighlights();
            UpdateTooltip();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Player refs + change subscriptions
        // ─────────────────────────────────────────────────────────────────────

        private void ResolvePlayerRefs()
        {
            var player = EntityRegistry.Player;
            if (player == null)
            {
                Debug.Log($"[InventoryUI] ResolvePlayerRefs: EntityRegistry.Player is NULL — skipping wire-up.");
                return;
            }
            if (player == _playerGo) return; // already wired

            UnsubscribePlayer();

            _playerGo        = player;
            _playerInventory = player.GetComponent<Inventory>();
            _playerWallet    = player.GetComponent<CurrencyWallet>();
            _playerXp        = player.GetComponent<Experience>();
            _playerConsumer  = player.GetComponent<ItemConsumer>();
            _playerSprite    = player.GetComponentInChildren<SpriteRenderer>();
            _playerDef       = ResolvePlayerDefinition(PlayerSelectionState.SelectedPlayerKey);

            int liveCount = 0;
            if (_playerInventory != null)
                for (int i = 0; i < _playerInventory.Slots.Count; i++)
                    if (!_playerInventory.Slots[i].IsEmpty) liveCount++;
            Debug.Log($"[InventoryUI] ResolvePlayerRefs wired player={player.name} inventoryFound={_playerInventory != null} liveBagItems={liveCount}");

            if (_playerInventory != null) _playerInventory.OnInventoryChanged += OnInventoryChangedExternal;
            if (_playerXp != null)
            {
                _playerXp.OnXpGained += OnXpGainedExternal;
                _playerXp.OnLevelUp  += OnLevelUpExternal;
            }
            CurrencyWallet.OnCoinsChanged += OnCoinsChangedExternal;
        }

        private void UnsubscribePlayer()
        {
            if (_playerInventory != null) _playerInventory.OnInventoryChanged -= OnInventoryChangedExternal;
            if (_playerXp != null)
            {
                _playerXp.OnXpGained -= OnXpGainedExternal;
                _playerXp.OnLevelUp  -= OnLevelUpExternal;
            }
            CurrencyWallet.OnCoinsChanged -= OnCoinsChangedExternal;
        }

        private void OnInventoryChangedExternal()                 { if (_visible) RefreshAll(); }
        private void OnXpGainedExternal(int amount)               { if (_visible) UpdateHeaderInfo(); }
        private void OnLevelUpExternal(int level)                 { if (_visible) UpdateHeaderInfo(); }
        private void OnCoinsChangedExternal(int balance, int dlt) { if (_visible) UpdateGold(); }

        private static PlayerDefinition ResolvePlayerDefinition(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            // Optional resolve from Resources/Players if the project ships PlayerDefinition
            // assets there. Scoped, never the whole tree: LoadAll("") deserializes every
            // asset under Resources/ and surfaces a console error for each one whose
            // script no longer resolves.
            var all = Resources.LoadAll<PlayerDefinition>("Players");
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null &&
                    string.Equals(all[i].playerKey, key, System.StringComparison.OrdinalIgnoreCase))
                    return all[i];
            return null;
        }
    }
}
