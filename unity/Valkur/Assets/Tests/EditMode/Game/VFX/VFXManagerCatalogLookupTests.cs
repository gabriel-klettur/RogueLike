using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers the lazy catalog-lookup surface added to <see cref="VFXManager"/>:
    /// <c>HasParticleCatalog</c> and <c>GetParticlePreset</c>. These are the
    /// hooks <see cref="Valkur.Gameplay.ManaRegenAura"/> uses to resolve its
    /// preset without inspector wiring, so their null/empty/missing-id
    /// behaviour has to be airtight — a stray exception inside the player
    /// Update loop would tank the frame.
    /// </summary>
    public class VFXManagerCatalogLookupTests
    {
        private GameObject _managerGo;
        private VFXManager _manager;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _managerGo = new GameObject("VFXManager");
            _manager   = _managerGo.AddComponent<VFXManager>();

            // Awake doesn't run automatically in EditMode — invoke it so the
            // singleton is fully initialised (matches VFXManagerSpawnTests).
            typeof(VFXManager)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_manager, null);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_managerGo != null) Object.DestroyImmediate(_managerGo);
        }

        private static ParticlePresetDefinition MakePreset(string id)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id = id;
            def.displayName = id;
            def.vfx = new ParticleVfxParams { kind = "aura", lifespan = 1f };
            return def;
        }

        private static ParticlePresetCatalog MakeCatalogWith(params ParticlePresetDefinition[] presets)
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            catalog.SetPresets(presets);
            return catalog;
        }

        [Test]
        public void HasParticleCatalog_BeforeSet_ReturnsFalse()
        {
            Assert.IsFalse(_manager.HasParticleCatalog,
                "Fresh VFXManager must report no catalog until SetParticleCatalog runs.");
        }

        [Test]
        public void HasParticleCatalog_AfterSet_ReturnsTrue()
        {
            _manager.SetParticleCatalog(MakeCatalogWith(MakePreset("foo")));

            Assert.IsTrue(_manager.HasParticleCatalog,
                "After SetParticleCatalog, HasParticleCatalog must report ready.");
        }

        [Test]
        public void GetParticlePreset_NoCatalog_ReturnsNull()
        {
            // No catalog set — must not throw, must return null.
            ParticlePresetDefinition result = null;
            Assert.DoesNotThrow(() => result = _manager.GetParticlePreset("anything"),
                "GetParticlePreset must be a safe no-op when no catalog is set.");
            Assert.IsNull(result);
        }

        [Test]
        public void GetParticlePreset_KnownId_ReturnsPreset()
        {
            var preset = MakePreset("mana_regen_aura");
            _manager.SetParticleCatalog(MakeCatalogWith(preset));

            var result = _manager.GetParticlePreset("mana_regen_aura");

            Assert.AreSame(preset, result,
                "Known id must resolve to the exact ScriptableObject in the catalog.");
        }

        [Test]
        public void GetParticlePreset_UnknownId_ReturnsNull()
        {
            _manager.SetParticleCatalog(MakeCatalogWith(MakePreset("known")));

            var result = _manager.GetParticlePreset("missing");

            Assert.IsNull(result, "Unknown ids must return null without throwing.");
        }

        [Test]
        public void GetParticlePreset_NullOrEmptyId_ReturnsNull()
        {
            _manager.SetParticleCatalog(MakeCatalogWith(MakePreset("foo")));

            Assert.IsNull(_manager.GetParticlePreset(null),  "null id");
            Assert.IsNull(_manager.GetParticlePreset(""),    "empty id");
        }
    }
}
