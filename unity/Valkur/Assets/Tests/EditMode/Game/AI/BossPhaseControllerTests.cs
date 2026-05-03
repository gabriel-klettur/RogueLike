using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins <see cref="BossPhaseController"/>: phases activate as HP
    /// crosses their thresholds, transitions are one-way (heals don't
    /// regress), the OnPhaseChanged event fires on every distinct
    /// transition, and out-of-order phase entries are sorted at init.
    /// </summary>
    [TestFixture]
    public class BossPhaseControllerTests
    {
        private GameObject _go;
        private Health _health;
        private BossPhaseController _controller;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Boss");
            _health = _go.AddComponent<Health>();
            _health.Initialize(100);
            _controller = _go.AddComponent<BossPhaseController>();
            // Awake doesn't reliably run in EditMode for AddComponent.
            _controller.InitForTest(_health);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        // Replace the inspector-authored phase list via reflection so tests
        // can dial in specific HP thresholds without a serialized prefab.
        private static void SetPhases(BossPhaseController c,
                                      params (float hpFrac, string label)[] phases)
        {
            var f = typeof(BossPhaseController).GetField("phases",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var list = new System.Collections.Generic.List<BossPhaseController.PhaseBreakpoint>();
            foreach (var p in phases)
            {
                list.Add(new BossPhaseController.PhaseBreakpoint
                {
                    hpFraction = p.hpFrac,
                    label = p.label,
                });
            }
            f.SetValue(c, list);
            // NormalisePhases is private; force a reseed via InitForTest which
            // calls it and resets CurrentPhase to 0.
            c.InitForTest(c.GetComponent<Health>());
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void DefaultPhases_StartAtPhaseZero()
        {
            Assert.AreEqual(0, _controller.CurrentPhase,
                "Boss must enter Phase 0 on Awake regardless of HP — designers " +
                "expect intro VO and audio to play before any threshold fires.");
            Assert.AreEqual(3, _controller.PhaseCount,
                "Default phase list ships with three entries (Phase 1/2/3).");
        }

        [Test]
        public void Crossing_HalfHpThreshold_FiresPhaseOne()
        {
            SetPhases(_controller,
                (1f,   "P0"),
                (0.5f, "P1"),
                (0.2f, "P2"));

            int observedNew = -1;
            int observedOld = -1;
            _controller.OnPhaseChanged += (oldP, newP) =>
            { observedOld = oldP; observedNew = newP; };

            _controller.EvaluateAt(0.5f); // exactly at threshold
            Assert.AreEqual(1, _controller.CurrentPhase);
            Assert.AreEqual(0, observedOld);
            Assert.AreEqual(1, observedNew);
        }

        [Test]
        public void HealingBackOverThreshold_DoesNotRegress()
        {
            SetPhases(_controller, (1f, "P0"), (0.5f, "P1"));

            _controller.EvaluateAt(0.4f); // → P1
            Assert.AreEqual(1, _controller.CurrentPhase);

            int regressionEvents = 0;
            _controller.OnPhaseChanged += (_, _) => regressionEvents++;

            _controller.EvaluateAt(0.9f); // healed past the 0.5 threshold
            Assert.AreEqual(1, _controller.CurrentPhase,
                "Phases must escalate one-way; healing past a threshold must NOT " +
                "drop back to an earlier phase.");
            Assert.AreEqual(0, regressionEvents,
                "OnPhaseChanged must not fire on regression — listeners assume " +
                "phase changes are permanent escalations.");
        }

        [Test]
        public void MultiPhaseLeap_FiresOneEventToFinalPhase()
        {
            // A burst of damage takes the boss from full HP to 5% in one go.
            // The controller must end up in the deepest phase, with one
            // OnPhaseChanged event reflecting the final state.
            SetPhases(_controller,
                (1f,   "P0"),
                (0.5f, "P1"),
                (0.2f, "P2"),
                (0.1f, "P3"));

            int events = 0;
            int finalPhase = -1;
            _controller.OnPhaseChanged += (_, n) => { events++; finalPhase = n; };

            _controller.EvaluateAt(0.05f); // five percent → phase 3.

            Assert.AreEqual(3, _controller.CurrentPhase);
            Assert.AreEqual(1, events,
                "A multi-bucket leap must fire ONE phase-change event, not one " +
                "per intermediate phase — listeners only need the final state.");
            Assert.AreEqual(3, finalPhase);
        }

        [Test]
        public void OutOfOrderPhaseEntries_AreSortedDescending()
        {
            SetPhases(_controller,
                (0.2f, "P2"),    // intentionally out of order
                (1f,   "P0"),
                (0.5f, "P1"));

            // After NormalisePhases the list is sorted; ResolvePhaseAt(0.4f)
            // must return phase index 1 (the 0.5 entry).
            Assert.AreEqual(1, _controller.ResolvePhaseAt(0.4f));
            Assert.AreEqual(2, _controller.ResolvePhaseAt(0.1f));
        }

        [Test]
        public void HpEvent_DrivesPhaseTransition()
        {
            // Wire up the live Health → controller path: damaging Health
            // must trigger the same phase update as EvaluateAt would.
            SetPhases(_controller, (1f, "P0"), (0.5f, "P1"));

            // OnEnable doesn't fire reliably in EditMode AddComponent;
            // invoke it via reflection to subscribe to OnHpChanged.
            var onEnable = typeof(BossPhaseController).GetMethod("OnEnable",
                BindingFlags.NonPublic | BindingFlags.Instance);
            onEnable.Invoke(_controller, null);

            _health.TakeDamage(60); // 100 → 40, 0.4 frac → phase 1
            Assert.AreEqual(1, _controller.CurrentPhase,
                "Health.TakeDamage must drive the controller through Health.OnHpChanged.");
        }
    }
}
