using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.Frontend;

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
        // What a button's ColorBlock multiplies its (already tinted) frame by, per state.
        private static readonly Color ButtonRest = new Color(0.9f, 0.9f, 0.9f, 1f);
        private static readonly Color ButtonPressed = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color ButtonDisabled = new Color(0.6f, 0.6f, 0.6f, 0.4f);

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

        /// <summary>Frame width of a panel, in canvas units. The loading bar's is 3-6; a panel is bigger.</summary>
        public const float PanelFrameThickness = 5f;

        /// <summary>
        /// The panel body: the loading bar's housing at panel size — soft shadow, bevelled gold
        /// frame, recessed channel and corner brackets (<see cref="BevelFrameGraphic"/>). It used
        /// to be a 9-sliced sprite from the point-filtered atlas, which is the one surface of the
        /// menu that could never match the bar: a sprite's bevel is a few texels stretched to
        /// whatever size the kit asks for.
        /// </summary>
        public static BevelFrameGraphic Panel(string name, Transform parent, MenuArt art, MenuStyle style)
        {
            var frame = BevelFrameGraphic.Create(parent, name);
            frame.Thickness = PanelFrameThickness;
            frame.ShadowScale = 1.6f;
            frame.Brackets = true;
            frame.Tint = style.Gold;
            return frame;
        }

        /// <summary>The title band of a panel, with the gold rule under it.</summary>
        public static TextMeshProUGUI PanelHeader(Transform panel, MenuArt art, MenuStyle style, string text)
        {
            // The band itself is drawn by the panel's frame, so it can never disagree with the
            // bevel it sits inside; this rect only holds the title. Its rule, and the two gems
            // where the rule meets the frame, are the loading bar's etapa divider laid flat.
            var frameT = panel.Find("Frame");
            var frame = frameT != null ? frameT.GetComponent<BevelFrameGraphic>() : null;
            if (frame != null)
            {
                frame.HeaderHeight = Mathf.Max(0f, style.titleBarHeight - frame.Thickness);
                frame.HeaderGemLit = 0.85f;
            }

            var rt = Rect("Header", panel);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -style.titleBarHeight);
            rt.offsetMax = Vector2.zero;

            var labelRt = Stretch("Label", rt);
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

        /// <summary>A notched rule across a panel, at <paramref name="y"/> below its top.</summary>
        public static FrontendRuleGraphic Divider(Transform parent, MenuArt art, MenuStyle style, float y)
        {
            var rule = FrontendRuleGraphic.Create(parent, "Divider", vertical: false, style.Gold);
            var rt = rule.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-24f, 8f);
            return rule;
        }

        /// <summary>
        /// A real button: a small bevelled frame whose face carries the tint in the selected
        /// row's light profile, and a label in the ink that reads on it.
        ///
        /// <para><b>The tint is in the MESH; the ColorBlock only brightens or dims it.</b>
        /// <c>UIButton.SetTint</c>'s lesson still applies — a block assigned while the Selectable
        /// sits idle is not pushed to the CanvasRenderer — so the block holds near-white values
        /// and multiplies the whole frame, bevel included, which is what a lit button looks like.
        /// Hovering also raises the bevel's glow, the bar's own "something happened" signal.</para>
        /// </summary>
        public static Button Button(string name, Transform parent, MenuArt art, MenuStyle style,
                                    string label, Color tint, UnityEngine.Events.UnityAction onClick)
        {
            var frame = BevelFrameGraphic.Create(parent, name);
            frame.Thickness = 3f;
            frame.ShadowScale = 0.45f;
            frame.Brackets = false;
            frame.Tint = style.Gold;
            frame.Face = new Color(tint.r, tint.g, tint.b, 1f);
            frame.raycastTarget = true;
            var btn = frame.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            var colours = btn.colors;
            colours.normalColor = ButtonRest;
            colours.highlightedColor = Color.white;
            colours.pressedColor = ButtonPressed;
            colours.selectedColor = ButtonRest;
            colours.disabledColor = ButtonDisabled;
            colours.fadeDuration = 0.08f;
            btn.colors = colours;
            if (onClick != null) btn.onClick.AddListener(onClick);
            OnHover(frame.gameObject, _ => frame.Glow = 1f, EventTriggerType.PointerEnter);
            OnHover(frame.gameObject, _ => frame.Glow = 0f, EventTriggerType.PointerExit);

            var labelRt = Stretch("Label", frame.transform);
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
