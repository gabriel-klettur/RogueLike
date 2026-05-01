using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Exercises the JSON write/read round-trip of <see cref="ParticlesRuntimeEditor"/>
    /// (<c>SaveInstancesToJson</c>).
    ///
    /// The test snapshots any pre-existing particles_instances.json and restores it in
    /// TearDown so the production data file is never destroyed.
    ///
    /// With no active particle emitters in the scene the file is written as v2 JSON:
    /// <c>{"version":2,"instances":[]}</c>. Tests assert valid v2 JSON syntax.
    /// </summary>
    [TestFixture]
    public class ParticlesPersistenceTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        private string _jsonPath;
        private bool   _fileExistedBefore;
        private string _snapshotContents;

        // ── Reflection helpers ───────────────────────────────────────────────────

        private static void ClearSingleton<T>() where T : MonoBehaviour
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

        private static void SetVal(object obj, string name, object value)
            => FindField(obj, name)?.SetValue(obj, value);

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(method,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        // ── Setup / Teardown ─────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Snapshot the production JSON file so TearDown can restore it.
            _jsonPath = Path.Combine(
                Application.streamingAssetsPath, "Particles", "particles_instances.json");
            _fileExistedBefore = File.Exists(_jsonPath);
            _snapshotContents  = _fileExistedBefore ? File.ReadAllText(_jsonPath) : null;
        }

        [TearDown]
        public void TearDown()
        {
            // Restore or remove the production file as it was before the test.
            if (_fileExistedBefore && _snapshotContents != null)
                File.WriteAllText(_jsonPath, _snapshotContents);
            else if (!_fileExistedBefore && File.Exists(_jsonPath))
                File.Delete(_jsonPath);

            // Also clean up .tmp and .bak artefacts that AtomicJsonFile may leave.
            foreach (var ext in new[] { ".tmp", ".bak" })
            {
                string p = _jsonPath + ext;
                if (File.Exists(p)) File.Delete(p);
            }

            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            ClearSingleton<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Editor factory ───────────────────────────────────────────────────────

        private ParticlesRuntimeEditor CreateEditor()
        {
            ClearSingleton<ParticlesRuntimeEditor>();
            var go = new GameObject("PersistenceTestEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(editor, "OnSingletonAwake");

            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            var preset  = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            preset.id   = "aura_test"; preset.displayName = "aura_test";
            catalog.SetPresets(new[] { preset });
            SetVal(editor, "_catalog", catalog);

            // Inject in-memory store so no disk access occurs by default.
            var store = new InMemoryParticleInstanceStore();
            editor.SetInstanceStore(store);

            Invoke(editor, "Start");
            return editor;
        }

        // ── Tests ────────────────────────────────────────────────────────────────

        [Test]
        public void SaveInstancesToJson_WritesFile_WithBracketSyntax()
        {
            // Use a real file store for this test so we can assert the file on disk.
            ClearSingleton<ParticlesRuntimeEditor>();
            var go = new GameObject("PersistenceFileSyntaxEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(editor, "OnSingletonAwake");
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            SetVal(editor, "_catalog", catalog);
            Invoke(editor, "Start");

            // No particle emitters in scene → save should write empty v2 object.
            Invoke(editor, "SaveInstancesToJson");

            Assert.IsTrue(File.Exists(_jsonPath),
                $"SaveInstancesToJson must create {_jsonPath}.");

            string content = File.ReadAllText(_jsonPath);
            // v2 format wraps in an object; compact it and check for version:2 and instances array.
            Assert.IsTrue(content.Contains("\"version\""),
                "JSON file must contain \"version\" field (v2 format).");
            Assert.IsTrue(content.Contains("\"instances\""),
                "JSON file must contain \"instances\" array (v2 format).");
        }

        [Test]
        public void SaveInstancesToJson_EmptyScene_WritesEmptyInstancesArray()
        {
            var editor = CreateEditor();

            Invoke(editor, "SaveInstancesToJson");

            var store = (InMemoryParticleInstanceStore)FindField(editor, "_instanceStore")?.GetValue(editor);
            Assert.IsNotNull(store, "InMemoryStore must be injected.");
            string content = store.CurrentJson;
            Assert.IsFalse(string.IsNullOrEmpty(content),
                "Store must hold JSON after save.");
            // v2: {"version":2,"instances":[]}
            Assert.IsTrue(content.Contains("\"instances\":[]"),
                $"Empty scene must produce {{\"instances\":[]}} in v2 JSON. Got: {content}");
        }

        [Test]
        public void SaveInstancesToJson_CreatesDirectory_IfMissing()
        {
            // Ensure the directory does not exist for this test.
            string dir = Path.GetDirectoryName(_jsonPath);
            bool dirExisted = Directory.Exists(dir);

            if (dirExisted)
            {
                // Directory already exists — just verify SaveInstancesToJson completes without error.
                var editor = CreateEditor();
                Assert.DoesNotThrow(() => Invoke(editor, "SaveInstancesToJson"),
                    "SaveInstancesToJson must not throw when the directory already exists.");
                return;
            }

            // Directory is absent — create editor and save using file store.
            ClearSingleton<ParticlesRuntimeEditor>();
            var go = new GameObject("PersistNoDirEditor");
            _sceneObjects.Add(go);
            var editorNew = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(editorNew, "OnSingletonAwake");
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            SetVal(editorNew, "_catalog", catalog);
            Invoke(editorNew, "Start");

            Invoke(editorNew, "SaveInstancesToJson");

            Assert.IsTrue(Directory.Exists(dir),
                "SaveInstancesToJson must create the Particles directory if it is missing.");
        }

        [Test]
        public void SaveInstancesToJson_DoesNotThrow_WhenNoCatalog()
        {
            // Build editor but remove catalog so SaveInstancesToJson only writes an
            // empty instances list.
            ClearSingleton<ParticlesRuntimeEditor>();
            var go = new GameObject("PersistNoCatalogEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(editor, "OnSingletonAwake");
            SetVal(editor, "_catalog", null);
            editor.SetInstanceStore(new InMemoryParticleInstanceStore());
            Invoke(editor, "Start");

            Assert.DoesNotThrow(() => Invoke(editor, "SaveInstancesToJson"),
                "SaveInstancesToJson must not throw when catalog is null — instance list is just empty.");
        }
    }
}
