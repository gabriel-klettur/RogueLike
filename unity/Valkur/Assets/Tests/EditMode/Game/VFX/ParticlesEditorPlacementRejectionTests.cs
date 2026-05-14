using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Verifies that <see cref="ParticlesRuntimeEditor"/> rejects placement of finite
    /// (loops=false) presets and permits placement of looping (loops=true) presets.
    /// </summary>
    [TestFixture]
    public class ParticlesEditorPlacementRejectionTests
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

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingleton<ParticlesRuntimeEditor>();

            var go = new GameObject("PlacementTestEditor");
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

        private ParticlePresetDefinition RegisterPreset(string id, string kind, bool loops)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id  = id;
            def.vfx = new ParticleVfxParams
            {
                kind     = kind,
                loops    = loops,
                count    = 5,
                lifespan = 0.3f,
                speed    = 1f,
                sizeMin  = 0.1f,
                sizeMax  = 0.2f,
            };
            _catalog.SetPresets(new List<ParticlePresetDefinition> { def });
            return def;
        }

        private void SetSelectedPreset(string id)
        {
            SetVal(_editor, "_selectedPresetId", id);
            // Also set mode to Place so HandleMapInteraction routes to SpawnFromMapClick.
            // We invoke SpawnFromMapClick directly, so only _selectedPresetId matters.
        }

        private static int CountEmittersInScene()
        {
            return Object.FindObjectsOfType<ParticleEmitter>().Length;
        }

        // ── tests ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Regression: a preset with loops=false must not spawn a GO when placed via
        /// SpawnFromMapClick. This was the original bug before the loops attribute existed.
        /// </summary>
        [Test]
        public void SpawnFromMapClick_FiniteKindPreset_DoesNotSpawn_RegressionFix()
        {
            RegisterPreset("firework_big", "firework", loops: false);
            SetSelectedPreset("firework_big");

            int before = CountEmittersInScene();

            // Invoke SpawnFromMapClick(presetId, worldPos) directly.
            Invoke(_editor, "SpawnFromMapClick",
                "firework_big", new Vector3(1f, 2f, 0f));

            int after = CountEmittersInScene();
            Assert.AreEqual(before, after,
                "SpawnFromMapClick must NOT spawn a ParticleEmitter for a preset " +
                "with loops=false (one-shot finite kind).");
        }

        /// <summary>
        /// A looping preset (loops=true) must spawn normally.
        /// </summary>
        [Test]
        public void SpawnFromMapClick_LoopingPreset_SpawnsNormally()
        {
            RegisterPreset("aura_loop", "aura", loops: true);
            SetSelectedPreset("aura_loop");

            int before = CountEmittersInScene();

            Invoke(_editor, "SpawnFromMapClick",
                "aura_loop", new Vector3(3f, 4f, 0f));

            // The spawned GO is not added to _created because we find it via scene search.
            var spawned = Object.FindObjectsOfType<ParticleEmitter>();
            int after = spawned.Length;

            // Cleanup the spawned emitter before the assertion to avoid leaks.
            foreach (var em in spawned)
                if (em != null) _created.Add(em.gameObject);

            Assert.Greater(after, before,
                "SpawnFromMapClick must spawn a ParticleEmitter for a looping preset (loops=true).");
        }
    }
}
