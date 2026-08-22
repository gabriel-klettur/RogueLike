using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Exercises the preset autosave debounce in
    /// <c>ParticlesRuntimeEditor.PresetPersistence.cs</c> — the fix for the asymmetry where
    /// placed instances autosaved to StreamingAssets JSON on every edit but preset
    /// ScriptableObject edits silently waited for the user to press Save.
    ///
    /// Mirrors <see cref="ParticlesEditorLifecycleTests"/>'s reflection helpers exactly: the
    /// debounce fields (<c>_dirtyPresets</c>, <c>_presetFlushDueAt</c>, <c>_presetWriter</c>)
    /// and methods (<c>MarkParticlePresetDirty</c>, <c>TickPresetAutosave</c>,
    /// <c>FlushDirtyPresets</c>) have no public surface by design — the debounce is an
    /// internal implementation detail of the editor, not something callers should poke.
    /// </summary>
    [TestFixture]
    public class ParticlesPresetAutosaveTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // ScriptableObject probes (catalog + presets) created by MakeCatalog or by an
        // individual test — tracked separately from _sceneObjects because they are assets,
        // not scene objects, and DestroyImmediate on an already-destroyed one is fine but
        // we still guard with a null check (Unity fake-null) in TearDown.
        private readonly List<UnityEngine.Object> _trackedAssets = new List<UnityEngine.Object>();

        // ── Reflection helpers (mirrors ParticlesEditorLifecycleTests) ──────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static FieldInfo FindField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public |
                    BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetFieldValue(object obj, string name)
            => FindField(obj, name)?.GetValue(obj);

        private static void SetFieldValue(object obj, string name, object value)
            => FindField(obj, name)?.SetValue(obj, value);

        private static object InvokeMethod(object obj, string methodName, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            return m?.Invoke(obj, args);
        }

        /// <summary>Reads the debounce set with its concrete type — the field is declared
        /// <c>HashSet&lt;ParticlePresetDefinition&gt;</c> in the design, so no boxing dance
        /// is needed to inspect membership/count.</summary>
        private static HashSet<ParticlePresetDefinition> GetDirtySet(ParticlesRuntimeEditor editor)
            => (HashSet<ParticlePresetDefinition>) GetFieldValue(editor, "_dirtyPresets");

        /// <summary>Resolves a preset by id from the editor's assigned catalog.</summary>
        private static ParticlePresetDefinition GetPreset(ParticlesRuntimeEditor editor, string id)
        {
            var catalog = (ParticlePresetCatalog) GetFieldValue(editor, "_catalog");
            return catalog.GetById(id);
        }

        /// <summary>Installs a fake <c>_presetWriter</c> that records every def it is asked to
        /// write and reports success, so tests can assert on write calls without ever
        /// touching AssetDatabase.</summary>
        private static void InstallFakeWriter(ParticlesRuntimeEditor editor,
            List<ParticlePresetDefinition> calls, bool result = true)
        {
            Func<ParticlePresetDefinition, bool> writer = def =>
            {
                calls.Add(def);
                return result;
            };
            SetFieldValue(editor, "_presetWriter", writer);
        }

        /// <summary>Creates a minimal catalog with two presets (mirrors the Lifecycle fixture).</summary>
        private ParticlePresetCatalog MakeCatalog(params string[] ids)
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            _trackedAssets.Add(catalog);
            var presets = new List<ParticlePresetDefinition>();
            foreach (var id in ids)
            {
                var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
                def.id          = id;
                def.displayName = id;
                _trackedAssets.Add(def);
                presets.Add(def);
            }
            catalog.SetPresets(presets);
            return catalog;
        }

        /// <summary>Creates a standalone probe preset not registered in any catalog — the
        /// "throwaway CreateInstance probe, never a real asset" pattern from
        /// ParticlePresetFieldWriterTests, used where a test needs a def AssetDatabase has
        /// never heard of.</summary>
        private ParticlePresetDefinition MakeProbe(string id)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id          = id;
            def.displayName = id;
            _trackedAssets.Add(def);
            return def;
        }

        /// <summary>Creates a <see cref="ParticlesRuntimeEditor"/> in EditMode (no Play Mode needed).</summary>
        private ParticlesRuntimeEditor CreateEditor(bool withUI = false)
        {
            ClearSingletonInstance<ParticlesRuntimeEditor>();

            var go = new GameObject("TestParticlesEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();

            // Force OnSingletonAwake so the toggle action is initialized.
            InvokeMethod(editor, "OnSingletonAwake");

            // Assign a minimal catalog via reflection so RefreshPicker doesn't complain.
            var catalog = MakeCatalog("preset_a", "preset_b");
            SetFieldValue(editor, "_catalog", catalog);

            // Stub the preview service: mark it as already initialized so that
            // Activate() → _previewService.Initialize() does nothing.
            StubPreviewService(editor);

            if (withUI)
                InvokeMethod(editor, "Start");

            return editor;
        }

        /// <summary>
        /// Stubs out the <see cref="ParticlePreviewService"/> inside <paramref name="editor"/>
        /// so that all its public methods are safe to call in EditMode (no Camera,
        /// no RenderTexture, no GPU resources created). Copied verbatim from
        /// <see cref="ParticlesEditorLifecycleTests"/> so both fixtures stay in lockstep.
        /// </summary>
        private static void StubPreviewService(ParticlesRuntimeEditor editor)
        {
            var serviceField = FindField(editor, "_previewService");
            if (serviceField == null) return;

            var service = serviceField.GetValue(editor);
            if (service == null) return;

            var serviceType = service.GetType();
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;

            var initField = serviceType.GetField("_initialized", bf);
            initField?.SetValue(service, true);

            var poolField = serviceType.GetField("_pool", bf);
            if (poolField != null)
            {
                var pool = poolField.GetValue(service) as Array;
                if (pool != null)
                {
                    var thumbSlotType = serviceType.GetNestedType("ThumbSlot", BindingFlags.NonPublic);
                    if (thumbSlotType != null)
                    {
                        for (int i = 0; i < pool.Length; i++)
                            if (pool.GetValue(i) == null)
                                pool.SetValue(Activator.CreateInstance(thumbSlotType), i);
                    }
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var asset in _trackedAssets)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            _trackedAssets.Clear();

            ClearSingletonInstance<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── MarkParticlePresetDirty ──────────────────────────────────────────────

        /// <summary>The whole debounce is worthless if marking dirty doesn't track the def
        /// and arm the deadline that TickPresetAutosave polls.</summary>
        [Test]
        public void MarkDirty_TracksThePreset_AndSchedulesAFlush()
        {
            var editor = CreateEditor();
            var def = GetPreset(editor, "preset_a");

            InvokeMethod(editor, "MarkParticlePresetDirty", def);

            Assert.IsTrue(GetDirtySet(editor).Contains(def),
                "MarkParticlePresetDirty must add the preset to the dirty set.");
            float dueAt = (float) GetFieldValue(editor, "_presetFlushDueAt");
            Assert.GreaterOrEqual(dueAt, 0f,
                "Marking a preset dirty must schedule a non-negative flush deadline.");
        }

        // ── TickPresetAutosave ───────────────────────────────────────────────────

        /// <summary>A slider drag or a run of typed keystrokes must coalesce into one disk
        /// write, not one per keystroke — so ticking before the deadline must not write.</summary>
        [Test]
        public void Tick_BeforeTheDebounceElapses_DoesNotWrite()
        {
            var editor = CreateEditor();
            var def = GetPreset(editor, "preset_a");
            var calls = new List<ParticlePresetDefinition>();
            InstallFakeWriter(editor, calls);

            InvokeMethod(editor, "MarkParticlePresetDirty", def);
            SetFieldValue(editor, "_presetFlushDueAt", Time.unscaledTime + 100f);

            InvokeMethod(editor, "TickPresetAutosave");

            Assert.AreEqual(0, calls.Count, "No write should happen before the deadline.");
            Assert.IsTrue(GetDirtySet(editor).Contains(def), "The preset must remain dirty.");
        }

        /// <summary>Once the debounce elapses the tick must flush exactly once and clear the
        /// deadline so it doesn't keep firing every frame.</summary>
        [Test]
        public void Tick_AfterTheDebounce_WritesOnceAndClears()
        {
            var editor = CreateEditor();
            var def = GetPreset(editor, "preset_a");
            var calls = new List<ParticlePresetDefinition>();
            InstallFakeWriter(editor, calls);

            InvokeMethod(editor, "MarkParticlePresetDirty", def);
            SetFieldValue(editor, "_presetFlushDueAt", Time.unscaledTime - 1f);

            InvokeMethod(editor, "TickPresetAutosave");

            Assert.AreEqual(1, calls.Count, "Exactly one write must happen once the deadline has passed.");
            Assert.AreSame(def, calls[0]);
            Assert.AreEqual(0, GetDirtySet(editor).Count, "A written preset must leave the dirty set.");
            float dueAt = (float) GetFieldValue(editor, "_presetFlushDueAt");
            Assert.Less(dueAt, 0f, "The deadline must be cleared after a flush.");
        }

        // ── FlushDirtyPresets ─────────────────────────────────────────────────────

        /// <summary>Flushing with nothing dirty must be a true no-op — no writer calls, no
        /// exceptions — since Update() calls TickPresetAutosave() every frame.</summary>
        [Test]
        public void Flush_WithNothingDirty_IsANoOp()
        {
            var editor = CreateEditor();
            var calls = new List<ParticlePresetDefinition>();
            InstallFakeWriter(editor, calls);

            var result = InvokeMethod(editor, "FlushDirtyPresets", "probe");

            Assert.AreEqual(0, (int) result, "Flushing an empty dirty set must report zero writes.");
            Assert.AreEqual(0, calls.Count, "The writer must not be invoked when nothing is dirty.");
        }

        /// <summary>Closing F1 (Deactivate) is one of the four places the design adds a
        /// flush, precisely because it is NOT covered by the periodic Update() tick once
        /// the editor is inactive.</summary>
        [Test]
        public void Deactivate_FlushesDirtyPresets()
        {
            var editor = CreateEditor(withUI: true);
            editor.Activate();
            var def = GetPreset(editor, "preset_a");
            var calls = new List<ParticlePresetDefinition>();
            InstallFakeWriter(editor, calls);
            InvokeMethod(editor, "MarkParticlePresetDirty", def);

            editor.Deactivate();

            CollectionAssert.Contains(calls, def, "Deactivate must flush pending preset edits.");
        }

        /// <summary>Unity raises OnApplicationQuit when the Editor leaves Play Mode too, so an
        /// edit made seconds before Stop must not be lost with it.</summary>
        [Test]
        public void OnApplicationQuit_FlushesDirtyPresets()
        {
            var editor = CreateEditor();
            var def = GetPreset(editor, "preset_a");
            var calls = new List<ParticlePresetDefinition>();
            InstallFakeWriter(editor, calls);
            InvokeMethod(editor, "MarkParticlePresetDirty", def);

            InvokeMethod(editor, "OnApplicationQuit");

            CollectionAssert.Contains(calls, def, "OnApplicationQuit must flush pending preset edits.");
        }

        /// <summary>OnDestroy must flush before the component (and its debounce state) is
        /// gone for good.
        ///
        /// The GameObject stays in _sceneObjects so TearDown really destroys it: an
        /// EditMode fixture that leaves a live singleton MonoBehaviour in the open scene
        /// pollutes every test that runs after it. The resulting second, real OnDestroy is
        /// harmless and deliberate — FlushDirtyPresets returns early on an empty set,
        /// Unregister is behind a GameEditorManager.HasInstance guard, and
        /// SingletonMonoBehaviour.OnDestroy only nulls _instance when it still points here.
        /// </summary>
        [Test]
        public void OnDestroy_FlushesDirtyPresets()
        {
            var editor = CreateEditor();
            var go = editor.gameObject;
            var def = GetPreset(editor, "preset_a");
            var calls = new List<ParticlePresetDefinition>();
            InstallFakeWriter(editor, calls);
            InvokeMethod(editor, "MarkParticlePresetDirty", def);

            InvokeMethod(editor, "OnDestroy");

            CollectionAssert.Contains(calls, def, "OnDestroy must flush pending preset edits.");
            Assert.IsTrue(_sceneObjects.Contains(go),
                "The editor GameObject must stay tracked so TearDown destroys it.");
        }

        // ── Writer contract ───────────────────────────────────────────────────────

        /// <summary>The single most important guarantee in the whole feature: an EditMode
        /// fixture (Application.isPlaying == false) must never let the default writer reach
        /// a real .asset. If this regresses, every other EditMode test that touches a preset
        /// risks dirtying real project assets.</summary>
        [Test]
        public void DefaultWriter_RefusesToWriteOutsidePlayMode_AndKeepsThePresetDirty()
        {
            var editor = CreateEditor();
            var probe = MakeProbe("__autosave_probe");

            InvokeMethod(editor, "MarkParticlePresetDirty", probe);
            var result = InvokeMethod(editor, "FlushDirtyPresets", "probe");

            Assert.AreEqual(0, (int) result,
                "The default writer must refuse to write outside Play Mode.");
            Assert.IsTrue(GetDirtySet(editor).Contains(probe),
                "A refused write must leave the preset dirty for the next flush attempt.");
        }

        /// <summary>A writer that fails (returns false) must not silently drop the edit — the
        /// def stays queued so the very next flush retries it.</summary>
        [Test]
        public void WriterThatFails_LeavesThePresetDirtyForTheNextFlush()
        {
            var editor = CreateEditor();
            var def = GetPreset(editor, "preset_a");
            var calls = new List<ParticlePresetDefinition>();
            InstallFakeWriter(editor, calls, result: false);
            InvokeMethod(editor, "MarkParticlePresetDirty", def);

            var result = InvokeMethod(editor, "FlushDirtyPresets", "probe");

            Assert.AreEqual(0, (int) result, "A failed write must not count as flushed.");
            Assert.IsTrue(GetDirtySet(editor).Contains(def),
                "A failed write must keep the preset dirty for a retry.");
        }

        /// <summary>A preset destroyed between being marked dirty and the flush (e.g. the
        /// catalog was reloaded) must be dropped quietly rather than handed to the writer as
        /// a dangling reference.</summary>
        [Test]
        public void DestroyedPreset_IsDroppedWithoutBeingWritten()
        {
            var editor = CreateEditor();
            var probe = MakeProbe("__destroyed_probe");
            var calls = new List<ParticlePresetDefinition>();
            InstallFakeWriter(editor, calls);
            InvokeMethod(editor, "MarkParticlePresetDirty", probe);

            UnityEngine.Object.DestroyImmediate(probe);
            InvokeMethod(editor, "FlushDirtyPresets", "probe");

            Assert.AreEqual(0, calls.Count, "A destroyed preset must never reach the writer.");
            Assert.AreEqual(0, GetDirtySet(editor).Count, "A destroyed preset must be dropped from the dirty set.");
        }

        /// <summary>Proves the debounce actually debounces: a second edit before the first
        /// deadline fires must push the deadline out again, so a slider drag never writes
        /// mid-drag no matter how long the drag lasts.</summary>
        [Test]
        public void SecondEdit_ExtendsTheDebounce()
        {
            var editor = CreateEditor();
            var def = GetPreset(editor, "preset_a");

            InvokeMethod(editor, "MarkParticlePresetDirty", def);
            // Simulate the deadline having nearly arrived.
            SetFieldValue(editor, "_presetFlushDueAt", Time.unscaledTime + 0.05f);

            InvokeMethod(editor, "MarkParticlePresetDirty", def);

            float dueAt = (float) GetFieldValue(editor, "_presetFlushDueAt");
            Assert.GreaterOrEqual(dueAt, Time.unscaledTime + 0.5f,
                "A second edit must re-arm the debounce, not just keep the earlier deadline.");
        }

        // ── Loops toggle call site ───────────────────────────────────────────────

        /// <summary>The Loops toggle in the Properties panel is the one call site the design
        /// explicitly re-routes through MarkParticlePresetDirty instead of a raw
        /// EditorUtility.SetDirty — this is what makes flipping Loops autosave like every
        /// other Properties-panel edit.</summary>
        [Test]
        public void LoopsToggle_RoutesThroughDirtyTracking()
        {
            var editor = CreateEditor(withUI: true);
            var def = GetPreset(editor, "preset_a");
            SetFieldValue(editor, "_selectedPresetId", "preset_a");

            InvokeMethod(editor, "OnLoopsToggled", true);

            Assert.IsTrue(GetDirtySet(editor).Contains(def),
                "Toggling Loops must mark the selected preset dirty via MarkParticlePresetDirty.");
        }
    }
}
