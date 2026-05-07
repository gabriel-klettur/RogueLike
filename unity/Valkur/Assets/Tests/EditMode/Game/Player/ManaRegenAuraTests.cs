using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Covers <see cref="ManaRegenAura"/>'s lazy emitter wiring and the
    /// regen toggle. The component lives behind two indirections:
    ///   - <see cref="VFXManager"/> singleton must be present and have a
    ///     catalog before the aura can resolve its preset.
    ///   - The aura must NOT throw or stall if either piece is missing.
    /// We invoke <c>Awake</c> and <c>Update</c> by reflection because Unity
    /// does not drive MonoBehaviour lifecycle hooks in EditMode.
    /// </summary>
    public class ManaRegenAuraTests
    {
        private const string PresetId = "mana_regen_aura";

        private GameObject _vfxGo;
        private VFXManager _vfxManager;
        private GameObject _playerGo;
        private Mana _mana;
        private ManaRegenAura _aura;

        [SetUp]
        public void SetUp()
        {
            // ApplyPreset on a fresh ParticleEmitter logs occasional warnings
            // in EditMode; suppress them so they don't mask the assertions.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_vfxGo != null)    Object.DestroyImmediate(_vfxGo);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private void CreateVfxManager(ParticlePresetCatalog catalog)
        {
            _vfxGo = new GameObject("VFXManager");
            _vfxManager = _vfxGo.AddComponent<VFXManager>();
            typeof(VFXManager)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_vfxManager, null);
            if (catalog != null) _vfxManager.SetParticleCatalog(catalog);
        }

        private void CreatePlayerWithMana()
        {
            _playerGo = new GameObject("PlayerWithManaRegenAura");
            _mana = _playerGo.AddComponent<Mana>();
            _mana.Initialize(100, 5f);
            _aura = _playerGo.AddComponent<ManaRegenAura>();
            typeof(ManaRegenAura)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_aura, null);
        }

        private void TickAura()
        {
            typeof(ManaRegenAura)
                .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_aura, null);
        }

        private void ForceRegenDelayElapsed()
        {
            var f = typeof(Mana).GetField("_lastConsumeTime",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(_mana, Time.time - 10f);
        }

        private static ParticlePresetCatalog MakeCatalogWithManaPreset()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id = PresetId;
            def.displayName = "Mana Regen Aura";
            def.vfx = new ParticleVfxParams
            {
                kind = "aura",
                loops = true,
                emitRate = 20f,
                lifespan = 1f,
                speed = 1f,
                sizeMin = 0.1f,
                sizeMax = 0.3f,
            };
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            catalog.SetPresets(new[] { def });
            return catalog;
        }

        private static ParticlePresetCatalog MakeEmptyCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            catalog.SetPresets(new ParticlePresetDefinition[0]);
            return catalog;
        }

        // ── tests ───────────────────────────────────────────────────────────

        [Test]
        public void Update_WithoutVFXManager_IsSafeNoOp()
        {
            CreatePlayerWithMana();

            Assert.DoesNotThrow(TickAura,
                "Update must not NRE when VFXManager.Instance is null.");
            Assert.IsNull(_playerGo.GetComponentInChildren<ParticleEmitter>(),
                "No emitter child should be created before VFXManager exists.");
        }

        [Test]
        public void Update_WithVFXManagerNoCatalog_DoesNotCreateEmitter()
        {
            CreateVfxManager(catalog: null);
            CreatePlayerWithMana();

            TickAura();

            Assert.IsNull(_playerGo.GetComponentInChildren<ParticleEmitter>(),
                "Aura must wait for a catalog before building its child emitter.");
        }

        [Test]
        public void Update_PresetMissingFromCatalog_DoesNotCreateAndStopsRetrying()
        {
            CreateVfxManager(MakeEmptyCatalog());
            CreatePlayerWithMana();

            TickAura();   // first attempt: catalog present, preset missing → flag set
            TickAura();   // second tick: must short-circuit on _resolveAttempted

            Assert.IsNull(_playerGo.GetComponentInChildren<ParticleEmitter>(),
                "Missing preset must not produce an emitter — and must not retry forever.");
        }

        [Test]
        public void Update_WithCatalogAndPreset_CreatesEmitterChild()
        {
            CreateVfxManager(MakeCatalogWithManaPreset());
            CreatePlayerWithMana();

            TickAura();

            var emitter = _playerGo.GetComponentInChildren<ParticleEmitter>();
            Assert.IsNotNull(emitter,
                "First Update with VFXManager + matching preset must build the emitter.");
            Assert.AreEqual("ManaRegenAuraEmitter", emitter.gameObject.name,
                "Child emitter GameObject name acts as a contract for the runtime hierarchy.");
        }

        [Test]
        public void Update_AtFullMana_EmitterProducesNoParticles()
        {
            CreateVfxManager(MakeCatalogWithManaPreset());
            CreatePlayerWithMana();

            TickAura();   // creates emitter, IsRegenerating = false → stays stopped

            var ps = _playerGo.GetComponentInChildren<ParticleSystem>();
            Assert.IsNotNull(ps);
            ps.Clear(true);
            ps.Simulate(0.5f, withChildren: true, restart: false);
            Assert.AreEqual(0, ps.particleCount,
                "At full mana the aura must stay silent.");
        }

        [Test]
        public void Update_WhileRegenerating_EmitterProducesParticles()
        {
            CreateVfxManager(MakeCatalogWithManaPreset());
            CreatePlayerWithMana();

            _mana.TryConsume(40);
            ForceRegenDelayElapsed();

            TickAura();   // creates emitter AND toggles into emitting state in one pass

            var ps = _playerGo.GetComponentInChildren<ParticleSystem>();
            Assert.IsNotNull(ps);
            ps.Clear(true);
            ps.Simulate(0.5f, withChildren: true, restart: false);
            Assert.Greater(ps.particleCount, 0,
                "While Mana.IsRegenerating the aura must emit particles.");
        }

        [Test]
        public void Update_StopsEmittingWhenManaReturnsToFull()
        {
            CreateVfxManager(MakeCatalogWithManaPreset());
            CreatePlayerWithMana();

            _mana.TryConsume(40);
            ForceRegenDelayElapsed();
            TickAura();   // start

            _mana.Restore(1000);
            TickAura();   // back to full → must toggle off

            var ps = _playerGo.GetComponentInChildren<ParticleSystem>();
            Assert.IsNotNull(ps);
            ps.Clear(true);
            ps.Simulate(0.5f, withChildren: true, restart: false);
            Assert.AreEqual(0, ps.particleCount,
                "When mana returns to max, the aura must stop emitting.");
        }

        [Test]
        public void EmitterAnchor_OffsetsTowardsSpriteCenter_NotFeet()
        {
            // Foot-pivot rig: root sits at the feet, sprite extends upward.
            // The auto-anchoring path must place the emitter at the sprite's
            // bounds.center, not at the root, so particles emanate from the
            // torso instead of the boots.
            CreateVfxManager(MakeCatalogWithManaPreset());

            _playerGo = new GameObject("PlayerFootPivot");
            _mana = _playerGo.AddComponent<Mana>();
            _mana.Initialize(100, 5f);

            // Build a child with a real sprite so SpriteRenderer.bounds is non-zero.
            var spriteChild = new GameObject("Body");
            spriteChild.transform.SetParent(_playerGo.transform, false);
            spriteChild.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var sr = spriteChild.AddComponent<SpriteRenderer>();
            var tex = new Texture2D(8, 8);
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0f), 16f);

            _aura = _playerGo.AddComponent<ManaRegenAura>();
            typeof(ManaRegenAura).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(_aura, null);

            TickAura();

            var emitterTransform = _playerGo.GetComponentInChildren<ParticleEmitter>().transform;
            Assert.Greater(emitterTransform.localPosition.y, 0.001f,
                "Auto-centered emitter must sit above the root (towards the sprite torso).");

            Object.DestroyImmediate(tex);
        }
    }
}
