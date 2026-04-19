using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Central editor exclusivity manager. Opening one runtime editor closes all others.
    /// Mirrors Python's open_editor_exclusive() / close_all_editors() from editors_common.py.
    /// </summary>
    public class GameEditorManager : SingletonMonoBehaviour<GameEditorManager>
    {
        public interface IGameEditor
        {
            string EditorName { get; }
            bool IsActive { get; }
            void Activate();
            void Deactivate();
        }

        private readonly List<IGameEditor> _registered = new List<IGameEditor>();
        private IGameEditor _activeEditor;

        public IGameEditor ActiveEditor => _activeEditor;
        public bool AnyEditorActive => _activeEditor != null;

        /// <summary>
        /// Returns the existing instance, or creates a new GameObject hosting one if missing.
        /// Use this from any editor's Awake/Start to guarantee the manager exists at runtime.
        /// </summary>
        public static GameEditorManager EnsureInstance()
        {
            if (HasInstance) return Instance;
            var go = new GameObject("[GameEditorManager]");
            return go.AddComponent<GameEditorManager>();
        }

        public void Register(IGameEditor editor)
        {
            if (editor == null || _registered.Contains(editor)) return;
            _registered.Add(editor);
        }

        public void Unregister(IGameEditor editor)
        {
            if (editor == null) return;
            if (_activeEditor == editor) _activeEditor = null;
            _registered.Remove(editor);
        }

        /// <summary>
        /// Opens the target editor exclusively — closes any other active editor first.
        /// </summary>
        public void OpenExclusive(IGameEditor target)
        {
            if (target == null) return;

            if (_activeEditor != null && _activeEditor != target)
            {
                _activeEditor.Deactivate();
            }

            _activeEditor = target;
            target.Activate();
        }

        /// <summary>
        /// Toggle the target editor: if active, close it; otherwise open exclusively.
        /// </summary>
        public void ToggleExclusive(IGameEditor target)
        {
            if (target == null) return;

            if (_activeEditor == target && target.IsActive)
            {
                target.Deactivate();
                _activeEditor = null;
            }
            else
            {
                OpenExclusive(target);
            }
        }

        /// <summary>
        /// Close all editors.
        /// </summary>
        public void CloseAll()
        {
            if (_activeEditor != null)
            {
                _activeEditor.Deactivate();
                _activeEditor = null;
            }
        }

        public void NotifyDeactivated(IGameEditor editor)
        {
            if (_activeEditor == editor)
                _activeEditor = null;
        }
    }
}
