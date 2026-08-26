using System;
using System.Collections.Generic;

namespace Valkur.UIKit
{
    /// <summary>
    /// Generic command-based undo/redo stack for runtime editors. Mirrors
    /// Python editors' history services (e.g. entities/services/history.py).
    ///
    /// Usage:
    ///   var undo = new UndoStack(capacity: 64);
    ///   undo.Do(new LambdaCommand("Delete", () => list.Remove(x), () => list.Add(x)));
    ///   undo.Undo();
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
            try { cmd.Undo(); }
            catch (Exception ex) { ReportFailure("Undo", cmd, ex); }
            _redo.Push(cmd);
            Changed?.Invoke();
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var cmd = _redo.Pop();
            try { cmd.Execute(); }
            catch (Exception ex) { ReportFailure("Redo", cmd, ex); }
            _undo.AddLast(cmd);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// A command that throws still moves between the stacks, because wedging the history is
        /// worse for the author than one lost step. But it must not do so in SILENCE: both of
        /// these catches used to be empty, so a broken undo looked exactly like a working one and
        /// the stack went on claiming edits the world had never seen. Five runtime editors share
        /// this class, which is why the failure is reported rather than rethrown — a throw here
        /// would take the whole editor down for a single bad step.
        /// </summary>
        private static void ReportFailure(string direction, ICommand cmd, Exception ex)
        {
            UnityEngine.Debug.LogError(
                $"[UndoStack] {direction} of '{cmd?.Label ?? "?"}' threw {ex.GetType().Name}: {ex.Message}. " +
                "The step is consumed anyway; the world and the history may now disagree." +
                Environment.NewLine + ex.StackTrace);
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
