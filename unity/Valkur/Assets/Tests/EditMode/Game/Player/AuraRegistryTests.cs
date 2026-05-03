using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Pins <see cref="AuraRegistry"/>: register/unregister/replace
    /// idempotency, TryApply success/failure paths, exception capture
    /// (a buggy aura must not crash the dispatcher), and the built-in
    /// aura wiring (toughness HP regen, manaflow mana regen).
    /// </summary>
    [TestFixture]
    public class AuraRegistryTests
    {
        [SetUp]
        public void SetUp() { AuraRegistry.ClearForTesting(); }

        [TearDown]
        public void TearDown() { AuraRegistry.ClearForTesting(); }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void Register_ThenTryApply_InvokesHandler()
        {
            int calls = 0;
            float lastMagnitude = 0f;
            AuraRegistry.Register("test_aura", (entity, mag) => { calls++; lastMagnitude = mag; });

            var go = new GameObject("subject");
            try
            {
                Assert.IsTrue(AuraRegistry.TryApply("test_aura", go, 1.5f));
                Assert.AreEqual(1, calls);
                Assert.AreEqual(1.5f, lastMagnitude, 0.0001f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TryApply_UnknownKey_ReturnsFalse_DoesNotThrow()
        {
            var go = new GameObject("subject");
            try
            {
                Assert.IsFalse(AuraRegistry.TryApply("nonexistent", go, 1f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Register_SameKey_ReplacesHandler()
        {
            int firstCalls  = 0;
            int secondCalls = 0;
            AuraRegistry.Register("aura", (_, _) => firstCalls++);
            AuraRegistry.Register("aura", (_, _) => secondCalls++);

            var go = new GameObject("subject");
            try
            {
                AuraRegistry.TryApply("aura", go, 1f);
                Assert.AreEqual(0, firstCalls,
                    "Re-registering must REPLACE the old handler — old must NOT fire.");
                Assert.AreEqual(1, secondCalls);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void HandlerThatThrows_DoesNotPropagate()
        {
            // A buggy aura handler must not bring down the dispatcher.
            // The error is logged; TryApply returns false.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("Handler for 'crash' threw"));
            AuraRegistry.Register("crash", (_, _) => { throw new System.Exception("boom"); });

            var go = new GameObject("subject");
            try
            {
                Assert.IsFalse(AuraRegistry.TryApply("crash", go, 1f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Builtin_Toughness_AddsHpRegenAura()
        {
            AuraRegistry.InitializeBuiltinsForTesting();

            var go = new GameObject("subject");
            try
            {
                go.AddComponent<Health>().Initialize(100);
                Assert.IsTrue(AuraRegistry.TryApply("toughness", go, magnitude: 2f),
                    "Built-in 'toughness' must register on Init.");

                var aura = go.GetComponent<HpRegenAura>();
                Assert.IsNotNull(aura, "Toughness must add HpRegenAura component.");
                Assert.AreEqual(2f, aura.RatePerSecond, 0.0001f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Builtin_Toughness_StackingAcrossLearns_SumsRegen()
        {
            AuraRegistry.InitializeBuiltinsForTesting();

            var go = new GameObject("subject");
            try
            {
                go.AddComponent<Health>().Initialize(100);
                AuraRegistry.TryApply("toughness", go, 1f);
                AuraRegistry.TryApply("toughness", go, 0.5f);
                AuraRegistry.TryApply("toughness", go, 2f);

                var aura = go.GetComponent<HpRegenAura>();
                Assert.AreEqual(3.5f, aura.RatePerSecond, 0.0001f,
                    "Stacking the same aura across multiple skill nodes must sum.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void HpRegenAura_Tick_HealsAtConfiguredRate()
        {
            var go = new GameObject("subject");
            try
            {
                var health = go.AddComponent<Health>();
                health.Initialize(100);
                health.TakeDamage(50); // 50/100

                var aura = go.AddComponent<HpRegenAura>();
                aura.AddRegen(2f); // 2 HP/sec

                aura.TickForTest(1f);  // +2 HP
                Assert.AreEqual(52, health.CurrentHp);

                aura.TickForTest(0.4f); // +0.8 HP, accumulates
                Assert.AreEqual(52, health.CurrentHp,
                    "Sub-1-HP fractions must accumulate, not floor immediately.");

                aura.TickForTest(0.4f); // +0.8 more, total accum 1.6, floor to +1
                Assert.AreEqual(53, health.CurrentHp);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Builtin_Manaflow_AddsRegenBonus()
        {
            AuraRegistry.InitializeBuiltinsForTesting();

            var go = new GameObject("subject");
            try
            {
                var mana = go.AddComponent<Mana>();
                mana.Initialize(100, regen: 2f);
                Assert.AreEqual(2f, mana.RegenPerSecond, 0.0001f);

                AuraRegistry.TryApply("manaflow", go, magnitude: 1.5f);
                Assert.AreEqual(3.5f, mana.RegenPerSecond, 0.0001f,
                    "Manaflow must stack on top of the existing regen rate.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Unregister_RemovesHandler()
        {
            AuraRegistry.Register("removable", (_, _) => { });
            Assert.IsTrue(AuraRegistry.IsRegistered("removable"));

            Assert.IsTrue(AuraRegistry.Unregister("removable"));
            Assert.IsFalse(AuraRegistry.IsRegistered("removable"));
            Assert.IsFalse(AuraRegistry.Unregister("removable"),
                "Second Unregister of the same key must return false.");
        }
    }
}
