using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.MainMenu.Kit
{
    /// <summary>
    /// One sub-screen's panel: frame, title band, body and hint bar, with an open animation.
    ///
    /// <para><b>One anchor for all of them.</b> Every panel hangs from
    /// <see cref="MenuStyle.panelTopOffset"/> below the top of the canvas. The shipped screens
    /// used four different anchors and one of them (Controls) centred itself instead, which put
    /// it over the game's own title.</para>
    ///
    /// <para><b>It sizes itself to its content.</b> The shipped builders each computed a panel
    /// height by hand from their own constants, so adding a row meant editing arithmetic; here
    /// the body reports what it needs and the panel follows.</para>
    ///
    /// <para><b>The open animation is a scale and a fade, not a slide.</b> A panel that slides in
    /// has a direction, and a direction implies a spatial relationship between two screens that
    /// the menu does not have — Options is not to the right of the main menu. Scale plus alpha
    /// says "this arrived" and nothing else.</para>
    /// </summary>
    public sealed class MenuPanelView
    {
        public readonly RectTransform Root;
        public readonly RectTransform Body;
        public readonly CanvasGroup Group;
        public readonly TextMeshProUGUI Title;

        private readonly MenuStyle _style;
        private TextMeshProUGUI _hint;
        private float _open;            // 0 closed, 1 open
        private float _openTarget;
        private bool _reduceMotion;

        /// <summary>How far through its open animation the panel is. 1 at rest, on screen.</summary>
        public float Openness => _open;

        public MenuPanelView(Transform parent, MenuArt art, MenuStyle style, string title,
                             float width, bool reduceMotion)
        {
            _style = style;
            _reduceMotion = reduceMotion;

            Root = MenuUIKit.Rect("Panel", parent);
            Root.anchorMin = new Vector2(0.5f, 1f);
            Root.anchorMax = new Vector2(0.5f, 1f);
            Root.pivot = new Vector2(0.5f, 1f);
            Root.anchoredPosition = new Vector2(0f, -style.panelTopOffset);
            Root.sizeDelta = new Vector2(width, 200f);

            Group = Root.gameObject.AddComponent<CanvasGroup>();

            var frame = MenuUIKit.Panel("Frame", Root, art, style);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            Title = MenuUIKit.PanelHeader(Root, art, style, title);

            Body = MenuUIKit.Rect("Body", Root);
            Body.anchorMin = new Vector2(0f, 0f);
            Body.anchorMax = new Vector2(1f, 1f);
            Body.offsetMin = new Vector2(style.panelPadding, style.hintBarHeight);
            Body.offsetMax = new Vector2(-style.panelPadding, -style.titleBarHeight - 8f);

            _open = _openTarget = 1f;
        }

        /// <summary>Adds the hint bar. Separate so a panel with no hint has no empty strip.</summary>
        public void SetHint(string text)
        {
            if (_hint == null) _hint = MenuUIKit.PanelHint(Root, _style, text);
            else _hint.text = text;
        }

        /// <summary>
        /// Grows the panel so <paramref name="contentHeight"/> fits inside the body — and NEVER
        /// past the screen.
        ///
        /// <para><b>The clamp is not defensive, it is the fix for a measured defect.</b> Every
        /// panel hangs from <see cref="MenuStyle.panelTopOffset"/> (296) and grew freely from
        /// there, so on the 800-unit reference canvas: Audio with its advanced rows open came to
        /// 660 and ended at <b>956</b>, and Controls to 804 ending at <b>1100</b> — the last rows
        /// and the whole hint bar off the bottom of the screen. Each number was reasonable on its
        /// own (a row is 46, a panel starts at 296); only the SUM was false, which is the shape
        /// this project already records for the spawners' coordinate drift.</para>
        ///
        /// <para>What it does when the content does not fit: the panel takes the height it can
        /// have, and the list inside it scrolls. Shrinking the rows instead would make a long
        /// screen quietly different from a short one.</para>
        /// </summary>
        public void FitToContent(float contentHeight, float extra = 0f)
        {
            float chrome = _style.titleBarHeight + 8f + extra + _style.hintBarHeight + _style.panelPadding;
            float wanted = chrome + contentHeight;
            float available = AvailableHeight(_style);
            float h = Mathf.Clamp(wanted, 120f, available);
            Root.sizeDelta = new Vector2(Root.sizeDelta.x, h);
            BodyHeight = Mathf.Max(0f, h - chrome);
            Overflows = wanted > available + 0.5f;
        }

        /// <summary>How tall the body ended up, so a list can size its own window to it.</summary>
        public float BodyHeight { get; private set; }

        /// <summary>True when the content did not fit and the list inside has to scroll.</summary>
        public bool Overflows { get; private set; }

        /// <summary>
        /// The tallest a panel may be: the canvas minus where panels start minus a margin at the
        /// foot. Derived from the style rather than written down, so moving
        /// <see cref="MenuStyle.panelTopOffset"/> moves the ceiling with it.
        /// </summary>
        public static float AvailableHeight(MenuStyle style)
        {
            float canvas = style.referenceResolution.y > 1f ? style.referenceResolution.y : 800f;
            const float bottomMargin = 56f;      // room for the footer hint and the version line
            return Mathf.Max(160f, canvas - style.panelTopOffset - bottomMargin);
        }

        /// <summary>Shows the panel, playing the open animation from the start.</summary>
        public void Open()
        {
            Root.gameObject.SetActive(true);
            _openTarget = 1f;
            _open = _reduceMotion ? 1f : 0f;
            Apply();
        }

        /// <summary>Hides the panel at once. There is no close animation, on purpose: a screen
        /// the player has left should not still be on screen while the next one arrives.</summary>
        public void Close()
        {
            _openTarget = 0f;
            _open = 0f;
            Apply();
            Root.gameObject.SetActive(false);
        }

        public void SetReduceMotion(bool reduce)
        {
            _reduceMotion = reduce;
            if (reduce) { _open = _openTarget; Apply(); }
        }

        /// <summary>Advances the open animation. Own delta, so a test can drive it.</summary>
        public void Tick(float dt)
        {
            if (Mathf.Approximately(_open, _openTarget)) return;
            float span = Mathf.Max(0.02f, _style.panelFadeSeconds);
            _open = Mathf.MoveTowards(_open, _openTarget, dt / span);
            Apply();
        }

        private void Apply()
        {
            float e = Mathf.SmoothStep(0f, 1f, _open);
            Group.alpha = e;
            float s = Mathf.Lerp(_style.panelOpenScale, 1f, e);
            Root.localScale = new Vector3(s, s, 1f);
            // A panel mid-animation must not catch clicks: a player who clicks where a row is
            // ABOUT to be would otherwise hit whatever is there for those few frames.
            Group.blocksRaycasts = _open > 0.85f;
            Group.interactable = Group.blocksRaycasts;
        }
    }
}
