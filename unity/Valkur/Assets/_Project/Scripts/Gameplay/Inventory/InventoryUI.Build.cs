using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Builds the window. Laid out bottom-up in TEXELS from <see cref="InventoryHudStyle"/>:
    /// footer, bag grid, tabs, paper doll, header, title bar — so a change to any row height moves
    /// everything above it instead of leaving a gap or an overlap.
    /// </summary>
    public partial class InventoryUI
    {
        /// <summary>The tabs, in order. -1 is "everything".</summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of constant category ids, written once at " +
                                       "type init and never mutated; holds no Unity objects.")]
        internal static readonly int[] TabCategories =
        {
            -1,
            (int)ItemCategory.Equipment,
            (int)ItemCategory.Consumable,
            (int)ItemCategory.Material,
            (int)ItemCategory.Quest,
            (int)ItemCategory.Other,
        };

        [Valkur.Core.SelfHealingStatic("Immutable table of literal tab names, written once at type " +
                                       "init and never mutated; holds no Unity objects.")]
        internal static readonly string[] TabNames =
        {
            "Todo", "Equipo", "Consumibles", "Materiales", "Misión", "Otros",
        };

        private const int SectionGap = 4;
        private const int DividerTexels = 3;
        private const int HeaderTexels = 28;
        private const int ButtonTexels = 11;
        private const int MedallionTexels = 17;

        private InventoryHudStyle _style;
        private HudTheme _theme;
        private PlayerHudStyle _gridStyle;
        private HudArt _art;
        private InventoryArt _inv;
        private Material _additive;

        private Canvas _canvas;
        private RectTransform _panelRect;
        private CanvasGroup _panelGroup;
        private RectTransform _pixels;
        private RectTransform _content;
        private RectTransform _body;
        private RectTransform _titleBar;
        private RawImage _stone;
        private Texture2D _stoneTex;
        private Texture2D _stoneCollapsedTex;

        private int _widthTexels;
        private int _heightTexels;
        private int _collapsedHeight;
        private bool _built;

        private RectTransform _grid;
        private InventorySlotView[] _bagViews = new InventorySlotView[0];
        private InventorySlotView[] _equipViews = new InventorySlotView[0];
        private int _bagRows;

        private HudPixelText _title;
        private Image _collapseGlyph;
        private Image _closeGlyph;

        private TextMeshProUGUI _nameText;
        private HudMedallion _medallion;
        private HudBar _xpBar;
        private readonly HudPixelText[] _statText = new HudPixelText[4];
        private readonly Image[] _statIcon = new Image[4];
        private readonly RectTransform[] _statHit = new RectTransform[4];
        private RectTransform _capacityHit;
        private RectTransform _weightHit;
        private readonly StatKind[] _statKinds = { StatKind.MeleeDamage, StatKind.Defense, StatKind.MaxHp, StatKind.MaxMana };

        private Image[] _tabFrame;
        private Image[] _tabGlyph;
        private Image _sortGlyph;
        private int _activeTab;

        private Image _bagGlyph;
        private HudPixelText _capacityText;
        private Image _weightGlyph;
        private HudPixelText _weightText;
        private Image _coin;
        private HudPixelText _goldText;

        private RectTransform _doll;
        private RawImage _backdrop;
        private Texture2D _backdropTex;
        private RawImage _figure;
        private Texture2D _figureTex;
        private Sprite _figureSource;

        private InventoryItemCard _card;
        private InventoryConfirm _confirm;
        private HudMoteLayer _motes;
        private HudFloatText _floats;

        private RectTransform _ghostRoot;
        private RawImage _ghost;
        private Image _ghostFallback;
        private HudPixelText _ghostCount;

        // ─────────────────────────────────────────────────────────────────────
        //  BuildUI
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            if (_built) return;
            _built = true;

            _style = InventoryHudStyle.Active;
            _theme = HudTheme.Active;
            _gridStyle = PlayerHudStyle.Active;
            _art = HudArt.Get(_gridStyle);
            _inv = InventoryArt.Get();

            var shader = _gridStyle.hudFxShader != null ? _gridStyle.hudFxShader : Shader.Find("Valkur/UI/HudFx");
            if (shader != null)
            {
                _additive = new Material(shader) { name = "InventoryAdditive", hideFlags = HideFlags.DontSave };
                _additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }

            // Root canvas, on the reference every HUD canvas uses (HudLayout), so the window and
            // the minimap agree about where the top-right column ends.
            var canvasGo = new GameObject("InventoryCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(HudLayout.ReferenceWidth, HudLayout.ReferenceHeight);
            scaler.matchWidthOrHeight = HudLayout.Match;
            canvasGo.AddComponent<GraphicRaycaster>();

            var panelGo = new GameObject("InventoryPanel", typeof(RectTransform));
            panelGo.transform.SetParent(_canvas.transform, false);
            _panelRect = (RectTransform)panelGo.transform;
            _panelRect.anchorMin = _panelRect.anchorMax = Vector2.zero;
            _panelRect.pivot = new Vector2(0f, 1f);
            // A nested canvas: slots flashing and motes flying rebuild this window's batch, not
            // the whole HUD's.
            var nested = panelGo.AddComponent<Canvas>();
            nested.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;
            panelGo.AddComponent<GraphicRaycaster>();
            _panelGroup = panelGo.AddComponent<CanvasGroup>();

            int capacity = _playerInventory != null ? _playerInventory.Capacity : Inventory.DefaultBagCapacity;
            ComputeGeometry(capacity);

            _pixels = HudRect.Make("Pixels", _panelRect, 0, 0, _widthTexels, _heightTexels);
            _content = HudRect.Make("Content", _pixels, 0, 0, _widthTexels, _heightTexels);

            var stoneRt = HudRect.Make("Stone", _content, 0, 0, _widthTexels, _heightTexels);
            _stone = stoneRt.gameObject.AddComponent<RawImage>();
            _stone.raycastTarget = true;   // the window eats clicks that land on its stone
            BakeStone();

            _body = HudRect.Make("Body", _content, 0, 0, _widthTexels, _heightTexels);
            BuildTitleBar();
            BuildHeader();
            BuildDoll();
            BuildTabs();
            BuildGrid(capacity);
            BuildFooter();

            _motes = HudMoteLayer.Create(_pixels, _art, _style.moteCapacity, _additive);
            _floats = new HudFloatText(_pixels, _art, 6);
            _card = new InventoryItemCard(_pixels, _art, _style.cardWidthTexels);
            _confirm = new InventoryConfirm(_pixels, _art, _theme);
            BuildGhost();

            LoadPlacement();
            ApplyCollapsed();
            Refit(force: true);
        }

        private int _footerY, _gridY, _tabsY, _dollY, _headerY, _titleY;

        private void ComputeGeometry(int capacity)
        {
            var s = _style;
            _widthTexels = s.PanelWidthTexels;
            _bagRows = Mathf.Max(1, Mathf.CeilToInt(capacity / (float)s.bagColumns));
            int dollH = DollHeight;

            _footerY = s.paddingTexels;
            _gridY = _footerY + s.footerTexels + SectionGap;
            _tabsY = _gridY + s.GridHeightTexels(_bagRows) + 3;
            _dollY = _tabsY + s.tabTexels + SectionGap;
            _headerY = _dollY + dollH + SectionGap + 1;
            int dividerY = _headerY + HeaderTexels + 2;
            _titleY = dividerY + DividerTexels + 2;
            _heightTexels = _titleY + s.titleBarTexels + 4;
            _collapsedHeight = s.titleBarTexels + 9;
        }

        private int DollHeight => _style.GridHeightTexels(4);

        private void BakeStone()
        {
            HudLifetime.Release(_stoneTex);
            HudLifetime.Release(_stoneCollapsedTex);
            _stoneTex = HudArt.BakePanel(_widthTexels, _heightTexels, _gridStyle);
            _stoneCollapsedTex = HudArt.BakePanel(_widthTexels, _collapsedHeight, _gridStyle);
            _stone.texture = _stoneTex;
        }

        // ── Title bar ───────────────────────────────────────────────────────

        private void BuildTitleBar()
        {
            var s = _style;
            _titleBar = HudRect.Make("TitleBar", _content, 0, _titleY, _widthTexels, s.titleBarTexels);
            var drag = _titleBar.gameObject.AddComponent<Image>();
            drag.color = Color.clear;
            var relay = _titleBar.gameObject.AddComponent<InventoryPointerRelay>();
            relay.BeginDrag = BeginWindowDrag;
            relay.Drag = WindowDrag;
            relay.EndDrag = EndWindowDrag;
            relay.DoubleClick = _ => ToggleCollapsed();

            _title = HudPixelText.Create(_titleBar, "Title", _art, HudFontFace.Small, HudTextAlign.Left,
                                         s.paddingTexels + 1, 0, 60, s.titleBarTexels);
            _title.SetText("INVENTARIO");
            _title.SetColour(_theme.gold);

            int x = _widthTexels - s.paddingTexels - ButtonTexels;
            _closeGlyph = MakeButton("Close", _titleBar, x, (s.titleBarTexels - ButtonTexels) / 2, _inv.Close,
                                     () => SetVisible(false), () => "Cerrar", CloseHint, _theme.danger);
            x -= ButtonTexels + 2;
            _collapseGlyph = MakeButton("Collapse", _titleBar, x, (s.titleBarTexels - ButtonTexels) / 2, _inv.Collapse,
                                        ToggleCollapsed, () => _collapsed ? "Desplegar" : "Recoger",
                                        () => "Deja solo la barra de título", _theme.gold);

            int divY = _headerY + HeaderTexels + 2;
            HudRect.MakeImage("Divider", _body, _inv.Divider, s.paddingTexels, divY + 1,
                              _widthTexels - s.paddingTexels * 2, 2);
            int gem = _inv.DividerGem != null ? (int)_inv.DividerGem.rect.width : 5;
            HudRect.MakeImage("DividerGem", _body, _inv.DividerGem, (_widthTexels - gem) / 2, divY - 1, gem, gem);
        }

        private string CloseHint()
        {
            string key = InputBindingLabel(_toggleAction);
            return string.IsNullOrEmpty(key) ? "Esc" : key + "  ·  Esc";
        }

        /// <summary>A small stone button with a glyph; returns the glyph so its tint can follow state.</summary>
        private Image MakeButton(string name, Transform parent, int x, int y, Sprite glyph,
                                 System.Action onClick, System.Func<string> title, System.Func<string> body,
                                 Color hoverTint)
        {
            var frame = HudRect.MakeImage(name, parent, _art.Slot, x, y, ButtonTexels, ButtonTexels, Image.Type.Sliced);
            frame.raycastTarget = true;
            int g = glyph != null ? (int)glyph.rect.width : 7;
            var img = HudRect.MakeImage("Glyph", frame.rectTransform, glyph, (ButtonTexels - g) / 2,
                                        (ButtonTexels - g) / 2, g, g);
            img.color = _theme.textDim;
            var relay = frame.gameObject.AddComponent<InventoryPointerRelay>();
            relay.Click = ev => { if (ev.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) onClick(); };
            relay.Enter = _ =>
            {
                img.color = hoverTint;
                ShowTextCard(title(), body(), hoverTint, frame.rectTransform);
            };
            relay.Exit = _ =>
            {
                img.color = _theme.textDim;
                HideCard();
            };
            return img;
        }

        // ── Header: name, level, experience, the four numbers that matter ───

        private void BuildHeader()
        {
            var s = _style;
            int pad = s.paddingTexels;
            int inner = _widthTexels - pad * 2;
            int y = _headerY;

            _medallion = new HudMedallion(_body, _art, _widthTexels - pad - MedallionTexels, y + 11, MedallionTexels, _additive);
            _medallion.SetColour(_theme.text);

            var nameRt = HudRect.Make("Name", _body, pad, y + 18, inner - MedallionTexels - 4, 10);
            _nameText = nameRt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_nameText.font == null) _nameText.font = TMP_Settings.defaultFontAsset;
            _nameText.fontSize = 8.5f;
            _nameText.fontStyle = FontStyles.Bold;
            _nameText.alignment = TextAlignmentOptions.BottomLeft;
            _nameText.enableWordWrapping = false;
            _nameText.overflowMode = TextOverflowModes.Ellipsis;
            _nameText.raycastTarget = false;
            _nameText.color = _theme.text;

            _xpBar = new HudBar(_body, "XpBar", _art, pad, y + 12, inner - MedallionTexels - 4, 5,
                                HudFontFace.Small, withLabel: false, notchSegments: 10, _additive);
            var gs = _gridStyle;
            _xpBar.SetColours(gs.xp, gs.xp, -1f, gs.xpChip, gs.xpChip, gs.xp);
            var xpNotch = gs.notch;
            xpNotch.a *= 0.6f;
            _xpBar.SetNotchColour(xpNotch);
            var xpHit = _xpBar.Root.gameObject.AddComponent<Image>();
            xpHit.color = Color.clear;
            var xpRelay = _xpBar.Root.gameObject.AddComponent<InventoryPointerRelay>();
            xpRelay.Enter = _ => ShowTextCard(XpTitle(), XpBody(), gs.xp, _xpBar.Root);
            xpRelay.Exit = _ => HideCard();

            // Four numbers, each behind the glyph that says what it is. Shape carries the meaning,
            // the number the amount; nothing here is a word.
            Sprite[] icons = { _inv.Sword, _inv.Shield, _art.Heart, _art.Drop };
            Color[] tints = { Color.Lerp(_theme.text, _theme.gold, 0.35f), _theme.textDim,
                              WorldBarStyle.Active != null ? WorldBarStyle.Active.HealthFor(WorldBarRank.Player) : _theme.success,
                              WorldBarStyle.Active != null ? WorldBarStyle.Active.mana : _theme.info };
            int groupW = inner / 4;
            for (int i = 0; i < 4; i++)
            {
                int gx = pad + i * groupW;
                var icon = icons[i];
                int iw = icon != null ? (int)icon.rect.width : 9, ih = icon != null ? (int)icon.rect.height : 9;
                _statIcon[i] = HudRect.MakeImage("StatIcon" + i, _body, icon, gx, y + (9 - ih) / 2, iw, ih);
                _statIcon[i].color = tints[i];
                _statText[i] = HudPixelText.Create(_body, "Stat" + i, _art, HudFontFace.Large, HudTextAlign.Left,
                                                   gx + iw + 2, y, groupW - iw - 2, 9);
                _statText[i].SetColour(_theme.text);
                int k = i;
                var hitRt = HudRect.Make("StatHit" + i, _body, gx, y, groupW - 1, 9);
                _statHit[i] = hitRt;
                var hit = hitRt.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                var relay = hitRt.gameObject.AddComponent<InventoryPointerRelay>();
                relay.Enter = _ => ShowTextCard(StatCatalog.DisplayName(_statKinds[k]), StatCatalog.Describe(_statKinds[k]),
                                                tints[k], hitRt);
                relay.Exit = _ => HideCard();
            }
        }

        private string XpTitle() => "Nivel " + (_playerXp != null ? _playerXp.Level : 0);

        private string XpBody()
        {
            if (_playerXp == null) return "";
            if (_playerXp.IsAtLevelCap) return "Nivel máximo";
            int need = Mathf.Max(1, _playerXp.XpForNextLevel - _playerXp.XpRequiredForLevel(_playerXp.Level));
            return _playerXp.XpInCurrentLevel + " / " + need + " de experiencia";
        }

        // ── Paper doll ──────────────────────────────────────────────────────

        private void BuildDoll()
        {
            var s = _style;
            int pad = s.paddingTexels;
            int slot = s.slotTexels;
            int dollH = DollHeight;
            int frameX = pad + slot + SectionGap;
            int frameW = _widthTexels - 2 * frameX;

            _doll = HudRect.Make("Doll", _body, frameX, _dollY, frameW, dollH);
            var backRt = HudRect.Make("Backdrop", _doll, 3, 3, frameW - 6, dollH - 6);
            _backdrop = backRt.gameObject.AddComponent<RawImage>();
            _backdrop.raycastTarget = false;
            _backdropTex = HudArt.BakeBackdrop(frameW - 6, dollH - 6, _gridStyle.backdropCentre, _gridStyle.backdropEdge);
            _backdrop.texture = _backdropTex;
            var figRt = HudRect.Make("Figure", _doll, 4, 4, frameW - 8, dollH - 8);
            _figure = figRt.gameObject.AddComponent<RawImage>();
            _figure.raycastTarget = false;
            _figure.enabled = false;
            HudRect.MakeImage("Frame", _doll, _art.PortraitFrame, 0, 0, frameW, dollH, Image.Type.Sliced);

            int equipBase = _playerInventory != null ? _playerInventory.Capacity : Inventory.DefaultBagCapacity;
            _equipViews = new InventorySlotView[EquipmentLayout.Count];
            for (int i = 0; i < EquipmentLayout.Count; i++)
            {
                int row = i / 2, col = i % 2;
                int x = col == 0 ? pad : _widthTexels - pad - slot;
                int y = _dollY + dollH - slot - row * (slot + s.slotGapTexels);
                var v = new InventorySlotView(_body, _art, _inv, equipBase + i, true, EquipmentLayout.KindAt(i),
                                              x, y, slot, s.iconInsetTexels, _additive);
                v.SetAlphas(s.filteredAlpha, s.dragSourceAlpha);
                v.SetSilhouetteColour(new Color(_theme.textDim.r, _theme.textDim.g, _theme.textDim.b, s.silhouetteAlpha));
                v.Root.gameObject.AddComponent<InventorySlotDragHandler>().Bind(this, v.UnifiedIndex);
                _equipViews[i] = v;
            }
        }

        // ── Tabs ────────────────────────────────────────────────────────────

        private void BuildTabs()
        {
            var s = _style;
            int pad = s.paddingTexels;
            int inner = _widthTexels - pad * 2;
            int count = TabCategories.Length + 1;          // + the sort button
            int gap = 2;
            int w = (inner - gap * (count - 1)) / count;
            int left = pad + (inner - (w * count + gap * (count - 1))) / 2;

            Sprite[] glyphs = { _inv.TabAll, _inv.TabEquipment, _inv.TabConsumable, _inv.TabMaterial, _inv.TabQuest, _inv.TabOther };
            _tabFrame = new Image[TabCategories.Length];
            _tabGlyph = new Image[TabCategories.Length];
            for (int i = 0; i < TabCategories.Length; i++)
            {
                int k = i;
                int x = left + i * (w + gap);
                var frame = HudRect.MakeImage("Tab" + i, _body, _art.Slot, x, _tabsY, w, s.tabTexels, Image.Type.Sliced);
                frame.raycastTarget = true;
                var g = glyphs[i];
                int gw = g != null ? (int)g.rect.width : 9, gh = g != null ? (int)g.rect.height : 9;
                _tabGlyph[i] = HudRect.MakeImage("Glyph", frame.rectTransform, g, (w - gw) / 2, (s.tabTexels - gh) / 2, gw, gh);
                var ring = HudRect.MakeImage("Active", frame.rectTransform, _art.SlotGlow, 0, 0, w, s.tabTexels, Image.Type.Sliced);
                ring.color = _theme.gold;
                ring.enabled = false;
                _tabFrame[i] = ring;
                var relay = frame.gameObject.AddComponent<InventoryPointerRelay>();
                relay.Click = ev => { if (ev.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) SetTab(k); };
                relay.Enter = _ =>
                {
                    if (k != _activeTab) _tabGlyph[k].color = _theme.text;
                    ShowTextCard(TabNames[k], TabCountLine(k), _theme.gold, frame.rectTransform);
                };
                relay.Exit = _ => { ApplyTabs(); HideCard(); };
            }

            int sx = left + TabCategories.Length * (w + gap);
            var sort = HudRect.MakeImage("Sort", _body, _art.Slot, sx, _tabsY, w, s.tabTexels, Image.Type.Sliced);
            sort.raycastTarget = true;
            int sw = _inv.Sort != null ? (int)_inv.Sort.rect.width : 9;
            _sortGlyph = HudRect.MakeImage("Glyph", sort.rectTransform, _inv.Sort, (w - sw) / 2, (s.tabTexels - sw) / 2, sw, sw);
            _sortGlyph.color = _theme.textDim;
            var sortRelay = sort.gameObject.AddComponent<InventoryPointerRelay>();
            sortRelay.Click = ev => { if (ev.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) SortBag(); };
            sortRelay.Enter = _ =>
            {
                _sortGlyph.color = _theme.gold;
                ShowTextCard("Ordenar", "Equipo, consumibles, materiales, misión; de más a menos raro. Junta las pilas.",
                             _theme.gold, sort.rectTransform);
            };
            sortRelay.Exit = _ => { _sortGlyph.color = _theme.textDim; HideCard(); };
            _activeTab = 0;
            ApplyTabs();
        }

        private string TabCountLine(int tab)
        {
            if (_playerInventory == null) return "";
            int n = 0;
            var slots = _playerInventory.Slots;
            for (int i = 0; i < slots.Count; i++)
                if (!slots[i].IsEmpty && MatchesTab(slots[i].Item, tab)) n++;
            return n == 1 ? "1 objeto en la mochila" : n + " objetos en la mochila";
        }

        // ── Bag grid ────────────────────────────────────────────────────────

        private void BuildGrid(int capacity)
        {
            var s = _style;
            _grid = HudRect.Make("Grid", _body, s.paddingTexels, _gridY, s.GridWidthTexels, s.GridHeightTexels(_bagRows));
            _bagViews = new InventorySlotView[capacity];
            for (int i = 0; i < capacity; i++)
            {
                int col = i % s.bagColumns, row = i / s.bagColumns;
                int x = col * (s.slotTexels + s.slotGapTexels);
                int y = s.GridHeightTexels(_bagRows) - s.slotTexels - row * (s.slotTexels + s.slotGapTexels);
                var v = new InventorySlotView(_grid, _art, _inv, i, false, default, x, y, s.slotTexels,
                                              s.iconInsetTexels, _additive);
                v.SetAlphas(s.filteredAlpha, s.dragSourceAlpha);
                v.Root.gameObject.AddComponent<InventorySlotDragHandler>().Bind(this, i);
                _bagViews[i] = v;
            }
        }

        /// <summary>
        /// The bag grid is built for the capacity it saw at build time; a different capacity (a
        /// save with a bigger bag) rebuilds the window rather than drawing slots it cannot reach.
        /// </summary>
        private void EnsureBagViews()
        {
            if (_playerInventory == null || _bagViews.Length == _playerInventory.Capacity) return;
            RebuildUI();
        }

        private void RebuildUI()
        {
            bool visible = _visible;
            ReleaseArt();
            if (_canvas != null) HudLifetime.Release(_canvas.gameObject);
            _built = false;
            BuildUI();
            SetVisible(visible, animate: false);
            RefreshAll(announce: false);
        }

        // ── Footer ──────────────────────────────────────────────────────────

        private void BuildFooter()
        {
            var s = _style;
            int pad = s.paddingTexels;
            int y = _footerY;
            int h = s.footerTexels;

            int bw = _inv.Bag != null ? (int)_inv.Bag.rect.width : 9;
            _bagGlyph = HudRect.MakeImage("BagIcon", _body, _inv.Bag, pad, y + (h - bw) / 2, bw, bw);
            _bagGlyph.color = _theme.textDim;
            _capacityText = HudPixelText.Create(_body, "Capacity", _art, HudFontFace.Large, HudTextAlign.Left,
                                                pad + bw + 2, y, 36, h);
            var capHit = HudRect.Make("CapacityHit", _body, pad, y, bw + 38, h);
            _capacityHit = capHit;
            capHit.gameObject.AddComponent<Image>().color = Color.clear;
            var capRelay = capHit.gameObject.AddComponent<InventoryPointerRelay>();
            capRelay.Enter = _ => ShowTextCard("Mochila", CapacityBody(), _theme.textDim, capHit);
            capRelay.Exit = _ => HideCard();

            int wx = pad + bw + 40;
            int ww = _inv.Weight != null ? (int)_inv.Weight.rect.width : 9;
            _weightGlyph = HudRect.MakeImage("WeightIcon", _body, _inv.Weight, wx, y + (h - ww) / 2, ww, ww);
            _weightGlyph.color = _theme.textDim;
            _weightText = HudPixelText.Create(_body, "Weight", _art, HudFontFace.Small, HudTextAlign.Left,
                                              wx + ww + 2, y, 26, h);
            _weightText.SetColour(_theme.textDim);
            var wHit = HudRect.Make("WeightHit", _body, wx, y, ww + 26, h);
            _weightHit = wHit;
            wHit.gameObject.AddComponent<Image>().color = Color.clear;
            var wRelay = wHit.gameObject.AddComponent<InventoryPointerRelay>();
            wRelay.Enter = _ => ShowTextCard("Peso", "Lo que llevas encima: mochila y equipo.", _theme.textDim, wHit);
            wRelay.Exit = _ => HideCard();

            int cw = _inv.Coin != null ? (int)_inv.Coin.rect.width : 7;
            _goldText = HudPixelText.Create(_body, "Gold", _art, HudFontFace.Large, HudTextAlign.Right,
                                            _widthTexels - pad - 40, y, 40, h);
            _goldText.SetColour(Color.Lerp(_theme.gold, Color.white, 0.25f));
            _coin = HudRect.MakeImage("Coin", _body, _inv.Coin, _widthTexels - pad - 40 - cw, y + (h - cw) / 2, cw, cw);
        }

        private string CapacityBody()
        {
            if (_playerInventory == null) return "";
            int free = _playerInventory.Capacity - _playerInventory.UsedSlots;
            return free == 0 ? "Llena: lo que recojas se quedará en el suelo."
                 : free == 1 ? "Queda 1 hueco libre."
                 : "Quedan " + free + " huecos libres.";
        }

        // ── Drag ghost ──────────────────────────────────────────────────────

        private void BuildGhost()
        {
            int inner = _style.slotTexels - _style.iconInsetTexels * 2;
            _ghostRoot = HudRect.Make("DragGhost", _pixels, 0, 0, inner, inner);
            var shadow = HudRect.MakeImage("Shadow", _ghostRoot, _art.White, 1, -2, inner, inner);
            shadow.color = new Color(0f, 0f, 0f, 0.4f);
            var gRt = HudRect.Make("Icon", _ghostRoot, 0, 0, inner, inner);
            _ghost = gRt.gameObject.AddComponent<RawImage>();
            _ghost.raycastTarget = false;
            _ghostFallback = HudRect.MakeImage("IconRaw", _ghostRoot, null, 0, 0, inner, inner);
            _ghostFallback.preserveAspect = true;
            // The shadow is a square, which is wrong for most icons; it reads as the item being
            // lifted off the stone only while it is faint and offset, so it stays faint.
            shadow.enabled = false;
            // How many are being carried: a Shift-drag takes half a stack, and a split the player
            // cannot see the size of is a split they have to undo to check.
            _ghostCount = HudPixelText.Create(_ghostRoot, "Count", _art, HudFontFace.Small, HudTextAlign.Right,
                                              0, -1, inner + 3, 5);
            _ghostCount.SetColour(_theme.gold);
            _ghostRoot.gameObject.SetActive(false);
        }

        // ── Lifetime ────────────────────────────────────────────────────────

        private void ReleaseArt()
        {
            HudLifetime.Release(_stoneTex);
            HudLifetime.Release(_stoneCollapsedTex);
            HudLifetime.Release(_backdropTex);
            HudLifetime.Release(_figureTex);
            HudLifetime.Release(_additive);
            _stoneTex = _stoneCollapsedTex = _backdropTex = _figureTex = null;
            _additive = null;
        }
    }
}
