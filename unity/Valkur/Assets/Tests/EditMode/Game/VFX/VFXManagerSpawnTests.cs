using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// EditMode tests for <see cref="VFXManager.SpawnParticlePreset"/>.
    ///
    /// VFXManager is a <see cref="Valkur.Core.SingletonMonoBehaviour{T}"/>.
    /// We create a fresh instance per test by calling Awake via reflection (Awake
    /// does not run automatically in EditMode). The instance is destroyed in TearDown
    /// so the static <c>Instance</c> field is cleared for the next test.
    ///
    /// ParticleEmitter.ApplyPreset creates a real ParticleSystem child —
    /// this can log warnings in EditMode. <c>LogAssert.ignoreFailingMessages = true</c>
    /// silences those so our null/non-null assertions aren't masked.
    /// </summary>
    public class VFXManagerSpawnTests
    {
        private GameObject _managerGo;
        private VFXManager _manager;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _managerGo = new GameObject("VFXManager");
            _manager   = _managerGo.AddComponent<VFXManager>();

            // Awake does not run in EditMode — call it manually so Instance and
            // _poolParent are initialised.
            typeof(VFXManager)
                .GetMethod("Awake",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_manager, null);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            if (_managerGo != null)
                Object.DestroyImmediate(_managerGo);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static ParticlePresetDefinition MakePreset(string id, float lifespan = 2f)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id = id;
            def.displayName = id;
            def.vfx = new ParticleVfxParams { kind = "spark", lifespan = lifespan };
            return def;
        }

        private static ParticlePresetCatalog MakeCatalogWith(params ParticlePresetDefinition[] presets)
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            catalog.SetPresets(presets);
            return catalog;
        }

        // ── Null catalog guard ────────────────────────────────────────────

        [Test]
        public void SpawnParticlePreset_NullCatalog_ReturnsNull()
        {
            // SetParticleCatalog never called — _particleCatalog is null by default.
            var result = _manager.SpawnParticlePreset("dash_trail", Vector3.zero);

            Assert.IsNull(result, "SpawnParticlePreset must return null when no catalog is set");
        }

        // ── Unknown preset id ─────────────────────────────────────────────

        [Test]
        public void SpawnParticlePreset_UnknownPresetId_ReturnsNull()
        {
            var catalog = MakeCatalogWith(MakePreset("known_preset"));
            _manager.SetParticleCatalog(catalog);

            var result = _manager.SpawnParticlePreset("nonexistent_id", Vector3.zero);

            Assert.IsNull(result, "SpawnParticlePreset must return null for an id not in the catalog");
        }

        [Test]
        public void SpawnParticlePreset_EmptyPresetId_ReturnsNull()
        {
            var catalog = MakeCatalogWith(MakePreset("some_preset"));
            _manager.SetParticleCatalog(catalog);

            var result = _manager.SpawnParticlePreset("", Vector3.zero);

            Assert.IsNull(result, "SpawnParticlePreset must return null for an empty preset id");
        }

        // ── Successful spawn ──────────────────────────────────────────────

        [Test]
        public void SpawnParticlePreset_ValidPreset_ReturnsNonNullGO()
        {
            var preset  = MakePreset("dash_trail", lifespan: 2f);
            var catalog = MakeCatalogWith(preset);
            _manager.SetParticleCatalog(catalog);

            var result = _manager.SpawnParticlePreset("dash_trail", new Vector3(3f, 4f, 0f));

            Assert.IsTrue(result != null, "SpawnParticlePreset must return a non-null GameObject on success");
        }

        [Test]
        public void SpawnParticlePreset_ValidPreset_SpawnedAtCorrectWorldPosition()
        {
            var preset  = MakePreset("heal_aura", lifespan: 3f);
            var catalog = MakeCatalogWith(preset);
            _manager.SetParticleCatalog(catalog);

            var spawnPos = new Vector3(7f, -2f, 0f);
            var result   = _manager.SpawnParticlePreset("heal_aura", spawnPos);

            Assert.IsTrue(result != null, "GO must be non-null");
            Assert.AreEqual(spawnPos.x, result.transform.position.x, 0.001f, "Spawned GO x must match requested position");
            Assert.AreEqual(spawnPos.y, result.transform.position.y, 0.001f, "Spawned GO y must match requested position");
        }

        [Test]
        public void SpawnParticlePreset_ValidPreset_GOHasParticleEmitterComponent()
        {
            var preset  = MakePreset("explosion", lifespan: 1f);
            var catalog = MakeCatalogWith(preset);
            _manager.SetParticleCatalog(catalog);

            var result = _manager.SpawnParticlePreset("explosion", Vector3.zero);

            Assert.IsTrue(result != null, "GO must be non-null");
            Assert.IsNotNull(result.GetComponent<ParticleEmitter>(),
                "Spawned GO must have a ParticleEmitter component");
        }

        [Test]
        public void SpawnParticlePreset_TwoDistinctPresets_ReturnsDifferentInstances()
        {
            var preset1 = MakePreset("dash_trail");
            var preset2 = MakePreset("explosion");
            var catalog = MakeCatalogWith(preset1, preset2);
            _manager.SetParticleCatalog(catalog);

            var go1 = _manager.SpawnParticlePreset("dash_trail", Vector3.zero);
            var go2 = _manager.SpawnParticlePreset("explosion",  Vector3.one);

            Assert.IsTrue(go1 != null, "First spawn must succeed");
            Assert.IsTrue(go2 != null, "Second spawn must succeed");
            Assert.AreNotSame(go1, go2, "Two separate preset spawns must produce distinct GameObjects");
        }

        [Test]
        public void SpawnParticlePreset_SamePresetTwice_ReturnsDifferentInstances()
        {
            var preset  = MakePreset("dash_trail");
            var catalog = MakeCatalogWith(preset);
            _manager.SetParticleCatalog(catalog);

            var go1 = _manager.SpawnParticlePreset("dash_trail", Vector3.zero);
            var go2 = _manager.SpawnParticlePreset("dash_trail", Vector3.one);

            Assert.IsTrue(go1 != null && go2 != null, "Both spawns must succeed");
            Assert.AreNotSame(go1, go2, "Each call to SpawnParticlePreset must create a new instance");
        }
    }
}
