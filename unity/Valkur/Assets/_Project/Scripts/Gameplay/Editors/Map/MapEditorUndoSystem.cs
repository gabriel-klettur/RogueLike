using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Bounded undo/redo stack for the F11 Map Editor. Each recorded operation
    /// is a pair of "do" / "undo" closures plus a human-readable label so the
    /// status bar can announce what the user just reverted.
    ///
    /// Capacity is hard-capped (<see cref="MaxOps"/>) — the oldest entry is
    /// dropped silently when the stack is full. Pushing a new op clears the
    /// redo stack (the canonical "if you act after undoing, you can't redo
    /// the abandoned future" rule). The stack survives slot switches because
    /// it lives on the manager, but new-map / load-slot operations call
    /// <see cref="Clear"/> so cross-slot undo never resurrects a zone in the
    /// wrong map.
    /// </summary>
    public sealed class MapEditorUndoSystem
    {
        public const int MaxOps = 50;

        public readonly struct Op
        {
            public readonly string Label;
            public readonly Action Do;
            public readonly Action Undo;
            public Op(string label, Action @do, Action undo)
            { Label = label; Do = @do; Undo = undo; }
        }

        private readonly LinkedList<Op> _undoStack = new LinkedList<Op>();
        private readonly Stack<Op> _redoStack     = new Stack<Op>();

        public int UndoDepth => _undoStack.Count;
        public int RedoDepth => _redoStack.Count;
        public bool CanUndo  => _undoStack.Count > 0;
        public bool CanRedo  => _redoStack.Count > 0;

        /// <summary>
        /// Record an op that has ALREADY been performed by the editor — only
        /// the inverse closure is needed to walk it back. The "do" closure is
        /// captured for redo.
        /// </summary>
        public void Push(string label, Action @do, Action undo)
        {
            if (@do == null || undo == null) return;
            _undoStack.AddLast(new Op(label ?? string.Empty, @do, undo));
            if (_undoStack.Count > MaxOps)
                _undoStack.RemoveFirst();
            // Any new op invalidates the redo branch — reverse-time forking
            // would let the user redo into a state that no longer exists.
            _redoStack.Clear();
        }

        public bool Undo(out string label)
        {
            label = null;
            if (_undoStack.Count == 0) return false;
            var op = _undoStack.Last.Value;
            _undoStack.RemoveLast();
            try { op.Undo(); }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor.Undo] '{op.Label}' undo threw: {ex.Message}");
                return false;
            }
            _redoStack.Push(op);
            label = op.Label;
            return true;
        }

        public bool Redo(out string label)
        {
            label = null;
            if (_redoStack.Count == 0) return false;
            var op = _redoStack.Pop();
            try { op.Do(); }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor.Redo] '{op.Label}' redo threw: {ex.Message}");
                return false;
            }
            _undoStack.AddLast(op);
            label = op.Label;
            return true;
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
