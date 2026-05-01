using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Gameplay.VFX
{
    /// <summary>
    /// REGRESSION TESTS — Bug #1 (data loss on save while emitters are viewport-culled).
    ///
    /// Before the fix, <c>SaveInstancesToJson</c> filtered by <c>activeInHierarchy</c>,
    /// so any emitter that the viewport culling had deactivated would be silently dropped
    /// from the JSON on the next save.
    ///
    /// After the fix, save iterates <c>FindObjectsOfType&lt;PersistedParticleInstance&gt;(includeInactive:true)</c>
    /// so culled (inactive) emitters are always preserved.
    /// </summary>
    [TestFixture]
    public class ParticlePersistenceCullingTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private ParticlesRuntimeEditor _editor;

        // ── Reflection helpers ────────────────────────────────────────────────────

        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

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

        private static void SetVal(object obj, string name, object value)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public |
                    BindingFlags.Instance | BindingFlags.Static);
                if (f != null) { f.SetValue(obj, value); return; }
                t = t.BaseType;
            }
        }

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingleton<ParticlesRuntimeEditor>();

            var go = new GameObject("CullingTestEditor");
            _created.Add(go);
            _editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(_editor, "OnSingletonAwake");

            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            SetVal(_editor, "_catalog", catalog);
            _editor.SetInstanceStore(new InMemoryParticleInstanceStore());
            Invoke(_editor, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();

            ClearSingleton<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helper ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a persisted emitter GO directly (bypasses SpawnEmitterAt since we
        /// want control over active state for culling simulation).
        /// </summary>
        private GameObject CreatePersistedEmitter(string presetId, Vector3 pos, bool active)
        {
            var go = new GameObject($"PE_{presetId}");
            go.transform.position = pos;
            var inst = go.AddComponent<PersistedParticleInstance>();
            inst.Initialize(presetId, 1f);
            go.SetActive(active);
            _created.Add(go);
            return go;
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// REGRESSION — Bug #1:
        /// Spawn 3 emitters, deactivate 2 (simulating viewport culling),
        /// save, verify JSON contains all 3 preset_ids.
        /// </summary>
        [Test]
        public void Save_IncludesCulledInactiveEmitters_RegressionBug1()
        {
            // 1 active, 2 culled (inactive).
            CreatePersistedEmitter("fire_aura",    new Vector3(1f, 1f), active: true);
            CreatePersistedEmitter("smoke_ring",   new Vector3(2f, 2f), active: false);
            CreatePersistedEmitter("water_splash", new Vector3(3f, 3f), active: false);

            Invoke(_editor, "SaveInstancesToJson");

            // Retrieve saved JSON from in-memory store.
            var store = (InMemoryParticleInstanceStore)
                _editor.GetType()
                    .GetField("_instanceStore",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(_editor);

            Assert.IsNotNull(store, "Store must be set.");
            string json = store.CurrentJson;
            Assert.IsFalse(string.IsNullOrEmpty(json), "JSON must not be empty after save.");

            // All 3 presets must appear in the JSON.
            Assert.IsTrue(json.Contains("fire_aura"),
                "Culling fix: active emitter 'fire_aura' must be saved.");
            Assert.IsTrue(json.Contains("smoke_ring"),
                "Culling fix: inactive (culled) emitter 'smoke_ring' must be saved — was bug #1.");
            Assert.IsTrue(json.Contains("water_splash"),
                "Culling fix: inactive (culled) emitter 'water_splash' must be saved — was bug #1.");
        }

        [Test]
        public void Save_ExcludesPreviewEmitters()
        {
            // Preview emitters (PPrev_*) must never be persisted.
            CreatePersistedEmitter("fire_aura", new Vector3(1f, 1f), active: true);

            // Create a fake preview emitter WITH PersistedParticleInstance (should not happen
            // in production, but we want to verify the filter is name-based).
            var previewGo = new GameObject("PPrev_Emitter_fire");
            previewGo.AddComponent<PersistedParticleInstance>().Initialize("fire_aura", 1f);
            _created.Add(previewGo);

            Invoke(_editor, "SaveInstancesToJson");

            var store = (InMemoryParticleInstanceStore)
                _editor.GetType()
                    .GetField("_instanceStore",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(_editor);

            string json = store.CurrentJson;
            var records = ParticleInstanceSerializer.Deserialize(json, null);

            // Must have exactly 1 record (the real one), not 2 (the preview would be the 2nd).
            // Note: if the save correctly filters previews, count is 1.
            Assert.AreEqual(1, records.Count,
                "Preview emitters (PPrev_*) must not appear in saved JSON.");
        }
    }
}
