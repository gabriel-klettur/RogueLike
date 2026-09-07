using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Editors.Controls
{
    /// <summary>
    /// Undo and redo for the two things this editor changes: where a control is bound, and
    /// which contexts an action lives in.
    ///
    /// <para>WHY IT IS NOT OPTIONAL PARITY. <c>EditorShared/Undo</c> is a verb declared once and
    /// live in EVERY editor context — that is what "shared" means here — so Ctrl+Z inside the
    /// Controls editor was a control the layer promised and the panel silently ignored. It also
    /// matters more here than in most editors: the mistake this panel invites is pressing the
    /// wrong key during a capture, and without an undo the only way back is to remember what
    /// the key used to be.</para>
    ///
    /// <para>AN EDIT IS STORED AS TWO NAMES AND TWO VALUES, never as a captured
    /// <see cref="InputAction"/>. The canonical asset survives Domain Reload being off, but an
    /// action object captured in a closure is exactly the zombie-after-hot-reload shape
    /// <c>EditorHotkeyBindings</c> exists to survive; re-resolving from the map and action name
    /// costs a dictionary lookup at undo time, which is a human-scale event.</para>
    ///
    /// <para>RESET CLEARS BOTH STACKS. It drops the file from disk as well as the live state,
    /// so an undo could only ever put back half of what it took — and a half-undo is worse than
    /// none. The confirmation says so before it runs.</para>
    /// </summary>
    public partial class ControlsRuntimeEditor
    {
        /// <summary>One reversible change. Both directions are the same operation with
        /// different values, which is what keeps redo from being a second implementation.</summary>
        private sealed class ControlsEdit
        {
            public string Label;

            /// <summary><c>Map/Action</c> of the action that changed.</summary>
            public string ActionId;

            /// <summary>Index into <c>action.bindings</c>, or -1 for a context-mask edit.</summary>
            public int BindingIndex = -1;

            /// <summary>Override path before and after. Null means "no override" — the asset's
            /// own path — which is a different state from the empty string, and the empty
            /// string is how a cleared binding is expressed.</summary>
            public string PathBefore;
            public string PathAfter;

            public InputContextMask MaskBefore;
            public InputContextMask MaskAfter;

            public bool IsMaskEdit => BindingIndex < 0;
        }

        private readonly List<ControlsEdit> _undoStack = new List<ControlsEdit>();
        private readonly List<ControlsEdit> _redoStack = new List<ControlsEdit>();

        /// <summary>How many steps deep the history goes. Bounded because it is unbounded work
        /// otherwise: an author who spends an hour in here would carry every keystroke of it.</summary>
        private const int UNDO_DEPTH = 64;

        internal int UndoDepth => _undoStack.Count;
        internal int RedoDepth => _redoStack.Count;

        private void PushEdit(ControlsEdit edit)
        {
            _undoStack.Add(edit);
            if (_undoStack.Count > UNDO_DEPTH) _undoStack.RemoveAt(0);

            // A new edit invalidates the redo branch, the same as every editor in this project
            // and every text editor ever written.
            _redoStack.Clear();
        }

        private void ClearHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) { SetStatus("Nada que deshacer."); return; }

            var edit = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            Apply(edit, forward: false);
            _redoStack.Add(edit);
            SetStatus($"Deshecho: {edit.Label}.");
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) { SetStatus("Nada que rehacer."); return; }

            var edit = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            Apply(edit, forward: true);
            _undoStack.Add(edit);
            SetStatus($"Rehecho: {edit.Label}.");
        }

        private void Apply(ControlsEdit edit, bool forward)
        {
            if (edit.IsMaskEdit)
            {
                var descriptor = InputActionCatalog.Find(edit.ActionId);
                InputContextPolicy.SetContexts(descriptor, forward ? edit.MaskAfter : edit.MaskBefore);
            }
            else
            {
                var action = ResolveActionById(edit.ActionId);
                if (action == null) return;

                var path = forward ? edit.PathAfter : edit.PathBefore;
                if (path == null)
                    InputActionRebindingExtensions.RemoveBindingOverride(action, edit.BindingIndex);
                else
                    InputActionRebindingExtensions.ApplyBindingOverride(action, edit.BindingIndex, path);

                InputBindingResolver.Invalidate();
            }

            InputBindingStore.MarkDirty();
            CancelCapture();
            RebuildActionList();
            RepaintAll();
        }

        private static InputAction ResolveActionById(string id)
        {
            int slash = id?.IndexOf('/') ?? -1;
            if (slash <= 0) return null;

            var asset = InputService.Instance?.Asset;
            var map = asset?.FindActionMap(id.Substring(0, slash), throwIfNotFound: false);
            return map?.FindAction(id.Substring(slash + 1), throwIfNotFound: false);
        }

        /// <summary>The override path currently on a binding, or null when it has none. The
        /// distinction matters: null restores the asset's own path, "" is a cleared binding.</summary>
        private static string OverridePathOf(InputAction action, int index)
        {
            if (action == null || index < 0 || index >= action.bindings.Count) return null;
            return action.bindings[index].overridePath;
        }
    }
}
