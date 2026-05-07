using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Tests for the <see cref="GameEvents.OnSpellCast"/> event and
    /// <see cref="GameEvents.FireSpellCast"/> / <see cref="GameEvents.Clear"/> lifecycle.
    /// </summary>
    public class GameEventsSpellCastTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            // Always clear the static bus so subscriptions don't bleed between tests.
            GameEvents.Clear();

            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        // ── FireSpellCast propagates args ────────────────────────────────

        [Test]
        public void FireSpellCast_SingleSubscriber_ReceivesCorrectArgs()
        {
            var caster = new GameObject("Caster");
            _scene.Add(caster);

            GameObject capturedCaster = null;
            string capturedKey = null;
            string capturedName = null;
            float capturedCd = -1f;

            GameEvents.OnSpellCast += (c, k, n, cd) =>
            {
                capturedCaster = c;
                capturedKey    = k;
                capturedName   = n;
                capturedCd     = cd;
            };

            GameEvents.FireSpellCast(caster, "fireball", "Fireball", 2.5f);

            Assert.AreSame(caster, capturedCaster, "caster arg must pass through");
            Assert.AreEqual("fireball",  capturedKey,  "spellKey arg must pass through");
            Assert.AreEqual("Fireball",  capturedName, "displayName arg must pass through");
            Assert.AreEqual(2.5f,        capturedCd,   1e-5f, "cooldownDuration arg must pass through");
        }

        [Test]
        public void FireSpellCast_MultipleSubscribers_AllReceiveFire()
        {
            var caster = new GameObject("Caster");
            _scene.Add(caster);

            int count = 0;
            GameEvents.OnSpellCast += (c, k, n, cd) => count++;
            GameEvents.OnSpellCast += (c, k, n, cd) => count++;
            GameEvents.OnSpellCast += (c, k, n, cd) => count++;

            GameEvents.FireSpellCast(caster, "dash", "Dash", 1f);

            Assert.AreEqual(3, count, "All three subscribers must be invoked");
        }

        [Test]
        public void FireSpellCast_WhenNoSubscribers_DoesNotThrow()
        {
            // GameEvents.OnSpellCast is null — FireSpellCast must guard with ?.
            var caster = new GameObject("Caster");
            _scene.Add(caster);

            Assert.DoesNotThrow(() =>
                GameEvents.FireSpellCast(caster, "fireball", "Fireball", 1f));
        }

        [Test]
        public void FireSpellCast_NullCaster_IsPassedThroughWithoutException()
        {
            // The event bus must not crash — filtering by identity is the subscriber's job.
            GameObject capturedCaster = new GameObject("sentinel");
            _scene.Add(capturedCaster);

            GameEvents.OnSpellCast += (c, k, n, cd) => capturedCaster = c;

            Assert.DoesNotThrow(() =>
                GameEvents.FireSpellCast(null, "dash", "Dash", 1f));

            Assert.IsNull(capturedCaster, "null caster must be forwarded as-is");
        }

        [Test]
        public void FireSpellCast_EmptySpellKey_IsPassedThroughUnchanged()
        {
            string received = "not-empty";
            GameEvents.OnSpellCast += (c, k, n, cd) => received = k;

            var go = new GameObject("Caster");
            _scene.Add(go);

            GameEvents.FireSpellCast(go, "", "Display", 1f);

            Assert.AreEqual("", received, "empty spellKey must propagate unchanged");
        }

        // ── Clear resets subscriptions ───────────────────────────────────

        [Test]
        public void Clear_RemovesAllOnSpellCastSubscribers()
        {
            int firedCount = 0;
            GameEvents.OnSpellCast += (c, k, n, cd) => firedCount++;

            GameEvents.Clear();

            var go = new GameObject("Caster");
            _scene.Add(go);
            GameEvents.FireSpellCast(go, "fireball", "Fireball", 1f);

            Assert.AreEqual(0, firedCount,
                "After Clear() no subscriber should fire for OnSpellCast");
        }

        [Test]
        public void Clear_DoesNotThrow_WhenCalledWithNoSubscribers()
        {
            // Static event is null — calling Clear again is safe.
            Assert.DoesNotThrow(() => GameEvents.Clear());
        }

        [Test]
        public void Clear_AllowsReSubscriptionAfterClear()
        {
            int firedCount = 0;

            GameEvents.OnSpellCast += (c, k, n, cd) => firedCount++;
            GameEvents.Clear();
            GameEvents.OnSpellCast += (c, k, n, cd) => firedCount++;

            var go = new GameObject("Caster");
            _scene.Add(go);
            GameEvents.FireSpellCast(go, "fireball", "Fireball", 1f);

            Assert.AreEqual(1, firedCount,
                "Exactly one new subscriber (added after Clear) should fire");
        }

        // ── Multiple casts ───────────────────────────────────────────────

        [Test]
        public void FireSpellCast_FiredTwice_SubscriberReceivesBoth()
        {
            var caster = new GameObject("Caster");
            _scene.Add(caster);

            var received = new List<string>();
            GameEvents.OnSpellCast += (c, k, n, cd) => received.Add(k);

            GameEvents.FireSpellCast(caster, "fireball", "Fireball", 1f);
            GameEvents.FireSpellCast(caster, "dash", "Dash", 0.5f);

            Assert.AreEqual(2, received.Count, "Both casts must notify the subscriber");
            Assert.AreEqual("fireball", received[0]);
            Assert.AreEqual("dash",     received[1]);
        }
    }
}
