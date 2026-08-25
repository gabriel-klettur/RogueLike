using System.IO;
using System.Text.RegularExpressions;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.State
{
    /// <summary>
    /// Regression coverage for the Colliders ↔ Layer-Jumps edit-mode state machine —
    /// zero prior tests called <c>OnDrawCollidersClicked</c>, <c>OnEraseCollidersClicked</c>,
    /// <c>OnShowCollidersClicked</c>, <c>IsColliderEditModeActive</c> or
    /// <c>IsLayerJumpsEditModeActive</c> at all.
    ///
    /// <see cref="TileEditorState.CurrentLayerJumpMode"/>'s own doc-comment promises the
    /// two sub-modes are "Mutually exclusive... the manager turns one off when the other
    /// turns on." The mutex used to only hold in ONE direction:
    ///   • <c>OnDrawLayerJumpsClicked</c> / <c>OnEraseLayerJumpsClicked</c> (LayerJumps.cs)
    ///     DID clear <c>CurrentColliderMode</c> before turning Layer-Jumps on.
    ///   • <c>OnDrawCollidersClicked</c> / <c>OnEraseCollidersClicked</c> (Colliders.cs)
    ///     did NOT clear <c>CurrentLayerJumpMode</c> — so turning Colliders on while
    ///     Layer-Jumps was already active left BOTH modes flagged active at once.
    /// <c>HandleMouseInput</c> checks <c>IsColliderEditModeActive()</c> before
    /// <c>IsLayerJumpsEditModeActive()</c>, so Colliders silently won every click while
    /// the Layer-Jumps panel kept showing Draw/Erase as lit — the toolbar lied about
    /// what a click would do.
    ///
    /// FIXED (2026-08-25): <c>OnDrawCollidersClicked</c> / <c>OnEraseCollidersClicked</c>
    /// now clear <c>CurrentLayerJumpMode</c> too and refresh the Layer-Jumps toggles,
    /// mirroring the direction that already worked. This file locks down the now-symmetric
    /// behaviour in both directions so a future regression shows up here as a failing
    /// assertion rather than a silent gap.
    ///
    /// Reflection convention matches <c>ColliderTagUndoTests</c> / <c>LayerJumpsUndoTests</c>:
    /// the Colliders-panel handlers are <c>private</c> (reflection required); the
    /// Layer-Jumps-panel handlers are <c>internal</c> and callable directly thanks to
    /// <c>[assembly: InternalsVisibleTo("Valkur.Tests.EditMode")]</c> on Valkur.Gameplay.
    /// </summary>
    [TestFixture]
    public class TileEditorColliderLayerJumpsMutexTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private TileEditorManager NewManager()
        {
            _host = new GameObject("ColliderLayerJumpsMutexTests_Host");
            return _host.AddComponent<TileEditorManager>();
        }

        // No UI/undo/grid is wired up on purpose — every handler under test guards its
        // UI/overlay calls with `_ui?.` / an early `if (_gridOverlay == null) return;`,
        // so a bare manager is enough to exercise the pure state transitions.

        private static object InvokePrivate(TileEditorManager manager, string methodName)
        {
            var mi = typeof(TileEditorManager).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, $"Reflection: '{methodName}' not found on TileEditorManager.");
            return mi.Invoke(manager, null);
        }

        private static void CallOnDrawCollidersClicked(TileEditorManager m) => InvokePrivate(m, "OnDrawCollidersClicked");
        private static void CallOnEraseCollidersClicked(TileEditorManager m) => InvokePrivate(m, "OnEraseCollidersClicked");
        private static bool CallIsColliderEditModeActive(TileEditorManager m) => (bool)InvokePrivate(m, "IsColliderEditModeActive");

        // ════════════════════════════════════════════════════════════════════
        // 1. Predicate correctness — pure functions of state, no mouse gating at all.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void IsColliderEditModeActive_TrueOnlyWhenNotNone()
        {
            var manager = NewManager();

            manager.State.CurrentColliderMode = TileEditorState.ColliderMode.None;
            Assert.IsFalse(CallIsColliderEditModeActive(manager), "None must report inactive.");

            manager.State.CurrentColliderMode = TileEditorState.ColliderMode.Draw;
            Assert.IsTrue(CallIsColliderEditModeActive(manager), "Draw must report active.");

            manager.State.CurrentColliderMode = TileEditorState.ColliderMode.Erase;
            Assert.IsTrue(CallIsColliderEditModeActive(manager), "Erase must report active.");
        }

        [Test]
        public void IsLayerJumpsEditModeActive_TrueOnlyWhenNotNone()
        {
            var manager = NewManager();

            manager.State.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.None;
            Assert.IsFalse(manager.IsLayerJumpsEditModeActive(), "None must report inactive.");

            manager.State.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.Draw;
            Assert.IsTrue(manager.IsLayerJumpsEditModeActive(), "Draw must report active.");

            manager.State.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.Erase;
            Assert.IsTrue(manager.IsLayerJumpsEditModeActive(), "Erase must report active.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. OnDrawCollidersClicked — guard + toggle + tool mirroring
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OnDrawCollidersClicked_WithEmptyActiveTag_RefusesAndLeavesModeNone()
        {
            var manager = NewManager();
            manager.State.ActiveCollisionTag = string.Empty;

            CallOnDrawCollidersClicked(manager);

            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode,
                "M1.10 guard: Draw must refuse to activate with no layer selected in the " +
                "Apply-To-Layer picker — painting with an empty mask has nowhere to route.");
        }

        [Test]
        public void OnDrawCollidersClicked_TogglesOnThenOff()
        {
            var manager = NewManager();
            manager.State.ActiveCollisionTag = "2";

            CallOnDrawCollidersClicked(manager);
            Assert.AreEqual(TileEditorState.ColliderMode.Draw, manager.State.CurrentColliderMode, "First click enters Draw.");
            Assert.IsTrue(manager.State.ShowColliderOverlay, "Entering an edit mode auto-enables the overlay.");
            Assert.AreEqual(TileEditorState.Tool.Brush, manager.State.CurrentTool, "Draw mirrors CurrentTool to Brush for the cursor preview.");

            CallOnDrawCollidersClicked(manager);
            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode, "Second click (re-clicking Draw) toggles back to None.");
        }

        [Test]
        public void OnEraseCollidersClicked_TogglesOnThenOff_AndMirrorsEraserTool()
        {
            var manager = NewManager();

            CallOnEraseCollidersClicked(manager);
            Assert.AreEqual(TileEditorState.ColliderMode.Erase, manager.State.CurrentColliderMode, "First click enters Erase.");
            Assert.AreEqual(TileEditorState.Tool.Eraser, manager.State.CurrentTool, "Erase mirrors CurrentTool to Eraser.");

            CallOnEraseCollidersClicked(manager);
            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode, "Second click toggles back to None.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. The mutex is now symmetric — Colliders → LayerJumps direction.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OnDrawCollidersClicked_ClearsLayerJumpMode()
        {
            var manager = NewManager();
            manager.State.ActiveCollisionTag = "2"; // pass the M1.10 guard
            manager.State.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.Draw; // Layer-Jumps already active

            CallOnDrawCollidersClicked(manager);

            Assert.AreEqual(TileEditorState.ColliderMode.Draw, manager.State.CurrentColliderMode,
                "Colliders Draw must still activate for the collider author's own click.");
            Assert.AreEqual(TileEditorState.LayerJumpMode.None, manager.State.CurrentLayerJumpMode,
                "Turning Colliders Draw on must clear an active Layer-Jumps mode — mirrors " +
                "OnDrawLayerJumpsClicked_ClearsColliderMode below, now symmetric in both directions.");

            // Only one predicate reports active at a time now — HandleMouseInput's
            // collider check running first no longer matters because Layer-Jumps was
            // already forced off by the click that turned Colliders on.
            Assert.IsTrue(CallIsColliderEditModeActive(manager));
            Assert.IsFalse(manager.IsLayerJumpsEditModeActive());
        }

        [Test]
        public void OnEraseCollidersClicked_ClearsLayerJumpMode()
        {
            var manager = NewManager();
            manager.State.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.Erase;

            CallOnEraseCollidersClicked(manager);

            Assert.AreEqual(TileEditorState.ColliderMode.Erase, manager.State.CurrentColliderMode);
            Assert.AreEqual(TileEditorState.LayerJumpMode.None, manager.State.CurrentLayerJumpMode,
                "Same symmetric mutex as the Draw pair, for Erase.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. The working direction — LayerJumps → Colliders must keep clearing.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OnDrawLayerJumpsClicked_ClearsColliderMode()
        {
            var manager = NewManager();
            manager.State.ActiveCollisionTag = "2";
            CallOnDrawCollidersClicked(manager); // Colliders Draw active first
            Assert.AreEqual(TileEditorState.ColliderMode.Draw, manager.State.CurrentColliderMode, "Sanity: Colliders is on.");

            manager.OnDrawLayerJumpsClicked(); // internal — direct call

            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode,
                "Turning Draw Jumps on must clear an active Colliders mode — this direction of the " +
                "mutex must not regress alongside the Colliders → LayerJumps direction fixed above.");
            Assert.AreEqual(TileEditorState.LayerJumpMode.Draw, manager.State.CurrentLayerJumpMode);
        }

        [Test]
        public void OnEraseLayerJumpsClicked_ClearsColliderMode()
        {
            var manager = NewManager();
            CallOnEraseCollidersClicked(manager);
            Assert.AreEqual(TileEditorState.ColliderMode.Erase, manager.State.CurrentColliderMode, "Sanity: Colliders is on.");

            manager.OnEraseLayerJumpsClicked();

            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode,
                "Turning Erase Jumps on must clear an active Colliders mode.");
            Assert.AreEqual(TileEditorState.LayerJumpMode.Erase, manager.State.CurrentLayerJumpMode);
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. Dispatch order — structural guard for HandleMouseInput (source text,
        //    same technique as Game/Meta/BraceBalanceRegressionTests). This is the
        //    only provable form of "with a mode active, the click doesn't run the
        //    selected tool": WasLeftMouseButtonPressedThisFrame() is always false in
        //    EditMode, so calling HandleMouseInput directly can't discriminate a
        //    correct early-return from a broken one — both produce zero observable
        //    side effects without a real click.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void HandleMouseInput_RoutesColliderModeThenLayerJumpsMode_BeforeTheToolSwitch()
        {
            string path = Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Editors/Tile/TileEditorManager.InputHandlers.cs");
            Assert.IsTrue(File.Exists(path), $"Production file not found: {path}");
            string source = File.ReadAllText(path);

            int idxColliderCheck   = source.IndexOf("IsColliderEditModeActive()");
            int idxColliderHandle  = source.IndexOf("HandleColliderInput();");
            int idxJumpsCheck      = source.IndexOf("IsLayerJumpsEditModeActive()");
            int idxJumpsHandle     = source.IndexOf("HandleLayerJumpsInput();");
            int idxSwitch          = source.IndexOf("switch (_state.CurrentTool)");

            Assert.Greater(idxColliderCheck, -1, "HandleMouseInput must check IsColliderEditModeActive().");
            Assert.Greater(idxColliderHandle, -1, "HandleMouseInput must dispatch to HandleColliderInput().");
            Assert.Greater(idxJumpsCheck, -1, "HandleMouseInput must check IsLayerJumpsEditModeActive().");
            Assert.Greater(idxJumpsHandle, -1, "HandleMouseInput must dispatch to HandleLayerJumpsInput().");
            Assert.Greater(idxSwitch, -1, "HandleMouseInput must still dispatch tools via a switch on CurrentTool.");

            Assert.Less(idxColliderCheck, idxJumpsCheck,
                "Colliders must be checked BEFORE Layer-Jumps. Now that the toggle handlers keep " +
                "the two modes mutually exclusive in both directions this ordering is no longer " +
                "load-bearing for correctness, but it must stay stable so a future regression that " +
                "reintroduces the asymmetric gap fails loud (Colliders would silently win again).");
            Assert.Less(idxJumpsCheck, idxSwitch,
                "Both mode checks must happen before the tool switch, not after.");
            Assert.Less(idxColliderHandle, idxSwitch);
            Assert.Less(idxJumpsHandle, idxSwitch);

            // Each dispatch must `return;` immediately — falling through would let the
            // click ALSO run the tool switch on the same frame.
            //
            // Assert the SHAPE, not a character distance: a raw offset budget breaks on
            // any reindent (it did — the gap measured 41 against a 40 budget while the
            // code was perfectly correct) and would still pass if someone slipped a real
            // statement in under the budget. This pattern allows whitespace and comments
            // between the call and the return, and nothing else.
            AssertReturnsImmediatelyAfter(source, "HandleColliderInput();");
            AssertReturnsImmediatelyAfter(source, "HandleLayerJumpsInput();");

            int returnAfterJumps = source.IndexOf("return;", idxJumpsHandle);
            Assert.Less(returnAfterJumps, idxSwitch,
                "The Layer-Jumps early-return must land before the tool switch.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. OnLayerJumpsTargetChanged — TARGET LAYER picker callback (audit gap #9).
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OnLayerJumpsTargetChanged_ValidDigit_SetsActiveTarget()
        {
            var manager = NewManager();

            manager.OnLayerJumpsTargetChanged("3");

            Assert.AreEqual("3", manager.State.ActiveJumpTargetLayer);
        }

        [Test]
        public void OnLayerJumpsTargetChanged_InvalidInput_FallsBackToZero()
        {
            var manager = NewManager();

            manager.OnLayerJumpsTargetChanged("abc");
            Assert.AreEqual("0", manager.State.ActiveJumpTargetLayer, "Non-digit garbage must fall back to '0'.");

            manager.OnLayerJumpsTargetChanged("9");
            Assert.AreEqual("0", manager.State.ActiveJumpTargetLayer, "'9' is out of the valid 0..8 range and must fall back to '0'.");

            manager.OnLayerJumpsTargetChanged(null);
            Assert.AreEqual("0", manager.State.ActiveJumpTargetLayer, "Null must fall back to '0', not throw.");
        }

        /// <summary>
        /// Asserts that <paramref name="call"/> is followed by <c>return;</c> with nothing
        /// between them but whitespace and comments. Structural, but immune to formatting —
        /// unlike a character-offset budget, which fails on a reindent and passes on a
        /// smuggled-in short statement.
        /// </summary>
        private static void AssertReturnsImmediatelyAfter(string source, string call)
        {
            // Whitespace, then any run of // line comments, then the return.
            string pattern = Regex.Escape(call) + @"\s*(?://[^\r\n]*[\r\n]+\s*)*return\s*;";
            Assert.IsTrue(Regex.IsMatch(source, pattern),
                $"`{call}` must be followed immediately by `return;` (whitespace/comments aside). " +
                "Falling through would let the same click also run the tool switch.");
        }

    }
}
