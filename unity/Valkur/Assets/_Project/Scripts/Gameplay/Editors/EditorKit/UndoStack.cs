using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.Editors.EditorKit
{
    /// <summary>
    /// Generic command-based undo/redo stack for runtime editors.
    /// Mirrors Python editors' history services (e.g. entities/services/history.py).
    ///
    /// Usage:
    ///   var undo = new UndoStack(capacity: 64);
    ///   undo.Do(new LambdaCommand("Delete", () => list.Remove(x), () => list.Add(x)));
    ///   undo.Undo();  // restores
    ///   undo.Redo();
    /// </summary>
    public sealed class UndoStack
    {
        public interface ICommand
        {
            string Label { get; }
            void Execute();
            void Undo();
        }

        /// <summary>Convenience command built from two delegates.</summary>
        public sealed class LambdaCommand : ICommand
        {
            private readonly Action _do, _undo;
            public string Label { get; }
            public LambdaCommand(string label, Action doAction, Action undoAction)
            {
                Label = label ?? "edit";
                _do = doAction ?? throw new ArgumentNullException(nameof(doAction));
                _undo = undoAction ?? throw new ArgumentNullException(nameof(undoAction));
            }
            public void Execute() => _do();
            public void Undo() => _undo();
        }

        private readonly LinkedList<ICommand> _undo = new LinkedList<ICommand>();
        private readonly Stack<ICommand> _redo = new Stack<ICommand>();
        private readonly int _capacity;

        public int Capacity => _capacity;
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public event Action Changed;

        public UndoStack(int capacity = 64)
        {
            if (capacity < 1) capacity = 1;
            _capacity = capacity;
        }

        /// <summary>Executes a command then records it for undo. Clears redo stack.</summary>
        public void Do(ICommand cmd)
        {
            if (cmd == null) return;
            cmd.Execute();
            _undo.AddLast(cmd);
            while (_undo.Count > _capacity) _undo.RemoveFirst();
            _redo.Clear();
            Changed?.Invoke();
        }

        public void Do(string label, Action doAction, Action undoAction)
            => Do(new LambdaCommand(label, doAction, undoAction));

        /// <summary>Records a command that is already executed (no Execute call).</summary>
        public void Record(ICommand cmd)
        {
            if (cmd == null) return;
            _undo.AddLast(cmd);
            while (_undo.Count > _capacity) _undo.RemoveFirst();
            _redo.Clear();
            Changed?.Invoke();
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var cmd = _undo.Last.Value; _undo.RemoveLast();
            try { cmd.Undo(); } catch { /* swallow — keep stack consistent */ }
            _redo.Push(cmd);
            Changed?.Invoke();
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var cmd = _redo.Pop();
            try { cmd.Execute(); } catch { /* swallow */ }
            _undo.AddLast(cmd);
            Changed?.Invoke();
            return true;
        }

        public string PeekUndoLabel() => _undo.Count > 0 ? _undo.Last.Value.Label : null;
        public string PeekRedoLabel() => _redo.Count > 0 ? _redo.Peek().Label : null;

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
        }
    }
}
