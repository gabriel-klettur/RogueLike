using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.State
{
    /// <summary>
    /// Same class of bug as the already-fixed "BUG 3" in <c>TileEditorLifecycleTests</c>
    /// (<c>CurrentLayerJumpMode</c> surviving a close/reopen cycle), but for the drag
    /// bookkeeping instead of the collider/jumps mode enums. <c>HandleToggle</c>'s
    /// activate branch resets <c>CurrentTool</c>, <c>CurrentLayer</c>,
    /// <c>CurrentColliderMode</c> and <c>CurrentLayerJumpMode</c> — but never
    /// <see cref="TileEditorState.IsDragging"/>, <see cref="TileEditorState.RectDragStart"/>,
    /// <see cref="TileEditorState.RectDragCurrent"/>, <see cref="TileEditorState.RegionDragStart"/>
    /// or <see cref="TileEditorState.RegionDragCurrent"/>. The deactivate branch doesn't
    /// touch them either.
    ///
    /// Repro: the user is mid-drag on a Rect-select or an AutoTile-region box (LMB still
    /// held) and presses F8 with the other hand. <c>IsDragging</c> plus the stale
    /// drag-anchor coordinates survive the close/reopen cycle intact. The first frame
    /// after reopening where <c>IsLeftMouseButtonPressed()</c> happens to be true (e.g.
    /// the user is still holding the mouse button down from before) would then read as
    /// "continuing" a drag that has no business existing anymore.
    ///
    /// These tests PIN the current (buggy) behaviour so a future change to either reset
    /// branch is a deliberate, visible diff here — not a silent regression in either
    /// direction. If this fix ships, these assertions should flip to Assert.IsFalse /
    /// Assert.IsNull, mirroring how TileEditorLifecycleTests documents BUG 3.
    /// </summary>
    [TestFixture]
    public class TileEditorDragStateResetTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();

            SetSingletonInstance<TileEditorManager>(null);
            SetSingletonInstance<GameEditorManager>(null);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Harness, duplicated from TileEditorLifecycleTests.NewManagerWithUI per
        // project convention (small test harnesses are copied per file, not shared). ──

        private static void SetSingletonInstance<T>(T value) where T : MonoBehaviour
        {
            var baseType = typeof(T).BaseType; // SingletonMonoBehaviour<T>
            var f = baseType?.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            f?.SetValue(null, value);
        }

        private static void SetField(object obj, string name, object value)
        {
            var f = typeof(TileEditorManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Reflection: field '{name}' not found on TileEditorManager.");
            f.SetValue(obj, value);
        }

        private (TileEditorManager manager, TileEditorUI ui) NewManagerWithUI()
        {
            SetSingletonInstance<TileEditorManager>(null);
            SetSingletonInstance<GameEditorManager>(null);

            var managerGo = new GameObject("DragStateResetTests_Manager");
            _scene.Add(managerGo);
            var manager = managerGo.AddComponent<TileEditorManager>();

            var uiGo = new GameObject("DragStateResetTests_UI");
            uiGo.transform.SetParent(managerGo.transform);
            _scene.Add(uiGo);
            var ui = uiGo.AddComponent<TileEditorUI>();
            ui.Initialize(manager.State, catalog: null,
                onTileSelected: null, onToolChanged: null,
                onLayerChanged: null, onBrushSizeChanged: null);

            SetField(manager, "_ui", ui);
            SetField(manager, "_undo", new TileEditorUndoSystem());

            return (manager, ui);
        }

        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Reopen_DoesNotResetRectSelectDragState_KnownGap()
        {
            LogAssert.ignoreFailingMessages = true;
            var (manager, _) = NewManagerWithUI();

            manager.Activate();
            // Simulate the user mid-drag on a Rect selection when F8 is pressed.
            manager.State.IsDragging = true;
            manager.State.RectDragStart = new Vector3Int(2, 3, 0);
            manager.State.RectDragCurrent = new Vector3Int(4, 5, 0);

            manager.Deactivate();
            manager.Activate();

            Assert.IsTrue(manager.State.IsDragging,
                "KNOWN GAP — HandleToggle never resets IsDragging on activate or deactivate. If this " +
                "now reads false, the gap was closed: update this assertion (and the two below) to " +
                "expect the reset, mirroring TileEditorLifecycleTests' BUG-3 fix pattern.");
            Assert.AreEqual(new Vector3Int(2, 3, 0), manager.State.RectDragStart,
                "KNOWN GAP — stale RectDragStart survives a close/reopen cycle.");
            Assert.AreEqual(new Vector3Int(4, 5, 0), manager.State.RectDragCurrent,
                "KNOWN GAP — stale RectDragCurrent survives a close/reopen cycle.");

            manager.Deactivate();
        }

        [Test]
        public void Reopen_DoesNotResetAutoTileRegionDragState_KnownGap()
        {
            LogAssert.ignoreFailingMessages = true;
            var (manager, _) = NewManagerWithUI();

            manager.Activate();
            // Simulate the user mid-drag on an AutoTileRegion box when F8 is pressed.
            manager.State.IsDragging = true;
            manager.State.RegionDragStart = new Vector3Int(1, 1, 0);
            manager.State.RegionDragCurrent = new Vector3Int(9, 9, 0);

            manager.Deactivate();
            manager.Activate();

            Assert.AreEqual(new Vector3Int(1, 1, 0), manager.State.RegionDragStart,
                "KNOWN GAP — stale RegionDragStart survives a close/reopen cycle, the same class of " +
                "bug as the fixed CurrentLayerJumpMode leak (BUG 3).");
            Assert.AreEqual(new Vector3Int(9, 9, 0), manager.State.RegionDragCurrent,
                "KNOWN GAP — stale RegionDragCurrent survives a close/reopen cycle.");

            manager.Deactivate();
        }

        [Test]
        public void Reopen_StillResetsCurrentTool_UnaffectedBySiblingDragGap()
        {
            // Narrow control test: proves the harness's Activate/Deactivate cycle still
            // performs A reset on reopen (CurrentTool, not otherwise covered elsewhere —
            // TileEditorStateTests only checks the value on a freshly-constructed state,
            // not after a reopen cycle). Without this, a failure in the two "KNOWN GAP"
            // tests above would be ambiguous: harness broken vs. gap confirmed. Deliberately
            // does NOT re-assert CurrentColliderMode / CurrentLayerJumpMode reset — those
            // are already owned by TileEditorLifecycleTests (BUG 3 fix coverage).
            LogAssert.ignoreFailingMessages = true;
            var (manager, _) = NewManagerWithUI();

            manager.Activate();
            manager.State.CurrentTool = TileEditorState.Tool.Fill;

            manager.Deactivate();
            manager.Activate();

            Assert.AreEqual(TileEditorState.Tool.Select, manager.State.CurrentTool,
                "Activate must still reset CurrentTool to Select on reopen.");

            manager.Deactivate();
        }
    }
}
