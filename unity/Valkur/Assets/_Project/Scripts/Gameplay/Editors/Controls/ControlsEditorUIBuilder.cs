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
    ///
    /// <para>THE CAPTURE SCRIM IS THE FIRST SIBLING, NOT THE LAST, and that single fact is
    /// what makes "click a drawn key to bind it" work. It shipped last — full-screen, a
    /// raycast target, with a Button that cancels — so it sat OVER the board it was inviting
    /// the author to click, and every click on a key cap cancelled the capture instead. It was
    /// intermittent, too, which is worse: <see cref="DraggablePanel.OnPointerDown"/> calls
    /// <c>SetAsLastSibling</c>, so an author who had dragged the board panel once had raised
    /// it above the scrim and the click DID land. The prompt is a separate banner that is
    /// raised to the top and takes no raycasts at all, so what the author reads is always on
    /// top and what they click is always the board.</para>
    /// </summary>
    public static class ControlsEditorUIBuilder
    {
        public const float BOARD_W = 1092f;

        /// <summary>
        /// Sized to the drawn board rather than guessed. The ISO keyboard is 224 px tall and
        /// the mouse 230, so the devices need 246 with their margin; the rest is the header,
        /// the two toolbars, the legend and the status line. At the previous 500 the panel
        /// carried a quarter of its height as empty backdrop under the keyboard, which reads as
        /// a panel that failed to load something rather than as one that fits.
        /// </summary>
        public const float BOARD_H = 424f;
        public const float LIST_W  = 400f;
        public const float LIST_H  = 620f;

        /// <summary>
        /// Bumped whenever the DEFAULT geometry of these panels changes. A stored workspace
        /// carrying an older value has its panel geometry discarded once — see
        /// <c>ControlsRuntimeEditor.RestoreWorkspace</c>.
        /// </summary>
        public const string LAYOUT_VERSION = "3";

        /// <summary>Everything in the board panel that is NOT the drawn board: the layout and
        /// clash toolbar, the context strip, the legend, the status line, and the padding and
        /// spacing the panel's own layout group adds around them.</summary>
        private const float CHROME_H = 154f;

        /// <summary>Exposed so a test derives the board's room from the same number the builder
        /// uses, rather than restating it and drifting.</summary>
        public static float ChromeHeight => CHROME_H;

        private const float RESIZE_HANDLE_PX = 14f;
        private const float CONTEXT_STRIP_H  = 24f;
        private const float LEGEND_STRIP_H   = 20f;

        public sealed class Callbacks
        {
            public Action<string> OnContext;
            public Action<KeyboardLayoutKind> OnLayoutTab;
            public Action OnSave;
            public Action OnReset;
            public Action OnCancelCapture;
            public Action<string> OnSearch;
            public Action OnToggleHelp;
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

            /// <summary>One tab per context: the two play postures, the shared-editor view,
            /// then one per editor that owns tools of its own. Built at runtime because the
            /// editor list is a runtime registry — a fixed pair of stance tabs could never
            /// show the editor layouts.</summary>
            public readonly List<ContextTab> ContextTabs = new List<ContextTab>();

            public Button IsoTab;
            public Button AnsiTab;
            public TextMeshProUGUI IsoTabLabel;
            public TextMeshProUGUI AnsiTabLabel;

            public RectTransform ContextStrip;
            public RectTransform LegendStrip;

            public TextMeshProUGUI Status;
            public TextMeshProUGUI Conflicts;
            public TextMeshProUGUI Detail;
            public TMP_InputField Search;
            public Button SaveButton;
            public TextMeshProUGUI SaveLabel;
            public Button ResetButton;
            public Button HelpButton;
            public GameObject HelpOverlay;

            /// <summary>Full-screen click-to-cancel behind BOTH panels. See the class note.</summary>
            public GameObject CaptureScrim;

            /// <summary>The "press a key" banner: top-most, and never a raycast target.</summary>
            public GameObject CapturePrompt;
            public TextMeshProUGUI CaptureText;
        }

        public static UIRefs BuildAll(Transform canvasT, Callbacks cb)
        {
            var refs = new UIRefs();

            // Order matters only as a starting point — SetCaptureVisible re-asserts it every
            // time, because DraggablePanel raises whichever panel was last clicked.
            BuildCaptureScrim(canvasT, cb, refs);
            BuildBoardPanel(canvasT, cb, refs);
            BuildListPanel(canvasT, cb, refs);
            BuildCapturePrompt(canvasT, refs);
            return refs;
        }

        /// <summary>
        /// Shows or hides the capture affordances, re-asserting the sibling order every time.
        ///
        /// <para>Re-asserting is the whole point. The scrim must be BEHIND both panels so a
        /// click on a drawn key reaches the key, and the prompt must be ABOVE them so the
        /// instruction is readable — and a single <c>SetAsLastSibling</c> from
        /// <see cref="DraggablePanel"/> reorders all three. Doing it on every open makes the
        /// arrangement independent of what the author dragged five minutes ago.</para>
        /// </summary>
        public static void SetCaptureVisible(UIRefs refs, bool visible, string message = "")
        {
            if (refs?.CaptureScrim == null || refs.CapturePrompt == null) return;

            refs.CaptureScrim.SetActive(visible);
            refs.CapturePrompt.SetActive(visible);
            if (!visible) return;

            refs.CaptureScrim.transform.SetAsFirstSibling();
            refs.CapturePrompt.transform.SetAsLastSibling();
            if (refs.CaptureText != null) refs.CaptureText.text = message ?? "";
        }

        // ── Capture affordances ──────────────────────────────────────────────

        private static void BuildCaptureScrim(Transform canvasT, Callbacks cb, UIRefs refs)
        {
            var go = UIFactory.CreateUI("CaptureScrim", canvasT);
            EditorUIHelpers.StretchFill(go);
            var img = go.AddComponent<Image>();
            img.color = UITheme.OVERLAY_SCRIM;
            img.raycastTarget = true;

            // Clicking anywhere that is NOT a panel cancels. A capture with no visible way out
            // is how an author ends up pressing Escape and rebinding Escape.
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => cb?.OnCancelCapture?.Invoke());

            go.SetActive(false);
            refs.CaptureScrim = go;
        }

        private static void BuildCapturePrompt(Transform canvasT, UIRefs refs)
        {
            var go = UIFactory.CreateUI("CapturePrompt", canvasT);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 28f);
            rt.sizeDelta = new Vector2(760f, 96f);

            var img = go.AddComponent<Image>();
            img.color = UITheme.BG_SURFACE;
            // NOT a raycast target: the banner sits above everything so it can be read, and a
            // readable thing that eats clicks is exactly the defect the scrim used to have.
            img.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = UITheme.ACCENT;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            refs.CaptureText = Label(go.transform, "", 17f, UITheme.TEXT_PRIMARY, flexible: false);
            var trt = refs.CaptureText.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12f, 8f);
            trt.offsetMax = new Vector2(-12f, -8f);
            refs.CaptureText.alignment = TextAlignmentOptions.Center;
            refs.CaptureText.raycastTarget = false;

            go.SetActive(false);
            refs.CapturePrompt = go;
        }

        // ── Board panel ──────────────────────────────────────────────────────

        private static void BuildBoardPanel(Transform canvasT, Callbacks cb, UIRefs refs)
        {
            // POSITIVE offsets. ApplyPanelDock negates them itself — TopLeft yields
            // (xOff, -yOff) — so passing -56 put the panel FIFTY-SIX PIXELS ABOVE THE TOP EDGE
            // and took its header, its title and its close button off screen with it. Both
            // panels shipped that way; every other editor in the project passes a positive
            // gap (Camera's TOP = MENUBAR_H + GAP, Boss's PANEL_TOP_OFFSET).
            refs.BoardPanel = EditorUIHelpers.MakeDropPanel(
                "ControlsBoardPanel", canvasT,
                TileEditorUIHelpers.PanelDock.TopLeft,
                16f, 56f, BOARD_W, BOARD_H,
                "Teclado y raton",
                out var content, out var drag);
            refs.BoardDrag = drag;

            // These two panels ARE the editor, not dropdowns it can live without, and nothing
            // in here could bring one back — the only toolbar lives inside the board panel. The
            // close button's own doc names this exact case. It is set before the first frame,
            // which is when the chrome is built.
            drag.ShowCloseButton = false;

            // Row 1: the layout choice, the help toggle and the clash read-out.
            var toolbar = Row(content, 28f);
            refs.IsoTab  = Tab(toolbar, "ISO",  62f, () => cb?.OnLayoutTab?.Invoke(KeyboardLayoutKind.Iso),  out refs.IsoTabLabel);
            refs.AnsiTab = Tab(toolbar, "ANSI", 62f, () => cb?.OnLayoutTab?.Invoke(KeyboardLayoutKind.Ansi), out refs.AnsiTabLabel);
            refs.HelpButton = EditorUIHelpers.MakeButton(toolbar.transform, "?",
                () => cb?.OnToggleHelp?.Invoke(), 22f, 12f);
            var helpLe = refs.HelpButton.gameObject.GetComponent<LayoutElement>()
                      ?? refs.HelpButton.gameObject.AddComponent<LayoutElement>();
            helpLe.preferredWidth = 26f;
            helpLe.flexibleWidth = 0f;
            Spacer(toolbar, 12f);
            refs.Conflicts = Label(toolbar.transform, "", 11f, UITheme.TEXT_SECONDARY, flexible: true);

            // Row 2: the context strip, horizontally scrollable because a long editor name
            // must not push the tail off the end in silence.
            refs.ContextStrip = BuildStrip(content, "ContextStrip", CONTEXT_STRIP_H);

            // Row 3: what the nine key-cap tints mean. Without it the colours are a code the
            // author has to reverse-engineer from the keys they already know.
            refs.LegendStrip = BuildStrip(content, "LegendStrip", LEGEND_STRIP_H);
            PopulateLegend(refs.LegendStrip);

            // NO layout group and NO ContentSizeFitter — see MakeAbsoluteScrollView.
            // ControlsKeyboardView places every cap at an absolute position from the row model,
            // and a layout group stacks them from the top-left and puts the numpad where
            // Escape belongs.
            var (_, scrollContent) = MakeAbsoluteScrollView(content, "BoardScroll",
                BOARD_H - TileEditorUIHelpers.PANEL_HDR_H - CHROME_H,
                horizontal: true, vertical: true, flexible: true,
                background: UITheme.INPUT_BOARD_BG);
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

            AddResizeHandle(refs.BoardPanel, new Vector2(560f, 300f));
        }

        /// <summary>
        /// The context strip: Guerra / Paz / Editores, then one tab per editor that owns tools.
        /// Built from <paramref name="contexts"/> rather than a literal list, because an editor
        /// context only exists once its editor has registered.
        /// </summary>
        public static void PopulateContextStrip(UIRefs refs, Callbacks cb,
                                                IReadOnlyList<string> contexts)
        {
            if (refs?.ContextStrip == null) return;

            // Object.Destroy is an ERROR in Edit Mode, not a warning, and this runs on every
            // open — including from an EditMode fixture.
            foreach (var tab in refs.ContextTabs)
            {
                if (tab.Button == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(tab.Button.gameObject);
                else                       UnityEngine.Object.DestroyImmediate(tab.Button.gameObject);
            }
            refs.ContextTabs.Clear();

            float x = 0f;
            foreach (var contextId in contexts)
            {
                string label = InputContexts.Label(contextId).ToUpperInvariant();
                float w = Mathf.Max(64f, 11f + label.Length * 7.2f);

                string captured = contextId;
                var btn = TabAbsolute(refs.ContextStrip, label, x, w, CONTEXT_STRIP_H,
                                      () => cb?.OnContext?.Invoke(captured), out var tmp);
                refs.ContextTabs.Add(new ContextTab { ContextId = contextId, Button = btn, Label = tmp });
                x += w + 4f;
            }

            refs.ContextStrip.sizeDelta = new Vector2(Mathf.Max(0f, x - 4f), CONTEXT_STRIP_H);
        }

        /// <summary>What each key-cap tint means. One swatch per category, in enum order so
        /// the legend and <c>TintFor</c> cannot disagree about which colour is which.</summary>
        private static void PopulateLegend(RectTransform strip)
        {
            float x = 0f;
            x = LegendChip(strip, x, UITheme.INPUT_FREE, "libre");
            foreach (InputActionCategory c in Enum.GetValues(typeof(InputActionCategory)))
                x = LegendChip(strip, x, TintForCategory(c), CategoryLabel(c));
            x = LegendChip(strip, x, UITheme.DANGER, "doble disparo");
            x = LegendChip(strip, x, UITheme.WARNING, "modificador");
            strip.sizeDelta = new Vector2(Mathf.Max(0f, x - 6f), LEGEND_STRIP_H);
        }

        private static float LegendChip(RectTransform strip, float x, Color tint, string label)
        {
            var swatch = UIFactory.CreateUI("Swatch_" + label, strip);
            var srt = (RectTransform)swatch.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(x, -3f);
            srt.sizeDelta = new Vector2(14f, 14f);
            var img = swatch.AddComponent<Image>();
            img.color = tint;
            img.raycastTarget = false;

            float w = 10f + label.Length * 5.6f;
            var text = UIFactory.CreateUI("Legend_" + label, strip);
            var trt = (RectTransform)text.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(x + 17f, 0f);
            trt.sizeDelta = new Vector2(w, LEGEND_STRIP_H);
            var tmp = text.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 9f;
            tmp.color = UITheme.TEXT_SECONDARY;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;

            return x + 17f + w + 8f;
        }

        /// <summary>The key-cap tint for a category. Shared with the painting pass so the
        /// legend can never describe a colour the board does not use.</summary>
        public static Color TintForCategory(InputActionCategory category) => category switch
        {
            InputActionCategory.Movement    => UITheme.INPUT_MOVEMENT,
            InputActionCategory.Traversal   => UITheme.INPUT_TRAVERSAL,
            InputActionCategory.Combat      => UITheme.INPUT_COMBAT,
            InputActionCategory.Spell       => UITheme.INPUT_SPELL,
            InputActionCategory.Interaction => UITheme.INPUT_INTERACT,
            InputActionCategory.Interface   => UITheme.INPUT_INTERFACE,
            InputActionCategory.Editor      => UITheme.INPUT_EDITOR,
            InputActionCategory.System      => UITheme.INPUT_SYSTEM,
            _                               => UITheme.INPUT_FREE,
        };

        public static string CategoryLabel(InputActionCategory category) => category switch
        {
            InputActionCategory.Movement    => "movimiento",
            InputActionCategory.Traversal   => "desplazam.",
            InputActionCategory.Combat      => "combate",
            InputActionCategory.Spell       => "hechizos",
            InputActionCategory.Interaction => "interaccion",
            InputActionCategory.Interface   => "interfaz",
            InputActionCategory.Editor      => "editores",
            InputActionCategory.System      => "sistema",
            _                               => "otros",
        };

        private static RectTransform BuildStrip(Transform content, string name, float height)
        {
            var (_, stripContent) = MakeAbsoluteScrollView(content, name, height + 6f,
                horizontal: true, vertical: false, flexible: false);
            return stripContent;
        }

        /// <summary>
        /// A scroll view whose CONTENT is positioned by hand, with no layout group and no
        /// ContentSizeFitter on it.
        ///
        /// <para>THIS EXISTS BECAUSE THE SHARED HELPER CANNOT BE USED HERE, AND SAYING SO IN A
        /// COMMENT WAS NOT ENOUGH. <see cref="UIFactory.MakeScrollView"/> puts a
        /// <c>VerticalLayoutGroup</c> and a <c>ContentSizeFitter</c> on the content it returns,
        /// which is right for a list of rows and catastrophic for a drawn keyboard: the layout
        /// group stacks every child from the top-left and forces its width, so the keyboard,
        /// the mouse, the seven context tabs and the eleven legend swatches were all going to
        /// be dealt out in a single column. Three surfaces in this editor carried a code
        /// comment claiming there was no layout group on them while the helper was quietly
        /// adding one to each.</para>
        ///
        /// <para>It survived every test because <b>uGUI does not lay out in EditMode</b> — the
        /// documented trap in this repo. Reading back <c>sizeDelta</c> returns the value that
        /// was written, never what a layout pass would have done with it, so a fixture can
        /// measure a board that is correct in the numbers and scrambled on screen. The only
        /// thing that catches this class of defect is a rendered frame.</para>
        ///
        /// <para>The content is anchored TOP-LEFT rather than stretched, which is the other
        /// half. With the helper's stretch anchors and a 0.5 pivot, a <c>sizeDelta.x</c> of
        /// 1062 makes a rect 1062 px WIDER THAN ITS PARENT — measured at 2072 — so a
        /// horizontal scroll had nothing sane to scroll.</para>
        /// </summary>
        private static (ScrollRect scroll, RectTransform content) MakeAbsoluteScrollView(
            Transform parent, string name, float height, bool horizontal, bool vertical,
            bool flexible, Color? background = null)
        {
            var scrollGo = UIFactory.CreateUI(name, parent);

            var le = scrollGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = flexible ? 1f : 0f;

            scrollGo.AddComponent<RectMask2D>();
            var bg = scrollGo.AddComponent<Image>();
            bg.color = background ?? UITheme.BG_SURFACE;

            var viewport = UIFactory.CreateUI("Viewport", scrollGo.transform);
            EditorUIHelpers.StretchFill(viewport);

            var contentGo = UIFactory.CreateUI("Content", viewport.transform);
            var rt = contentGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(1f, height);

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.content = rt;
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.horizontal = horizontal;
            sr.vertical = vertical;
            sr.scrollSensitivity = 20f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            return (sr, rt);
        }

        private static Button TabAbsolute(RectTransform parent, string label, float x, float width,
                                          float height, Action onClick, out TextMeshProUGUI labelOut)
        {
            var btn = EditorUIKitTab(parent, label, onClick, out labelOut);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, height);
            return btn;
        }

        // ── Action list ──────────────────────────────────────────────────────

        private static void BuildListPanel(Transform canvasT, Callbacks cb, UIRefs refs)
        {
            refs.ListPanel = EditorUIHelpers.MakeDropPanel(
                "ControlsListPanel", canvasT,
                TileEditorUIHelpers.PanelDock.TopRight,
                16f, 56f, LIST_W, LIST_H,
                "Acciones",
                out var content, out var drag);
            refs.ListDrag = drag;
            drag.ShowCloseButton = false;

            // onValueChanged as well as the helper's commit callback: a search that only
            // filters on Enter is a search the author does not believe is working.
            refs.Search = EditorUIHelpers.AddInputField(content, "", v => cb?.OnSearch?.Invoke(v));
            if (refs.Search != null)
            {
                refs.Search.onValueChanged.AddListener(v => cb?.OnSearch?.Invoke(v));
                AddPlaceholder(refs.Search, "Buscar accion o tecla...");
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

            // The row controls its children's width and does not force-expand them, so a button
            // that declares no width is laid out at its MINIMUM — measured on screen as a
            // one-character column with GUARDAR and VALORES POR DEFECTO overprinting each other
            // vertically down the panel. Sharing the row is a flexibleWidth, not a preferred one:
            // the panel is resizable.
            var buttons = Row(content, 26f);
            refs.SaveButton  = EditorUIHelpers.MakeButton(buttons.transform, "GUARDAR", () => cb?.OnSave?.Invoke());
            refs.SaveLabel   = refs.SaveButton.GetComponentInChildren<TextMeshProUGUI>();
            refs.ResetButton = EditorUIHelpers.MakeDangerButton(buttons.transform, "POR DEFECTO", () => cb?.OnReset?.Invoke());
            StretchInRow(refs.SaveButton, 1f);
            StretchInRow(refs.ResetButton, 1.4f);

            AddResizeHandle(refs.ListPanel, new Vector2(320f, 260f));
        }

        /// <summary>
        /// Gives a commit-style input field the placeholder it does not ship with.
        ///
        /// <para><c>UIInputField.AddCommit</c> — the flavour every editor property row uses —
        /// builds no placeholder at all; only <c>MakeWithPlaceholder</c> does, and that one
        /// takes no commit callback. So setting <c>.placeholder</c>'s text was writing to null
        /// and the search box rendered as a bare dark bar with nothing saying it was a search
        /// box. Building the child here keeps the commit semantics and the hint.</para>
        /// </summary>
        private static void AddPlaceholder(TMP_InputField input, string text)
        {
            if (input == null || input.placeholder != null) return;

            var host = input.textViewport != null ? input.textViewport : (RectTransform)input.transform;
            var go = UIFactory.CreateUI("Placeholder", host);
            EditorUIHelpers.StretchFill(go);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 11f;
            tmp.color = UITheme.TEXT_MUTED;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;

            // Behind the typed text, or the hint draws over what the author types.
            go.transform.SetAsFirstSibling();
            input.placeholder = tmp;
        }

        // ── Help overlay ─────────────────────────────────────────────────────

        /// <summary>
        /// The shared tutorial overlay, moved to the bottom-left instead of its default
        /// top-right dock: that dock is where the action list lives, and a help panel that
        /// covers the thing it is explaining is worse than none.
        /// </summary>
        public static GameObject BuildHelpOverlay(Transform parent)
        {
            var go = TutorialOverlay.Build(parent, "CONTROLES", new[]
            {
                ("Click tecla",  "que hay encima / asignar durante la captura"),
                ("...",          "reasignar esa accion"),
                ("x",            "dejar la accion sin tecla"),
                ("Raton",        "click der. / central / rueda durante la captura"),
                ("G / P",        "en que postura vive la accion"),
                ("Ctrl+Z / Y",   "deshacer / rehacer la ultima reasignacion"),
                ("Ctrl+S",       "guardar el perfil"),
                ("Esc",          "cancelar la captura, o cerrar"),
                ("Aro rojo",     "dos acciones reales en una tecla"),
                ("Aro ambar",    "una es un modificador"),
            });

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot     = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(16f, 16f);
            go.SetActive(false);
            return go;
        }

        // ── Small primitives ─────────────────────────────────────────────────

        private static void AddResizeHandle(GameObject panelRoot, Vector2 minSize)
        {
            var panelRt = panelRoot != null ? panelRoot.GetComponent<RectTransform>() : null;
            if (panelRt == null) return;

            var go = UIFactory.CreateUI("ResizeHandle", panelRoot.transform);
            var rt = go.GetComponent<RectTransform>();
            // The grip's corner is fixed by the PANEL's pivot: both panels here are
            // top-anchored, so the bottom-right corner is the one that can move both axes.
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(RESIZE_HANDLE_PX, RESIZE_HANDLE_PX);

            var tri = go.AddComponent<TriangleHandleGraphic>();
            tri.color = UITheme.BORDER;
            tri.raycastTarget = true;

            var handle = go.AddComponent<PanelResizeHandle>();
            handle.Target  = panelRt;
            handle.MinSize = minSize;
        }

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

        /// <summary>Lets a button share a HorizontalLayoutGroup row proportionally.</summary>
        private static void StretchInRow(Button btn, float weight)
        {
            if (btn == null) return;
            var le = btn.gameObject.GetComponent<LayoutElement>()
                  ?? btn.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 60f;
            le.flexibleWidth = weight;
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

        /// <summary>
        /// Puts both panels back at their shipped size and dock.
        ///
        /// <para>Needed because the workspace layer cannot tell a size the author CHOSE from a
        /// size that merely used to be the default. A document written before the board grew
        /// pinned it at 1010x470, which is 66 px narrower than the drawn devices now need — so
        /// the mouse was cut in half for anyone who had ever opened this editor, with no hint
        /// that a stale default rather than a preference was doing it.</para>
        /// </summary>
        public static void ApplyDefaultGeometry(UIRefs refs)
        {
            if (refs == null) return;
            if (refs.BoardPanel != null)
                EditorUIHelpers.ApplyPanelDock(refs.BoardPanel.GetComponent<RectTransform>(),
                    TileEditorUIHelpers.PanelDock.TopLeft, 16f, 56f, BOARD_W, BOARD_H);
            if (refs.ListPanel != null)
                EditorUIHelpers.ApplyPanelDock(refs.ListPanel.GetComponent<RectTransform>(),
                    TileEditorUIHelpers.PanelDock.TopRight, 16f, 56f, LIST_W, LIST_H);
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
