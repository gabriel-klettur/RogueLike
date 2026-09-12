using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.MainMenu.Kit
{
    /// <summary>
    /// A list of <see cref="MenuRow"/>s with ONE selection highlight that slides between them.
    ///
    /// <para><b>The sliding pill is the point.</b> Every shipped menu gave each row its own pill
    /// and switched them on and off, so the highlight teleported and there was nothing that could
    /// be animated. One pill that moves costs a lerp and is the single cheapest thing that makes
    /// a keyboard menu feel built rather than assembled.</para>
    ///
    /// <para><b>It owns the selection, not the screen.</b> A screen sets <see cref="Index"/> or
    /// calls <see cref="MoveBy"/> and listens to <see cref="Changed"/> / <see cref="Chosen"/>,
    /// so every list in the menu wraps at the same edge, plays the same sound and moves the same
    /// distance in the same time.</para>
    ///
    /// <para><b>A row that is not interactable is SKIPPED by the keyboard but stays visible.</b>
    /// Hiding it would make it indistinguishable from an option that does not exist, which is the
    /// argument the quest sheet already makes for showing a level-gated quest greyed.</para>
    /// </summary>
    public sealed class MenuList
    {
        private readonly List<MenuRow> _rows = new List<MenuRow>();
        private readonly MenuStyle _style;
        private readonly RectTransform _body;
        private readonly Image _pill;
        private readonly Image _bar;

        private int _index;
        private float _pillY;          // where the pill is drawn now
        private float _pillTargetY;    // where it is heading
        private float _pillHeight;
        private bool _reduceMotion;
        private float _viewportHeight;  // 0 = no window, every row is drawn where it was placed
        private float _scroll;          // how far the rows are pushed up, in canvas units

        /// <summary>Raised when the highlight moves. The screen plays its sound from here.</summary>
        public event Action<int> Changed;

        /// <summary>Raised when a row is activated by Enter or a click.</summary>
        public event Action<int> Chosen;

        public IReadOnlyList<MenuRow> Rows => _rows;
        public int Count => _rows.Count;

        public int Index
        {
            get => _index;
            set => Select(value, raise: false);
        }

        /// <summary>Where the highlight sits right now, in the body's own space. For the tests.</summary>
        public float HighlightY => _pillY;

        /// <summary>Where the highlight is heading. Equal to <see cref="HighlightY"/> at rest.</summary>
        public float HighlightTargetY => _pillTargetY;

        /// <summary>How far the rows are scrolled. 0 when everything fits. For the tests.</summary>
        public float Scroll => _scroll;

        /// <summary>True when the list is taller than the window it was given.</summary>
        public bool Scrolls => _viewportHeight > 0f && ContentHeight > _viewportHeight + 0.5f;

        /// <summary>
        /// Tells the list how tall its window is, so a panel the screen clamped can still reach
        /// every row. Zero means "no window": the rows sit where they were placed, which is what
        /// every list that fits does.
        ///
        /// <para>A WINDOW rather than smaller rows: shrinking type to make a long screen fit
        /// would make Controls quietly different from Options, and the row height is the one
        /// thing this kit exists to keep identical everywhere.</para>
        /// </summary>
        public void SetViewport(float height)
        {
            _viewportHeight = Mathf.Max(0f, height);
            ClampScroll();
            ApplyScroll();
        }

        public MenuList(RectTransform body, MenuArt art, MenuStyle style, bool reduceMotion)
        {
            _body = body;
            _style = style;
            _reduceMotion = reduceMotion;

            // Built FIRST so it sits under every row: the pill is the background of the selected
            // row, and a highlight drawn over the label is a highlight that hides it.
            _pill = MenuUIKit.Sprite("Selection", body, art.Pill, style.Gold);
            var prt = (RectTransform)_pill.transform;
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            _pill.enabled = false;

            _bar = MenuUIKit.Sprite("SelectionBar", body, art.AccentBar, style.Gold);
            var brt = (RectTransform)_bar.transform;
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            _bar.enabled = false;
        }

        /// <summary>Adds a row at the next slot down and wires its pointer to this list.</summary>
        public MenuRow Add(MenuArt art, string label, float indent = 22f, float heightOverride = 0f)
        {
            float h = heightOverride > 0f ? heightOverride : _style.rowHeight;
            float y = -_rows.Count * (h + _style.rowGap);
            var row = new MenuRow(_body, art, _style, label, _rows.Count, y, h, indent);
            int index = _rows.Count;
            row.Bind(
                onClick: () => Choose(index),
                onEnter: () => { Hover(index, true); Select(index, raise: true); },
                onExit: () => Hover(index, false));
            _rows.Add(row);
            _pillHeight = h;
            if (_rows.Count == 1) Select(0, raise: false, snap: true);
            return row;
        }

        /// <summary>Total height the rows occupy, so a panel can size itself to its own list.</summary>
        public float ContentHeight =>
            _rows.Count == 0 ? 0f : _rows.Count * _pillHeight + (_rows.Count - 1) * _style.rowGap;

        public void SetReduceMotion(bool reduce)
        {
            _reduceMotion = reduce;
            if (reduce) { _pillY = _pillTargetY; ApplyPill(); }
        }

        /// <summary>Moves the highlight by <paramref name="delta"/>, wrapping and skipping the dead rows.</summary>
        public void MoveBy(int delta)
        {
            if (_rows.Count == 0 || delta == 0) return;
            int step = delta > 0 ? 1 : -1;
            int i = _index;
            for (int guard = 0; guard < _rows.Count; guard++)
            {
                i = (i + step + _rows.Count) % _rows.Count;
                if (_rows[i].Interactable) { Select(i, raise: true); return; }
            }
        }

        /// <summary>Activates the current row. Refused, loudly to the caller, on a dead row.</summary>
        public bool ChooseCurrent()
        {
            if (_index < 0 || _index >= _rows.Count) return false;
            if (!_rows[_index].Interactable) return false;
            Chosen?.Invoke(_index);
            return true;
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= _rows.Count || !_rows[index].Interactable) return;
            Select(index, raise: true);
            Chosen?.Invoke(index);
        }

        private void Hover(int index, bool on)
        {
            if (index < 0 || index >= _rows.Count) return;
            _rows[index].Hovered = on;
        }

        private void Select(int index, bool raise, bool snap = false)
        {
            if (_rows.Count == 0) return;
            index = Mathf.Clamp(index, 0, _rows.Count - 1);
            bool moved = index != _index;
            for (int i = 0; i < _rows.Count; i++) _rows[i].Selected = i == index;
            _index = index;

            _pillHeight = _rows[index].Root.sizeDelta.y;
            ScrollIntoView(index);
            _pillTargetY = RowY(index) + _scroll;
            if (snap || _reduceMotion) _pillY = _pillTargetY;
            ApplyPill();

            if (moved && raise) Changed?.Invoke(index);
        }

        /// <summary>
        /// Slides the highlight. Public and taking its own delta so an EditMode test can drive it
        /// without a clock — the rule every animated piece in this project follows.
        /// </summary>
        public void Tick(float dt)
        {
            if (Mathf.Approximately(_pillY, _pillTargetY)) return;
            float span = Mathf.Max(0.01f, _style.selectionSlideSeconds);
            // Exponential rather than linear: the highlight leaves at once and arrives softly,
            // which is what makes a 90 ms move read as fast instead of as a jump.
            float k = 1f - Mathf.Exp(-dt / (span * 0.36f));
            _pillY = Mathf.Lerp(_pillY, _pillTargetY, k);
            if (Mathf.Abs(_pillY - _pillTargetY) < 0.25f) _pillY = _pillTargetY;
            ApplyPill();
        }

        /// <summary>Where row <paramref name="index"/> was PLACED, before any scrolling.</summary>
        private float RowY(int index)
        {
            float h = _rows[index].Root.sizeDelta.y;
            return -index * (h + _style.rowGap);
        }

        /// <summary>
        /// Pushes the window so the selected row is inside it. Only the minimum needed, so moving
        /// down a long list scrolls one row at a time instead of jumping the selection to the
        /// middle — which is what makes a keyboard list feel like a list.
        /// </summary>
        private void ScrollIntoView(int index)
        {
            if (!Scrolls) { _scroll = 0f; ApplyScroll(); return; }
            float rowTop = -RowY(index);                       // distance from the top of the list
            float rowBottom = rowTop + _rows[index].Root.sizeDelta.y;
            if (rowTop < _scroll) _scroll = rowTop;
            else if (rowBottom > _scroll + _viewportHeight) _scroll = rowBottom - _viewportHeight;
            ClampScroll();
            ApplyScroll();
        }

        private void ClampScroll()
        {
            float max = Mathf.Max(0f, ContentHeight - _viewportHeight);
            _scroll = _viewportHeight <= 0f ? 0f : Mathf.Clamp(_scroll, 0f, max);
        }

        /// <summary>
        /// Moves the rows, and HIDES the ones outside the window. Hiding matters: a row left
        /// drawn outside its panel is drawn over whatever is behind the panel, and the body has
        /// no mask — adding one would break the batching every other menu surface relies on.
        /// </summary>
        private void ApplyScroll()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var rt = _rows[i].Root;
                float y = RowY(i) + _scroll;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
                bool visible = _viewportHeight <= 0f
                    || (-y + rt.sizeDelta.y > -0.01f && -y < _viewportHeight + 0.01f);
                if (rt.gameObject.activeSelf != visible) rt.gameObject.SetActive(visible);
            }
        }

        private void ApplyPill()
        {
            if (_pill == null) return;
            bool show = _rows.Count > 0;
            _pill.enabled = show;
            _bar.enabled = show;
            if (!show) return;

            // The highlight is hidden with the row it belongs to: a pill left drawn at the edge
            // of a scrolled window is a gold bar floating over the panel's own frame.
            bool inside = _viewportHeight <= 0f
                || (-_pillY + _pillHeight > -0.01f && -_pillY < _viewportHeight + 0.01f);
            _pill.enabled = inside;
            _bar.enabled = inside;

            var prt = (RectTransform)_pill.transform;
            prt.anchoredPosition = new Vector2(0f, _pillY);
            prt.sizeDelta = new Vector2(0f, _pillHeight);

            var brt = (RectTransform)_bar.transform;
            brt.anchoredPosition = new Vector2(0f, _pillY);
            brt.sizeDelta = new Vector2(_style.accentBarWidth, _pillHeight);
        }

        /// <summary>The centre of the selected row in the body's space. Where a mote is born.</summary>
        public Vector2 SelectionCentre()
        {
            if (_rows.Count == 0) return Vector2.zero;
            var rect = _body.rect;
            return new Vector2(rect.width * 0.5f, rect.height + _pillY - _pillHeight * 0.5f);
        }
    }
}
