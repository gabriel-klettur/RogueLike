using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;

namespace Valkur.UI.HUD
{
    public sealed partial class WorldMapPanel
    {
        private const float FRAME_W = 1280f;
        private const float FRAME_H = 700f;
        private const float TITLE_H = 44f;
        private const float FOOTER_H = 30f;
        private const float LEGEND_W = 206f;
        private const float PAD = 14f;

        private GameObject _canvasGo;
        private Canvas _canvas;
        private RectTransform _frame;
        private RawImage _mapImage;
        private MinimapQuadGraphic _fxUnder, _glyphs, _fxOver;
        private RectTransform _labelLayer;
        private TextMeshProUGUI _titleZone;
        private TextMeshProUGUI _cursorInfo;

        private void BuildUI()
        {
            _canvasGo = new GameObject("WorldMapCanvas");
            _canvasGo.transform.SetParent(transform, false);
            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD (105), the music widget (150) and the quest tracker; below the pause
            // menu and toasts, which must still be able to speak over an open map.
            _canvas.sortingOrder = 170;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Valkur.Core.UI.HudLayout.ReferenceWidth, Valkur.Core.UI.HudLayout.ReferenceHeight);
            scaler.matchWidthOrHeight = Valkur.Core.UI.HudLayout.Match;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _group = _canvasGo.AddComponent<CanvasGroup>();

            // Backdrop: dims the world and swallows clicks; a click on it closes the map.
            var backdrop = NewImage(_canvasGo.transform, "Backdrop", null, new Color(0.01f, 0.012f, 0.02f, 0.62f));
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            var closer = backdrop.gameObject.AddComponent<Button>();
            closer.transition = Selectable.Transition.None;
            closer.onClick.AddListener(Close);

            var style = _style;
            _frame = NewImage(_canvasGo.transform, "Frame", MinimapChromeSprites.Plate(style), Color.white).rectTransform;
            var frameImg = _frame.GetComponent<Image>();
            frameImg.type = Image.Type.Sliced;
            frameImg.pixelsPerUnitMultiplier = 1.2f;
            frameImg.raycastTarget = true;   // clicks on the frame do not fall through to the closer
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = new Vector2(FRAME_W, FRAME_H);
            _frame.anchoredPosition = Vector2.zero;

            BuildTitle();
            BuildMapArea();
            BuildLegend();
            BuildFooter();
        }

        private void BuildTitle()
        {
            var title = NewLabel(_frame, "Title", 18f, FontStyles.Bold, _style.ringHighlight);
            title.text = "MAPA DEL MUNDO";
            title.characterSpacing = 8f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            TopLeft(title.rectTransform, new Vector2(PAD + 6f, -8f), new Vector2(420f, TITLE_H - 12f));

            _titleZone = NewLabel(_frame, "Zone", 15f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
            _titleZone.alignment = TextAlignmentOptions.Center;
            var zr = _titleZone.rectTransform;
            zr.anchorMin = zr.anchorMax = new Vector2(0.5f, 1f);
            zr.pivot = new Vector2(0.5f, 1f);
            zr.anchoredPosition = new Vector2(-LEGEND_W * 0.5f, -8f);
            zr.sizeDelta = new Vector2(420f, TITLE_H - 12f);

            var close = NewButton(_frame, "Close", MinimapIcon.Plus, 45f, Close);
            var cr = close.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-PAD, -9f);
            cr.sizeDelta = new Vector2(26f, 26f);

            var centre = NewButton(_frame, "Recentre", MinimapIcon.Arrow, 0f, Recentre);
            var rr = centre.rectTransform;
            rr.anchorMin = rr.anchorMax = new Vector2(1f, 1f);
            rr.pivot = new Vector2(1f, 1f);
            rr.anchoredPosition = new Vector2(-PAD - 34f, -9f);
            rr.sizeDelta = new Vector2(26f, 26f);
        }

        private void BuildMapArea()
        {
            float w = FRAME_W - LEGEND_W - PAD * 3f;
            float h = FRAME_H - TITLE_H - FOOTER_H - PAD;

            // A gold hairline around the map, so the rectangle reads as a sheet laid on the frame.
            var border = NewImage(_frame, "MapBorder", null, _style.ringGold * new Color(1f, 1f, 1f, 0.55f));
            TopLeft(border.rectTransform, new Vector2(PAD - 2f, -TITLE_H + 2f), new Vector2(w + 4f, h + 4f));
            border.raycastTarget = false;

            var mapRt = NewRect(_frame, "Map");
            TopLeft(mapRt, new Vector2(PAD, -TITLE_H), new Vector2(w, h));
            _mapImage = mapRt.gameObject.AddComponent<RawImage>();
            _mapImage.raycastTarget = true;

            // Zone names sit UNDER the glyphs: a name is background information, and the first
            // build put "LOBBY" over the player's own arrow.
            _fxUnder = QuadLayer(mapRt, "FxUnder", _additive);
            _labelLayer = NewRect(mapRt, "ZoneNames");
            Stretch(_labelLayer);
            _glyphs = QuadLayer(mapRt, "Glyphs", null);
            _fxOver = QuadLayer(mapRt, "FxOver", _additive);

            // What is under the pointer: the zone, the coordinates and how much of it is known.
            // The coordinates used to sit permanently under the minimap, where they were a
            // developer's readout; here they answer a question the player is actually asking.
            _cursorInfo = NewLabel(mapRt, "CursorInfo", 12f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
            _cursorInfo.alignment = TextAlignmentOptions.BottomLeft;
            _cursorInfo.outlineWidth = 0.2f;
            _cursorInfo.outlineColor = new Color32(0, 0, 0, 230);
            var cr = _cursorInfo.rectTransform;
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0f, 0f);
            cr.anchoredPosition = new Vector2(10f, 8f);
            cr.sizeDelta = new Vector2(520f, 18f);
            _cursorInfo.text = string.Empty;
        }

