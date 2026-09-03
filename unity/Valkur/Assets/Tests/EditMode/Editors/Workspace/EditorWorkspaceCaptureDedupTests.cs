using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Editors;

namespace Valkur.Tests.EditMode.Editors.Workspace
{
    /// <summary>
    /// Pins that one close produces exactly ONE workspace capture.
    ///
    /// A close reaches <see cref="GameEditorManager"/> along two paths, and both fire for
    /// an editor that closes itself: the caller captures before it calls
    /// <c>Deactivate</c>, and the editor then calls <c>NotifyDeactivated</c> after it has
    /// already deactivated. A second capture is not harmless, because an editor is free to
    /// clear its own transient state in <c>Deactivate</c> — the Tile Editor really does
    /// (<c>_state.SelectedCellPos = null</c>), so the second pass would write that null
    /// over the selection the first pass had correctly recorded.
    ///
    /// This is the defect the Tile pilot surfaced. It is pinned here rather than in the
    /// Tile tests because the fault was in the layer, not in that editor.
    /// </summary>
    [TestFixture]
    public sealed class EditorWorkspaceCaptureDedupTests
    {
        /// <summary>
        /// Mimics the Tile Editor's shape: clears its "selection" during Deactivate, and
        /// notifies the manager afterwards — so a capture taken after Deactivate records
        /// the cleared value.
        /// </summary>
        private sealed class SelfClosingEditor : GameEditorManager.IGameEditor
        {
            private readonly GameEditorManager _mgr;

            public string EditorName => "Self Closing";
            public bool IsActive { get; private set; }
            public string Selection = "cell:12,34";

            public SelfClosingEditor(GameEditorManager mgr) => _mgr = mgr;

            public void Activate() => IsActive = true;

            public void Deactivate()
            {
                IsActive = false;
                Selection = null;          // exactly what TileEditorManager.HandleToggle does
                _mgr.NotifyDeactivated(this);
            }
        }

        private sealed class RecordingService : IEditorWorkspaceService
        {
            public readonly List<string> Captured = new List<string>();
            public int Restores;

            public void RestoreOnOpen(GameEditorManager.IGameEditor editor) => Restores++;

            public void CaptureOnClose(GameEditorManager.IGameEditor editor)
                => Captured.Add((editor as SelfClosingEditor)?.Selection ?? "<null>");

            public void ResetWorkspace(GameEditorManager.IGameEditor editor) { }
        }

        private readonly List<Object> _created = new List<Object>();
        private RecordingService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new RecordingService();
            ServiceLocator.Register<IEditorWorkspaceService>(_service);
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private GameEditorManager CreateManager()
        {
            var go = new GameObject("GameEditorManagerUnderTest");
            _created.Add(go);
            return go.AddComponent<GameEditorManager>();
        }

        [Test]
        public void ClosingAnEditorThatNotifiesItself_CapturesExactlyOnce()
        {
            var mgr    = CreateManager();
            var editor = new SelfClosingEditor(mgr);

            mgr.OpenExclusive(editor);
            mgr.ToggleExclusive(editor);   // closes: captures, then Deactivate notifies

            Assert.AreEqual(1, _service.Captured.Count,
                "Both close paths fire for a self-notifying editor; only the first may capture.");
        }

        [Test]
        public void TheCapturedSnapshot_IsTakenBeforeDeactivateClearsState()
        {
            var mgr    = CreateManager();
            var editor = new SelfClosingEditor(mgr);

            mgr.OpenExclusive(editor);
            mgr.ToggleExclusive(editor);

            Assert.AreEqual("cell:12,34", _service.Captured[0],
                "Capturing after Deactivate records the state the editor just cleared — " +
                "which is how a remembered selection turns into null on every close.");
        }

        [Test]
        public void ReopeningTheEditor_ReArmsItsNextCapture()
        {
            var mgr    = CreateManager();
            var editor = new SelfClosingEditor(mgr);

            mgr.OpenExclusive(editor);
            mgr.ToggleExclusive(editor);

            editor.Selection = "cell:99,99";
            mgr.OpenExclusive(editor);
            mgr.ToggleExclusive(editor);

            Assert.AreEqual(2, _service.Captured.Count,
                "The de-duplication is per close, not for the lifetime of the editor.");
            Assert.AreEqual("cell:99,99", _service.Captured[1]);
        }

        [Test]
        public void OpeningAnEditor_RestoresItsWorkspace()
        {
            var mgr    = CreateManager();
            var editor = new SelfClosingEditor(mgr);

            mgr.OpenExclusive(editor);

            Assert.AreEqual(1, _service.Restores,
                "Restore hangs off OpenExclusive — the one seam every editor open passes.");
        }

        [Test]
        public void CloseAll_CapturesTheActiveEditorOnce()
        {
            var mgr    = CreateManager();
            var editor = new SelfClosingEditor(mgr);

            mgr.OpenExclusive(editor);
            mgr.CloseAll();

            Assert.AreEqual(1, _service.Captured.Count);
            Assert.AreEqual("cell:12,34", _service.Captured[0]);
        }

        [Test]
        public void SwitchingEditors_CapturesTheOutgoingOneAndRestoresTheIncoming()
        {
            var mgr = CreateManager();
            var a   = new SelfClosingEditor(mgr);
            var b   = new SelfClosingEditor(mgr);

            mgr.OpenExclusive(a);
            mgr.OpenExclusive(b);

            Assert.AreEqual(1, _service.Captured.Count, "Opening B must capture A.");
            Assert.AreEqual("cell:12,34", _service.Captured[0],
                "A's snapshot must predate its own Deactivate.");
            Assert.AreEqual(2, _service.Restores, "Both opens restore.");
        }

        [Test]
        public void WithNoServiceInstalled_TheManagerStillWorks()
        {
            ServiceLocator.Clear();

            var mgr    = CreateManager();
            var editor = new SelfClosingEditor(mgr);

            // Core may reference nothing, so the layer is resolved through ServiceLocator
            // and every call no-ops when it is absent. That is what keeps this manager
            // working in the many tests that never install the workspace layer.
            Assert.DoesNotThrow(() =>
            {
                mgr.OpenExclusive(editor);
                mgr.ToggleExclusive(editor);
            });
        }
    }
}
