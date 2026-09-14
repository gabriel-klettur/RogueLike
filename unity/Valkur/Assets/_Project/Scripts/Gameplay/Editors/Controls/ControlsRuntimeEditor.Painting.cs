using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Controls
{
    /// <summary>
    /// What the board looks like: one tint per category, a ring for a clash, and the bound
    /// action's name printed on the cap.
    ///
    /// <para>The tint is the whole reason a drawn board beats a list. An author does not read
    /// eighty rows to find out that the spell block is the digit row and the left hand — they
    /// see it.</para>
    ///
    /// <para>THE RINGS ARE CONTEXT-AWARE, and that is what makes them worth looking at. Painted
    /// from a map-based scan, the War board rang FIFTEEN keys red — WASD, the arrows, all three
    /// mouse buttons, space, Escape — because every one of them is both a gameplay verb and a
    /// UI verb, which is the arrangement the project has always shipped and has never been a
    /// bug: only one consumer is listening at a time. Fifteen permanent false positives next to
    /// a summary line reading "Sin conflictos reales" in green is a board that teaches the
    /// author to ignore its own alarm. <see cref="InputConflictScanner.Classify"/> now grades
    /// each control, and only a real double fire is red.</para>
    /// </summary>
    public partial class ControlsRuntimeEditor
    {
        // Category tints live in UITheme via ControlsEditorUIBuilder.TintForCategory, not here.
        // They are shared vocabulary — the legend, the caps and the mouse have to agree on the
        // same nine colours — and a colour that exists in one file is a colour the next surface
        // guesses at.

        /// <summary>Two real gestures on one control, both live in the context being painted.</summary>
        private static readonly Color RING_BLOCKING = UITheme.DANGER;

        /// <summary>One of the two is a held modifier: real, and usually deliberate.</summary>
        private static readonly Color RING_MODIFIER = UITheme.WARNING;

        private static readonly Color RING_SELECTED = UITheme.SELECTION_BORDER;

        /// <summary>Rebuilt on every repaint. Cheap — measured at 0.6 ms for the whole board —
        /// and always correct, which a cache invalidated by hand would not be after a rebind, a
        /// context change, a layout change and a reset all move it.</summary>
        private Dictionary<string, List<InputActionDescriptor>> _liveByPath;

        internal void RepaintAll()
        {
            var asset = InputService.Instance?.Asset;
            _liveByPath = InputConflictScanner.LiveByPath(asset, _viewContext);

            _keyboard.Refresh(VisualForControlName);
            _mouse.Refresh(VisualForMouse);
            RefreshTabs();
            RefreshClashSummary();
            RefreshDetail();
        }

        /// <summary>
        /// Which context tab and which layout tab read as selected. The context strip is built
        /// per open (the editor registry is a runtime thing), so this paints whatever is
        /// there rather than a fixed pair.
        /// </summary>
        private void RefreshTabs()
        {
            if (_ui == null) return;

            foreach (var tab in _ui.ContextTabs)
                ControlsEditorUIBuilder.PaintTab(tab.Button, tab.Label,
                    string.Equals(tab.ContextId, _viewContext, System.StringComparison.Ordinal));

            ControlsEditorUIBuilder.PaintTab(_ui.IsoTab,  _ui.IsoTabLabel,  _layout == KeyboardLayoutKind.Iso);
            ControlsEditorUIBuilder.PaintTab(_ui.AnsiTab, _ui.AnsiTabLabel, _layout == KeyboardLayoutKind.Ansi);
        }

        private KeyCapVisual VisualForControlName(string controlName)
        {
            string path = InputControlPaths.KeyboardPrefix + controlName;
            bool selected = controlName == _selectedControl;
            return VisualForPath(path, selected);
        }

        private KeyCapVisual VisualForMouse(MouseControl control)
        {
            string path = InputControlPaths.PathForMouse(control);
            bool selected = control == _selectedMouse;
            return VisualForPath(path, selected);
        }

        private KeyCapVisual VisualForPath(string path, bool selected)
        {
            var live = LiveOn(path);
            var chords = ChordLayersOn(path);

            // A key free on its own but carrying a Shift chord is NOT free — tint it as what the
            // chord is, or the second layer is invisible on the one surface meant to show it.
            InputActionDescriptor first = live.Count > 0 ? live[0]
                                        : chords.Count > 0 && chords[0].live.Count > 0 ? chords[0].live[0]
                                        : null;
            Color fill = first == null
                ? UITheme.INPUT_FREE
                : ControlsEditorUIBuilder.TintForCategory(first.Category);
            Color legend = first == null ? UITheme.TEXT_MUTED : UITheme.TEXT_PRIMARY;

            Color ring = Color.clear;
            if (selected) ring = RING_SELECTED;
            else
            {
                // Each layer is its own press, so each is graded on its own; the cap shows the worse.
                var severity = InputConflictScanner.Classify(live);
                foreach (var c in chords)
                {
                    var s = InputConflictScanner.Classify(c.live);
                    if (s > severity) severity = s;
                }
                if (severity == InputClashSeverity.Blocking) ring = RING_BLOCKING;
                else if (severity == InputClashSeverity.Modifier) ring = RING_MODIFIER;
            }

            return new KeyCapVisual(fill, legend, ring, LayeredSubtitle(live, chords));
        }

        /// <summary>
        /// Every chord whose KEY is <paramref name="buttonPath"/>, with what is live on it —
        /// "Shift+1" for the cap of 1. A chord is keyed by its chord path in the live map, so
        /// without this the second layer would never reach the board.
        /// </summary>
        private List<(string modifierPath, IReadOnlyList<InputActionDescriptor> live)> ChordLayersOn(string buttonPath)
        {
            var result = new List<(string, IReadOnlyList<InputActionDescriptor>)>(1);
            if (buttonPath == null || _liveByPath == null) return result;
            foreach (var kv in _liveByPath)
            {
                if (!InputChord.TrySplit(kv.Key, out var modifier, out var button)) continue;
                if (!string.Equals(button, buttonPath, System.StringComparison.OrdinalIgnoreCase)) continue;
                result.Add((modifier, kv.Value));
            }
            return result;
        }

        /// <summary>"Bola oscura | Shift: Lanza del vacio". The bare layer first, because that is
        /// what the key does when pressed on its own.</summary>
        private static string LayeredSubtitle(IReadOnlyList<InputActionDescriptor> live,
            List<(string modifierPath, IReadOnlyList<InputActionDescriptor> live)> chords)
        {
            string text = SubtitleFor(live);
            foreach (var c in chords)
            {
                string layer = InputChord.ShortModifier(c.modifierPath) + ": " + SubtitleFor(c.live);
                text = string.IsNullOrEmpty(text) ? layer : text + " | " + layer;
            }
            return text;
        }

        /// <summary>
        /// The actions on this control that are live in the context being painted.
        ///
        /// <para>The context filter is what makes the board a picture of a LAYOUT rather than
        /// of the asset. Two actions on one key in different contexts are not a clash — they
        /// are the whole point — so painting them as one would report the correct arrangement
        /// as broken.</para>
        /// </summary>
        private IReadOnlyList<InputActionDescriptor> LiveOn(string path)
        {
            // Array.Empty rather than a shared static list: a `static readonly` collection is
            // exactly what DomainReloadStaticResetTests refuses, and correctly so — the ratchet
            // cannot tell an immutable empty list from a cache that will carry a destroyed
            // reference into the next Play session. The BCL's singleton is not this project's
            // static at all, so there is nothing to reset.
            if (path == null || _liveByPath == null) return System.Array.Empty<InputActionDescriptor>();
            return _liveByPath.TryGetValue(path, out var all)
                ? (IReadOnlyList<InputActionDescriptor>)all
                : System.Array.Empty<InputActionDescriptor>();
        }

        private static string SubtitleFor(IReadOnlyList<InputActionDescriptor> live)
        {
            if (live == null || live.Count == 0) return "";
            if (live.Count == 1) return live[0].DisplayName;

            var sb = new StringBuilder();
            for (int i = 0; i < live.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(live[i].DisplayName);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The one-line verdict for the whole board, in the context being viewed.
        ///
        /// <para>It counts the same thing the rings paint, from the same call, so the summary
        /// cannot say "no conflicts" while fifteen keys are ringed — which is exactly what the
        /// map-based version did.</para>
        /// </summary>
        private void RefreshClashSummary()
        {
            if (_ui?.Conflicts == null) return;

            int blocking = 0, modifier = 0;
            string firstBlocking = null;
            foreach (var kv in _liveByPath)
            {
                var severity = InputConflictScanner.Classify(kv.Value);
                if (severity == InputClashSeverity.Blocking)
                {
                    blocking++;
                    firstBlocking ??= $"{InputControlPaths.LabelForPath(kv.Key)} ({SubtitleFor(kv.Value)})";
                }
                else if (severity == InputClashSeverity.Modifier) modifier++;
            }

            if (blocking > 0)
            {
                _ui.Conflicts.text = $"{blocking} tecla(s) con doble disparo: {firstBlocking}";
                _ui.Conflicts.color = UITheme.DANGER;
                return;
            }

            if (modifier > 0)
            {
                _ui.Conflicts.text = $"Sin dobles disparos ({modifier} con un modificador encima)";
                _ui.Conflicts.color = UITheme.WARNING;
                return;
            }

            _ui.Conflicts.text = "Sin dobles disparos";
            _ui.Conflicts.color = UITheme.SUCCESS;
        }

        private void RefreshDetail()
        {
            if (_ui?.Detail == null) return;

            string path = SelectedPath();
            if (path == null)
            {
                _ui.Detail.text = "Ninguna tecla seleccionada.";
                _ui.Detail.color = UITheme.TEXT_MUTED;
                return;
            }

            var live = LiveOn(path);
            var chords = ChordLayersOn(path);
            string label = InputControlPaths.LabelForPath(path);
            var severity = InputConflictScanner.Classify(live);
            foreach (var c in chords)
            {
                var s = InputConflictScanner.Classify(c.live);
                if (s > severity) severity = s;
            }

            _ui.Detail.color = severity switch
            {
                InputClashSeverity.Blocking => UITheme.DANGER,
                InputClashSeverity.Modifier => UITheme.WARNING,
                _                                                => UITheme.ACCENT,
            };
            _ui.Detail.text = live.Count == 0 && chords.Count == 0
                ? $"{label}: libre. Elige una accion de la lista y pulsa «...» para ponerla aqui."
                : $"{label}: {LayeredSubtitle(live, chords)}";
        }

        /// <summary>The selected control as a binding path, or null when nothing is
        /// selected.</summary>
        private string SelectedPath()
        {
            if (_selectedMouse != MouseControl.None)
                return InputControlPaths.PathForMouse(_selectedMouse);
            if (!string.IsNullOrEmpty(_selectedControl))
                return InputControlPaths.KeyboardPrefix + _selectedControl;
            return null;
        }

        private void OnKeyClicked(string controlName)
        {
            if (IsCapturing) { CompleteCaptureWithPath(InputControlPaths.KeyboardPrefix + controlName); return; }
            _selectedMouse = MouseControl.None;
            _selectedControl = controlName == _selectedControl ? null : controlName;
            RepaintAll();
        }

        private void OnMouseClicked(MouseControl control)
        {
            if (IsCapturing) { CompleteCaptureWithPath(InputControlPaths.PathForMouse(control)); return; }
            _selectedControl = null;
            _selectedMouse = control == _selectedMouse ? MouseControl.None : control;
            RepaintAll();
        }
    }
}
