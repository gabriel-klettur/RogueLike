using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UIKit;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The autocomplete popup.
    ///
    /// <para>Completion is not new — <c>Tab</c> has resolved command names and delegated to a
    /// command's own <c>Completer</c> since the registry was written. What it did with the
    /// result was print <c>"Matches: a, b, c"</c> INTO THE LOG, which is the one place the
    /// reader is not looking while typing, and it then had to guess which of the three to
    /// finish typing. The candidates are drawn over the input line now, with each command's
    /// one-line help beside it, and Tab takes the highlighted one.</para>
    ///
    /// <para>It is also the half of the feature that was unreachable: the console raises
    /// <c>InputBlocker</c> when it opens, and Tab is not on the always-allowed list, so
    /// <c>KeyboardInputManager</c> refused the key for as long as the panel was up. See
    /// <c>KeyboardInputManager.WasKeyPressedThisFrameIgnoringBlock</c>.</para>
    /// </summary>
    public partial class DevConsole
    {
        private const int   SUGGEST_MAX_ROWS = 8;
        private const float SUGGEST_ROW_H    = 15f;
        private const float SUGGEST_PADDING  = 6f;

        private GameObject _suggestRoot;
        private RectTransform _suggestRt;
        private TextMeshProUGUI _suggestText;

        private readonly List<string> _suggestions    = new List<string>();
        private readonly List<string> _suggestionHelp = new List<string>();
        private int _suggestIndex;

        /// <summary>True while the popup is up and owns the arrow keys and Tab.</summary>
        private bool SuggestionsVisible => _suggestRoot != null && _suggestRoot.activeSelf;

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        private void BuildSuggestions()
        {
            _suggestRoot = UIFactory.CreateUI("Suggestions", _panelRoot.transform);
            _suggestRoot.AddComponent<LayoutElement>().ignoreLayout = true;

            _suggestRt = _suggestRoot.GetComponent<RectTransform>();
            _suggestRt.anchorMin = new Vector2(0f, 0f);
            _suggestRt.anchorMax = new Vector2(0f, 0f);
            _suggestRt.pivot     = new Vector2(0f, 0f);

            var bg   = _suggestRoot.AddComponent<Image>();
            bg.color = UITheme.BG_ELEVATED;

            var outline           = _suggestRoot.AddComponent<Outline>();
            outline.effectColor   = UITheme.BORDER;
            outline.effectDistance = new Vector2(1f, 1f);

            // Nothing here is clickable on purpose. A popup that steals the pointer while the
            // caret is in the box is the shape that eats the very click it invites, and the
            // keyboard is where a console user already is.
            bg.raycastTarget = false;

            _suggestText = UILabel.Add(_suggestRoot.transform, string.Empty, 10f);
            _suggestText.color = UITheme.TEXT_SECONDARY;
            UIFactory.StretchFill(_suggestText.gameObject);
            var textRt = _suggestText.rectTransform;
            textRt.offsetMin = new Vector2(SUGGEST_PADDING, SUGGEST_PADDING * 0.5f);
            textRt.offsetMax = new Vector2(-SUGGEST_PADDING, -SUGGEST_PADDING * 0.5f);

            _suggestText.alignment          = TextAlignmentOptions.TopLeft;
            _suggestText.richText           = true;
            _suggestText.enableWordWrapping = false;
            _suggestText.overflowMode       = TextOverflowModes.Truncate;
            _suggestText.raycastTarget      = false;

            _suggestRoot.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Candidate collection
        // ------------------------------------------------------------------

        private void OnInputChanged(string value)
        {
            RecomputeSuggestions(value);
        }

        private void RecomputeSuggestions(string input)
        {
            _suggestions.Clear();
            _suggestionHelp.Clear();
            _suggestIndex = 0;

            if (string.IsNullOrWhiteSpace(input)) { HideSuggestions(); return; }

            var tokens = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) { HideSuggestions(); return; }

            bool completingCommand = tokens.Length == 1 && !input.EndsWith(" ", StringComparison.Ordinal);
            if (completingCommand)
            {
                string prefix = tokens[0].TrimStart('/');
                foreach (var cmd in AllCommands)
                {
                    if (!cmd.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    _suggestions.Add(cmd.Name);
                    _suggestionHelp.Add(cmd.Help);
                }
            }
            else
            {
                // An ARGUMENT. Only the command itself knows what its arguments can be, which
                // is what the Completer on the registry record is for; a command without one
                // simply offers nothing rather than offering command names in an argument
                // position.
                string cmdName = tokens[0].TrimStart('/');
                if (TryResolve(cmdName, out var cmd) && cmd.Completer != null)
                {
                    var matches = cmd.Completer(tokens);
                    if (matches != null)
                        foreach (var m in matches) { _suggestions.Add(m); _suggestionHelp.Add(string.Empty); }
                }
            }

            if (_suggestions.Count == 0) { HideSuggestions(); return; }
            ShowSuggestions();
        }

        private void ShowSuggestions()
        {
            if (_suggestRoot == null) return;
            _suggestRoot.SetActive(true);
            _suggestRoot.transform.SetAsLastSibling();
            LayoutSuggestions();
            RenderSuggestions();
        }

        private void HideSuggestions()
        {
            if (_suggestRoot != null) _suggestRoot.SetActive(false);
        }

        /// <summary>
        /// Sized to what it actually holds and parked directly above the input row. The panel
        /// is pivoted top-left, so the popup anchors to the panel's BOTTOM-left corner and is
        /// pushed up past the hint line and the input row — which keeps it correct when the
        /// window is resized, since those two rows have fixed heights.
        /// </summary>
        private void LayoutSuggestions()
        {
            int rows = Mathf.Min(_suggestions.Count, SUGGEST_MAX_ROWS);
            float h  = rows * SUGGEST_ROW_H + SUGGEST_PADDING;
            float w  = Mathf.Max(240f, _panelRt.sizeDelta.x - 24f);

            _suggestRt.sizeDelta        = new Vector2(w, h);
            _suggestRt.anchoredPosition = new Vector2(12f, INPUT_ROW_H + HINT_H + 14f);
        }

        private void RenderSuggestions()
        {
            var sb = new StringBuilder(512);
            int rows = Mathf.Min(_suggestions.Count, SUGGEST_MAX_ROWS);

            // The window scrolls with the selection instead of always starting at zero, or a
            // ninth candidate could be selected and never drawn.
            int first = Mathf.Clamp(_suggestIndex - rows + 1, 0, Mathf.Max(0, _suggestions.Count - rows));

            for (int i = first; i < first + rows; i++)
            {
                bool selected = i == _suggestIndex;
                // ">" and not a nicer wedge: the shipped LiberationSans SDF atlas has no
                // U+25B8, and TMP draws a missing glyph as a filled BOX — which on the one
                // row that is supposed to read as selected is worse than no marker at all.
                sb.Append(selected ? "<color=#" + HEX_ECHO + ">> " : "<color=#" + HEX_OUTPUT + ">  ")
                  .Append(_suggestions[i])
                  .Append("</color>");

                if (!string.IsNullOrEmpty(_suggestionHelp[i]))
                    sb.Append("  <color=#").Append(HEX_LOG).Append('>')
                      .Append(_suggestionHelp[i]).Append("</color>");

                sb.Append('\n');
            }

            if (_suggestions.Count > rows)
                sb.Append("<color=#").Append(HEX_LOG).Append(">  +")
                  .Append(_suggestions.Count - rows).Append(" mas...</color>");

            _suggestText.text = sb.ToString();
        }

        // ------------------------------------------------------------------
        // Keyboard
        // ------------------------------------------------------------------

        private void MoveSuggestion(int delta)
        {
            if (_suggestions.Count == 0) return;
            _suggestIndex = (_suggestIndex + delta + _suggestions.Count) % _suggestions.Count;
            RenderSuggestions();
        }

        /// <summary>
        /// Replaces the token being typed with the highlighted candidate, and leaves a
        /// trailing space after a command name so the next Tab completes its ARGUMENT rather
        /// than re-offering the command that is already there.
        /// </summary>
        private void AcceptSuggestion()
        {
            if (_inputField == null || _suggestions.Count == 0) return;

            string chosen = _suggestions[Mathf.Clamp(_suggestIndex, 0, _suggestions.Count - 1)];
            string input  = _inputField.text ?? string.Empty;
            var tokens    = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            bool completingCommand = tokens.Length <= 1 && !input.EndsWith(" ", StringComparison.Ordinal);
            string rebuilt;
            if (completingCommand)
            {
                rebuilt = chosen + " ";
            }
            else
            {
                var sb = new StringBuilder(input.Length + chosen.Length);
                int keep = input.EndsWith(" ", StringComparison.Ordinal) ? tokens.Length : tokens.Length - 1;
                for (int i = 0; i < keep; i++) sb.Append(tokens[i]).Append(' ');
                sb.Append(chosen);
                rebuilt = sb.ToString();
            }

            // SetTextWithoutNotify, then recompute by hand: the change callback would rebuild
            // the candidate list from the completed text and pop the list straight back open
            // on the value it was just used to produce.
            _inputField.SetTextWithoutNotify(rebuilt);
            _inputField.caretPosition = rebuilt.Length;
            HideSuggestions();
            FocusInput();
        }
    }
}
