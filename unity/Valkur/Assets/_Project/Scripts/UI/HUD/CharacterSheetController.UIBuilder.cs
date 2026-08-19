using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

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

        private const float TabWidth    = 132f;
        private const float TabGap      = 4f;
        private const float CloseSize   = 30f;
        private const int   CanvasOrder = 120;   // above SkillTree (60) and Statistics (70)

        private static readonly Color StripColor    = new Color(0.05f, 0.05f, 0.07f, 0.94f);
        private static readonly Color TabIdleColor  = new Color(0.13f, 0.13f, 0.17f, 0.95f);
        private static readonly Color TabHotColor   = new Color(0.22f, 0.24f, 0.32f, 1f);
        private static readonly Color AccentColor   = new Color(1f, 0.78f, 0.30f, 1f);
        private static readonly Color LabelIdle     = new Color(0.78f, 0.80f, 0.86f, 1f);

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
            _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
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

            for (int i = 0; i < count; i++)
            {
                int index = i;   // captured per iteration for the click handler

                var tabGo = NewChild("Tab_" + _tabs[i].Label, stripRt);
                var tabRt = tabGo.GetComponent<RectTransform>();
                tabRt.anchorMin = new Vector2(0f, 0f);
                tabRt.anchorMax = new Vector2(0f, 1f);
                tabRt.pivot     = new Vector2(0f, 0.5f);
                tabRt.sizeDelta = new Vector2(TabWidth, -12f);
                tabRt.anchoredPosition = new Vector2(8f + i * (TabWidth + TabGap), 0f);

                var bg = tabGo.AddComponent<Image>();
                bg.sprite = WhiteSprite();
                bg.color  = TabIdleColor;
                _tabBackgrounds[i] = bg;

                var button = tabGo.AddComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() => SelectTab(index));

                // Label on its own GameObject — TMP and Image must not share one.
                var labelGo = NewChild("Label", tabRt);
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;

                var label = labelGo.AddComponent<TextMeshProUGUI>();
                label.text             = _tabs[i].Label;
                label.fontSize         = 15f;
                label.fontStyle        = FontStyles.Bold;
                label.characterSpacing = 6f;
                label.alignment        = TextAlignmentOptions.Midline;
                label.color            = LabelIdle;
                label.raycastTarget    = false;
                _tabLabels[i] = label;

                // Accent underline marking the active tab.
                var lineGo = NewChild("Underline", tabRt);
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
