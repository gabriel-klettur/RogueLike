using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Coverage for the NEW debounced / off-thread autosave pump added in
    /// <c>TileOverlayPersistence.Autosave.cs</c> (Tile Editor perf wave 2).
    ///
    /// What this file deliberately does NOT try to exercise: the actual
    /// debounce countdown (<c>DebounceLoopAsync</c> / <c>Task.Delay</c>) and
    /// the background snapshot→serialize→write flow
    /// (<c>PerformDeferredFlushAsync</c>). Both are armed by
    /// <c>ArmAutosaveTimer</c> ONLY when <c>Application.isPlaying</c> is true,
    /// which is always false in EditMode — see
    /// <see cref="MarkCellDirty_InEditMode_NeverArmsDeferredAutosaveLoop"/>,
    /// which locks that guard down as an explicit regression test rather than
    /// just relying on it being true by absence of evidence. Those two methods
    /// need a PlayMode test to exercise directly.
    ///
    /// What IS fully testable here, deterministically, in EditMode:
    ///   • <c>WaitForInFlightAutosave()</c> — the mechanism every immediate
    ///     flush (<c>SaveAllDirty</c> / <c>SaveZone</c>) calls first to force
    ///     any in-flight deferred write to finish before proceeding, and to
    ///     swallow (not propagate) a faulted deferred write.
    ///   • The cross-instance lifecycle registry (<c>_liveAutosaveInstances</c>,
    ///     <c>FlushAllLiveAutosaveInstances</c>, <c>ResetAutosaveStaticState</c>)
    ///     that backs the "flush on exit-Play / quit" safety net — all plain
    ///     synchronous methods once invoked directly via reflection.
    ///
    /// All fixtures here use <see cref="InMemoryTileOverrideRepository"/> so
    /// nothing touches disk and no <c>TearDown</c> cleanup of real files is
    /// needed.
    /// </summary>
    [TestFixture]
    public class TileOverlayPersistenceAutosaveTests
    {
        private const string ZONE_A = "zone_autosave_test_A";
        private const string ZONE_B = "zone_autosave_test_B";

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zonesGo;
        private ZoneManager _zones;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zonesGo = new GameObject("ZoneManager");
            _zones = _zonesGo.AddComponent<ZoneManager>();
            _zones.AddZone(ZONE_A, new Vector2Int(0, 0), editableInTileEditor: true);
            _zones.AddZone(ZONE_B, new Vector2Int(50, 0), editableInTileEditor: true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) UnityEngine.Object.DestroyImmediate(_gridGo);
            if (_zonesGo != null) UnityEngine.Object.DestroyImmediate(_zonesGo);

            // Every persistence instance constructed by this fixture self-registers
            // into the static cross-instance registry (Domain Reload is OFF, so it
            // would otherwise accumulate across every other TileOverlayPersistence
            // fixture in the same EditMode session). Reset it so this file never
            // leaks state into an unrelated test.
            InvokeStatic("ResetAutosaveStaticState");

            LogAssert.ignoreFailingMessages = false;
        }

        // ── Setup helper ─────────────────────────────────────────────────

        private TileOverlayPersistence NewPersistence(ITileOverrideRepository repo = null)
            => new TileOverlayPersistence(_zones, _grid, repo ?? new InMemoryTileOverrideRepository());

        // ── Reflection helpers ───────────────────────────────────────────

        private static object GetPrivateInstance(object obj, string name)
        {
            var f = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Reflection: instance field '{name}' not found on {obj.GetType().Name}.");
            return f.GetValue(obj);
        }

        private static void SetPrivateInstance(object obj, string name, object value)
        {
            var f = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Reflection: instance field '{name}' not found on {obj.GetType().Name}.");
            f.SetValue(obj, value);
        }

        private static object InvokeStatic(string name, params object[] args)
        {
            var m = typeof(TileOverlayPersistence).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, $"Reflection: static method '{name}' not found on TileOverlayPersistence.");
            return m.Invoke(null, args);
        }

        private static int GetLiveInstanceCount()
        {
            var f = typeof(TileOverlayPersistence).GetField("_liveAutosaveInstances",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "Reflection: _liveAutosaveInstances field not found.");
            var list = f.GetValue(null);
            var countProp = list.GetType().GetProperty("Count");
            return (int)countProp.GetValue(list);
        }

        // ════════════════════════════════════════════════════════════════
        // 1. WaitForInFlightAutosave — the deferred-before-immediate ordering
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void SaveAllDirty_BlocksUntilInFlightAutosaveTask_CompletesBeforeReturning()
        {
            var persistence = NewPersistence();
            bool backgroundWorkDone = false;
            var bgTask = Task.Run(() =>
            {
                Thread.Sleep(50);
                backgroundWorkDone = true;
            });
            SetPrivateInstance(persistence, "_inFlightAutosaveTask", bgTask);

            persistence.SaveAllDirty();

            Assert.IsTrue(backgroundWorkDone,
                "SaveAllDirty must block on WaitForInFlightAutosave() until the deferred " +
                "background write has actually finished — otherwise an explicit save could race " +
                "a debounced autosave and either clobber it or report zones saved before they are.");
            Assert.IsTrue(bgTask.IsCompleted);
        }

        [Test]
        public void SaveZone_BlocksUntilInFlightAutosaveTask_CompletesBeforeReturning()
        {
            var persistence = NewPersistence();
            bool backgroundWorkDone = false;
            var bgTask = Task.Run(() =>
            {
                Thread.Sleep(50);
                backgroundWorkDone = true;
            });
            SetPrivateInstance(persistence, "_inFlightAutosaveTask", bgTask);

            persistence.SaveZone(ZONE_A);

            Assert.IsTrue(backgroundWorkDone,
                "SaveZone is the second immediate-flush entry point and must honour the exact " +
                "same ordering guarantee as SaveAllDirty.");
        }

        [Test]
        public void SaveAllDirty_SwallowsFaultedInFlightAutosaveTask_AndStillCompletesSynchronousSave()
        {
            var persistence = NewPersistence();

            var faultedTask = Task.Run(() =>
                throw new InvalidOperationException("simulated deferred-flush failure"));
            // Force the fault to materialize deterministically before handing the task
            // to SaveAllDirty, so the assertion below never races the ThreadPool.
            try { faultedTask.Wait(); } catch (AggregateException) { /* expected, observed here on purpose */ }
            Assert.IsTrue(faultedTask.IsFaulted, "Precondition: task must be faulted.");

            SetPrivateInstance(persistence, "_inFlightAutosaveTask", faultedTask);
            persistence.MarkCellDirty(new Vector3Int(1, 1, 0)); // give the synchronous half real work to do

            int saved = -1;
            Assert.DoesNotThrow(() => saved = persistence.SaveAllDirty(),
                "A faulted background autosave task must never propagate out of SaveAllDirty — " +
                "an explicit save must still succeed even if the previous deferred flush failed.");
            Assert.AreEqual(1, saved, "The synchronous save itself must still complete normally afterward.");
        }

        // ════════════════════════════════════════════════════════════════
        // 2. ArmAutosaveTimer — must be a hard no-op in EditMode
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void MarkCellDirty_InEditMode_NeverArmsDeferredAutosaveLoop()
        {
            // Application.isPlaying is always false in EditMode. If ArmAutosaveTimer's
            // guard on it were ever removed, every existing EditMode test across the
            // whole Tile Editor suite that calls MarkCellDirty would start spawning
            // real background Tasks mid-test, turning the suite non-deterministic.
            Assert.IsFalse(Application.isPlaying, "Sanity: this test must run in EditMode.");

            var persistence = NewPersistence();
            persistence.MarkCellDirty(new Vector3Int(1, 1, 0));

            Assert.IsFalse((bool)GetPrivateInstance(persistence, "_autosaveLoopRunning"),
                "MarkCellDirty must not arm the deferred autosave debounce loop while " +
                "Application.isPlaying is false.");
            Assert.IsNull(GetPrivateInstance(persistence, "_inFlightAutosaveTask"),
                "No background write should ever be started as a side effect of an EditMode test.");
        }

        [Test]
        public void MarkBatchDirty_InEditMode_NeverArmsDeferredAutosaveLoop()
        {
            var persistence = NewPersistence();
            var edits = new List<TileEdit> { new TileEdit(new Vector3Int(2, 2, 0), null, null) };

            persistence.MarkBatchDirty(edits);

            Assert.IsFalse((bool)GetPrivateInstance(persistence, "_autosaveLoopRunning"));
            Assert.IsNull(GetPrivateInstance(persistence, "_inFlightAutosaveTask"));
        }

        // ════════════════════════════════════════════════════════════════
        // 3. Cross-instance lifecycle registry — the exit-Play / quit net
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void ConstructingInstance_RegistersItInLiveAutosaveRegistry()
        {
            InvokeStatic("ResetAutosaveStaticState");
            Assert.AreEqual(0, GetLiveInstanceCount(), "Precondition after reset.");

            var persistence = NewPersistence();
            GC.KeepAlive(persistence);

            Assert.AreEqual(1, GetLiveInstanceCount(),
                "Every TileOverlayPersistence constructor overload must self-register via " +
                "RegisterForAutosaveLifecycleTracking so a hard-quit / exit-Play flush can find " +
                "it even though nothing else holds a reference long enough to call Dispose — " +
                "RebindToWorld constructs a new instance per map-slot switch without freeing " +
                "the old one.");
        }

        [Test]
        public void FlushAllLiveAutosaveInstances_SavesEveryLiveInstance_ClearingTheirDirtyState()
        {
            InvokeStatic("ResetAutosaveStaticState");

            var repoA = new InMemoryTileOverrideRepository();
            var persistenceA = NewPersistence(repoA);
            persistenceA.MarkCellDirty(new Vector3Int(1, 1, 0)); // inside ZONE_A

            var repoB = new InMemoryTileOverrideRepository();
            var persistenceB = NewPersistence(repoB);
            persistenceB.MarkCellDirty(new Vector3Int(60, 10, 0)); // inside ZONE_B

            Assert.IsTrue(persistenceA.HasUnsavedChanges, "Precondition.");
            Assert.IsTrue(persistenceB.HasUnsavedChanges, "Precondition.");

            InvokeStatic("FlushAllLiveAutosaveInstances");

            Assert.IsFalse(persistenceA.HasUnsavedChanges,
                "The exit-Play / quit safety net must flush every still-live instance's dirty " +
                "zones so no edit is lost when the process (or Play session) ends — this is the " +
                "one explicit-save scenario ('salir de Play') that does not already flow through " +
                "an existing SaveAllDirty() call site.");
            Assert.IsFalse(persistenceB.HasUnsavedChanges);
            Assert.IsTrue(repoA.Exists(WorldId.Base, ZONE_A));
            Assert.IsTrue(repoB.Exists(WorldId.Base, ZONE_B));
        }

        [Test]
        public void ResetAutosaveStaticState_ClearsLiveInstanceRegistry()
        {
            var persistence = NewPersistence();
            GC.KeepAlive(persistence);
            Assert.Greater(GetLiveInstanceCount(), 0, "Precondition: at least one instance registered.");

            InvokeStatic("ResetAutosaveStaticState");

            Assert.AreEqual(0, GetLiveInstanceCount(),
                "ResetAutosaveStaticState must clear the registry — required because Domain " +
                "Reload is OFF, so plain static state would otherwise leak stale instances (and " +
                "duplicate event subscriptions) across Play sessions.");
        }

        [Test]
        public void ResetAutosaveStaticState_IsTaggedForSubsystemRegistration()
        {
            var method = typeof(TileOverlayPersistence).GetMethod("ResetAutosaveStaticState",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ResetAutosaveStaticState must exist as a private static method.");

            var attr = (RuntimeInitializeOnLoadMethodAttribute)Attribute.GetCustomAttribute(
                method, typeof(RuntimeInitializeOnLoadMethodAttribute));
            Assert.IsNotNull(attr,
                "Project convention: static mutable state needs [RuntimeInitializeOnLoadMethod] " +
                "since Domain Reload is OFF.");
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, attr.loadType,
                "Must reset at SubsystemRegistration, matching every other static-reset hook in " +
                "the project.");
        }

        // ════════════════════════════════════════════════════════════════
        // 4. Debounce window constant
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void AutosaveDebounceSeconds_Is_0_4Seconds()
        {
            Assert.AreEqual(0.4f, TileEditorConstants.AutosaveDebounceSeconds, 0.0001f,
                "The documented 0.4s debounce window is what decides how bursty a series of " +
                "quick strokes needs to be before they coalesce into a single background write.");
        }
    }
}
