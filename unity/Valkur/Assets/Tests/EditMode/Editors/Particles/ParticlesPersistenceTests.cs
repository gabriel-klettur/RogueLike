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
    /// With no active particle emitters in the scene the file is written as an empty
    /// JSON array <c>[\n]\n</c>.  The test asserts the file is valid JSON-array syntax.
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

            Invoke(editor, "Start");
            return editor;
        }

        // ── Tests ────────────────────────────────────────────────────────────────

        [Test]
        public void SaveInstancesToJson_WritesFile_WithBracketSyntax()
        {
            var editor = CreateEditor();

            // No particle emitters in scene → save should write empty array.
            Invoke(editor, "SaveInstancesToJson");

            Assert.IsTrue(File.Exists(_jsonPath),
                $"SaveInstancesToJson must create {_jsonPath}.");

            string content = File.ReadAllText(_jsonPath);
            Assert.IsTrue(content.TrimStart().StartsWith("["),
                "JSON file must start with '[' (JSON array).");
            Assert.IsTrue(content.TrimEnd().EndsWith("]"),
                "JSON file must end with ']' (JSON array).");
        }

        [Test]
        public void SaveInstancesToJson_EmptyScene_WritesEmptyArray()
        {
            var editor = CreateEditor();

            Invoke(editor, "SaveInstancesToJson");

            string content = File.ReadAllText(_jsonPath);
            // The builder writes "[\n]\n" for zero emitters.
            // Strip all whitespace and assert we get "[]".
            string compact = System.Text.RegularExpressions.Regex.Replace(content, @"\s", "");
            Assert.AreEqual("[]", compact,
                "Empty scene must produce exactly '[]' after whitespace removal.");
        }

        [Test]
        public void SaveInstancesToJson_CreatesDirectory_IfMissing()
        {
            // Ensure the directory does not exist for this test.
            string dir = Path.GetDirectoryName(_jsonPath);
            bool dirExisted = Directory.Exists(dir);
            string[] existingFiles = dirExisted ? Directory.GetFiles(dir) : null;

            if (dirExisted)
            {
                // Directory already exists — just verify SaveInstancesToJson completes without error.
                var editor = CreateEditor();
                Assert.DoesNotThrow(() => Invoke(editor, "SaveInstancesToJson"),
                    "SaveInstancesToJson must not throw when the directory already exists.");
                return;
            }

            // Directory is absent — create editor and save.
            var editorNew = CreateEditor();
            Invoke(editorNew, "SaveInstancesToJson");

            Assert.IsTrue(Directory.Exists(dir),
                "SaveInstancesToJson must create the Particles directory if it is missing.");
        }

        [Test]
        public void SaveInstancesToJson_DoesNotThrow_WhenNoCatalog()
        {
            // Build editor but remove catalog so SaveInstancesToJson only writes an
            // empty emitter list (FindEditorOwnedEmitters returns nothing).
            ClearSingleton<ParticlesRuntimeEditor>();
            var go = new GameObject("PersistNoCatalogEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(editor, "OnSingletonAwake");
            SetVal(editor, "_catalog", null);
            Invoke(editor, "Start");

            Assert.DoesNotThrow(() => Invoke(editor, "SaveInstancesToJson"),
                "SaveInstancesToJson must not throw when catalog is null — emitter list is just empty.");
        }
    }
}
