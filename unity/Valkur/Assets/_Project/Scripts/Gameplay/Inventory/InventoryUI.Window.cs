using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.UI;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// The window as a window: whole-pixel scale, where it opens, dragging it, collapsing it,
    /// remembering both, the open/close motion, and where the item card goes.
    /// </summary>
    public partial class InventoryUI
    {
        private const string PrefX = "valkur.inventory.fx";
        private const string PrefY = "valkur.inventory.fy";
        private const string PrefCollapsed = "valkur.inventory.collapsed";

        private int _pixelScale = 1;
        private int _screenW = -1, _screenH = -1;
        private float _rootScale = -1f;

        private bool _collapsed;
        private bool _hasSavedPlacement;
        private Vector2 _savedFraction;     // top-left corner, as a fraction of the screen
        private Vector2 _topLeft;           // top-left corner, in screen pixels
        private bool _draggingWindow;
        private Vector2 _dragOffset;

        private float _openT;
        private float _hoverT;
        private int _cardSlot = -1;

        /// <summary>Screen pixels per texel right now. For the tests and the probe.</summary>
        public int PixelScale => _pixelScale;

        /// <summary>Window size in texels (the expanded size).</summary>
        public Vector2Int SizeTexels => new Vector2Int(_widthTexels, _heightTexels);

        /// <summary>The window's outer rect in its canvas.</summary>
        public RectTransform Panel => _panelRect;

        /// <summary>True while only the title bar shows.</summary>
        public bool IsCollapsed => _collapsed;

        public InventoryItemCard Card => _card;
        internal InventoryConfirm Confirm => _confirm;
        internal InventorySlotView BagView(int i) => i >= 0 && i < _bagViews.Length ? _bagViews[i] : null;
        internal InventorySlotView EquipView(int i) => i >= 0 && i < _equipViews.Length ? _equipViews[i] : null;
        internal int BagViewCount => _bagViews.Length;
        internal Valkur.UI.HUD.HudMoteLayer Motes => _motes;

        // ── Pixel grid and placement ───────────────────────────────────────

        /// <summary>
        /// Re-derives the whole-pixel scale and the window's footprint when the screen or the
        /// canvas scale changed, and snaps its corner onto a whole screen pixel, so every texel
        /// inside is exactly <see cref="PixelScale"/> pixels — the player panel's rule.
        /// </summary>
        public void Refit(bool force = false)
        {
            if (!_built || _canvas == null) return;
            int sw = Screen.width, sh = Screen.height;
            float root = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            bool changed = force || sw != _screenW || sh != _screenH || !Mathf.Approximately(root, _rootScale);
            if (!changed && !_draggingWindow) return;

            if (changed)
            {
                int oldScale = _pixelScale;
                _screenW = sw;
                _screenH = sh;
                _rootScale = root;
                _pixelScale = _gridStyle.HudPixelScaleFor(sw, sh);
                if (oldScale != _pixelScale)
                {
                    for (int i = 0; i < _bagViews.Length; i++) _bagViews[i].Rescale(_pixelScale);
                    for (int i = 0; i < _equipViews.Length; i++) _equipViews[i].Rescale(_pixelScale);
                }
                if (!_draggingWindow) _topLeft = _hasSavedPlacement ? FromFraction(_savedFraction) : DefaultTopLeft();
            }

            float perTexel = _pixelScale / root;
            _pixels.localScale = new Vector3(perTexel, perTexel, 1f);
            int h = _collapsed ? _collapsedHeight : _heightTexels;
            _panelRect.sizeDelta = new Vector2(_widthTexels * perTexel, h * perTexel);

            var tl = ClampTopLeft(_topLeft, h);
            tl = new Vector2(Mathf.Round(tl.x), Mathf.Round(tl.y));
            _panelRect.anchoredPosition = tl / root;
        }

        /// <summary>
        /// Opens to the LEFT of the instruments docked on the right (<see cref="HudLayout.GameWindowRightInset"/>),
        /// never over them: the old window sat at (-16, -16) and hid the minimap completely, at
        /// order 200 against 105. A window may be dragged over an instrument; it must not open
        /// over one.
        /// </summary>
        internal Vector2 DefaultTopLeft()
        {
            float root = _rootScale > 0f ? _rootScale : 1f;
            float right = _screenW - HudLayout.GameWindowRightInset * root;
            float widthPx = _widthTexels * _pixelScale;
            return new Vector2(right - widthPx, _screenH - HudLayout.ScreenMargin * root);
        }

        private Vector2 ClampTopLeft(Vector2 tl, int heightTexels)
        {
            float w = _widthTexels * _pixelScale, h = heightTexels * _pixelScale;
            // Keep at least the title bar on screen, so the window can always be dragged back.
            float titleH = (_style.titleBarTexels + 4) * _pixelScale;
            tl.x = Mathf.Clamp(tl.x, 0f, Mathf.Max(0f, _screenW - w));
            tl.y = Mathf.Clamp(tl.y, Mathf.Min(h, _screenH), _screenH);
            if (tl.y - titleH < 0f) tl.y = titleH;
            return tl;
        }

        private Vector2 FromFraction(Vector2 f) => new Vector2(f.x * _screenW, f.y * _screenH);

        private void LoadPlacement()
        {
            try
            {
                _hasSavedPlacement = PlayerPrefs.HasKey(PrefX) && PlayerPrefs.HasKey(PrefY);
                if (_hasSavedPlacement)
                    _savedFraction = new Vector2(PlayerPrefs.GetFloat(PrefX), PlayerPrefs.GetFloat(PrefY));
                _collapsed = PlayerPrefs.GetInt(PrefCollapsed, 0) == 1;
            }
            catch (System.Exception) { _hasSavedPlacement = false; _collapsed = false; }
        }

        private void SavePlacement()
        {
            if (!Application.isPlaying || _screenW <= 0 || _screenH <= 0) return;
            _savedFraction = new Vector2(_topLeft.x / _screenW, _topLeft.y / _screenH);
            _hasSavedPlacement = true;
            PlayerPrefs.SetFloat(PrefX, _savedFraction.x);
            PlayerPrefs.SetFloat(PrefY, _savedFraction.y);
            PlayerPrefs.SetInt(PrefCollapsed, _collapsed ? 1 : 0);
        }

        /// <summary>Forgets the dragged position and puts the window back where it opens by default.</summary>
        public void ResetPlacement()
        {
            PlayerPrefs.DeleteKey(PrefX);
            PlayerPrefs.DeleteKey(PrefY);
            _hasSavedPlacement = false;
            _topLeft = DefaultTopLeft();
            Refit(force: true);
        }

        // ── Dragging the window by its title bar ───────────────────────────

        private void BeginWindowDrag(PointerEventData ev)
        {
            if (ev.button != PointerEventData.InputButton.Left) return;
            _draggingWindow = true;
            _dragOffset = ev.position - _topLeft;
            HideCard();
        }

        private void WindowDrag(PointerEventData ev)
        {
            if (!_draggingWindow) return;
            _topLeft = ev.position - _dragOffset;
            Refit();
        }

        // Written at the END of the gesture, never per frame: PlayerPrefs is a file write.
        private void EndWindowDrag(PointerEventData ev)
        {
            if (!_draggingWindow) return;
            _draggingWindow = false;
            _topLeft = ClampTopLeft(_topLeft, _collapsed ? _collapsedHeight : _heightTexels);
            SavePlacement();
            Refit(force: true);
        }

        // ── Collapse ───────────────────────────────────────────────────────

        public void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            if (_collapsed) { EndDragQuietly(); HideCard(); }
            ApplyCollapsed();
            SavePlacement();
            Refit(force: true);
        }

        /// <summary>
        /// Collapsing is a VIEW, not a resize: the body is hidden and the title bar moves to the
        /// top of a short stone, and expanding brings the whole window back as it was.
        /// </summary>
        private void ApplyCollapsed()
        {
            if (!_built) return;
            int h = _collapsed ? _collapsedHeight : _heightTexels;
            _body.gameObject.SetActive(!_collapsed);
            HudRect.Place(_pixels, 0, 0, _widthTexels, h);
            HudRect.Place(_content, 0, 0, _widthTexels, h);
            HudRect.Place(_stone.rectTransform, 0, 0, _widthTexels, h);
            _stone.texture = _collapsed ? _stoneCollapsedTex : _stoneTex;
            var tb = _titleBar;
            tb.anchoredPosition = new Vector2(0f, _collapsed ? (h - _style.titleBarTexels) / 2 : _titleY);
            if (_collapseGlyph != null) _collapseGlyph.sprite = _collapsed ? _inv.Expand : _inv.Collapse;
            // The pixel scale lives on _pixels; HudRect.Place resets it, so re-apply it.
            float root = _rootScale > 0f ? _rootScale : 1f;
            float perTexel = _pixelScale / root;
            _pixels.localScale = new Vector3(perTexel, perTexel, 1f);
        }

        // ── Open / close motion ────────────────────────────────────────────

        private void TickOpen(float dt)
        {
            float target = _visible ? 1f : 0f;
            if (Mathf.Approximately(_openT, target)) return;
            float seconds = _visible ? _style.openSeconds : _style.closeSeconds;
            _openT = seconds <= 0f ? target : Mathf.MoveTowards(_openT, target, dt / seconds);
            ApplyOpen();
        }

        private void ApplyOpen()
        {
            if (_panelGroup == null) return;
            float e = 1f - (1f - _openT) * (1f - _openT);
            _panelGroup.alpha = e;
            bool show = _openT > 0f;
            if (_panelRect.gameObject.activeSelf != show) _panelRect.gameObject.SetActive(show);
            ApplyContentOffset();
        }

        private void ApplyContentOffset()
        {
            if (_content == null) return;
            float e = 1f - (1f - _openT) * (1f - _openT);
            // Rises into place in whole texels, the way everything in the kit moves.
            float rise = -Mathf.Round(_style.openRiseTexels * (1f - e));
            _content.anchoredPosition = new Vector2(ShakeOffset(), rise);
        }

        // ── The item card ──────────────────────────────────────────────────

        private void TickHover(float dt)
        {
            if (_hoverSlot < 0 || IsDragging || _collapsed) return;
            _hoverT += dt;
            if (_cardSlot == _hoverSlot || _hoverT < _style.cardDelaySeconds) return;
            ShowCardFor(_hoverSlot);
        }

        private void ShowCardFor(int unified)
        {
            var view = ViewFor(unified);
            var slot = SlotAt(unified);
            if (view == null || slot.IsEmpty || _card == null) { HideCard(); return; }
            bool equipped = _playerInventory.IsEquipmentIndex(unified);
            int home = Valkur.Data.EquipmentLayout.IndexFor(slot.Item);
            var worn = home >= 0 ? _playerInventory.EquipmentSlots[home].Item : null;
            _card.Build(slot.Item, slot.Quantity, equipped, worn, _playerInventory.CharacterLevel, _theme);
            PlaceCardBeside(view.Root);
            _cardSlot = unified;
        }

        /// <summary>A card with a title and a line, for the window's controls.</summary>
        private void ShowTextCard(string title, string body, Color accent, RectTransform anchor)
        {
            if (_card == null || string.IsNullOrEmpty(title) || IsDragging) return;
            _card.BuildText(title, body, accent, _theme);
            PlaceCardBeside(anchor);
            _cardSlot = -2;
        }

        private void HideCard()
        {
            _card?.Hide();
            _cardSlot = -1;
        }

        /// <summary>
        /// Beside the window, never over it — on the side with room, at the height of the thing
        /// it describes, clamped inside the screen.
        /// </summary>
        private void PlaceCardBeside(RectTransform anchor)
        {
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            // Corners in the window's texel space.
            Vector2 top = _pixels.InverseTransformPoint(corners[1]);
            int cardW = _card.Width, cardH = _card.Height;
            float scale = Mathf.Max(1, _pixelScale);
            float panelLeftPx = _topLeft.x;
            int x = panelLeftPx >= (cardW + 4) * scale ? -cardW - 3 : _widthTexels + 3;
            int y = Mathf.RoundToInt(top.y) - cardH;

            int h = _collapsed ? _collapsedHeight : _heightTexels;
            float panelBottomPx = _topLeft.y - h * scale;
            int minY = Mathf.CeilToInt(-panelBottomPx / scale) + 2;
            int maxY = Mathf.FloorToInt((_screenH - panelBottomPx) / scale) - cardH - 2;
            y = Mathf.Clamp(y, minY, Mathf.Max(minY, maxY));
            _card.PlaceAt(x, y);
        }
    }
}
