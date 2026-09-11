using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// The player's inventory window: what they wear, what they carry, what they are worth.
    ///
    /// <para><b>It speaks the player panel's language, not an editor's.</b> Until 2026-09-11 it
    /// wore the Tile editor's theme (a live-tweakable static, so retuning the editor recoloured
    /// the player's bag), drew 76 flat rectangles and one sprite, sat translucent over the
    /// minimap, and taught the player to press Tab to close it (Tab is the stance) and Q to drop
    /// (Q is the teleport). It is built now from the shared HUD kit — the generated stone, the
    /// slot hollows, the bitmap face, the event motes — on the same whole-pixel texel grid as the
    /// player panel beside it (<see cref="PlayerHudStyle.HudPixelScaleFor"/>), with its colours
    /// from <see cref="HudTheme"/> and its own geometry from <see cref="InventoryHudStyle"/>.
    /// Audit and rationale: <c>.github/INVENTORY_HUD_BEAUTY_AUDIT_2026-09-11.md</c>.</para>
    ///
    /// <para><b>Events are seen, states are read.</b> Nothing moves while nothing happens. A
    /// pickup flashes the slot it landed in, a rare one throws light in its rarity's colour,
    /// equipping draws a trail to the paper doll and counts the stats to their new value, gold
    /// counts and glints — and all of it dies inside a second.</para>
    ///
    /// <para>Every widget is a plain class driven by <see cref="Tick"/>, so an EditMode test can
    /// build the window, tick it and read back what the player would see.</para>
    /// </summary>
    public partial class InventoryUI : SingletonMonoBehaviour<InventoryUI>
    {
        // ── Player references ──
        private Inventory        _playerInventory;
        private CurrencyWallet   _playerWallet;
        private Experience       _playerXp;
        private PlayerStats      _playerStats;
        private GameObject       _playerGo;
        private PlayerDefinition _playerDef;
        private ItemConsumer     _playerConsumer;

        // ── Input ──
        // Resolved from the canonical asset on every read rather than held in a field: with
        // Domain Reload off a serialized InputAction comes back a zombie after a mid-Play
        // recompile.
        private InputAction _toggleAction => InputService.Instance?.Gameplay?.Inventory;
        private InputAction _dropAction   => InputService.Instance?.Gameplay?.DropItem;

        private static InputActionDescriptor _descInventory =>
            InputActionCatalog.Find(InputActionCatalog.MapGameplay, "Inventory");

        private static InputActionDescriptor _descDropItem =>
            InputActionCatalog.Find(InputActionCatalog.MapGameplay, "DropItem");

        private bool _visible;
        private int  _selectedSlot = -1;

        /// <summary>True while the window is open (including while it is collapsed).</summary>
        public bool IsVisible => _visible;

        protected override void OnSingletonAwake()
        {
            // Bindings live in ValkurInputActions only; this used to build its own InputActions
            // in code, which is how Tab went on opening the inventory for months after the stance
            // took it.
            InputService.Initialize();
        }

        private void Start()
        {
            ResolvePlayerRefs();
            BuildUI();
            SetVisible(false, animate: false);
            RegisterTrayButton();
        }

        private void Update()
        {
            if (InputContextPolicy.IsLive(_descInventory) &&
                InputBindingResolver.WasPerformedThisFrame(_toggleAction))
                SetVisible(!_visible);

            if (_visible && !_collapsed)
            {
                // The context mask as well as the binding: without it the Controls editor drew
                // posture chips on this row that reported a change nothing honoured.
                if (InputContextPolicy.IsLive(_descDropItem) &&
                    InputBindingResolver.WasPerformedThisFrame(_dropAction) && _selectedSlot >= 0)
                    RequestWorldDrop(_selectedSlot, null, 0);
            }

            // A pending "throw it away?" takes Enter and Escape first, so answering it can never
            // also close the window behind it.
            if (_confirm != null && _confirm.Visible)
            {
                if (KeyboardInputManager.WasEscapePressedThisFrame()) _confirm.Resolve(false);
                else if (KeyboardInputManager.WasEnterPressedThisFrame()) _confirm.Resolve(true);
            }
            // Escape closes the window before it opens the General Editor: the window claims the
            // key for as long as it is up, exactly as the world map does.
            else if (_visible && KeyboardInputManager.WasEscapePressedThisFrame())
                SetVisible(false);

            Refit();
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>Opens or closes the window.</summary>
        public void SetVisible(bool visible) => SetVisible(visible, animate: true);

        public void SetVisible(bool visible, bool animate)
        {
            bool was = _visible;
            _visible = visible;

            if (visible)
            {
                ResolvePlayerRefs();
                RefreshAll(announce: false);
                EscapeOwnership.Claim(this);
                if (!was && animate) InventoryAudio.Play(InventorySound.Open, _style);
            }
            else
            {
                EscapeOwnership.Release(this);
                EndDragQuietly();
                HideCard();
                _confirm?.Resolve(false);
                SetHover(-1);
            }

            if (_panelGroup != null)
            {
                _panelGroup.blocksRaycasts = visible;
                _panelGroup.interactable = visible;
                if (!animate) { _openT = visible ? 1f : 0f; ApplyOpen(); }
            }
            RefreshTrayBadge();
        }

        public void SelectSlot(int index)
        {
            _selectedSlot = index;
            UpdateMarks();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Player refs + change subscriptions
        // ─────────────────────────────────────────────────────────────────────

        private void ResolvePlayerRefs()
        {
            var player = EntityRegistry.Player;
            if (player == null)
            {
                VerboseLog.Log(VerboseLog.Category.Bootstrap, () => "[InventoryUI] ResolvePlayerRefs: no player yet.");
                return;
            }
            if (player == _playerGo) return; // already wired
            WirePlayer(player);
        }

        /// <summary>
        /// Builds the window against <paramref name="player"/> and opens it. For EditMode tests,
        /// where Unity calls no Start and no player is registered.
        /// </summary>
        internal void BuildFor(GameObject player)
        {
            WirePlayer(player);
            BuildUI();
            SetVisible(true, animate: false);
        }

        /// <summary>
        /// The half of OnDestroy a test needs: Unity sends no OnDestroy to a component whose Awake
        /// never ran, and <see cref="CurrencyWallet.OnCoinsChanged"/> is a STATIC event, so a
        /// fixture's window left subscribed would answer the next test's coins from a destroyed
        /// canvas.
        /// </summary>
        internal void TeardownForTests()
        {
            UnsubscribePlayer();
            EscapeOwnership.Release(this);
            ReleaseArt();
        }

        private void WirePlayer(GameObject player)
        {
            if (player == null || player == _playerGo) return;
            UnsubscribePlayer();

            _playerGo        = player;
            _playerInventory = player.GetComponent<Inventory>();
            _playerWallet    = player.GetComponent<CurrencyWallet>();
            _playerXp        = player.GetComponent<Experience>();
            _playerStats     = player.GetComponent<PlayerStats>();
            _playerConsumer  = player.GetComponent<ItemConsumer>();
            _playerDef       = ResolvePlayerDefinition(PlayerSelectionState.SelectedPlayerKey);

            if (_playerInventory != null) _playerInventory.OnInventoryChanged += OnInventoryChangedExternal;
            if (_playerXp != null)
            {
                _playerXp.OnXpGained += OnXpGainedExternal;
                _playerXp.OnLevelUp  += OnLevelUpExternal;
                _playerXp.OnStateChanged += OnXpStateExternal;
            }
            if (_playerStats != null) _playerStats.OnStatsChanged += OnStatsChangedExternal;
            CurrencyWallet.OnCoinsChanged += OnCoinsChangedExternal;

            // The bag's first picture is taken now, so nothing already carried is announced as
            // just picked up.
            TakeSnapshot();
            _figureSource = null;
            if (_built) EnsureBagViews();
        }

        private void UnsubscribePlayer()
        {
            if (_playerInventory != null) _playerInventory.OnInventoryChanged -= OnInventoryChangedExternal;
            if (_playerXp != null)
            {
                _playerXp.OnXpGained -= OnXpGainedExternal;
                _playerXp.OnLevelUp  -= OnLevelUpExternal;
                _playerXp.OnStateChanged -= OnXpStateExternal;
            }
            if (_playerStats != null) _playerStats.OnStatsChanged -= OnStatsChangedExternal;
            CurrencyWallet.OnCoinsChanged -= OnCoinsChangedExternal;
        }

        private void OnInventoryChangedExternal()                 => OnInventoryChanged();
        private void OnXpGainedExternal(int amount)               { if (_visible) UpdateHeader(); }
        private void OnLevelUpExternal(int level)                 { if (_visible) { UpdateHeader(); _medallion?.Pulse(0.9f); } }
        private void OnXpStateExternal()                          { if (_visible) UpdateHeader(); }
        private void OnStatsChangedExternal()                     => OnStatsChanged();
        private void OnCoinsChangedExternal(int balance, int dlt) => OnCoinsChanged(dlt);

        private static PlayerDefinition ResolvePlayerDefinition(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            // Scoped, never the whole tree: LoadAll("") deserializes every asset under
            // Resources/ and surfaces a console error for each one whose script no longer
            // resolves.
            var all = Resources.LoadAll<PlayerDefinition>("Players");
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null &&
                    string.Equals(all[i].playerKey, key, System.StringComparison.OrdinalIgnoreCase))
                    return all[i];
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Tray
        // ─────────────────────────────────────────────────────────────────────

        internal void RegisterTrayButton()
        {
            var bar = Valkur.UIKit.HUDIconBar.Instance;
            if (bar == null) return;
            // From the style asset, never the editor's asset database: that does not exist in a player build,
            // where the button used to be a grey square.
            var sprite = _style != null ? _style.trayIcon : null;
            bar.Register("inventory", sprite, () => SetVisible(!_visible), order: 0);
        }

        protected override void OnDestroy()
        {
            UnsubscribePlayer();
            EscapeOwnership.Release(this);
            ReleaseArt();
            base.OnDestroy();
        }
    }
}
