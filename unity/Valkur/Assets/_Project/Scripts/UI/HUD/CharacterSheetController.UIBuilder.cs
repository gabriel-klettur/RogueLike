using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The character sheet's chrome: a slim tab strip that sits just above the
    /// panels it switches between, on its own canvas so it always draws over
    /// them. Built procedurally from the tab list, so the strip follows whatever
    /// <c>BuildTabs</c> declares.
    /// </summary>
    public sealed partial class CharacterSheetController : SingletonMonoBehaviour<CharacterSheetController>
    {
        // Sits directly on top of the SkillTree / Statistics panels, which both
        // span x 0.20–0.80 and stop at y 0.85.
        private const float StripLeft   = 0.20f;
        private const float StripRight  = 0.80f;
        private const float StripBottom = 0.855f;
        private const float StripTop    = 0.915f;

        /// <summary>
        /// Widest a tab may get. It used to be the ONLY width, and a fixed width in a strip
        /// whose size comes from the screen is an overflow waiting for its fourth tab:
        /// measured at 1600x800 on the shipped 800x600 canvas, four tabs plus the close
        /// button needed 548 units in a 480-unit strip, so RECORDS was drawn 136 px outside
        /// the panel and the X landed on top of its label — the "RX" in the bug report.
        /// The strip divides what it HAS now, and this is only a ceiling.
        /// </summary>
        private const float TabMaxWidth = 132f;

        /// <summary>
        /// Narrowest a tab may get before the strip admits it cannot fit them. Below this the
        /// labels are unreadable and silently shrinking them further would trade one invisible
        /// failure for another; the strip clamps here and the test says so.
        /// </summary>
        private const float TabMinWidth = 72f;

        private const float TabGap      = 4f;
        private const float CloseSize   = 30f;
        private const float StripInset  = 8f;

        /// <summary>
        /// The strip draws over the panels it switches between, and it is the only way out of
        /// them. Band, not a number: the panels themselves each wrote 60 / 60 / 60 / 70, which
        /// put all four under the minimap and the music plaque.
        /// </summary>
        private const int   CanvasOrder = HudLayout.CharacterSheetChromeSortingOrder;

        private static readonly Color StripColor    = new Color(0.05f, 0.05f, 0.07f, 0.94f);
        private static readonly Color TabIdleColor  = new Color(0.13f, 0.13f, 0.17f, 0.95f);
        private static readonly Color TabHotColor   = new Color(0.22f, 0.24f, 0.32f, 1f);
        private static readonly Color AccentColor   = new Color(1f, 0.78f, 0.30f, 1f);
        private static readonly Color LabelIdle     = new Color(0.78f, 0.80f, 0.86f, 1f);

        // ── The fit, as arithmetic a test can run ─────────────────────────
        // uGUI performs no layout in EditMode, so the only honest way to pin "the tabs fit"
        // is to state the rule as a function. The shipped panel failed it by 68 units and
        // nothing could see that: the overflow was only visible on screen, as an X drawn
        // over the last tab's label.

        /// <summary>Width the tab strip has, in canvas units, at the reference resolution.</summary>
        public static float StripWidthAtReference() =>
            (StripRight - StripLeft) * HudLayout.ReferenceWidth;

        /// <summary>What <paramref name="count"/> tabs want: their ceiling width, plus the
        /// gaps, the inset and the reserved close button.</summary>
        public static float TabsPreferredWidth(int count) => TabsWidth(count, TabMaxWidth);

        /// <summary>The least the same tabs can be squeezed into before the strip gives up.</summary>
        public static float TabsMinimumWidth(int count) => TabsWidth(count, TabMinWidth);

        private static float TabsWidth(int count, float each)
        {
            if (count <= 0) return StripInset * 3f + CloseSize;
            return StripInset                       // left inset
                 + count * each                     // the tabs
                 + (count - 1) * TabGap             // the gaps between them
                 + StripInset * 2f + CloseSize;     // the reserved close button
        }

        private GameObject _root;
        private Image[]    _tabBackgrounds;
        private Image[]    _tabUnderlines;
        private TextMeshProUGUI[] _tabLabels;

        private static Sprite _whiteSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpriteCacheOnPlayModeEnter() => _whiteSprite = null;

        private void EnsureBuilt()
        {
            if (_root != null) return;

            _root = new GameObject("CharacterSheet_Root", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasOrder;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // HUD_VISUAL_LANGUAGE.md R9. Unity's default 800x600 with match 0 is a 2.0 scale
            // factor at 1600 wide against every other HUD surface's 1.0, and it is what made
            // the fixed-width tabs overflow a strip that looked twice as wide in code as it
            // was on screen.
            scaler.referenceResolution = new Vector2(HudLayout.ReferenceWidth,
                                                     HudLayout.ReferenceHeight);
            scaler.matchWidthOrHeight = HudLayout.Match;

            _root.AddComponent<GraphicRaycaster>();

            var strip = NewChild("Strip", _root.transform);
            var stripRt = strip.GetComponent<RectTransform>();
            stripRt.anchorMin = new Vector2(StripLeft, StripBottom);
            stripRt.anchorMax = new Vector2(StripRight, StripTop);
            stripRt.offsetMin = Vector2.zero;
            stripRt.offsetMax = Vector2.zero;

            var stripImg = strip.AddComponent<Image>();
            stripImg.sprite = WhiteSprite();
            stripImg.color  = StripColor;

            BuildTabButtons(stripRt);
            BuildCloseButton(stripRt);
        }

        private void BuildTabButtons(RectTransform stripRt)
        {
            int count = _tabs.Count;
            _tabBackgrounds = new Image[count];
            _tabUnderlines  = new Image[count];
            _tabLabels      = new TextMeshProUGUI[count];

            // The strip DIVIDES the width it has instead of each tab claiming a fixed one.
            // The right padding is what reserves the close button, so the X can never be
            // dealt a tab's space and drawn on top of its label.
            var layout = stripRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(
                (int)StripInset, (int)(StripInset * 2f + CloseSize), 6, 6);
            layout.spacing                = TabGap;
            layout.childAlignment         = TextAnchor.MiddleLeft;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            layout.childForceExpandWidth  = false;
            layout.childForceExpandHeight = true;

            for (int i = 0; i < count; i++)
            {
                int index = i;   // captured per iteration for the click handler

                var tabGo = NewChild("Tab_" + _tabs[i].Label, stripRt);

                // Prefer TabMaxWidth, shrink proportionally toward TabMinWidth when a fifth
                // tab arrives or the window narrows. uGUI hands out min widths first and then
                // distributes what is left up to preferred, which is exactly this rule.
                var element = tabGo.AddComponent<LayoutElement>();
                element.minWidth       = TabMinWidth;
                element.preferredWidth = TabMaxWidth;
                element.flexibleWidth  = 0f;

                var bg = tabGo.AddComponent<Image>();
                bg.sprite = WhiteSprite();
                bg.color  = TabIdleColor;
                _tabBackgrounds[i] = bg;

                var button = tabGo.AddComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() => SelectTab(index));

                // Label on its own GameObject — TMP and Image must not share one.
                var labelGo = NewChild("Label", tabGo.transform);
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;

                var label = labelGo.AddComponent<TextMeshProUGUI>();
                label.text             = _tabs[i].Label;
                // Auto-sized, because the tab is no longer a fixed width: a strip that
                // shrinks its tabs and keeps a fixed point size has only moved the overflow
                // from the strip into the label.
                label.enableAutoSizing = true;
                label.fontSizeMin      = 9f;
                label.fontSizeMax      = 15f;
                label.enableWordWrapping = false;
                label.fontSize         = 15f;
                label.fontStyle        = FontStyles.Bold;
                label.characterSpacing = 6f;
                label.alignment        = TextAlignmentOptions.Midline;
                label.color            = LabelIdle;
                label.raycastTarget    = false;
                _tabLabels[i] = label;

                // Accent underline marking the active tab.
                var lineGo = NewChild("Underline", tabGo.transform);
                var lineRt = lineGo.GetComponent<RectTransform>();
                lineRt.anchorMin = new Vector2(0f, 0f);
                lineRt.anchorMax = new Vector2(1f, 0f);
                lineRt.pivot     = new Vector2(0.5f, 0f);
                lineRt.offsetMin = new Vector2(6f, 0f);
                lineRt.offsetMax = new Vector2(-6f, 3f);

                var line = lineGo.AddComponent<Image>();
                line.sprite        = WhiteSprite();
                line.color         = AccentColor;
                line.raycastTarget = false;
                line.enabled       = false;
                _tabUnderlines[i] = line;
            }
        }

        private void BuildCloseButton(RectTransform stripRt)
        {
            var closeGo = NewChild("Close", stripRt);
            var closeRt = closeGo.GetComponent<RectTransform>();
            // Anchored, never laid out: the strip's right padding reserves its space and the
            // HorizontalLayoutGroup must not deal it a tab's slot.
            closeGo.AddComponent<LayoutElement>().ignoreLayout = true;
            closeRt.anchorMin = new Vector2(1f, 0.5f);
            closeRt.anchorMax = new Vector2(1f, 0.5f);
            closeRt.pivot     = new Vector2(1f, 0.5f);
            closeRt.sizeDelta = new Vector2(CloseSize, CloseSize);
            closeRt.anchoredPosition = new Vector2(-8f, 0f);

            var bg = closeGo.AddComponent<Image>();
            bg.sprite = WhiteSprite();
            bg.color  = TabIdleColor;

            var button = closeGo.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(Close);

            var labelGo = NewChild("Label", closeRt);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text          = "X";
            label.fontSize      = 16f;
            label.fontStyle     = FontStyles.Bold;
            label.alignment     = TextAlignmentOptions.Midline;
            label.color         = new Color(0.92f, 0.92f, 0.95f, 1f);
            label.raycastTarget = false;
        }

        private void ApplyOpenState(bool open)
        {
            IsOpen = open;
            // The sheet closes on Escape, so it holds Escape while open — see EscapeOwnership.
            if (open) EscapeOwnership.Claim(this); else EscapeOwnership.Release(this);
            if (_root != null && _root.activeSelf != open) _root.SetActive(open);
        }

        private void RefreshTabVisuals(int activeIndex)
        {
            if (_tabBackgrounds == null) return;

            for (int i = 0; i < _tabBackgrounds.Length; i++)
            {
                bool active = i == activeIndex;
                if (_tabBackgrounds[i] != null) _tabBackgrounds[i].color = active ? TabHotColor : TabIdleColor;
                if (_tabUnderlines[i] != null)  _tabUnderlines[i].enabled = active;
                if (_tabLabels[i] != null)      _tabLabels[i].color = active ? Color.white : LabelIdle;
            }
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();

            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _whiteSprite.name = "CharacterSheet_White";
            return _whiteSprite;
        }
    }
}