        private void BuildLegend()
        {
            var s = _style;
            var col = NewRect(_frame, "Legend");
            col.anchorMin = col.anchorMax = new Vector2(1f, 1f);
            col.pivot = new Vector2(1f, 1f);
            col.anchoredPosition = new Vector2(-PAD, -TITLE_H);
            col.sizeDelta = new Vector2(LEGEND_W, FRAME_H - TITLE_H - FOOTER_H - PAD);

            var head = NewLabel(col, "LegendTitle", 13f, FontStyles.Bold, s.ringHighlight);
            head.text = "LEYENDA";
            head.characterSpacing = 6f;
            TopLeft(head.rectTransform, new Vector2(8f, -4f), new Vector2(LEGEND_W - 16f, 20f));

            float y = -32f;
            y = LegendRow(col, y, MinimapIcon.Arrow,    s.playerColor,      "Tú");
            y = LegendRow(col, y, MinimapIcon.Dot,      s.enemyColor,       "Enemigo");
            y = LegendRow(col, y, MinimapIcon.EliteDot, s.eliteColor,       "Enemigo élite");
            y = LegendRow(col, y, MinimapIcon.Skull,    s.bossColor,        "Jefe");
            y = LegendRow(col, y, MinimapIcon.Shield,   s.allyColor,        "Aliado");
            y = LegendRow(col, y, MinimapIcon.Dot,      s.neutralColor,     "Aldeano");
            y = LegendRow(col, y, MinimapIcon.Coin,     s.vendorColor,      "Comerciante");
            y = LegendRow(col, y, MinimapIcon.Exclaim,  s.questOfferColor,  "Misión disponible");
            y = LegendRow(col, y, MinimapIcon.Question, s.questTurnInColor, "Entregar misión");
            y = LegendRow(col, y, MinimapIcon.Star,     s.objectiveColor,   "Objetivo");
            y = LegendRow(col, y, MinimapIcon.Portal,   s.portalColor,      "Portal");
            y = LegendRow(col, y, MinimapIcon.Door,     s.doorColor,        "Puerta");
            y = LegendRow(col, y, MinimapIcon.Ankh,     s.altarColor,       "Altar");
            y = LegendRow(col, y, MinimapIcon.Tomb,     s.corpseColor,      "Tu cuerpo");
            LegendRow(col, y, MinimapIcon.Pin,          s.waypointColor,    "Tu destino");
        }

        private float LegendRow(RectTransform parent, float y, MinimapIcon icon, Color color, string text)
        {
            var img = NewImage(parent, "Icon" + icon, MinimapIconAtlas.SpriteOf(icon), color);
            TopLeft(img.rectTransform, new Vector2(10f, y), new Vector2(20f, 20f));
            img.raycastTarget = false;
            var label = NewLabel(parent, "Label" + icon, 12.5f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
            label.text = text;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            TopLeft(label.rectTransform, new Vector2(38f, y), new Vector2(LEGEND_W - 44f, 20f));
            return y - 26f;
        }

        private void BuildFooter()
        {
            var hint = NewLabel(_frame, "Hints", 12f, FontStyles.Normal, UITheme.TEXT_SECONDARY);
            hint.text = "Rueda: zoom   ·   Arrastrar: mover   ·   Clic: marcar destino   ·   Clic derecho: quitarlo   ·   N / Esc: cerrar";
            hint.alignment = TextAlignmentOptions.Center;
            var rt = hint.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 6f);
            rt.sizeDelta = new Vector2(-PAD * 2f, FOOTER_H - 8f);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private Image NewButton(RectTransform parent, string name, MinimapIcon icon, float iconRotation, UnityEngine.Events.UnityAction onClick)
        {
            var img = NewImage(parent, name, MinimapChromeSprites.Button(_style), Color.white);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 0.95f, 0.8f, 1f);
            colors.pressedColor = new Color(0.8f, 0.7f, 0.5f, 1f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var glyph = NewImage(img.rectTransform, "Icon", MinimapIconAtlas.SpriteOf(icon), _style.ringHighlight);
            var g = glyph.rectTransform;
            g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
            g.anchoredPosition = Vector2.zero;
            g.sizeDelta = new Vector2(16f, 16f);
            g.localRotation = Quaternion.Euler(0f, 0f, iconRotation);
            glyph.raycastTarget = false;
            return img;
        }

        private static MinimapQuadGraphic QuadLayer(RectTransform parent, string name, Material material)
        {
            var rt = NewRect(parent, name);
            Stretch(rt);
            var g = rt.gameObject.AddComponent<MinimapQuadGraphic>();
            g.raycastTarget = false;
            if (material != null) g.material = material;
            return g;
        }

        private static void TopLeft(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI NewLabel(Transform parent, string name, float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            return tmp;
        }
    }
}
