using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Crafting
{
    /// <summary>
    /// The player's crafting screen: one tab per trade, every recipe in that trade, and a
    /// button that makes it.
    ///
    /// <para>WHY EVERY RECIPE IS LISTED, INCLUDING THE ONES THE PLAYER CANNOT MAKE. The panel's
    /// job is not "here is what you can make" — the player can work that out by looking in
    /// their bag. It is "here is what you could make IF", which is the only version of the
    /// screen that tells them what to go and collect. A list filtered down to the affordable
    /// rows is empty at the exact moment it would be most useful, which is the first time it is
    /// opened.</para>
    ///
    /// <para>WHY IT IS NOT PART OF <c>InventoryUI</c>. That class is already five partials and
    /// 1,286 lines, it owns drag-and-drop and equipment, and its grid is rebuilt on every
    /// inventory change. Crafting shares only the bag with it, and shares that through
    /// <see cref="CraftingService"/>, which takes the <c>Inventory</c> as an argument and knows
    /// nothing about either screen.</para>
    ///
    /// <para>WHY IT DOES NOT SET <c>InputBlocker</c>. That is one global bool with no notion of
    /// an owner, and this project has already paid for a flag with several writers once — see
    /// the <c>Health.SetInvincible</c> note in CLAUDE.md, where the shield switched off
    /// whichever of god mode or the Spells editor was holding it. Closing this panel while the
    /// inventory was also open would clear a block it never set. Escape is read through
    /// <c>InputCompat</c> instead, the same Cancel every other panel uses, so this one cannot
    /// drift onto a different key from the rest of the menus. Same call <c>VendorShopUI</c>
    /// made.</para>
    /// </summary>
    public partial class CraftingPanelUI : SingletonMonoBehaviour<CraftingPanelUI>
    {
        // ── Layout constants ──────────────────────────────────────────────────────
        private const float PANEL_W = 600f;
        private const float PANEL_H = 540f;
        private const float TITLE_H = 34f;
        private const float TABS_H = 32f;
        private const float LEVEL_H = 26f;
        private const float STATUS_H = 26f;
        private const float ROW_H = 62f;

        /// <summary>
        /// No virtualisation here, and this is why: the shipped catalog is 36 recipes and the
        /// largest trade shows six. The Items editor virtualises because it drew 38 columns x
        /// 180 items = 6,840 widgets and cost 3.5 s; six rows of five widgets is 30. Measure
        /// before adding machinery — this cap exists only so a trade that grows to hundreds
        /// degrades into a scroll rather than a hitch.
        /// </summary>
        private const int MAX_ROWS = 60;

        // ── Runtime UI ────────────────────────────────────────────────────────────
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _rowsParent;
        private RectTransform _tabStrip;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _stationText;
        private TextMeshProUGUI _levelText;
        private Image _levelFill;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<ProfessionDefinition> _tabs = new List<ProfessionDefinition>();

        // ── State ─────────────────────────────────────────────────────────────────
        private RecipeCatalog _catalog;
        private Inventory.Inventory _playerInventory;
        private PlayerProfessions _professions;
        private GameObject _playerGo;
        private int _activeTab;
        private bool _visible;
        private bool _built;

        /// <summary>
        /// Whether a station for the ACTIVE trade was in range on the last refresh.
        ///
        /// <para>Cached per refresh rather than asked per row, and re-asked every frame while
        /// the panel is open. Both halves matter: asking once per row would run the station
        /// sweep for every row of the screen, and caching it across frames would leave a player
        /// who walked away from the forge still able to make its hardest recipe — the panel
        /// refreshes on inventory change, and walking is not one.</para>
        /// </summary>
        private bool _stationInRange;

        public bool IsVisible => _visible;

        /// <summary>Station proximity as the panel last resolved it. Read by the tests.</summary>
        public bool StationInRange => _stationInRange;

        /// <summary>The trade whose tab is open, or null before the catalog resolves.</summary>
        public ProfessionDefinition ActiveProfession =>
            (_activeTab >= 0 && _activeTab < _tabs.Count) ? _tabs[_activeTab] : null;

        private void Start()
        {
            EnsureBuilt();
            SetVisible(false);
            RegisterTrayButton();
        }

        /// <summary>
        /// Build once, on demand. Separated from <c>Start</c> so an EditMode test can drive the
        /// panel without a scene running — <c>Start</c> never fires there.
        /// </summary>
        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            BuildUI();
        }

        private void Update()
        {
            if (!_visible) return;

            if (Valkur.Core.Input.InputCompat.CancelPressed())
            {
                SetVisible(false);
                return;
            }

            // Walking out of a forge has to re-lock its harder recipes, and no event reports
            // it — the inventory raises OnInventoryChanged, the player's position raises
            // nothing. Polling is what keeps the panel honest about where the player is
            // standing; it is one bounds test per registered station, against a list that is
            // normally empty.
            bool station = CraftingStation.IsInRangeOfPlayer(_playerGo, ActiveProfession);
            if (station != _stationInRange) RefreshRows();
        }

        /// <summary>Open on whichever tab was last used.</summary>
        public void Open() => OpenAt(null);

        /// <summary>
        /// Open with <paramref name="profession"/> selected. Null keeps the current tab, which
        /// is what the HUD button wants; a station passes its own trade so the player arrives
        /// looking at what the thing in front of them can make.
        /// </summary>
        public void OpenAt(ProfessionDefinition profession)
        {
            EnsureBuilt();
            ResolveRefs();
            RebuildTabsIfNeeded();

            if (profession != null)
            {
                int index = _tabs.IndexOf(profession);
                if (index >= 0) _activeTab = index;
            }

            SetVisible(true);
        }

        public void Close() => SetVisible(false);

        public void Toggle()
        {
            if (_visible) Close();
            else Open();
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null) _root.SetActive(visible);

            if (visible)
            {
                ResolveRefs();
                RebuildTabsIfNeeded();
                RefreshRows();
            }
            else
            {
                UnsubscribePlayer();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  References
        // ─────────────────────────────────────────────────────────────────────────

        private void ResolveRefs()
        {
            if (_catalog == null)
                _catalog = Resources.Load<RecipeCatalog>(RecipeCatalog.ResourcePath);

            var player = EntityRegistry.Player;
            if (player == _playerGo) return;

            UnsubscribePlayer();
            _playerGo = player;
            _playerInventory = player != null ? player.GetComponent<Inventory.Inventory>() : null;
            _professions = player != null ? player.GetComponent<PlayerProfessions>() : null;

            // Added rather than required. A trade the player has never practised must not be a
            // reason the panel refuses to work, and PlayerProfessions carries no state a fresh
            // component does not already imply — every trade starts at level 1.
            if (_professions == null && player != null)
                _professions = player.AddComponent<PlayerProfessions>();

            if (_playerInventory != null)
                _playerInventory.OnInventoryChanged += OnInventoryChanged;
            if (_professions != null)
                _professions.OnProfessionLevelUp += OnProfessionLevelUp;
        }

        private void UnsubscribePlayer()
        {
            if (_playerInventory != null)
                _playerInventory.OnInventoryChanged -= OnInventoryChanged;
            if (_professions != null)
                _professions.OnProfessionLevelUp -= OnProfessionLevelUp;
        }

        protected override void OnDestroy()
        {
            UnsubscribePlayer();
            base.OnDestroy();
        }

        private void OnInventoryChanged()
        {
            if (_visible) RefreshRows();
        }

        /// <summary>
        /// A level-up can UNLOCK rows, so the list has to be rebuilt rather than only the bar
        /// redrawn — a recipe that became available while the panel was open would otherwise
        /// stay greyed out until the player closed and reopened it.
        /// </summary>
        private void OnProfessionLevelUp(string key, int level)
        {
            if (!_visible) return;
            RefreshRows();
            var active = ActiveProfession;
            if (active != null && string.Equals(active.professionKey, key,
                    System.StringComparison.OrdinalIgnoreCase))
                SetStatus($"{active.displayName} sube a nivel {level}.", UIKit.UITheme.SUCCESS);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Crafting
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Craft one batch of <paramref name="recipe"/> and report what happened in the status
        /// line.
        ///
        /// <para>The refusal messages are specific on purpose. "No puedes fabricar esto" is the
        /// message that makes a player think the button is broken; naming the missing
        /// ingredient, the level or the missing forge tells them what to do next, which is the
        /// entire difference between a blocked action and a goal.</para>
        /// </summary>
        internal void CraftOne(RecipeDefinition recipe)
        {
            if (recipe == null) return;
            ResolveRefs();

            _stationInRange = CraftingStation.IsInRangeOfPlayer(_playerGo, recipe.profession);
            var availability = CraftingService.Evaluate(
                _playerInventory, recipe, _professions, _stationInRange);

            if (!availability.CanCraft)
            {
                SetStatus(DescribeRefusal(recipe, availability), UIKit.UITheme.WARNING);
                RefreshRows();
                return;
            }

            if (CraftingService.TryCraft(_playerInventory, recipe, _professions, _stationInRange))
                SetStatus($"Has fabricado {recipe.displayName}.", UIKit.UITheme.SUCCESS);
            else
                SetStatus("No hay sitio en la mochila.", UIKit.UITheme.WARNING);

            RefreshRows();
        }

        private static string DescribeRefusal(RecipeDefinition recipe, CraftAvailability a)
        {
            switch (a.Reason)
            {
                case CraftBlockReason.LevelTooLow:
                    return $"{recipe.displayName} necesita nivel {a.RequiredLevel} de " +
                           $"{recipe.profession.displayName}.";
                case CraftBlockReason.NeedsStation:
                    return $"{recipe.displayName} necesita {StationNoun(recipe)} cerca.";
                case CraftBlockReason.NoRoomForOutput:
                    return "No hay sitio en la mochila.";
                case CraftBlockReason.Malformed:
                    return "Esta receta esta incompleta.";
                default:
                    return "Te falta: " + DescribeShortfalls(a);
            }
        }

        private static string StationNoun(RecipeDefinition recipe)
        {
            var p = recipe.profession;
            if (p == null || string.IsNullOrWhiteSpace(p.stationName)) return "una estacion";
            return p.stationName;
        }

        /// <summary>
        /// "2 remolacha, 1 carne de res" — every shortfall, not the first. See
        /// <see cref="CraftAvailability.Shortfalls"/> on why.
        /// </summary>
        private static string DescribeShortfalls(CraftAvailability a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Shortfalls.Count; i++)
            {
                var s = a.Shortfalls[i];
                if (i > 0) sb.Append(", ");
                sb.Append(s.Missing).Append(' ');
                sb.Append(s.Item != null ? s.Item.displayName : "?");
            }
            return sb.ToString();
        }

        private void SetStatus(string message, Color color)
        {
            if (_statusText == null) return;
            _statusText.text = message;
            _statusText.color = color;
        }

        // Built in the Builder partial.
        private partial void BuildUI();
        private partial void RefreshRows();
        private partial void RebuildTabsIfNeeded();
        private partial void RegisterTrayButton();
    }
}
