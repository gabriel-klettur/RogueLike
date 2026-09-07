using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The console's window: a real uGUI panel instead of the IMGUI slab it drew for the
    /// life of the project.
    ///
    /// <para>The old surface was a fixed 640x280 rectangle nailed to the bottom of the
    /// screen — it could not be moved, resized or closed with the mouse, it ignored
    /// <see cref="UITheme"/>, and clicking anywhere outside it dismissed it, which is a
    /// gesture no other window in this game uses. It is rebuilt here on the same chrome
    /// every runtime editor uses (<c>EditorUIHelpers.MakeDropPanel</c> supplies the header,
    /// the outline, the <see cref="DraggablePanel"/> and the X), plus the
    /// <see cref="PanelResizeHandle"/> the chat window and the four resizable editors share.
    /// One implementation of "a window", not a second one.</para>
    ///
    /// <para>Layout is remembered in <c>PlayerPrefs</c> under <c>valkur.devconsole.*</c>,
    /// the convention <c>MusicPlayerHUD</c> established for a gameplay panel — the editor
    /// workspace layer cannot be used here, because every entry point on
    /// <c>IEditorWorkspaceService</c> is typed on an editor and keyed on its EditorName.</para>
    /// </summary>
    public partial class DevConsole
    {
        // ── Geometry ─────────────────────────────────────────────────────────
        private const float PANEL_DEFAULT_W = 780f;
        private const float PANEL_DEFAULT_H = 360f;
        private const float PANEL_MIN_W     = 380f;
        private const float PANEL_MIN_H     = 190f;
        private const float PANEL_MAX_W     = 2400f;
        private const float PANEL_MAX_H     = 1400f;
        private const float TOOLBAR_H       = 22f;
        private const float INPUT_ROW_H     = 26f;
        private const float HINT_H          = 14f;
        private const float SUBMIT_BTN_W    = 74f;
        private const float TOOL_BTN_W      = 66f;
        private const float RESIZE_GRIP_SIZE = 16f;

        /// <summary>
        /// Above the chat panel (200) and above every editor canvas, because the console is
        /// the thing you open when something else is misbehaving — a window that can be
        /// covered by the system it is being used to inspect is the wrong way round.
        /// </summary>
        private const int CANVAS_SORT_ORDER = 800;

        // ── Persistence ──────────────────────────────────────────────────────
        private const string PREF_PREFIX  = "valkur.devconsole.";
        private const string PREF_X       = PREF_PREFIX + "x";
        private const string PREF_Y       = PREF_PREFIX + "y";
        private const string PREF_W       = PREF_PREFIX + "w";
        private const string PREF_H       = PREF_PREFIX + "h";
        private const string PREF_OPACITY = PREF_PREFIX + "opacity";

        /// <summary>
        /// The rungs the opacity button cycles. Discrete rather than a slider: a slider is a
        /// control that has to be dragged accurately in a window whose whole point is to be
        /// out of the way, and four steps cover "solid", "I want to see the fight behind it"
        /// and everything between.
        ///
        /// <para>An INSTANCE field although nothing ever writes it. A
        /// <c>static readonly</c> array is still mutable through its elements, which is the
        /// shape <c>DomainReloadStaticResetTests</c> exists to refuse and which cannot be
        /// reset in a form that scanner recognises — and a table on a singleton has no reason
        /// to be static in the first place.</para>
        /// </summary>
        private readonly float[] _opacitySteps = { 1f, 0.85f, 0.7f, 0.55f };

        // ── Live UI ──────────────────────────────────────────────────────────
        private Canvas _canvas;
        private GameObject _panelRoot;
        private RectTransform _panelRt;
        private DraggablePanel _drag;
        private CanvasGroup _panelGroup;

        private TMP_InputField _inputField;
        private TMP_InputField _searchField;
        private TextMeshProUGUI _opacityLabel;

        private int _opacityStep;
        private Vector4 _lastLayout;
        private bool _layoutDirty;
        private bool _uiBuilt;

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        private void EnsureUI()
        {
            if (_uiBuilt && _panelRoot != null) return;

            // The panel is uGUI, so it needs an EventSystem to receive a single click. In a
            // scene that has one this adopts it; the console can also be the first thing up
            // in a stripped scene, and a window nothing can click is worse than no window.
            PersistentEventSystem.Ensure();

            _canvas = UICanvasFactory.CreateOverlayCanvas("DevConsoleCanvas", CANVAS_SORT_ORDER);
            _canvas.transform.SetParent(transform, false);

            _panelRoot = EditorUIHelpers.MakeDropPanel(
                "DevConsolePanel", _canvas.transform,
                TileEditorUIHelpers.PanelDock.TopLeft,
                0f, 0f, PANEL_DEFAULT_W, PANEL_DEFAULT_H,
                "CONSOLA",
                out var content, out _drag);

            _panelRt    = _panelRoot.GetComponent<RectTransform>();
            _panelGroup = _panelRoot.GetComponent<CanvasGroup>();

            // The X hides the panel and calls back; the console owns the open flag, so the
            // callback is where the two are reconciled. EnsureChrome is explicit because the
            // button is otherwise built in OnEnable, and building it here keeps that
            // independent of when the panel first becomes active.
            _drag.PersistenceKey = "DevConsolePanel";
            _drag.OnClose = HandlePanelClosed;
            _drag.EnsureChrome();

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.padding = new RectOffset(6, 6, 4, 6);
                vlg.spacing = 4f;
                vlg.childForceExpandHeight = false;
                vlg.childControlHeight     = true;
            }

            BuildToolbar(content);
            BuildLogView(content);
            BuildInputRow(content);
            BuildHintRow(content);
            BuildSuggestions();
            BuildResizeGrip();

            RestoreLayout();
            _uiBuilt = true;
        }

        private void BuildToolbar(Transform parent)
        {
            var row = UIFactory.CreateUI("Toolbar", parent);
            PinRowHeight(row, TOOLBAR_H);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            // Search first and flexible: it is the control that gets used while READING, and
            // the buttons beside it are one-shot actions.
            _searchField = UIInputField.AddCommit(row.transform, string.Empty, null, TOOLBAR_H, 10f);
            var searchLe = _searchField.GetComponent<LayoutElement>();
            searchLe.flexibleWidth  = 1f;
            searchLe.preferredWidth = 120f;
            AddPlaceholder(_searchField, "buscar en el log...");
            _searchField.onValueChanged.AddListener(_ => MarkLogDirty());

            BuildFilterButtons(row.transform);

            EditorUIHelpers.AddActionBtn(row.transform, "COPIAR", TOOLBAR_H,
                CopyLogToClipboard, out _, 9f);
            SetButtonWidth(row.transform, TOOL_BTN_W);

            EditorUIHelpers.AddActionBtn(row.transform, "LIMPIAR", TOOLBAR_H,
                ClearLog, out _, 9f);
            SetButtonWidth(row.transform, TOOL_BTN_W);

            EditorUIHelpers.AddActionBtn(row.transform, OpacityLabelText(), TOOLBAR_H,
                CycleOpacity, out _opacityLabel, 9f);
            SetButtonWidth(row.transform, 46f);
        }

        private void BuildInputRow(Transform parent)
        {
            var row = UIFactory.CreateUI("InputRow", parent);
            PinRowHeight(row, INPUT_ROW_H);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            _inputField = UIInputField.AddCommit(row.transform, string.Empty, null, INPUT_ROW_H, 12f);
            var le = _inputField.GetComponent<LayoutElement>();
            le.flexibleWidth  = 1f;
            le.preferredWidth = 200f;
            AddPlaceholder(_inputField, "escribe un comando y pulsa Enter — Tab autocompleta");

            _inputField.lineType         = TMP_InputField.LineType.SingleLine;
            _inputField.richText         = false;
            _inputField.customCaretColor = true;
            _inputField.caretColor       = UITheme.ACCENT;
            _inputField.caretWidth       = 2;
            _inputField.selectionColor   = UITheme.ACCENT_BG;
            _inputField.onValueChanged.AddListener(OnInputChanged);

            // onSubmit rather than onEndEdit: the latter also fires on focus loss, so
            // clicking the log to scroll it would run whatever half-typed line was in the box.
            _inputField.onSubmit.AddListener(_ => SubmitInputBuffer());

            EditorUIHelpers.AddActionBtn(row.transform, "ENVIAR", INPUT_ROW_H,
                SubmitInputBuffer, out _, 10f);
            SetButtonWidth(row.transform, SUBMIT_BTN_W);
        }

        private void BuildHintRow(Transform parent)
        {
            var hint = UILabel.Add(parent, string.Empty, 9f);
            hint.color = UITheme.TEXT_MUTED;
            // Spelled out rather than drawn with arrow glyphs, for the same reason the
            // suggestion marker is a ">": U+2191 / U+2193 are not in the shipped atlas.
            hint.text = "Tab autocompleta  ·  Arriba/Abajo historial  ·  Enter ejecuta  ·  ~ o Esc cierra";
            hint.alignment     = TextAlignmentOptions.MidlineLeft;
            hint.raycastTarget = false;

            PinRowHeight(hint.gameObject, HINT_H);
        }

        private void BuildResizeGrip()
        {
            var gripGo = new GameObject("ResizeGrip", typeof(RectTransform));
            gripGo.transform.SetParent(_panelRoot.transform, false);

            var gripRt = (RectTransform)gripGo.transform;
            gripGo.AddComponent<LayoutElement>().ignoreLayout = true;
            gripRt.anchorMin = new Vector2(1f, 0f);
            gripRt.anchorMax = new Vector2(1f, 0f);
            gripRt.pivot     = new Vector2(1f, 0f);
            gripRt.anchoredPosition = Vector2.zero;
            gripRt.sizeDelta = new Vector2(RESIZE_GRIP_SIZE, RESIZE_GRIP_SIZE);

            var graphic   = gripGo.AddComponent<TriangleHandleGraphic>();
            graphic.color = new Color(0.55f, 0.58f, 0.68f, 0.85f);

            // BottomRight both times: MakeDropPanel docks TopLeft, so the panel's pivot is its
            // top-left corner and the two edges it is free to move are the right and bottom
            // ones. A grip in any other corner would advertise a drag that cannot happen.
            graphic.Corner = ResizeGripCorner.BottomRight;

            var grip     = gripGo.AddComponent<PanelResizeHandle>();
            grip.Target  = _panelRt;
            grip.Corner  = ResizeGripCorner.BottomRight;
            grip.MinSize = new Vector2(PANEL_MIN_W, PANEL_MIN_H);
            grip.MaxSize = new Vector2(PANEL_MAX_W, PANEL_MAX_H);
            grip.Resized += _ => _layoutDirty = true;
        }

        /// <summary>
        /// <c>UIInputField.AddCommit</c> builds no placeholder — only
        /// <c>UIInputField.MakeWithPlaceholder</c> does, and that one takes no commit
        /// callback. Writing to <c>field.placeholder.text</c> on the first is a write to null
        /// and the row renders as a bare dark bar with nothing saying what it is for.
        /// </summary>
        private static void AddPlaceholder(TMP_InputField field, string text)
        {
            var viewport = field.textViewport;
            if (viewport == null) return;

            var go = UIFactory.CreateUI("Placeholder", viewport);
            UIFactory.StretchFill(go);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = field.textComponent != null ? field.textComponent.fontSize : 11f;
            tmp.fontStyle          = FontStyles.Italic;
            tmp.color              = UITheme.TEXT_MUTED;
            tmp.alignment          = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Truncate;
            tmp.raycastTarget      = false;

            // Behind the live text, or a long line would draw over the hint.
            go.transform.SetAsFirstSibling();
            field.placeholder = tmp;
        }

        /// <summary>
        /// Fixes a row's height, and states the SECOND half that actually makes it fixed.
        ///
        /// <para>uGUI resolves each layout property independently, taking it from the
        /// highest-priority component that supplies one. A LayoutElement (priority 1) wins the
        /// preferred height while leaving <c>flexibleHeight</c> at its unset -1 — so the value
        /// really used comes from the HorizontalLayoutGroup on the SAME GameObject, which
        /// reports 1 whenever <c>childForceExpandHeight</c> is on. Measured on the first build
        /// of this panel: a toolbar asking for 22 px was laid out at 66, and the input row at
        /// 70 against its 26, both of them eating the log view. The chat panel's input row hit
        /// exactly this and it is worth the helper so the third row cannot.</para>
        /// </summary>
        private static void PinRowHeight(GameObject row, float height)
        {
            var le = row.GetComponent<LayoutElement>();
            if (le == null) le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight       = height;
            le.flexibleHeight  = 0f;
        }

        /// <summary>
        /// Gives the button just added to <paramref name="row"/> an explicit width. A button
        /// in a HorizontalLayoutGroup with childControlWidth and no childForceExpandWidth is
        /// laid out at its MINIMUM otherwise — which for a sprite-less Image is zero, and the
        /// label collapses to a one-character column.
        /// </summary>
        private static void SetButtonWidth(Transform row, float width)
        {
            var last = row.GetChild(row.childCount - 1);
            var le   = last.GetComponent<LayoutElement>();
            if (le == null) le = last.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
        }

        // ------------------------------------------------------------------
        // Show / hide
        // ------------------------------------------------------------------

        private void ShowPanel()
        {
            EnsureUI();
            _panelRoot.SetActive(true);
            _panelRoot.transform.SetAsLastSibling();
            _drag.MarkOpened();
            ClampIntoView();
            MarkLogDirty();
            RefreshLog(scrollToBottom: true);
            FocusInput();
        }

        private void HidePanel()
        {
            HideSuggestions();
            if (_panelRoot != null) _panelRoot.SetActive(false);
            SaveLayout();
        }

        /// <summary>
        /// The panel's own X was clicked. It has already hidden itself; all that is left is
        /// to bring the console's open flag and its listeners into agreement — going back
        /// through <see cref="SetOpen"/> unconditionally would call
        /// <c>DraggablePanel.ClosePanel</c> a second time and re-raise its callback.
        /// </summary>
        private void HandlePanelClosed()
        {
            if (!_open) return;
            SetOpen(false);
        }

        /// <summary>True while the caret is in the toolbar's search box, not the command line.</summary>
        private bool SearchFieldFocused => _searchField != null && _searchField.isFocused;

        private void FocusInput()
        {
            if (_inputField == null) return;
            _inputField.ActivateInputField();
            _inputField.caretPosition = _inputField.text != null ? _inputField.text.Length : 0;
        }

        // ------------------------------------------------------------------
        // Layout persistence
        // ------------------------------------------------------------------

        private void RestoreLayout()
        {
            float w = Mathf.Clamp(PlayerPrefs.GetFloat(PREF_W, PANEL_DEFAULT_W), PANEL_MIN_W, PANEL_MAX_W);
            float h = Mathf.Clamp(PlayerPrefs.GetFloat(PREF_H, PANEL_DEFAULT_H), PANEL_MIN_H, PANEL_MAX_H);
            _panelRt.sizeDelta = new Vector2(w, h);

            bool hasPos = PlayerPrefs.HasKey(PREF_X) && PlayerPrefs.HasKey(PREF_Y);
            _panelRt.anchoredPosition = hasPos
                ? new Vector2(PlayerPrefs.GetFloat(PREF_X), PlayerPrefs.GetFloat(PREF_Y))
                : DefaultPosition(w, h);

            _opacityStep = Mathf.Clamp(PlayerPrefs.GetInt(PREF_OPACITY, 0), 0, _opacitySteps.Length - 1);
            ApplyOpacity();
            ClampIntoView();
            _lastLayout  = CurrentLayout();
            _layoutDirty = false;
        }

        /// <summary>
        /// Bottom-centre of the canvas, which is where the IMGUI console always drew and
        /// therefore where a returning user looks for it. Expressed against the canvas rect
        /// rather than <c>Screen</c>, because the scaler resolves to 1600x800 regardless of
        /// the real resolution.
        /// </summary>
        private Vector2 DefaultPosition(float w, float h)
        {
            var canvasRt = (RectTransform)_canvas.transform;
            float cw = canvasRt.rect.width  > 1f ? canvasRt.rect.width  : 1600f;
            float ch = canvasRt.rect.height > 1f ? canvasRt.rect.height : 800f;
            return new Vector2((cw - w) * 0.5f, -(ch - h - 24f));
        }

        private void SaveLayout()
        {
            if (_panelRt == null) return;
            PlayerPrefs.SetFloat(PREF_X, _panelRt.anchoredPosition.x);
            PlayerPrefs.SetFloat(PREF_Y, _panelRt.anchoredPosition.y);
            PlayerPrefs.SetFloat(PREF_W, _panelRt.sizeDelta.x);
            PlayerPrefs.SetFloat(PREF_H, _panelRt.sizeDelta.y);
            PlayerPrefs.SetInt(PREF_OPACITY, _opacityStep);
            _lastLayout  = CurrentLayout();
            _layoutDirty = false;
        }

        private Vector4 CurrentLayout() => new Vector4(
            _panelRt.anchoredPosition.x, _panelRt.anchoredPosition.y,
            _panelRt.sizeDelta.x, _panelRt.sizeDelta.y);

        /// <summary>
        /// Writes the layout once a drag or a resize has STOPPED, never per frame: this is a
        /// PlayerPrefs write, and PlayerPrefs is a file.
        /// </summary>
        private void TickLayoutPersistence()
        {
            if (_panelRt == null) return;

            var now = CurrentLayout();
            if (now != _lastLayout) { _lastLayout = now; _layoutDirty = true; return; }
            if (_layoutDirty) SaveLayout();
        }

        /// <summary>
        /// Pulls a panel back on screen when the window has shrunk since the layout was
        /// saved. Without it a console remembered against a 2560-wide window is unreachable
        /// — and unreachable includes its header, so it cannot be dragged back either.
        /// </summary>
        private void ClampIntoView()
        {
            var canvasRt = (RectTransform)_canvas.transform;
            float cw = canvasRt.rect.width;
            float ch = canvasRt.rect.height;
            if (cw < 1f || ch < 1f) return;

            var size = _panelRt.sizeDelta;
            size.x = Mathf.Min(size.x, cw);
            size.y = Mathf.Min(size.y, ch);
            _panelRt.sizeDelta = size;

            var p = _panelRt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, 0f, Mathf.Max(0f, cw - size.x));
            p.y = Mathf.Clamp(p.y, -Mathf.Max(0f, ch - size.y), 0f);
            _panelRt.anchoredPosition = p;
        }

        // ------------------------------------------------------------------
        // Opacity
        // ------------------------------------------------------------------

        private void CycleOpacity()
        {
            _opacityStep = (_opacityStep + 1) % _opacitySteps.Length;
            ApplyOpacity();
            _layoutDirty = true;
            FocusInput();
        }

        private void ApplyOpacity()
        {
            if (_panelGroup != null) _panelGroup.alpha = _opacitySteps[_opacityStep];
            if (_opacityLabel != null) _opacityLabel.text = OpacityLabelText();
        }

        private string OpacityLabelText() =>
            Mathf.RoundToInt(_opacitySteps[Mathf.Clamp(_opacityStep, 0, _opacitySteps.Length - 1)] * 100f) + "%";
    }
}
