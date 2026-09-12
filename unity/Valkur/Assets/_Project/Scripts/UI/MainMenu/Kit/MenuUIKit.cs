using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.MainMenu.Kit
{
    /// <summary>
    /// The pre-game menus' shared widgets: a panel, a row, a button, a divider, a key cap.
    ///
    /// <para><b>Why it exists.</b> The shipped menus placed every pixel with absolute arithmetic
    /// repeated per screen — five separate sets of <c>rowH / padX / padY / gap / panelW</c>
    /// constants that did not agree (row heights of 42, 52, 40, 44, 37 and 31 px for the same
    /// kind of row), and each builder reimplemented the pill, the accent bar and the invisible
    /// hit target from scratch. Adding an option meant recomputing a panel height by hand.</para>
    ///
    /// <para><b>Every size comes from <see cref="MenuStyle"/> and every bitmap from
    /// <see cref="MenuArt"/></b>, so the menu has one row height, one panel frame and one
    /// selection colour, and changing any of them is an edit to an asset.</para>
    ///
    /// <para><b>Exactly one object per row catches the pointer.</b> Labels, values, bars and
    /// pills all opt out of raycasting; a label that did not would take the hover away from the
    /// row it sits on, which is the defect the Sound panel's comment already describes working
    /// around by hand.</para>
    /// </summary>
    public static class MenuUIKit
    {
        /// <summary>A bare RectTransform child. The root of everything else here.</summary>
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A child stretched over its parent.</summary>
        public static RectTransform Stretch(string name, Transform parent, float inset = 0f)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>
        /// An Image from the menu atlas, never a raycast target unless asked — and STRETCHED over
        /// its parent unless the caller says otherwise.
        ///
        /// <para><b>The stretch is the fix for a measured defect, not a convenience.</b>
        /// <see cref="Rect"/> leaves Unity's own defaults on a new RectTransform, which are
        /// <b>100x100 centred</b> — a size nobody asked for and no call site states. Every caller
        /// that anchors the rect afterwards overwrote it and never noticed; the three that did not
        /// drew a 100x100 block: the class selector's stat bars put a track and a fill of that size
        /// over each of the six cards, twelve slabs that covered the class names and the words
        /// Vida / Ataque / Armadura. Every EditMode assertion about that panel was green, because
        /// uGUI performs no layout there — it took a rendered frame.</para>
        ///
        /// <para>Defaulting to the stretch closes the whole class rather than the three sites: a
        /// caller that wants its own geometry sets it on the returned rect exactly as before, and
        /// a child of a layout group is sized by the group whatever this says. What is no longer
        /// possible is an image whose size was never a decision.</para>
        /// </summary>
        public static Image Sprite(string name, Transform parent, Sprite sprite, Color colour,
                                   Image.Type type = Image.Type.Sliced, bool raycast = false)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = colour;
            img.type = sprite != null ? type : Image.Type.Simple;
            img.raycastTarget = raycast;
            img.pixelsPerUnitMultiplier = 1f;
            if (type == Image.Type.Sliced) img.fillCenter = true;
            return img;
        }

        /// <summary>
        /// The panel body: frame, bevel, chamfered corners and a recessed fill, as ONE 9-sliced
        /// sprite. What it replaces is a bare <c>Image</c> with a flat colour and square corners.
        /// </summary>
        public static Image Panel(string name, Transform parent, MenuArt art, MenuStyle style)
            => Sprite(name, parent, art.Panel, Color.white);

        /// <summary>The title band of a panel, with the gold rule under it.</summary>
        public static TextMeshProUGUI PanelHeader(Transform panel, MenuArt art, MenuStyle style, string text)
        {
            var bar = Sprite("Header", panel, art.Header, Color.white);
            var rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -style.titleBarHeight);
            rt.offsetMax = Vector2.zero;

            var labelRt = Stretch("Label", bar.transform);
            labelRt.offsetMin = new Vector2(12f, 3f);
            labelRt.offsetMax = new Vector2(-12f, -2f);
            return MenuTypography.Label(labelRt.gameObject, style, text, style.titleFontSize,
                                        style.Gold, TextAlignmentOptions.Center, bold: true);
        }

        /// <summary>
        /// The hint bar at the foot of a panel. It sits INSIDE the panel rather than floating
        /// under it: the shipped hints were placed by absolute offset from a panel height that
        /// each builder recomputed, so every one of them was a different distance from its panel.
        /// </summary>
        public static TextMeshProUGUI PanelHint(Transform panel, MenuStyle style, string text)
        {
            var rt = Rect("Hint", panel);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(10f, 6f);
            rt.offsetMax = new Vector2(-10f, style.hintBarHeight - 6f);
            return MenuTypography.Label(rt.gameObject, style, text, style.hintFontSize,
                                        style.TextMuted, TextAlignmentOptions.Center);
        }

        /// <summary>A one-pixel rule across a panel.</summary>
        public static Image Divider(Transform parent, MenuArt art, MenuStyle style, float y)
        {
            var img = Sprite("Divider", parent, art.Divider, new Color(style.Gold.r, style.Gold.g,
                                                                       style.Gold.b, 0.22f));
            var rt = (RectTransform)img.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-24f, 3f);
            return img;
        }

        /// <summary>
        /// A real button: a chamfered slab, a label, and a colour block that Unity will actually
        /// render. <c>UIButton.SetTint</c>'s lesson applies — a ColorBlock assigned while the
        /// Selectable sits idle is not pushed to the CanvasRenderer, so the graphic is held white
        /// and the block does the tinting.
        /// </summary>
        public static Button Button(string name, Transform parent, MenuArt art, MenuStyle style,
                                    string label, Color tint, UnityEngine.Events.UnityAction onClick)
        {
            var img = Sprite(name, parent, art.Pill, Color.white, Image.Type.Sliced, raycast: true);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colours = btn.colors;
            colours.normalColor = tint;
            colours.highlightedColor = Brighten(tint, 1.28f);
            colours.pressedColor = Brighten(tint, 0.82f);
            colours.selectedColor = tint;
            colours.disabledColor = new Color(tint.r, tint.g, tint.b, 0.35f);
            colours.fadeDuration = 0.08f;
            btn.colors = colours;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var labelRt = Stretch("Label", img.transform);
            labelRt.offsetMin = new Vector2(8f, 2f);
            labelRt.offsetMax = new Vector2(-8f, -2f);
            MenuTypography.Label(labelRt.gameObject, style, label, style.rowFontSize - 4f,
                                 ReadableOn(tint, style), TextAlignmentOptions.Center, bold: true);
            return btn;
        }

        /// <summary>
        /// The text colour that reads on <paramref name="background"/>. Measured rather than
        /// assumed: the shipped menu wrote gold on a gold pill and got 4.15:1, worse than the
        /// 9.61:1 of the row that was NOT selected.
        /// </summary>
        public static Color ReadableOn(Color background, MenuStyle style)
            => Luminance(background) > 0.42f ? style.textOnSelection : style.TextPrimary;

        /// <summary>WCAG relative luminance. Used to choose a text colour, and by the tests.</summary>
        public static float Luminance(Color c)
        {
            float R = Linear(c.r), G = Linear(c.g), B = Linear(c.b);
            return 0.2126f * R + 0.7152f * G + 0.0722f * B;
        }

        /// <summary>WCAG contrast ratio between two opaque colours.</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            float hi = Mathf.Max(la, lb), lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        /// <summary>Composites <paramref name="over"/> onto <paramref name="under"/> by alpha.</summary>
        public static Color Composite(Color over, Color under)
        {
            float a = Mathf.Clamp01(over.a);
            return new Color(over.r * a + under.r * (1f - a),
                             over.g * a + under.g * (1f - a),
                             over.b * a + under.b * (1f - a), 1f);
        }

        private static float Linear(float c)
            => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        public static Color Brighten(Color c, float f)
            => new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);

        /// <summary>Adds a pointer-enter listener without clobbering an existing EventTrigger.</summary>
        public static void OnHover(GameObject target, UnityEngine.Events.UnityAction<BaseEventData> action,
                                   EventTriggerType type = EventTriggerType.PointerEnter)
        {
            var trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        /// <summary>
        /// <c>Object.Destroy</c> is an outright ERROR in Edit Mode, and every one of these
        /// widgets is built by an EditMode fixture at some point. One helper rather than the
        /// branch repeated at every teardown.
        /// </summary>
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }
    }
}
