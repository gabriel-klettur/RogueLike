using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.State
{
    /// <summary>
    /// Regression coverage for the Tile Editor perf-wave-2 fix to
    /// <see cref="TileEditorPerfProbe"/>'s lifecycle: the probe used to be
    /// visible-toggled but never GameObject-deactivated (so its
    /// <c>Update()</c>/<c>OnGUI()</c> and 20 <see cref="Recorder"/>s ran
    /// forever, for every player, whether or not the overlay was ever
    /// opened). Fixed by:
    ///   • <c>CreatePerfProbe()</c> deactivating the probe's GameObject
    ///     immediately after creation.
    ///   • The Shift+F8 handler in <c>TileEditorManager.Update()</c> now
    ///     mirroring <c>Visible</c> onto <c>gameObject.SetActive(...)</c>.
    ///   • <see cref="TileEditorPerfProbe"/>'s 20 <see cref="Recorder"/>s no
    ///     longer force-enabled in <c>Awake()</c> — <c>OnEnable</c>/
    ///     <c>OnDisable</c> now own that state, tracking the GameObject's
    ///     actual active state.
    ///
    /// Measured impact is explicitly NOT the point here (1.5 microseconds/
    /// frame — negligible either way): this is a correctness fix (a probe
    /// that is supposed to be opt-in must actually cost nothing when never
    /// opened), and the tests below assert exactly that contract.
    /// </summary>
    [TestFixture]
    public class TileEditorPerfProbeLifecycleTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helpers ───────────────────────────────────────────

        private static object InvokePrivate(object target, string methodName)
        {
            var mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, $"Reflection: '{methodName}' not found on {target.GetType().Name}.");
            return mi.Invoke(target, null);
        }

        private static object GetPrivateInstance(object target, string name)
        {
            var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Reflection: instance field '{name}' not found on {target.GetType().Name}.");
            return f.GetValue(target);
        }

        private static IEnumerable<Recorder> GetRecorders(TileEditorPerfProbe probe)
        {
            var array = (System.Array)GetPrivateInstance(probe, "_recorders");
            Assert.IsNotNull(array, "_recorders must be initialized by Awake().");
            var rowType = array.GetType().GetElementType();
            var recorderField = rowType.GetField("Recorder", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(recorderField, "Reflection: RecorderRow.Recorder field not found.");

            var list = new List<Recorder>();
            foreach (var item in array)
            {
                var rec = (Recorder)recorderField.GetValue(item);
                if (rec != null) list.Add(rec);
            }
            Assert.Greater(list.Count, 0, "Precondition: at least one Recorder must have resolved.");
            return list;
        }

        private static string ReadProductionFile(string relativePath)
        {
            string path = Path.Combine(Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), $"Production file not found: {path}");
            return File.ReadAllText(path);
        }

        private TileEditorPerfProbe NewProbe()
        {
            LogAssert.ignoreFailingMessages = true; // Awake logs several Debug.Log lines
            var go = new GameObject("TileEditorPerfProbe_Test");
            _sceneObjects.Add(go);
            return go.AddComponent<TileEditorPerfProbe>();
        }

        private TileEditorManager NewManager()
        {
            var go = new GameObject("TileEditorManager_Test");
            _sceneObjects.Add(go);
            return go.AddComponent<TileEditorManager>();
        }

        // ════════════════════════════════════════════════════════════════
        // 1. TileEditorManager.CreatePerfProbe — created hidden
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void CreatePerfProbe_CreatesProbe_WithGameObjectInactive_AndVisibleFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            var manager = NewManager();

            InvokePrivate(manager, "CreatePerfProbe");

            var probeField = typeof(TileEditorManager).GetField("_perfProbe",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(probeField, "Reflection: _perfProbe field not found on TileEditorManager.");
            var probe = (TileEditorPerfProbe)probeField.GetValue(manager);

            Assert.IsNotNull(probe, "CreatePerfProbe() must assign _perfProbe.");
            Assert.IsFalse(probe.Visible, "A newly created probe must have Visible=false.");
            Assert.IsFalse(probe.gameObject.activeSelf,
                "A newly created probe's GameObject must be INACTIVE so its Update()/OnGUI() " +
                "never run, and its Profiler.Recorders never sample, until the operator " +
                "explicitly presses Shift+F8 — Visible=false alone does not stop either.");
        }

        [Test]
        public void CreatePerfProbe_Source_DeactivatesTheGameObjectAfterCreation()
        {
            string source = ReadProductionFile(
                "_Project/Scripts/Gameplay/Editors/Tile/TileEditorManager.cs");

            int start = source.IndexOf("private void CreatePerfProbe()");
            Assert.Greater(start, -1, "CreatePerfProbe() must exist.");
            int end = source.IndexOf("private void InitializePersistence()", start);
            Assert.Greater(end, start, "Could not bound CreatePerfProbe()'s body.");
            string body = source.Substring(start, end - start);

            Assert.IsTrue(body.Contains("probeGo.SetActive(false);"),
                "CreatePerfProbe() must deactivate the probe GameObject immediately after " +
                "creation — this is what actually stops Update()/OnGUI() and the 20 " +
                "Profiler.Recorders from running for the default (never opened) case.");
        }

        // ════════════════════════════════════════════════════════════════
        // 2. Shift+F8 toggle — GameObject active state tracks Visible
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void Update_Source_ShiftF8Handler_SetsGameObjectActiveState_AlongsideVisibleFlag()
        {
            string source = ReadProductionFile(
                "_Project/Scripts/Gameplay/Editors/Tile/TileEditorManager.cs");

            int idxVisibleToggle = source.IndexOf("_perfProbe.Visible = !_perfProbe.Visible;");
            Assert.Greater(idxVisibleToggle, -1, "Shift+F8 handler must toggle _perfProbe.Visible.");

            int idxSetActive = source.IndexOf(
                "_perfProbe.gameObject.SetActive(_perfProbe.Visible);", idxVisibleToggle);
            Assert.Greater(idxSetActive, -1,
                "Immediately after flipping Visible, the Shift+F8 handler must also " +
                "SetActive(...) the probe's GameObject to that same value — Visible alone (the " +
                "pre-fix code) left the component ticking forever after the first Shift+F8 " +
                "toggle turned it 'off', because the GameObject itself stayed active.");
        }

        // ════════════════════════════════════════════════════════════════
        // 3. TileEditorPerfProbe recorder lifecycle
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void Awake_Source_NoLongerForceEnablesRecorders()
        {
            string source = ReadProductionFile(
                "_Project/Scripts/Gameplay/Editors/Tile/TileEditorPerfProbe.cs");

            int idxAwake = source.IndexOf("private void Awake()");
            int idxOnEnable = source.IndexOf("private void OnEnable()");
            Assert.Greater(idxAwake, -1, "Awake() must exist.");
            Assert.Greater(idxOnEnable, idxAwake, "OnEnable() must exist, after Awake().");
            string awakeBody = source.Substring(idxAwake, idxOnEnable - idxAwake);

            Assert.IsFalse(awakeBody.Contains(".enabled = true"),
                "Awake() must no longer force-enable the Profiler.Recorders at creation time — " +
                "OnEnable()/OnDisable() now own recorder-enabled state so it always tracks " +
                "whether the probe's GameObject is actually active.");
        }

        [Test]
        public void OnEnable_EnablesEveryRecorder()
        {
            var probe = NewProbe();

            InvokePrivate(probe, "OnEnable");

            foreach (var recorder in GetRecorders(probe))
                Assert.IsTrue(recorder.enabled, "OnEnable() must enable every Profiler.Recorder.");
        }

        [Test]
        public void OnDisable_DisablesEveryRecorder()
        {
            var probe = NewProbe();
            InvokePrivate(probe, "OnEnable"); // start from a known enabled state

            InvokePrivate(probe, "OnDisable");

            foreach (var recorder in GetRecorders(probe))
                Assert.IsFalse(recorder.enabled,
                    "OnDisable() must disable every Profiler.Recorder again — otherwise the " +
                    "runtime Profiler keeps tracking these markers even after the probe's " +
                    "GameObject (and its overlay) is hidden, defeating the whole point of the fix.");
        }

        [Test]
        public void SetRecordersEnabled_IsNullSafe_WhenRecordersArrayIsNull()
        {
            var probe = NewProbe();
            var field = typeof(TileEditorPerfProbe).GetField("_recorders",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "Reflection: _recorders field not found.");
            field.SetValue(probe, null);

            Assert.DoesNotThrow(() => InvokePrivate(probe, "OnEnable"),
                "OnEnable must not throw even if _recorders somehow never got initialized.");
            Assert.DoesNotThrow(() => InvokePrivate(probe, "OnDisable"));
        }
    }
}
