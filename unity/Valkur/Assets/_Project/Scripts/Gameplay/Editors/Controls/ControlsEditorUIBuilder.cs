using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Controls
{
    /// <summary>
    /// Builds the Controls editor's two panels: the drawn board on the left and the action
    /// list on the right.
    ///
    /// <para>Two panels rather than one because they answer different questions and are read
    /// at different moments. The board answers "what is on this key / what is free"; the list
    /// answers "where is this verb". Merging them makes a 1300 px slab that cannot be moved
    /// out of the way of the thing being configured.</para>
    /// </summary>
    public static class ControlsEditorUIBuilder
    {
        public const float BOARD_W = 1010f;
        public const float BOARD_H = 470f;
        public const float LIST_W  = 372f;
        public const float LIST_H  = 600f;

        public sealed class Callbacks
        {
            public Action<string> OnContext;
            public Action<KeyboardLayoutKind> OnLayoutTab;
            public Action OnSave;
            public Action OnReset;
            public Action OnCancelCapture;
            public Action<string> OnSearch;
        }

        /// <summary>A context tab: the id it selects, its button and its label.</summary>
        public sealed class ContextTab
        {
            public string ContextId;
            public Button Button;
            public TextMeshProUGUI Label;
        }

        public sealed class UIRefs
        {
            public GameObject BoardPanel;
            public GameObject ListPanel;
            public DraggablePanel BoardDrag;
            public DraggablePanel ListDrag;

            public RectTransform BoardHost;      // keyboard + mouse go under this
            public RectTransform ListContent;    // one row per action
            public ScrollRect ListScroll;

            /// <summary>One tab per context: the two play postures, then one per registered
            /// editor. Built at runtime because the editor list is a runtime registry — a
            /// fixed pair of stance tabs could never show the sixteen editor layouts.</summary>
            public readonly List<ContextTab> ContextTabs = new List<ContextTab>();

            public Button IsoTab;
            public Button AnsiTab;
            public TextMeshProUGUI IsoTabLabel;
            public TextMeshProUGUI AnsiTabLabel;

            public RectTransform ContextStrip;

            public TextMeshProUGUI Status;
            public TextMeshProUGUI Conflicts;
            public TextMeshProUGUI Detail;
            public TMP_InputField Search;
            public Button SaveButton;
            public Button ResetButton;
            public GameObject CaptureOverlay;
            public TextMeshProUGUI CaptureText;
        }

        public static UIRefs BuildAll(Transform canvasT, Callbacks cb)
        {
            var refs = new UIRefs();
            BuildBoardPanel(canvasT, cb, refs);
            BuildListPanel(canvasT, cb, refs);
            BuildCaptureOverlay(canvasT, cb, refs);
            return refs;
        }

        // ── Board panel ──────────────────────────────────────────────────────

        private static void BuildBoardPanel(Transform canvasT, Callbacks cb, UIRefs refs)
        {
            refs.BoardPanel = EditorUIHelpers.MakeDropPanel(
                "ControlsBoardPanel", canvasT,
                TileEditorUIHelpers.PanelDock.TopLeft,
                16f, -56f, BOARD_W, BOARD_H,
                "Teclado y raton",
                out var content, out var drag);
            refs.BoardDrag = drag;

            // Row 1: the layout choice and the conflict read-out. Row 2: the context strip,
            // which is horizontally scrollable because there are eighteen of them and a
            // toolbar that silently truncates would hide the editors at the end of the list.
            var toolbar = Row(content, 28f);
            refs.IsoTab  = Tab(toolbar, "ISO",  62f, () => cb?.OnLayoutTab?.Invoke(KeyboardLayoutKind.Iso),  out refs.IsoTabLabel);
            refs.AnsiTab = Tab(toolbar, "ANSI", 62f, () => cb?.OnLayoutTab?.Invoke(KeyboardLayoutKind.Ansi), out refs.AnsiTabLabel);
            Spacer(toolbar, 18f);
            refs.Conflicts = Label(toolbar.transform, "", 11f, UITheme.TEXT_SECONDARY, flexible: true);

            refs.ContextStrip = BuildContextStrip(content, cb, refs);

            // The board host is a plain rect inside a scroll view. NO layout group and NO
            // ContentSizeFitter: ControlsKeyboardView places every cap at an absolute position
            // from the row model, and a layout group would stack them from the top-left and
            // put the numpad where Escape belongs.
            var (scroll, scrollContent) = EditorUIHelpers.MakeScrollView(content, "BoardScroll",
                BOARD_H - TileEditorUIHelpers.PANEL_HDR_H - 66f);
            scroll.horizontal = true;
            scroll.vertical = true;
            refs.BoardHost = scrollContent;

            refs.Status = Label(content, "", 11f, UITheme.TEXT_SECONDARY, flexible: false);
            refs.Status.alignment = TextAlignmentOptions.TopLeft;
            var statusLe = refs.Status.gameObject.AddComponent<LayoutElement>();
            statusLe.preferredHeight = 30f;
            // A LayoutElement that sets only preferredHeight does NOT stop the row expanding:
            // uGUI resolves each property from the highest-priority component that supplies
            // one, so flexibleHeight stays -1 and the parent group's value wins. The chat
            // input row lost 48 px to exactly this.
            statusLe.flexibleHeight = 0f;
        }

        /// <summary>
        /// The context strip: Guerra / Paz, then one tab per registered editor. Built from
        /// <paramref name="contexts"/> rather than a literal pair, because an editor context
        /// only exists once its editor has registered — and a hardcoded list would have to be
        /// edited every time an editor is added, which is the positional tax this project
        /// already pays four times over for AnimState.
        /// </summary>
        public static void PopulateContextStrip(UIRefs refs, Callbacks cb,
                                                IReadOnlyList<string> contexts)
        {
            if (refs?.ContextStrip == null) return;

            foreach (var tab in refs.ContextTabs)
                if (tab.Button != null) UnityEngine.Object.Destroy(tab.Button.gameObject);
            refs.ContextTabs.Clear();

            float x = 0f;
            foreach (var contextId in contexts)
            {
                string label = InputContexts.Label(contextId).ToUpperInvariant();
                float w = Mathf.Max(64f, 11f + label.Length * 7.2f);

                string captured = contextId;
                var btn = TabAbsolute(refs.ContextStrip, label, x, w,
                                      () => cb?.OnContext?.Invoke(captured), out var tmp);
                refs.ContextTabs.Add(new ContextTab { ContextId = contextId, Button = btn, Label = tmp });
                x += w + 4f;
            }

            refs.ContextStrip.sizeDelta = new Vector2(Mathf.Max(0f, x - 4f), CONTEXT_STRIP_H);
        }

        private const float CONTEXT_STRIP_H = 24f;

        private static RectTransform BuildContextStrip(Transform content, Callbacks cb, UIRefs refs)
        {
            // A scroll view rather than a layout group: the strip is placed by index at
            // absolute positions so a long editor name cannot push the tail off the end
            // silently, and a ContentSizeFitter would shrink it to whatever is realised.
            var (scroll, stripContent) = EditorUIHelpers.MakeScrollView(content, "ContextStrip",
                CONTEXT_STRIP_H + 12f);
            scroll.horizontal = true;
            scroll.vertical = false;
            return stripContent;
        }

        private static Button TabAbsolute(RectTransform parent, string label, float x, float width,
                                          Action onClick, out TextMeshProUGUI labelOut)
        {
            var btn = EditorUIKitTab(parent, label, onClick, out labelOut);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, CONTEXT_STRIP_H);
            return btn;
        }

        // ── Action list ──────────────────────────────────────────────────────

        private static void BuildListPanel(Transform canvasT, Callbacks cb, UIRefs refs)
        {
            refs.ListPanel = EditorUIHelpers.MakeDropPanel(
                "ControlsListPanel", canvasT,
                TileEditorUIHelpers.PanelDock.TopRight,
                -16f, -56f, LIST_W, LIST_H,
                "Acciones",
                out var content, out var drag);
            refs.ListDrag = drag;

            // onValueChanged as well as the helper's commit callback: a search that only
            // filters on Enter is a search the author does not believe is working.
            refs.Search = EditorUIHelpers.AddInputField(content, "", v => cb?.OnSearch?.Invoke(v));
            if (refs.Search != null)
            {
                refs.Search.onValueChanged.AddListener(v => cb?.OnSearch?.Invoke(v));
                if (refs.Search.placeholder is TextMeshProUGUI ph) ph.text = "Buscar accion...";
            }

            refs.Detail = Label(content, "", 11f, UITheme.ACCENT, flexible: false);
            var detailLe = refs.Detail.gameObject.AddComponent<LayoutElement>();
            detailLe.preferredHeight = 34f;
            detailLe.flexibleHeight = 0f;

            var (scroll, listContent) = EditorUIHelpers.MakeScrollView(content, "ActionScroll",
                LIST_H - TileEditorUIHelpers.PANEL_HDR_H - 150f);
            scroll.horizontal = false;
            refs.ListScroll = scroll;
            refs.ListContent = listContent;

            // CONFIGURE, never add: UIFactory.MakeScrollView already puts a
            // VerticalLayoutGroup and a ContentSizeFitter on the content it returns, and
            // LayoutGroup is [DisallowMultipleComponent] — so AddComponent returned NULL and
            // the next line threw a NullReferenceException that took the whole panel with it.
            // The editor opened to nothing and logged one line.
            var vlg = listContent.gameObject.GetComponent<VerticalLayoutGroup>()
                   ?? listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            var fitter = listContent.gameObject.GetComponent<ContentSizeFitter>()
                      ?? listContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var buttons = Row(content, 26f);
            refs.SaveButton  = EditorUIHelpers.MakeButton(buttons.transform, "GUARDAR", () => cb?.OnSave?.Invoke());
            refs.ResetButton = EditorUIHelpers.MakeDangerButton(buttons.transform, "VALORES POR DEFECTO", () => cb?.OnReset?.Invoke());
        }

        // ── Capture overlay ──────────────────────────────────────────────────

        private static void BuildCaptureOverlay(Transform canvasT, Callbacks cb, UIRefs refs)
        {
            var go = UIFactory.CreateUI("CaptureOverlay", canvasT);
            EditorUIHelpers.StretchFill(go);
            var img = go.AddComponent<Image>();
            img.color = UITheme.OVERLAY_SCRIM;
            img.raycastTarget = true;

            // Clicking anywhere cancels. A capture with no visible way out is how an author
            // ends up pressing Escape and rebinding Escape.
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => cb?.OnCancelCapture?.Invoke());

            refs.CaptureText = Label(go.transform, "", 20f, UITheme.TEXT_PRIMARY, flexible: false);
            var rt = refs.CaptureText.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(700f, 120f);
            refs.CaptureText.alignment = TextAlignmentOptions.Center;
            refs.CaptureText.raycastTarget = false;

            go.SetActive(false);
            refs.CaptureOverlay = go;
        }

        // ── Small primitives ─────────────────────────────────────────────────

        private static GameObject Row(Transform parent, float height)
        {
            var go = UIFactory.CreateUI("Row", parent);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            return go;
        }

        private static void Spacer(GameObject row, float width)
        {
            var go = UIFactory.CreateUI("Spacer", row.transform);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
        }

        private static Button Tab(GameObject row, string label, float width, Action onClick,
                                  out TextMeshProUGUI labelOut)
        {
            var btn = EditorUIKitTab(row.transform, label, onClick, out labelOut);
            var le = btn.gameObject.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
            return btn;
        }

        /// <summary>
        /// A tab is an Image+Button parent with a TMP CHILD, never both on one GameObject —
        /// Image and TextMeshProUGUI together throw a NullReferenceException in this project.
        /// </summary>
        private static Button EditorUIKitTab(Transform parent, string label, Action onClick,
                                             out TextMeshProUGUI labelOut)
        {
            var go = UIFactory.CreateUI("Tab_" + label, parent);
            var img = go.AddComponent<Image>();
            img.color = UITheme.BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var textGo = UIFactory.CreateUI("Label", go.transform);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            labelOut = textGo.AddComponent<TextMeshProUGUI>();
            labelOut.text = label;
            labelOut.fontSize = 11f;
            labelOut.fontStyle = FontStyles.Bold;
            labelOut.alignment = TextAlignmentOptions.Center;
            labelOut.color = UITheme.TEXT_SECONDARY;
            labelOut.raycastTarget = false;
            return btn;
        }

        private static TextMeshProUGUI Label(Transform parent, string text, float size,
                                             Color color, bool flexible)
        {
            var go = UIFactory.CreateUI("Label", parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.raycastTarget = false;
            if (flexible)
            {
                var le = go.AddComponent<LayoutElement>();
                le.flexibleWidth = 1f;
            }
            return tmp;
        }

        /// <summary>Paints a tab as selected or not. Shared so the two tab pairs cannot drift
        /// into looking like different controls.</summary>
        public static void PaintTab(Button tab, TextMeshProUGUI label, bool selected)
        {
            if (tab == null) return;
            var img = tab.GetComponent<Image>();
            if (img != null) img.color = selected ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
            if (label != null) label.color = selected ? UITheme.TEXT_PRIMARY : UITheme.TEXT_SECONDARY;
        }
    }
}
