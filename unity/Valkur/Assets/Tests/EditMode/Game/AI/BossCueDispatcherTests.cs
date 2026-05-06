using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Enemies;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins <see cref="BossCueDispatcher"/>: each <see cref="BossCueType"/>
    /// dispatches to the right subsystem, unknown spell keys are warned-and-
    /// skipped, and the auto-cast suspension flag toggles
    /// <c>NPCAutoCast.SetCastingEnabled</c> exactly once.
    /// </summary>
    [TestFixture]
    public class BossCueDispatcherTests
    {
        private GameObject _bossGo;
        private BossPhaseController _phases;
        private NPCAutoCast _autoCast;
        private SpellCaster _caster;
        private Health _health;
        private BossBeatChoreographer _choreo;
        private BossCueDispatcher _dispatcher;

        [SetUp]
        public void SetUp()
        {
            _bossGo = new GameObject("Boss");
            _bossGo.AddComponent<Rigidbody2D>();
            _health = _bossGo.AddComponent<Health>();
            _health.Initialize(100);

            _caster = _bossGo.AddComponent<SpellCaster>();
            var f = typeof(SpellCaster).GetField("_cooldownTimers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(_caster, new float[_caster.SlotCount]);

            _autoCast = _bossGo.AddComponent<NPCAutoCast>();
            _autoCast.Clear();

            _phases = _bossGo.AddComponent<BossPhaseController>();
            _phases.InitForTest(_health);

            _choreo = _bossGo.AddComponent<BossBeatChoreographer>();
            _dispatcher = _bossGo.AddComponent<BossCueDispatcher>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_bossGo != null) Object.DestroyImmediate(_bossGo);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static SpellDefinition MakeSpell(string key, float prepareSeconds = 0f)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = key;
            s.displayName = key;
            s.type = SpellType.Projectile;
            s.cooldownDuration = 1f;
            s.prepareDuration = prepareSeconds;
            return s;
        }

        private static SpellCatalog MakeCatalog(params SpellDefinition[] spells)
        {
            var c = ScriptableObject.CreateInstance<SpellCatalog>();
            c.SetSpellsRuntime(spells);
            return c;
        }

        private static BossCue Cue(BossCueType type, string key)
        {
            return new BossCue
            {
                bar = 0,
                beat = 0,
                beatFraction = 0f,
                type = type,
                targetKey = key,
                targeting = BossCueTargeting.Forward,
                payload = 0f,
            };
        }

        // ── Behaviours ──────────────────────────────────────────────────────

        [Test]
        public void CastSpellCue_RegistersSpellLookupAgainstCatalog()
        {
            var fb = MakeSpell("fireball");
            var catalog = MakeCatalog(fb);
            _dispatcher.InitForTest(_choreo, _caster, _phases, _autoCast, animator: null,
                                    spells: catalog, monsters: null, spawner: null);

            // Unknown spell key warns and skips without throwing.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("unknown spellKey"));
            _dispatcher.HandleCueForTest(Cue(BossCueType.CastSpell, "ghostball"));

            // Known spell key reaches the caster path. We can't verify the
            // cast actually executed (TryCastByKey gates on cooldown / mana
            // and depends on full scene wiring) but it must not warn or throw.
            Assert.DoesNotThrow(() =>
                _dispatcher.HandleCueForTest(Cue(BossCueType.CastSpell, "fireball")));

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(fb);
        }

        [Test]
        public void SwitchPhaseCue_AdvancesPhaseControllerToMatchingLabel()
        {
            // Set up 3 phases via reflection (BossPhaseController exposes only
            // inspector authoring at runtime).
            var listField = typeof(BossPhaseController).GetField("phases",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var phases = new System.Collections.Generic.List<BossPhaseController.PhaseBreakpoint>
            {
                new BossPhaseController.PhaseBreakpoint { hpFraction = 1.00f, label = "Entry" },
                new BossPhaseController.PhaseBreakpoint { hpFraction = 0.50f, label = "Frenzy" },
                new BossPhaseController.PhaseBreakpoint { hpFraction = 0.20f, label = "Final" },
            };
            listField.SetValue(_phases, phases);
            _phases.InitForTest(_health);

            _dispatcher.InitForTest(_choreo, _caster, _phases, _autoCast, animator: null,
                                    spells: null, monsters: null, spawner: null);

            int phaseChanges = 0;
            _phases.OnPhaseChanged += (_, _) => phaseChanges++;

            _dispatcher.HandleCueForTest(Cue(BossCueType.SwitchPhase, "Frenzy"));
            Assert.AreEqual(1, _phases.CurrentPhase, "Cue must move the controller to the matching phase.");
            Assert.AreEqual(1, phaseChanges, "OnPhaseChanged must fire exactly once for the transition.");

            // Lower-index phase is rejected — phases only escalate.
            _dispatcher.HandleCueForTest(Cue(BossCueType.SwitchPhase, "Entry"));
            Assert.AreEqual(1, _phases.CurrentPhase, "ForcePhase must refuse to regress to a lower phase.");
            Assert.AreEqual(1, phaseChanges, "Rejected switch must not fire OnPhaseChanged.");

            // Unknown label is a no-op.
            _dispatcher.HandleCueForTest(Cue(BossCueType.SwitchPhase, "Zzz"));
            Assert.AreEqual(1, _phases.CurrentPhase);
        }

        [Test]
        public void SuspendAutoCast_ToggleIsIdempotent()
        {
            _dispatcher.InitForTest(_choreo, _caster, _phases, _autoCast, animator: null,
                                    spells: null, monsters: null, spawner: null);

            // Pre-seed an entry so we can observe casting state via the engine.
            _autoCast.AddEntry(0, 5f, 0f);
            var castingField = typeof(NPCAutoCast).GetField("castingEnabled",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsTrue((bool)castingField.GetValue(_autoCast),
                "Sanity: NPCAutoCast starts with casting enabled.");

            _dispatcher.SuspendAutoCast();
            Assert.IsFalse((bool)castingField.GetValue(_autoCast),
                "SuspendAutoCast must disable casting on the rotation.");

            _dispatcher.SuspendAutoCast(); // idempotent — second call stays disabled
            Assert.IsFalse((bool)castingField.GetValue(_autoCast));

            _dispatcher.ResumeAutoCastIfSuspended();
            Assert.IsTrue((bool)castingField.GetValue(_autoCast),
                "Resume must re-enable casting.");

            _dispatcher.ResumeAutoCastIfSuspended(); // no-op when not suspended
            Assert.IsTrue((bool)castingField.GetValue(_autoCast));
        }

        [Test]
        public void SpawnAddCue_WithoutSpawner_WarnsAndSkips()
        {
            _dispatcher.InitForTest(_choreo, _caster, _phases, _autoCast, animator: null,
                                    spells: null, monsters: null, spawner: null);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("MonsterSpawner"));
            Assert.DoesNotThrow(() =>
                _dispatcher.HandleCueForTest(Cue(BossCueType.SpawnAdd, "slime")));
        }

        [Test]
        public void PlaySfxCue_WithoutKey_IsNoOp()
        {
            _dispatcher.InitForTest(_choreo, _caster, _phases, _autoCast, animator: null,
                                    spells: null, monsters: null, spawner: null);

            // Empty targetKey is a defensive no-op (no warning, no exception).
            Assert.DoesNotThrow(() =>
                _dispatcher.HandleCueForTest(Cue(BossCueType.PlaySfx, string.Empty)));
        }
    }
}
