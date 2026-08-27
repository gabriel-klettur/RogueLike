using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// Pins <see cref="GameEditorManager.RegisteredEditors"/> — the read-only accessor
    /// added over the previously write-only <c>_registered</c> roster (written by
    /// <see cref="GameEditorManager.Register"/>/<see cref="GameEditorManager.Unregister"/>
    /// and, before this, read by nothing). Uses a fake <see cref="GameEditorManager.IGameEditor"/>
    /// so the test needs no real runtime editor GameObject.
    /// </summary>
    public class GameEditorManagerRegisteredEditorsTests
    {
        private sealed class FakeEditor : GameEditorManager.IGameEditor
        {
            public string EditorName { get; }
            public bool IsActive { get; private set; }
            public FakeEditor(string name) => EditorName = name;
            public void Activate() => IsActive = true;
            public void Deactivate() => IsActive = false;
        }

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private GameEditorManager CreateManager()
        {
            var go = new GameObject("GameEditorManagerUnderTest");
            _created.Add(go);
            return go.AddComponent<GameEditorManager>();
        }

        [Test]
        public void Register_AddsToRegisteredEditors()
        {
            var mgr = CreateManager();
            var editor = new FakeEditor("Test");

            mgr.Register(editor);

            CollectionAssert.Contains(new List<GameEditorManager.IGameEditor>(mgr.RegisteredEditors), editor);
        }

        [Test]
        public void Register_SameEditorTwice_DoesNotDuplicate()
        {
            var mgr = CreateManager();
            var editor = new FakeEditor("Test");

            mgr.Register(editor);
            mgr.Register(editor);

            Assert.AreEqual(1, mgr.RegisteredEditors.Count);
        }

        [Test]
        public void Unregister_RemovesFromRegisteredEditors()
        {
            var mgr = CreateManager();
            var editor = new FakeEditor("Test");
            mgr.Register(editor);

            mgr.Unregister(editor);

            Assert.AreEqual(0, mgr.RegisteredEditors.Count);
        }

        [Test]
        public void RegisteredEditors_ReflectsEveryLiveRegistration_NotJustTheActiveOne()
        {
            var mgr = CreateManager();
            var a = new FakeEditor("A");
            var b = new FakeEditor("B");
            mgr.Register(a);
            mgr.Register(b);

            mgr.OpenExclusive(a);

            // ActiveEditor narrows to one; RegisteredEditors must not — a launcher panel
            // needs to know about every booted editor, not merely the currently open one.
            Assert.AreEqual(a, mgr.ActiveEditor);
            Assert.AreEqual(2, mgr.RegisteredEditors.Count);
        }
    }
}
