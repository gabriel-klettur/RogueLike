using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.Frontend;
using Valkur.UI.MainMenu;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// One launcher entry drawn the way a talent is: a square socket with an icon in it and the
    /// name under it — the loading bar's bevel at 46 px, the icon from the frontend kit.
    ///
    /// <para><b>State lives on the SOCKET, identity on the ICON.</b> Hovered and keyboard-selected
    /// raise the bevel's glow and put its corner brackets out; an editor that is open or an overlay
    /// that is on lights the gem in the socket's corner. None of that touches the icon's colour, so
    /// all 31 entries say "active" the same way.</para>
    ///
    /// <para><b>Particles answer events only</b>: a small themed puff when the pointer or the
    /// keyboard ARRIVES on the tile, a bigger one when it is pressed, and one when its state turns
    /// on. A launcher full of tiles at rest emits nothing.</para>
    ///
    /// <para>Its own <see cref="Tick"/>, driven by the launcher with unscaled time: the launcher
    /// freezes gameplay, and <c>Time.deltaTime</c> can be scaled to nothing while it is up.</para>
    /// </summary>
    public sealed class GeneralEditorTile : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public const float SocketSize = 46f;
        private const float IconInset = 7f;
        private const float GemSize = 14f;
        private const int HoverMotes = 6;
        private const int PressMotes = 18;

        private BevelFrameGraphic _socket;
        private FrontendGlyphGraphic _icon;
        private FrontendGemGraphic _gem;
        private TextMeshProUGUI _label;
        private MenuFxLayer _motes;
        private Color _labelRest;
        private Color _labelLit;

        private bool _hovered;
        private bool _selected;
        private bool _active;
        private float _glow;
        private float _flash;
        private float _time;

        public FrontendGlyph Glyph => _icon != null ? _icon.Glyph : FrontendGlyph.None;
        public BevelFrameGraphic Socket => _socket;
        public FrontendGlyphGraphic Icon => _icon;
        public bool Hovered => _hovered;
        public bool Selected => _selected;
        public bool ShowsActive => _active;

        /// <summary>Builds a tile under a grid cell and returns its Button.</summary>
        public static GeneralEditorTile Build(Transform parent, string label, FrontendGlyph glyph, MenuFxLayer motes,
                                              Action onClick, out Button button)
        {
            var go = new GameObject($"Act_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            // The one raycast target: transparent, the whole cell. An alpha-0 Image still catches
            // the pointer, and every drawn part below opts out so a hover never flickers between them.
            var hit = go.AddComponent<Image>();
            hit.color = Color.clear;

            button = go.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;

            var tile = go.AddComponent<GeneralEditorTile>();
            tile._motes = motes;
            tile._labelRest = UITheme.TEXT_SECONDARY;
            tile._labelLit = MenuStyle.Active != null ? MenuStyle.Active.Gold : UITheme.ACCENT;

            var socketRt = new GameObject("Socket", typeof(RectTransform)).GetComponent<RectTransform>();
            socketRt.SetParent(go.transform, false);
            socketRt.anchorMin = socketRt.anchorMax = new Vector2(0.5f, 1f);
            socketRt.pivot = new Vector2(0.5f, 1f);
            socketRt.anchoredPosition = new Vector2(0f, -3f);
            socketRt.sizeDelta = new Vector2(SocketSize, SocketSize);

            tile._socket = BevelFrameGraphic.Create(socketRt, "Frame");
            tile._socket.Thickness = 3f;
            tile._socket.ShadowScale = 0.5f;
            tile._socket.Brackets = false;
            tile._socket.Tint = tile._labelLit;

            tile._icon = FrontendGlyphGraphic.Create(socketRt, "Icon", glyph);
            var iconRt = tile._icon.rectTransform;
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(IconInset, IconInset);
            iconRt.offsetMax = new Vector2(-IconInset, -IconInset);

            tile._gem = FrontendGemGraphic.Create(socketRt, "ActiveGem", FrontendIconTheme.AccentOf(glyph));
            var gemRt = tile._gem.rectTransform;
            gemRt.anchorMin = gemRt.anchorMax = new Vector2(1f, 1f);
            gemRt.pivot = new Vector2(0.5f, 0.5f);
            gemRt.anchoredPosition = new Vector2(-2f, -2f);
            gemRt.sizeDelta = new Vector2(GemSize, GemSize);
            tile._gem.Lit = 0f;

            var labelRt = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
            labelRt.SetParent(go.transform, false);
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 0f);
            labelRt.pivot = new Vector2(0.5f, 0f);
            labelRt.anchoredPosition = new Vector2(0f, 1f);
            labelRt.sizeDelta = new Vector2(0f, 22f);
            tile._label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tile._label.text = label;
            tile._label.fontSize = 9f;
            tile._label.fontStyle = FontStyles.Bold;
            tile._label.alignment = TextAlignmentOptions.Top;
            tile._label.enableWordWrapping = true;
            tile._label.overflowMode = TextOverflowModes.Ellipsis;
            tile._label.color = tile._labelRest;
            tile._label.raycastTarget = false;

            if (onClick != null) button.onClick.AddListener(() => { tile.Pressed(); onClick(); });
            tile.Push();
            return tile;
        }

        /// <summary>
        /// Whether the thing this tile opens is on. Turning ON is an event and answers with the
        /// icon's own particles; staying on is a lit gem and nothing else.
        /// </summary>
        public void SetActiveState(bool on)
        {
            if (on == _active) return;
            _active = on;
            if (on) Emit(HoverMotes + 4);
            Push();
        }

        public void OnPointerEnter(PointerEventData eventData) { if (!_hovered) { _hovered = true; Emit(HoverMotes); } }
        public void OnPointerExit(PointerEventData eventData) => _hovered = false;
        public void OnSelect(BaseEventData eventData) { if (!_selected) { _selected = true; if (!_hovered) Emit(HoverMotes); } }
        public void OnDeselect(BaseEventData eventData) => _selected = false;

        /// <summary>The tile was pressed: a flash on the icon and the bigger burst.</summary>
        public void Pressed()
        {
            _flash = 1f;
            Emit(PressMotes);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            _time += Mathf.Min(dt, 0.1f);
            float want = _hovered ? 1f : _selected ? 0.7f : 0f;
            _glow = Mathf.MoveTowards(_glow, want, dt * (want > _glow ? 8f : 3f));
            _flash = Mathf.Max(0f, _flash - dt / 0.35f);
            Push();
        }

        private void Push()
        {
            if (_socket == null) return;
            bool lit = _hovered || _selected;
            // The selected tile breathes; a hovered one holds steady. Both are motion, not emission.
            float breathe = _selected && !_hovered ? 0.12f * Mathf.Sin(_time * 3.1f) : 0f;
            _socket.Glow = Mathf.Clamp01(_glow * 0.85f + breathe + (_active ? 0.25f : 0f) + _flash * 0.5f);
            _socket.Brackets = lit;
            _icon.Glow = Mathf.Clamp01(_glow * 0.35f + _flash * 0.6f);
            // The gem EXISTS only while the thing is on: 31 dim stones at rest read as 31 states.
            _gem.enabled = _active;
            _gem.Lit = _active ? 1f : 0f;
            _label.color = lit || _active ? _labelLit : _labelRest;
        }

        private void Emit(int count)
        {
            if (_motes == null || _socket == null) return;
            var rt = _socket.rectTransform;
            var glyph = Glyph;
            FrontendIconMotes.Burst(_motes, rt, rt.rect.center, SocketSize * 0.45f,
                                    FrontendIconTheme.AccentOf(glyph), FrontendIconTheme.MotesOf(glyph), count);
        }
    }
}
