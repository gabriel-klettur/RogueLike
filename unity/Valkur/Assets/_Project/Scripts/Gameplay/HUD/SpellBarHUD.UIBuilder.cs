using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Spells.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.UI
{
    public partial class SpellBarHUD
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("SpellBarCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 150;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            int total = rows * cols;
            float gridW = cols * slotSize + (cols - 1) * slotGap;
            float gridH = rows * slotSize + (rows - 1) * slotGap;
            float panelW = gridW + 16f + 24f; // padding + arrow column
            float panelH = gridH + 12f;

            // Root panel
            var rootGo = new GameObject("SpellBar", typeof(RectTransform));
            rootGo.transform.SetParent(_canvas.transform, false);
            _root = rootGo.GetComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0f);
            _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot     = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, bottomPad);
            _root.sizeDelta = new Vector2(panelW, panelH);

            var bg = rootGo.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.07f, 0.55f);
            bg.raycastTarget = true; // needed so empty space catches the drag

            var ol = rootGo.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(1f, 1f);

            _rootCg = rootGo.AddComponent<CanvasGroup>();

            // Window-style drag: clicking-and-dragging any empty space on the
            // panel BG moves the bar. Slot buttons consume their own clicks but
            // do not implement IDrag*, so drag events bubble up to this root.
            var dragger = rootGo.AddComponent<WindowDragHandler>();
            dragger.Target = _root;

            // Grid (right side, leaving 24 px column on the left for arrows)
            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(_root, false);
            var grt = (RectTransform)gridGo.transform;
            grt.anchorMin = new Vector2(0f, 0.5f);
            grt.anchorMax = new Vector2(0f, 0.5f);
            grt.pivot     = new Vector2(0f, 0.5f);
            grt.anchoredPosition = new Vector2(28f, 0f);
            grt.sizeDelta = new Vector2(gridW, gridH);

            _slotViews = new SlotView[total];
            for (int i = 0; i < total; i++)
            {
                int r = i / cols;
                int c = i % cols;
                float sx = c * (slotSize + slotGap);
                float sy = -r * (slotSize + slotGap); // row 0 at top
                _slotViews[i] = BuildSlot(grt, i, sx, sy);
            }

            BuildPagerArrows();
            BuildMinimizeButton();
        }

        private void BuildMinimizeButton()
        {
            var go = new GameObject("MinimizeBtn", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-2f, -2f);
            rt.sizeDelta = new Vector2(18f, 18f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.18f, 0.22f, 0.95f);

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = new Color(0.18f, 0.18f, 0.22f, 0.95f);
            c.highlightedColor = new Color(0.32f, 0.32f, 0.40f, 1f);
            c.pressedColor     = new Color(0.10f, 0.10f, 0.12f, 1f);
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(MinimizeToTray);

            var txtGo = new GameObject("Glyph", typeof(RectTransform));
            txtGo.transform.SetParent(rt, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text       = "_";
            tmp.fontSize   = 14f;
            tmp.fontStyle  = FontStyles.Bold;
            tmp.alignment  = TextAlignmentOptions.Center;
            tmp.color      = ACCENT;
            tmp.raycastTarget = false;
        }

        private SlotView BuildSlot(RectTransform parent, int index, float x, float y)
        {
            var go = new GameObject($"Slot_{index}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(slotSize, slotSize);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.10f, 0.95f);

            var slotOl = go.AddComponent<Outline>();
            slotOl.effectColor    = new Color(0.25f, 0.25f, 0.30f, 1f);
            slotOl.effectDistance = new Vector2(1f, 1f);

            var dropZone = go.AddComponent<DropZoneSpellSlot>();
            dropZone.Bind(this, index);

            // Icon
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(rt, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(2f, 2f);
            irt.offsetMax = new Vector2(-2f, -2f);
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget  = false;
            icon.enabled        = false;

            // Cooldown radial overlay (dark wedge that empties as cooldown elapses)
            var cdGo = new GameObject("CdOverlay", typeof(RectTransform));
            cdGo.transform.SetParent(rt, false);
            var cdRt = (RectTransform)cdGo.transform;
            cdRt.anchorMin = Vector2.zero;
            cdRt.anchorMax = Vector2.one;
            cdRt.offsetMin = new Vector2(2f, 2f);
            cdRt.offsetMax = new Vector2(-2f, -2f);
            var cdImg = cdGo.AddComponent<Image>();
            cdImg.color           = new Color(0f, 0f, 0f, 0.65f);
            cdImg.raycastTarget   = false;
            cdImg.type            = Image.Type.Filled;
            cdImg.fillMethod      = Image.FillMethod.Radial360;
            cdImg.fillOrigin      = (int)Image.Origin360.Top;
            cdImg.fillClockwise   = false;
            cdImg.fillAmount      = 0f;
            cdImg.sprite          = MakeWhitePixel();

            // Cooldown numeric label
            var cdTxtGo = new GameObject("CdText", typeof(RectTransform));
            cdTxtGo.transform.SetParent(rt, false);
            var ctRt = (RectTransform)cdTxtGo.transform;
            ctRt.anchorMin = Vector2.zero;
            ctRt.anchorMax = Vector2.one;
            ctRt.offsetMin = Vector2.zero;
            ctRt.offsetMax = Vector2.zero;
            var cdTxt = cdTxtGo.AddComponent<TextMeshProUGUI>();
            cdTxt.alignment = TextAlignmentOptions.Center;
            cdTxt.fontSize  = 14f;
            cdTxt.fontStyle = FontStyles.Bold;
            cdTxt.color     = ACCENT;
            cdTxt.raycastTarget = false;
            cdTxt.text = "";

            // Hotkey label (top-left)
            var hkGo = new GameObject("Hotkey", typeof(RectTransform));
            hkGo.transform.SetParent(rt, false);
            var hkRt = (RectTransform)hkGo.transform;
            hkRt.anchorMin = new Vector2(0f, 1f);
            hkRt.anchorMax = new Vector2(0f, 1f);
            hkRt.pivot     = new Vector2(0f, 1f);
            hkRt.anchoredPosition = new Vector2(2f, -1f);
            hkRt.sizeDelta = new Vector2(24f, 12f);
            var hk = hkGo.AddComponent<TextMeshProUGUI>();
            hk.text       = HotkeyForIndex(index);
            hk.fontSize   = 9f;
            hk.fontStyle  = FontStyles.Bold;
            hk.alignment  = TextAlignmentOptions.TopLeft;
            hk.color      = new Color(1f, 1f, 1f, 0.85f);
            hk.outlineWidth = 0.2f;
            hk.outlineColor = Color.black;
            hk.raycastTarget = false;

            // Mana cost label (bottom-right)
            var mcGo = new GameObject("Mana", typeof(RectTransform));
            mcGo.transform.SetParent(rt, false);
            var mcRt = (RectTransform)mcGo.transform;
            mcRt.anchorMin = new Vector2(1f, 0f);
            mcRt.anchorMax = new Vector2(1f, 0f);
            mcRt.pivot     = new Vector2(1f, 0f);
            mcRt.anchoredPosition = new Vector2(-2f, 1f);
            mcRt.sizeDelta = new Vector2(28f, 12f);
            var mc = mcGo.AddComponent<TextMeshProUGUI>();
            mc.text       = "";
            mc.fontSize   = 9f;
            mc.fontStyle  = FontStyles.Bold;
            mc.alignment  = TextAlignmentOptions.BottomRight;
            mc.color      = new Color(0.55f, 0.80f, 1f, 0.95f);
            mc.outlineWidth = 0.2f;
            mc.outlineColor = Color.black;
            mc.raycastTarget = false;

            // Click handler
            var click = go.AddComponent<SpellSlotButton>();
            click.Bind(this, index);

            return new SlotView
            {
                Root = go, Bg = bg, Icon = icon,
                CdOverlay = cdImg, CdText = cdTxt,
                HotkeyText = hk, ManaText = mc,
                SpellKey = null, SlotIndex = -1
            };
        }

        private void BuildPagerArrows()
        {
            // Up / Down arrows column (purely cosmetic for now — placeholder for paging).
            var colGo = new GameObject("Arrows", typeof(RectTransform));
            colGo.transform.SetParent(_root, false);
            var crt = (RectTransform)colGo.transform;
            crt.anchorMin = new Vector2(0f, 0.5f);
            crt.anchorMax = new Vector2(0f, 0.5f);
            crt.pivot     = new Vector2(0f, 0.5f);
            crt.anchoredPosition = new Vector2(4f, 0f);
            crt.sizeDelta = new Vector2(20f, slotSize * 2 + slotGap);

            BuildArrow(crt, true,  new Vector2(0f, 1f),  new Vector2(0f, -1f));
            BuildArrow(crt, false, new Vector2(0f, 0f),  new Vector2(0f, 1f));
        }

        private void BuildArrow(RectTransform parent, bool up, Vector2 anchor, Vector2 pivot)
        {
            var go = new GameObject(up ? "Up" : "Down", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot     = pivot;
            rt.sizeDelta = new Vector2(18f, 18f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text       = up ? "▲" : "▼";
            tmp.fontSize   = 14f;
            tmp.alignment  = TextAlignmentOptions.Center;
            tmp.color      = ACCENT;
            tmp.raycastTarget = false;
        }

        private static Sprite _whitePixel;
        private static Sprite MakeWhitePixel()
        {
            if (_whitePixel != null) return _whitePixel;
            var tex = new Texture2D(2, 2);
            var pixels = new Color[4];
            for (int i = 0; i < 4; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            _whitePixel = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            return _whitePixel;
        }
    }
}
