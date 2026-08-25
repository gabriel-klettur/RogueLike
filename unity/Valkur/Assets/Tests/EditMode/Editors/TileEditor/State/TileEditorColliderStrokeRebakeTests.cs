using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.State
{
    /// <summary>
    /// Regression coverage for the Tile Editor perf-wave-2 change that moved
    /// <c>RegenerateCompositeCollider</c> out of the collider paint/drag hot
    /// path (up to ~120 calls/sec while dragging with the editor forcing
    /// 120 FPS) and into a once-per-stroke commit
    /// (<c>_colliderStrokeDirty</c> + <c>FlushPendingColliderRebake</c>).
    ///
    /// Two kinds of assertion:
    ///   • STRUCTURAL — source-text scans proving the call-site policy
    ///     (mouse-down/drag branches never call the regenerate directly;
    ///     only the mouse-release branch and the two "force-end a stroke"
    ///     paths call the flush). The same technique
    ///     <c>TileEditorColliderLayerJumpsMutexTests</c> already uses for
    ///     <c>HandleMouseInput</c>'s dispatch order, for the same reason:
    ///     <c>WasLeftMouseButtonPressedThisFrame()</c> is always false in
    ///     EditMode, so a real mouse-driven stroke cannot be simulated.
    ///   • FUNCTIONAL — the <c>_colliderStrokeDirty</c> flag's lifecycle
    ///     itself (set → flushed → cleared), driven directly via reflection
    ///     on a bare <c>TileEditorManager</c> (no <c>worldGridBuilder</c>
    ///     wired, so <c>GetCollisionTilemap()</c> returns null and the
    ///     actual composite regeneration is skipped — exactly the same bare
    ///     setup <c>TileEditorColliderLayerJumpsMutexTests</c> already uses
    ///     for the mode-toggle handlers, which is why it stays safe without
    ///     a real Tilemap/CompositeCollider2D).
    /// </summary>
    [TestFixture]
    public class TileEditorColliderStrokeRebakeTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private TileEditorManager NewManager()
        {
            _host = new GameObject("ColliderStrokeRebakeTests_Host");
            return _host.AddComponent<TileEditorManager>();
        }

        private static object InvokePrivate(TileEditorManager manager, string methodName)
        {
            var mi = typeof(TileEditorManager).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, $"Reflection: '{methodName}' not found on TileEditorManager.");
            return mi.Invoke(manager, null);
        }

        private static bool GetColliderStrokeDirty(TileEditorManager manager)
        {
            var f = typeof(TileEditorManager).GetField("_colliderStrokeDirty",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Reflection: _colliderStrokeDirty field not found on TileEditorManager.");
            return (bool)f.GetValue(manager);
        }

        private static void SetColliderStrokeDirty(TileEditorManager manager, bool value)
        {
            var f = typeof(TileEditorManager).GetField("_colliderStrokeDirty",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Reflection: _colliderStrokeDirty field not found on TileEditorManager.");
            f.SetValue(manager, value);
        }

        private static string ReadProductionFile(string relativePath)
        {
            string path = Path.Combine(Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), $"Production file not found: {path}");
            return File.ReadAllText(path);
        }

        // ════════════════════════════════════════════════════════════════
        // 1. Structural — the per-frame paint/drag path never regenerates directly
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void HandleColliderInput_Source_NeverCallsRegenerateCompositeColliderDirectly()
        {
            string source = ReadProductionFile(
                "_Project/Scripts/Gameplay/Editors/Tile/TileEditorManager.Colliders.cs");

            int start = source.IndexOf("private void HandleColliderInput()");
            Assert.Greater(start, -1, "HandleColliderInput() must exist.");
            int end = source.IndexOf("internal void OnCollisionTagChanged", start);
            Assert.Greater(end, start, "Could not bound HandleColliderInput()'s body.");
            string body = source.Substring(start, end - start);

            Assert.IsFalse(body.Contains("RegenerateCompositeCollider("),
                "HandleColliderInput's mouse-down/drag/release branches must never call " +
                "RegenerateCompositeCollider directly — doing so on the drag branch is exactly " +
                "the up-to-120-calls/sec cost the perf pass removed. Only " +
                "FlushPendingColliderRebake() may call it now.");
            Assert.IsTrue(body.Contains("_colliderStrokeDirty = true"),
                "The paint/drag branches must mark the stroke dirty so the composite can be " +
                "regenerated once, at stroke end.");
            Assert.IsTrue(body.Contains("FlushPendingColliderRebake();"),
                "The mouse-release branch must call FlushPendingColliderRebake() to commit the " +
                "stroke's composite rebake exactly once.");
        }

        [Test]
        public void OnDrawAndEraseCollidersClicked_Source_FlushPendingReboundBeforeTogglingMode()
        {
            // Self-toggle-off mid-drag (or re-entering Draw/Erase) must not lose a
            // pending composite rebake either — both handlers flush before flipping
            // CurrentColliderMode.
            string source = ReadProductionFile(
                "_Project/Scripts/Gameplay/Editors/Tile/TileEditorManager.Colliders.cs");

            int drawStart = source.IndexOf("private void OnDrawCollidersClicked()");
            int eraseStart = source.IndexOf("private void OnEraseCollidersClicked()");
            Assert.Greater(drawStart, -1);
            Assert.Greater(eraseStart, drawStart);
            int eraseEnd = source.IndexOf("// ── Overlay binding", eraseStart);
            Assert.Greater(eraseEnd, eraseStart, "Could not bound OnEraseCollidersClicked()'s body.");

            string drawBody = source.Substring(drawStart, eraseStart - drawStart);
            string eraseBody = source.Substring(eraseStart, eraseEnd - eraseStart);

            Assert.IsTrue(drawBody.Contains("FlushPendingColliderRebake();"),
                "OnDrawCollidersClicked must flush a pending stroke rebake before toggling mode.");
            Assert.IsTrue(eraseBody.Contains("FlushPendingColliderRebake();"),
                "OnEraseCollidersClicked must flush a pending stroke rebake before toggling mode.");
        }

        [Test]
        public void LayerJumpsMutexHandlers_Source_FlushPendingColliderRebake_WhenForcingCollidersOff()
        {
            string source = ReadProductionFile(
                "_Project/Scripts/Gameplay/Editors/Tile/TileEditorManager.LayerJumps.cs");

            int drawStart = source.IndexOf("internal void OnDrawLayerJumpsClicked()");
            int eraseStart = source.IndexOf("internal void OnEraseLayerJumpsClicked()");
            // Unambiguous, plain-ASCII marker immediately following both handlers —
            // avoids relying on brace-depth counting, which the "FlushPendingColliderRebake();"
            // call itself sits inside a nested if-block for.
            int eraseEnd = source.IndexOf("internal void OnLayerJumpsTargetChanged", eraseStart);
            Assert.Greater(drawStart, -1, "OnDrawLayerJumpsClicked() must exist.");
            Assert.Greater(eraseStart, drawStart, "OnEraseLayerJumpsClicked() must exist, after Draw.");
            Assert.Greater(eraseEnd, eraseStart, "Could not bound OnEraseLayerJumpsClicked()'s body.");

            string drawBody = source.Substring(drawStart, eraseStart - drawStart);
            string eraseBody = source.Substring(eraseStart, eraseEnd - eraseStart);

            Assert.IsTrue(drawBody.Contains("FlushPendingColliderRebake();"),
                "OnDrawLayerJumpsClicked must flush a pending collider stroke when the mutex " +
                "forces Colliders mode off, or a half-finished drag's composite rebake is lost " +
                "silently.");
            Assert.IsTrue(eraseBody.Contains("FlushPendingColliderRebake();"),
                "OnEraseLayerJumpsClicked must do the same on its side of the mutex.");
        }

        // ════════════════════════════════════════════════════════════════
        // 2. Functional — _colliderStrokeDirty flag lifecycle
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void FlushPendingColliderRebake_WhenNothingPending_DoesNotThrow_AndLeavesFlagFalse()
        {
            var manager = NewManager(); // no worldGridBuilder wired -> GetCollisionTilemap() == null

            Assert.DoesNotThrow(() => InvokePrivate(manager, "FlushPendingColliderRebake"));
            Assert.IsFalse(GetColliderStrokeDirty(manager));
        }

        [Test]
        public void FlushPendingColliderRebake_WithPendingFlag_ConsumesIt_WithoutThrowing()
        {
            var manager = NewManager();
            SetColliderStrokeDirty(manager, true);

            Assert.DoesNotThrow(() => InvokePrivate(manager, "FlushPendingColliderRebake"),
                "Flushing with no worldGridBuilder wired (GetCollisionTilemap() == null) must be " +
                "a safe no-op for the actual regenerate call, while still consuming the flag.");
            Assert.IsFalse(GetColliderStrokeDirty(manager),
                "FlushPendingColliderRebake must clear _colliderStrokeDirty exactly once it has " +
                "been consumed — otherwise the next flush call would redundantly re-fire, or a " +
                "stale 'true' could linger and confuse a later assertion about whether a stroke " +
                "is still pending.");
        }

        [Test]
        public void OnDrawCollidersClicked_SelfToggleOff_FlushesPendingColliderStroke()
        {
            var manager = NewManager();
            manager.State.ActiveCollisionTag = "2"; // pass the M1.10 empty-mask guard

            InvokePrivate(manager, "OnDrawCollidersClicked"); // turn ON
            Assert.AreEqual(TileEditorState.ColliderMode.Draw, manager.State.CurrentColliderMode, "Sanity.");
            SetColliderStrokeDirty(manager, true); // simulate a stroke mid-drag when the user re-clicks Draw

            InvokePrivate(manager, "OnDrawCollidersClicked"); // self-toggle back OFF

            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode, "Sanity.");
            Assert.IsFalse(GetColliderStrokeDirty(manager),
                "Turning Draw Colliders back off mid-drag must flush any pending composite " +
                "rebake instead of silently dropping it.");
        }

        [Test]
        public void OnEraseCollidersClicked_SelfToggleOff_FlushesPendingColliderStroke()
        {
            var manager = NewManager();

            InvokePrivate(manager, "OnEraseCollidersClicked"); // turn ON
            Assert.AreEqual(TileEditorState.ColliderMode.Erase, manager.State.CurrentColliderMode, "Sanity.");
            SetColliderStrokeDirty(manager, true);

            InvokePrivate(manager, "OnEraseCollidersClicked"); // turn OFF

            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode, "Sanity.");
            Assert.IsFalse(GetColliderStrokeDirty(manager));
        }

        [Test]
        public void OnDrawLayerJumpsClicked_WhileColliderStrokePending_FlushesItBeforeTheMutexSwitch()
        {
            var manager = NewManager();
            manager.State.ActiveCollisionTag = "2";
            InvokePrivate(manager, "OnDrawCollidersClicked"); // Colliders Draw active
            SetColliderStrokeDirty(manager, true); // a stroke was mid-drag when the panel switch happens

            manager.OnDrawLayerJumpsClicked(); // internal — mutex forces Colliders off

            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode,
                "Sanity: the mutex fired.");
            Assert.IsFalse(GetColliderStrokeDirty(manager),
                "The Layer-Jumps mutex switch must flush a pending collider stroke, not just " +
                "clear the mode flag and abandon the composite rebake for whatever cells were " +
                "already painted.");
        }

        [Test]
        public void OnEraseLayerJumpsClicked_WhileColliderStrokePending_FlushesItBeforeTheMutexSwitch()
        {
            var manager = NewManager();
            InvokePrivate(manager, "OnEraseCollidersClicked");
            SetColliderStrokeDirty(manager, true);

            manager.OnEraseLayerJumpsClicked();

            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode,
                "Sanity: the mutex fired.");
            Assert.IsFalse(GetColliderStrokeDirty(manager));
        }
    }
}
