using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.MainMenu.Kit
{
    /// <summary>
    /// One row of any menu list: a label, an optional value, an optional content area for a
    /// slider or a pair of arrows, and exactly ONE object that catches the pointer.
    ///
    /// <para><b>The selection highlight is NOT here.</b> It belongs to <see cref="MenuList"/>,
    /// which owns a single pill that SLIDES between rows — the shipped menus gave every row its
    /// own pill and switched them on and off, which is why the highlight teleported and why
    /// there was nothing to animate.</para>
    ///
    /// <para><b>Hover and selection are different states and look different.</b> Hovering is a
    /// faint outline that says "this is what you would pick"; selection is the filled pill.
    /// Painting them the same makes a mouse look like it has already chosen.</para>
    /// </summary>
    public sealed class MenuRow
    {
        /// <summary>
        /// Where the label stops and where everything else starts, as fractions of the row.
        ///
        /// <para><b>They do not touch, and that gap is the point.</b> The first cut had the label
        /// running to 0.52 while the content began at 0.50 — a two per cent overlap, which on a
        /// 560 px panel is eleven pixels of a slider track passing under the tail of its own
        /// label. uGUI performs no layout in Edit Mode, so no structural test can see it; the
        /// only defence is that the numbers are declared once, here, where they can be compared.
        /// </para>
        /// </summary>
        public const float LabelColumnEnd = 0.50f;

        public const float ContentColumnStart = 0.54f;

        public const float ValueColumnStart = 0.56f;

        /// <summary>
        /// Where the value goes on a row that ALSO carries a widget. A slider reaching
        /// <see cref="ContentColumnStart"/> + 0.66 of the remainder ends at 0.84 of the row, so a
        /// value at 0.56 would sit on top of the track it describes. The two layouts are named
        /// rather than sharing one constant with an offset added at the call site.
        /// </summary>
        public const float WideValueColumnStart = 0.86f;


        public readonly RectTransform Root;
        public readonly TextMeshProUGUI Label;
        public readonly RectTransform Content;
        public readonly GameObject HitTarget;

        private readonly MenuStyle _style;
        private readonly Graphic _hover;
        private TextMeshProUGUI _value;
        private bool _selected;
        private bool _hovered;
        private bool _interactable = true;

        /// <summary>The value column, created the first time something asks for it.</summary>
        public TextMeshProUGUI Value
        {
            get
            {
                if (_value != null) return _value;
                var rt = MenuUIKit.Rect("Value", Root);
                rt.anchorMin = new Vector2(ValueColumnStart, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = new Vector2(0f, 0f);
                rt.offsetMax = new Vector2(-16f, 0f);
                _value = MenuTypography.Label(rt.gameObject, _style, string.Empty, _style.rowFontSize - 2f,
                                              _style.Gold, TextAlignmentOptions.Right);
                ApplyColours();
                return _value;
            }
        }

        /// <summary>A row the player cannot choose: greyed, still listed, still explained.</summary>
        public bool Interactable
        {
            get => _interactable;
            set
            {
                if (_interactable == value) return;
                _interactable = value;
                ApplyColours();
            }
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                ApplyColours();
            }
        }

        public bool Hovered
        {
            get => _hovered;
            set
            {
                if (_hovered == value) return;
                _hovered = value;
                if (_hover != null) _hover.enabled = _hovered && !_selected;
            }
        }

        public MenuRow(Transform parent, MenuArt art, MenuStyle style, string label, int index,
                       float y, float height, float indent)
        {
            _style = style;

            Root = MenuUIKit.Rect($"Row_{index}", parent);
            Root.anchorMin = new Vector2(0f, 1f);
            Root.anchorMax = new Vector2(1f, 1f);
            Root.pivot = new Vector2(0.5f, 1f);
            Root.anchoredPosition = new Vector2(0f, y);
            Root.sizeDelta = new Vector2(0f, height);

            // The bar's groove, empty: an outline and a thin gold border. The selection is the
            // same groove FILLED, so the two states are the same object at two levels.
            var hover = Valkur.UI.Frontend.FrontendHoverGraphic.Create(Root, "Hover");
            hover.Tint = style.Gold;
            _hover = hover;
            var hoverRt = hover.rectTransform;
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = new Vector2(2f, 2f);
            hoverRt.offsetMax = new Vector2(-2f, -2f);
            _hover.enabled = false;

            var labelRt = MenuUIKit.Rect("Label", Root);
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(LabelColumnEnd, 1f);
            labelRt.offsetMin = new Vector2(indent, 0f);
            labelRt.offsetMax = Vector2.zero;
            Label = MenuTypography.Label(labelRt.gameObject, style, label, style.rowFontSize,
                                         style.TextPrimary);

            Content = MenuUIKit.Rect("Content", Root);
            Content.anchorMin = new Vector2(ContentColumnStart, 0f);
            Content.anchorMax = new Vector2(1f, 1f);
            Content.offsetMin = new Vector2(0f, 0f);
            Content.offsetMax = new Vector2(-16f, 0f);

            // ONE hit target, last so it is on top of everything in the row, and transparent so
            // it changes nothing about how the row looks.
            var hit = MenuUIKit.Sprite("Hit", Root, null, new Color(0f, 0f, 0f, 0f),
                                       Image.Type.Simple, raycast: true);
            var hitRt = (RectTransform)hit.transform;
            hitRt.anchorMin = Vector2.zero;
            hitRt.anchorMax = Vector2.one;
            hitRt.offsetMin = Vector2.zero;
            hitRt.offsetMax = Vector2.zero;
            HitTarget = hit.gameObject;

            ApplyColours();
        }

        /// <summary>
        /// Re-lays the row for a widget PLUS a value: the number moves right, out of the
        /// widget's way. Called by any panel whose rows carry a slider.
        /// </summary>
        public void UseWideContentColumns()
        {
            if (_value != null)
            {
                var rt = (RectTransform)_value.transform;
                rt.anchorMin = new Vector2(WideValueColumnStart, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = new Vector2(-4f, 0f);
            }
        }

        private Valkur.UI.Frontend.FrontendGemGraphic _toggleGem;

        /// <summary>
        /// Marks the row as an on/off switch: a gem at the head of the value column that is LIT
        /// when on and dim stone when off — the loading bar's end gem, which lights on "ready".
        /// The word stays beside it, so the state is never carried by the colour alone.
        /// </summary>
        public void SetToggle(bool on)
        {
            if (_toggleGem == null)
            {
                _toggleGem = Valkur.UI.Frontend.FrontendGemGraphic.Create(Root, "ToggleGem", _style.Gold);
                var rt = _toggleGem.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(ToggleGemColumn, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(26f, 26f);
                // Under the hit target, which must stay the last child.
                _toggleGem.transform.SetSiblingIndex(HitTarget.transform.GetSiblingIndex());
            }
            _toggleGem.Lit = on ? 1f : 0f;
        }

        /// <summary>Where a toggle's gem sits: past the label column, clear of a right-aligned value.</summary>
        public const float ToggleGemColumn = 0.60f;

        /// <summary>Wires the pointer. The list does this so a row never has to know its index.</summary>
        public void Bind(System.Action onClick, System.Action onEnter, System.Action onExit,
                         System.Action onRightClick = null)
        {
            var btn = HitTarget.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            if (onEnter != null) MenuUIKit.OnHover(HitTarget, _ => onEnter(), EventTriggerType.PointerEnter);
            if (onExit != null) MenuUIKit.OnHover(HitTarget, _ => onExit(), EventTriggerType.PointerExit);
            if (onRightClick != null)
            {
                MenuUIKit.OnHover(HitTarget, data =>
                {
                    if (data is PointerEventData p && p.button == PointerEventData.InputButton.Right)
                        onRightClick();
                }, EventTriggerType.PointerClick);
            }
        }

        private void ApplyColours()
        {
            if (!_interactable)
            {
                Label.color = _style.TextMuted;
                if (_value != null) _value.color = _style.TextMuted;
                return;
            }

            // On the filled pill the text goes DARK. This is the whole of the 4.15:1 fix: gold
            // on gold measured worse than the unselected row, so the row being looked at was the
            // hardest one to read.
            Label.color = _selected ? _style.textOnSelection : _style.TextPrimary;
            if (_value != null)
                _value.color = _selected ? _style.textOnSelection : _style.Gold;
        }
    }
}
