using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Controls
{
    /// <summary>
    /// What the board looks like: one tint per category, a ring for a conflict, and the bound
    /// action's name printed on the cap.
    ///
    /// <para>The tint is the whole reason a drawn board beats a list. An author does not read
    /// eighty rows to find out that the spell block is the digit row and the left hand — they
    /// see it. Which is also how the four F-key collisions in the shipped asset become one
    /// glance instead of an afternoon.</para>
    /// </summary>
    public partial class ControlsRuntimeEditor
    {
        // Category tints live in UITheme, not here. They are shared vocabulary — the same
        // nine colours are what the legend, the mouse and any future controls surface have to
        // agree on — and a colour that exists in one file is a colour the next surface guesses
        // at. The tokens are INPUT_* there.

        /// <summary>A key that two live actions answer to, in the stance being painted.</summary>
        private static readonly Color RING_CONFLICT = UITheme.DANGER;
        private static readonly Color RING_SELECTED = UITheme.SELECTION_BORDER;

        /// <summary>Rebuilt on every repaint. Cheap — sixty-odd descriptors — and always
        /// correct, which a cache invalidated by hand would not be after a rebind, a stance
        /// change, a layout change and a reset all move it.</summary>
        private Dictionary<string, List<InputActionDescriptor>> _byPath;

        internal void RepaintAll()
        {
            var asset = InputService.Instance?.Asset;
            _byPath = InputConflictScanner.BindingsByPath(asset);

            _keyboard.Refresh(VisualForControlName);
            _mouse.Refresh(VisualForMouse);
            RefreshTabs();
            RefreshConflictSummary();
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

            Color fill = live.Count == 0 ? UITheme.INPUT_FREE : TintFor(live[0].Category);
            Color legend = live.Count == 0 ? UITheme.TEXT_MUTED : UITheme.TEXT_PRIMARY;

            Color ring = Color.clear;
            if (selected) ring = RING_SELECTED;
            else if (live.Count > 1) ring = RING_CONFLICT;

            return new KeyCapVisual(fill, legend, ring, SubtitleFor(live));
        }

        /// <summary>
        /// The actions on this control that are live in the stance being painted.
        ///
        /// <para>The stance filter is what makes the board a picture of a LAYOUT rather than
        /// of the asset. Two actions on one key in different stances are not a conflict — they
        /// are the whole point — so painting them as one would report the correct arrangement
        /// as broken.</para>
        /// </summary>
        private List<InputActionDescriptor> LiveOn(string path)
        {
            var result = new List<InputActionDescriptor>(2);
            if (path == null || _byPath == null) return result;
            if (!_byPath.TryGetValue(path, out var all)) return result;

            foreach (var d in all)
            {
                // One question, asked of the context being painted. It answers correctly for
                // all three shapes at once: a gameplay action against a posture, a shared
                // editor verb against any editor, and one editor's tool against ITS editor
                // only — which is what stops the Tile brush appearing on the Buildings board
                // even though both are free to use the same key.
                if (!InputContextPolicy.IsLive(d, _viewContext)) continue;
                result.Add(d);
            }
            return result;
        }

        private static string SubtitleFor(List<InputActionDescriptor> live)
        {
            if (live.Count == 0) return "";
            if (live.Count == 1) return live[0].DisplayName;

            var sb = new StringBuilder();
            for (int i = 0; i < live.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(live[i].DisplayName);
            }
            return sb.ToString();
        }

        private static Color TintFor(InputActionCategory category) => category switch
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

        private void RefreshConflictSummary()
        {
            if (_ui?.Conflicts == null) return;

            var conflicts = InputConflictScanner.Scan(InputService.Instance?.Asset);
            int sameMap = 0;
            foreach (var c in conflicts)
                if (c.Severity == InputConflictSeverity.SameMap) sameMap++;

            if (sameMap == 0)
            {
                _ui.Conflicts.text = conflicts.Count == 0
                    ? "Sin conflictos"
                    : $"Sin conflictos reales ({conflicts.Count} entre mapas distintos)";
                _ui.Conflicts.color = UITheme.SUCCESS;
                return;
            }

            _ui.Conflicts.text = $"{sameMap} conflicto(s): " + FirstFew(conflicts, 2);
            _ui.Conflicts.color = UITheme.DANGER;
        }

        private static string FirstFew(IReadOnlyList<InputConflict> conflicts, int max)
        {
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var c in conflicts)
            {
                if (c.Severity != InputConflictSeverity.SameMap) continue;
                if (shown > 0) sb.Append("  ·  ");
                sb.Append(c.Describe());
                if (++shown >= max) break;
            }
            return sb.ToString();
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
            string label = InputControlPaths.LabelForPath(path);
            _ui.Detail.color = live.Count > 1 ? UITheme.DANGER : UITheme.ACCENT;
            _ui.Detail.text = live.Count == 0
                ? $"{label}: libre. Elige una accion de la lista para ponerla aqui."
                : $"{label}: {SubtitleFor(live)}";
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
