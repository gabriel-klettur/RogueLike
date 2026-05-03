using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins <see cref="BossConfigurator"/>: ConfigurePhasesFromDefinition
    /// rebuilds BossPhaseController's phase list from the SO,
    /// ConfigureRotation rewires NPCAutoCast entries to match the active
    /// phase's spells, and unknown spell keys log warnings without
    /// breaking the boss.
    /// </summary>
    [TestFixture]
    public class BossConfiguratorTests
    {
        private GameObject _bossGo;
        private BossPhaseController _phases;
        private NPCAutoCast _autoCast;
        private SpellCaster _caster;
        private Health _health;
        private BossConfigurator _configurator;

        [SetUp]
        public void SetUp()
        {
            _bossGo = new GameObject("Boss");
            _bossGo.AddComponent<Rigidbody2D>();
            _health = _bossGo.AddComponent<Health>();
            _health.Initialize(100);
            _caster = _bossGo.AddComponent<SpellCaster>();
            // Prime cooldown array (Awake doesn't fire reliably in EditMode).
            var f = typeof(SpellCaster).GetField("_cooldownTimers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(_caster, new float[_caster.SlotCount]);

            _autoCast = _bossGo.AddComponent<NPCAutoCast>();
            _autoCast.Clear();

            _phases = _bossGo.AddComponent<BossPhaseController>();
            _phases.InitForTest(_health);

            _configurator = _bossGo.AddComponent<BossConfigurator>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_bossGo != null) Object.DestroyImmediate(_bossGo);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static SpellDefinition MakeSpell(string key)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = key;
            s.displayName = key;
            s.type = SpellType.Projectile;
            s.cooldownDuration = 1f;
            return s;
        }

        private static SpellCatalog MakeCatalog(params SpellDefinition[] spells)
        {
            var c = ScriptableObject.CreateInstance<SpellCatalog>();
            c.SetSpellsRuntime(spells);
            return c;
        }

        private static BossDefinition MakeBoss(params (float hp, string label, string[] spells)[] phases)
        {
            var d = ScriptableObject.CreateInstance<BossDefinition>();
            d.phases = new BossDefinition.Phase[phases.Length];
            for (int i = 0; i < phases.Length; i++)
            {
                d.phases[i] = new BossDefinition.Phase
                {
                    hpThreshold = phases[i].hp,
                    label = phases[i].label,
                    autoCastList = phases[i].spells ?? System.Array.Empty<string>(),
                };
            }
            return d;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void ConfigurePhases_RebuildsControllerPhaseList()
        {
            var def = MakeBoss(
                (1f,   "Entry", null),
                (0.5f, "Frenzy", null),
                (0.2f, "Final", null));

            _configurator.InitForTest(_phases, _autoCast, _caster, catalog: null);
            _configurator.SetDefinition(def);
            _configurator.ConfigurePhasesFromDefinition();

            Assert.AreEqual(3, _phases.PhaseCount);
            Assert.AreEqual(0, _phases.CurrentPhase, "Init must always reset to phase 0.");

            // Resolve phase index at HP fractions to confirm the threshold
            // ladder is in place.
            Assert.AreEqual(0, _phases.ResolvePhaseAt(1f));
            Assert.AreEqual(1, _phases.ResolvePhaseAt(0.4f),
                "0.4 HP frac must map to phase 1 (the 0.5 threshold).");
            Assert.AreEqual(2, _phases.ResolvePhaseAt(0.1f));

            Object.DestroyImmediate(def);
        }

        [Test]
        public void ConfigureRotation_PopulatesAutoCastForPhase()
        {
            var fb = MakeSpell("fireball");
            var ic = MakeSpell("iceball");
            var catalog = MakeCatalog(fb, ic);
            var def = MakeBoss(
                (1f,   "Entry",  new[] { "fireball" }),
                (0.5f, "Frenzy", new[] { "fireball", "iceball" }));

            _configurator.InitForTest(_phases, _autoCast, _caster, catalog);
            _configurator.SetDefinition(def, catalog);
            _configurator.ConfigurePhasesFromDefinition();

            _configurator.ConfigureRotation(0);
            Assert.AreEqual(1, _autoCast.EntryCount,
                "Phase 0 has one spell — exactly one auto-cast entry.");
            Assert.AreSame(fb, _caster.GetSpellAtSlot(0));

            _configurator.ConfigureRotation(1);
            Assert.AreEqual(2, _autoCast.EntryCount,
                "Phase 1 has two spells — Clear() + AddEntry rebuilds the rotation.");
            Assert.AreSame(fb, _caster.GetSpellAtSlot(0));
            Assert.AreSame(ic, _caster.GetSpellAtSlot(1));

            Object.DestroyImmediate(def);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(fb);
            Object.DestroyImmediate(ic);
        }

        [Test]
        public void ConfigureRotation_UnknownSpellKey_WarnsAndSkips()
        {
            var fb = MakeSpell("fireball");
            var catalog = MakeCatalog(fb);
            var def = MakeBoss((1f, "Entry", new[] { "fireball", "ghostball" }));

            _configurator.InitForTest(_phases, _autoCast, _caster, catalog);
            _configurator.SetDefinition(def, catalog);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("ghostball"));
            _configurator.ConfigureRotation(0);

            Assert.AreEqual(1, _autoCast.EntryCount,
                "Only the known spell registers; unknown keys must skip without breaking the boss.");

            Object.DestroyImmediate(def);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(fb);
        }

        [Test]
        public void EmptyAutoCastList_LeavesAutoCastClear()
        {
            var catalog = MakeCatalog();
            var def = MakeBoss((1f, "Entry", System.Array.Empty<string>()));

            // Pre-seed an entry to verify Clear() runs.
            _autoCast.AddEntry(0, 5f, 0.1f);
            Assert.AreEqual(1, _autoCast.EntryCount, "Sanity: pre-existing entry.");

            _configurator.InitForTest(_phases, _autoCast, _caster, catalog);
            _configurator.SetDefinition(def, catalog);
            _configurator.ConfigureRotation(0);

            Assert.AreEqual(0, _autoCast.EntryCount,
                "Empty autoCastList for a phase must wipe the rotation, not retain stale entries.");

            Object.DestroyImmediate(def);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void ConfigureRotation_OutOfRangePhaseIndex_IsNoOp()
        {
            var def = MakeBoss((1f, "Entry", null));
            _configurator.InitForTest(_phases, _autoCast, _caster, catalog: null);
            _configurator.SetDefinition(def);

            Assert.DoesNotThrow(() => _configurator.ConfigureRotation(99),
                "Out-of-range phase index must be a defensive no-op, not crash the boss.");
            Assert.DoesNotThrow(() => _configurator.ConfigureRotation(-1));

            Object.DestroyImmediate(def);
        }
    }
}
