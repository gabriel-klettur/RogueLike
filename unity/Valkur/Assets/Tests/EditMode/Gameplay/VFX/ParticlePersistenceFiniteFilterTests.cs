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
    /// Verifies that finite (loops=false) preset instances are:
    ///   1. Omitted from JSON during save (progressive legacy cleanup).
    ///   2. Skipped silently by ParticleInstancesLoader during load.
    /// </summary>
    [TestFixture]
    public class ParticlePersistenceFiniteFilterTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private ParticlesRuntimeEditor _editor;
        private ParticlePresetCatalog  _catalog;

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
                    BindingFlags.Instance  | BindingFlags.Static);
                if (f != null) { f.SetValue(obj, value); return; }
                t = t.BaseType;
            }
        }

        private static T GetVal<T>(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public |
                    BindingFlags.Instance  | BindingFlags.Static);
                if (f != null) return (T)f.GetValue(obj);
                t = t.BaseType;
            }
            return default;
        }

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingleton<ParticlesRuntimeEditor>();

            var go = new GameObject("FiniteFilterEditor");
            _created.Add(go);
            _editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(_editor, "OnSingletonAwake");

            _catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            SetVal(_editor, "_catalog", _catalog);
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

        // ── helpers ───────────────────────────────────────────────────────────────

        private void RegisterPreset(string id, bool loops)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id  = id;
            def.vfx = new ParticleVfxParams
            {
                kind     = loops ? "aura" : "firework",
                loops    = loops,
                count    = 5,
                lifespan = 0.3f,
            };
            _catalog.SetPresets(new List<ParticlePresetDefinition> { def });
        }

        private InMemoryParticleInstanceStore GetStore() =>
            (InMemoryParticleInstanceStore)
                GetVal<IParticleInstanceStore>(_editor, "_instanceStore");

        // ── tests ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A PersistedParticleInstance whose preset is finite (loops=false) must be
        /// omitted from the JSON produced by SaveInstancesToJson.
        /// </summary>
        [Test]
        public void Save_OmitsFinitePresetInstances()
        {
            RegisterPreset("firework_finite", loops: false);

            // Manually create a GO that mimics a legacy persisted finite instance.
            var go = new GameObject("PE_firework_finite");
            go.transform.position = new Vector3(5f, 5f, 0f);
            _created.Add(go);
            var inst = go.AddComponent<PersistedParticleInstance>();
            inst.Initialize("firework_finite", 1f);

            // Trigger save.
            Invoke(_editor, "SaveInstancesToJson");

            string json = GetStore().CurrentJson;
            Assert.IsNotNull(json, "Save must produce non-null JSON.");
            Assert.IsFalse(json.Contains("firework_finite"),
                "SaveInstancesToJson must omit finite preset instances (loops=false) " +
                "from the output JSON (progressive legacy cleanup).");
        }

        /// <summary>
        /// ParticleInstancesLoader must silently skip JSON entries whose preset_id
        /// resolves to a finite preset (loops=false).
        /// </summary>
        [Test]
        public void Loader_SkipsFinitePresetEntries()
        {
            // Register the finite preset in a catalog.
            var finitePreset = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            finitePreset.id  = "firework_tiny";
            finitePreset.vfx = new ParticleVfxParams
            {
                kind = "firework", loops = false, count = 3, lifespan = 0.2f,
            };
            var catalogForLoader = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            catalogForLoader.SetPresets(new List<ParticlePresetDefinition> { finitePreset });

            var loaderGo = new GameObject("TestLoader_FiniteFilter");
            _created.Add(loaderGo);
            var loader = loaderGo.AddComponent<ParticleInstancesLoader>();
            loader.Initialize(catalogForLoader);

            string json = @"{
  ""version"": 2,
  ""instances"": [
    {
      ""preset_id"": ""firework_tiny"",
      ""guid"": ""ffffffff-dead-beef-0000-000000000001"",
      ""zone"": """",
      ""rel_x"": 0,
      ""rel_y"": 0,
      ""scale_multiplier"": 1.0
    }
  ]
}";
            var store = new InMemoryParticleInstanceStore(json);
            loader.SetInstanceStore(store);

            // Manually invoke Start (EditMode doesn't call MonoBehaviour lifecycle).
            Invoke(loader, "Start");

            // No ParticleEmitter should have been spawned.
            var emitters = loaderGo.GetComponentsInChildren<ParticleEmitter>();
            Assert.AreEqual(0, emitters.Length,
                "ParticleInstancesLoader must skip finite preset entries (loops=false) " +
                "without spawning any emitter.");
        }
    }
}
