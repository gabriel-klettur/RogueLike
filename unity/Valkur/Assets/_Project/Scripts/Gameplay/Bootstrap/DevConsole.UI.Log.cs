using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The console's log: what is in it, what is shown of it, and how it is drawn.
    ///
    /// <para>Three things changed with the window. The log is a list of TYPED lines rather
    /// than a list of strings, so a command echo, its output and an engine error can be told
    /// apart and coloured; Unity's own <c>Debug.Log</c> stream is captured into it, which is
    /// what makes the console usable as an in-game error viewer in a build where no Editor
    /// console exists; and it is drawn into ONE TextMeshPro object rather than one per line.
    /// That last point is the same lesson the Items editor table taught — 400 lines of one
    /// widget each is 400 uGUI objects rebuilt on every append, and the cost is volume, not
    /// anything wrong with a single row.</para>
    /// </summary>
    public partial class DevConsole
    {
        /// <summary>
        /// Where a line came from. The distinction is not decoration: the three engine kinds
        /// are FILTERABLE and the two console kinds never are, because hiding the output of
        /// the command you just typed is never what anybody meant by a filter.
        /// </summary>
        private enum ConsoleLineKind
        {
            Echo,          // "> spawn slime 3"
            Output,        // whatever the command printed
            UnityLog,
            UnityWarning,
            UnityError,
        }

        private readonly struct ConsoleLine
        {
            public readonly string Text;
            public readonly ConsoleLineKind Kind;
            public ConsoleLine(string text, ConsoleLineKind kind) { Text = text; Kind = kind; }
        }

        /// <summary>
        /// Five times the old IMGUI cap. That one existed because the whole log was
        /// re-concatenated into a single GUI.Label every OnGUI pass, i.e. several times per
        /// frame; the uGUI text is rebuilt only when something changed.
        /// </summary>
        private const int LOG_MAX_LINES = 400;

        private readonly List<ConsoleLine> _log = new List<ConsoleLine>();

        private TextMeshProUGUI _logText;
        private ScrollRect _logScroll;
        private bool _logDirty;

        // ── Engine-message filters ───────────────────────────────────────────
        // Warnings and errors default ON and plain logs OFF: this project gates its
        // high-volume development logging behind VerboseLog precisely because an always-on
        // stream trains the reader to scroll past everything, and a console that opens full
        // of routine chatter does the same thing.
        private bool _showUnityLog;
        private bool _showUnityWarning = true;
        private bool _showUnityError   = true;

        private Image _filterLogImg, _filterWarnImg, _filterErrorImg;
        private TextMeshProUGUI _filterLogTmp, _filterWarnTmp, _filterErrorTmp;

        private static readonly string HEX_ECHO    = ColorUtility.ToHtmlStringRGB(UITheme.ACCENT);
        private static readonly string HEX_OUTPUT  = ColorUtility.ToHtmlStringRGB(UITheme.TEXT_PRIMARY);
        private static readonly string HEX_LOG     = ColorUtility.ToHtmlStringRGB(UITheme.TEXT_SECONDARY);
        private static readonly string HEX_WARNING = ColorUtility.ToHtmlStringRGB(new Color(0.95f, 0.75f, 0.30f));
        private static readonly string HEX_ERROR   = ColorUtility.ToHtmlStringRGB(UITheme.DANGER);

        private readonly StringBuilder _renderBuffer = new StringBuilder(8192);

        // ------------------------------------------------------------------
        // Model
        // ------------------------------------------------------------------

        private void AppendLine(string text, ConsoleLineKind kind)
        {
            _log.Add(new ConsoleLine(text ?? string.Empty, kind));
            while (_log.Count > LOG_MAX_LINES) _log.RemoveAt(0);
            _logDirty = true;
        }

        private void MarkLogDirty() => _logDirty = true;

        private void ClearLog()
        {
            _log.Clear();
            _logDirty = true;
            FocusInput();
        }

        private void CopyLogToClipboard()
        {
            var sb = new StringBuilder(4096);
            for (int i = 0; i < _log.Count; i++)
            {
                if (!PassesFilters(_log[i])) continue;
                sb.AppendLine(_log[i].Text);
            }
            GUIUtility.systemCopyBuffer = sb.ToString();
            AppendLine($"[consola] {CountVisible()} lineas copiadas al portapapeles.", ConsoleLineKind.Output);
            FocusInput();
        }

        private int CountVisible()
        {
            int n = 0;
            for (int i = 0; i < _log.Count; i++) if (PassesFilters(_log[i])) n++;
            return n;
        }

        private bool PassesFilters(in ConsoleLine line)
        {
            switch (line.Kind)
            {
                case ConsoleLineKind.UnityLog     when !_showUnityLog:     return false;
                case ConsoleLineKind.UnityWarning when !_showUnityWarning: return false;
                case ConsoleLineKind.UnityError   when !_showUnityError:   return false;
            }

            string needle = _searchField != null ? _searchField.text : null;
            if (string.IsNullOrEmpty(needle)) return true;
            return line.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------
        // Engine message capture
        // ------------------------------------------------------------------

        /// <summary>
        /// Subscribed for the console's whole lifetime, not only while it is open — an error
        /// that fires before you think to open the console is exactly the one worth having.
        /// <c>logMessageReceived</c> is the main-thread callback; the threaded variant would
        /// hand us lines from a background thread and mutating the list from there is a race.
        /// </summary>
        private void BeginCapturingEngineLogs() => Application.logMessageReceived += OnEngineLog;

        private void StopCapturingEngineLogs() => Application.logMessageReceived -= OnEngineLog;

        private void OnEngineLog(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    AppendLine("[warn] " + condition, ConsoleLineKind.UnityWarning);
                    break;

                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    AppendLine("[error] " + condition, ConsoleLineKind.UnityError);
                    // One frame of stack, not the whole trace: an error with no origin is a
                    // riddle, and an error with forty lines of trace buries the next one.
                    string first = FirstStackFrame(stackTrace);
                    if (!string.IsNullOrEmpty(first))
                        AppendLine("        " + first, ConsoleLineKind.UnityError);
                    break;

                default:
                    AppendLine(condition, ConsoleLineKind.UnityLog);
                    break;
            }
        }

        private static string FirstStackFrame(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return null;
            int nl = stackTrace.IndexOf('\n');
            string line = nl >= 0 ? stackTrace.Substring(0, nl) : stackTrace;
            return line.Trim();
        }

        // ------------------------------------------------------------------
        // View
        // ------------------------------------------------------------------

        private void BuildLogView(Transform parent)
        {
            var (scroll, content) = UIFactory.MakeScrollView(parent, "LogView");

            var scrollLe = scroll.gameObject.GetComponent<LayoutElement>();
            if (scrollLe == null) scrollLe = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1f;
            scrollLe.preferredHeight = 120f;

            var img = scroll.GetComponent<Image>();
            if (img != null) img.color = new Color(0.05f, 0.05f, 0.07f, 0.92f);

            // The content's own VerticalLayoutGroup drives the text's height from its
            // preferred size, and the ContentSizeFitter drives the content from that. Both
            // come from MakeScrollView; what has to be stated is that the height is
            // CONTROLLED rather than expanded, or the single child is stretched to the
            // viewport and a long log stops scrolling.
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childControlHeight     = true;
                vlg.childForceExpandHeight = false;
                vlg.childControlWidth      = true;
                vlg.padding = new RectOffset(6, 6, 4, 4);
            }

            _logText = UILabel.Add(content, string.Empty, 11f);
            _logText.color = UITheme.TEXT_PRIMARY;
            _logText.alignment          = TextAlignmentOptions.TopLeft;
            _logText.richText           = true;
            _logText.enableWordWrapping = true;
            _logText.overflowMode       = TextOverflowModes.Overflow;
            _logText.raycastTarget      = false;

            _logScroll = scroll;
            UIFactory.AddVerticalScrollbar(scroll);
        }

        private void BuildFilterButtons(Transform row)
        {
            _filterLogImg = EditorUIHelpers.AddActionBtn(row, "LOG", TOOLBAR_H,
                () => ToggleFilter(ref _showUnityLog), out _filterLogTmp, 9f);
            SetButtonWidth(row, 44f);

            _filterWarnImg = EditorUIHelpers.AddActionBtn(row, "WARN", TOOLBAR_H,
                () => ToggleFilter(ref _showUnityWarning), out _filterWarnTmp, 9f);
            SetButtonWidth(row, 50f);

            _filterErrorImg = EditorUIHelpers.AddActionBtn(row, "ERROR", TOOLBAR_H,
                () => ToggleFilter(ref _showUnityError), out _filterErrorTmp, 9f);
            SetButtonWidth(row, 54f);

            RefreshFilterButtons();
        }

        private void ToggleFilter(ref bool flag)
        {
            flag = !flag;
            RefreshFilterButtons();
            _logDirty = true;
            FocusInput();
        }

        /// <summary>
        /// A filter button has to say which way it is set without being clicked. The Button's
        /// own <c>colors.normalColor</c> is what uGUI repaints on every pointer exit, so the
        /// state has to be written there and not only on the Image.
        /// </summary>
        private void RefreshFilterButtons()
        {
            PaintFilter(_filterLogImg,   _filterLogTmp,   _showUnityLog);
            PaintFilter(_filterWarnImg,  _filterWarnTmp,  _showUnityWarning);
            PaintFilter(_filterErrorImg, _filterErrorTmp, _showUnityError);
        }

        /// <summary>
        /// The state goes on the LABEL as well as the fill, and the fill alone is why: the
        /// kit's "active" tint is <see cref="UITheme.ACCENT_BG"/>, gold at alpha 0.15, which
        /// against the panel is a couple of percent of a channel — measured on the first
        /// capture, an off filter and an on one were not tellable apart. Colouring the text
        /// gold against muted grey is the half a reader actually sees.
        ///
        /// <para>It also has to be written to the Button's <c>colors.normalColor</c> and not
        /// only to the Image: uGUI repaints the target graphic from that on every pointer
        /// exit, so an Image-only write survives until the cursor next leaves the button.</para>
        /// </summary>
        private static void PaintFilter(Image img, TextMeshProUGUI label, bool on)
        {
            if (img != null)
            {
                var color = on ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL;
                img.color = color;

                var btn = img.GetComponent<Button>();
                if (btn != null)
                {
                    var c = btn.colors;
                    c.normalColor = color;
                    btn.colors    = c;
                }
            }

            if (label != null) label.color = on ? UITheme.ACCENT : UITheme.TEXT_MUTED;
        }

        private void RefreshLog(bool scrollToBottom)
        {
            if (_logText == null) return;
            if (!_logDirty && !scrollToBottom) return;
            _logDirty = false;

            bool wasAtBottom = scrollToBottom ||
                               (_logScroll != null && _logScroll.verticalNormalizedPosition <= 0.02f);

            _renderBuffer.Length = 0;
            for (int i = 0; i < _log.Count; i++)
            {
                var line = _log[i];
                if (!PassesFilters(line)) continue;
                _renderBuffer.Append("<color=#").Append(HexFor(line.Kind)).Append('>')
                             .Append(line.Text)
                             .Append("</color>\n");
            }
            _logText.text = _renderBuffer.ToString();

            if (!wasAtBottom || _logScroll == null) return;

            // The fitter has not run yet on the text just assigned, so scrolling now would
            // scroll the PREVIOUS content's extent.
            Canvas.ForceUpdateCanvases();
            _logScroll.verticalNormalizedPosition = 0f;
        }

        private static string HexFor(ConsoleLineKind kind) => kind switch
        {
            ConsoleLineKind.Echo         => HEX_ECHO,
            ConsoleLineKind.UnityLog     => HEX_LOG,
            ConsoleLineKind.UnityWarning => HEX_WARNING,
            ConsoleLineKind.UnityError   => HEX_ERROR,
            _                            => HEX_OUTPUT,
        };
    }
}
