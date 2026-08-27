using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the fix for the "spell kills are unattributed" bug: <c>Projectile.ResolveHit</c>
    /// used to call <c>Health.TakeDamage(dealt)</c> — the unattributed overload — while the
    /// caster it had stored two lines earlier sat unused. The consequence: a killing blow
    /// from a fireball/iceball/darkball/lightball reached <c>GameEvents.OnEntityDied</c>
    /// with <c>attacker == null</c>, so <c>CameraFeelDirector</c>'s Hurt cue had no direction
    /// and <c>PlayerHurtReaction</c> had nothing to face.
    /// </summary>
    [TestFixture]
    public class ProjectileCasterAttributionTests
    {
        private readonly List<GameObject> _scene = new();

        [SetUp]
        public void SetUp()
        {
            // Object.Destroy (used by Projectile.Expire when un-pooled) logs an error
            // outside Play mode; every sibling Projectile test suite ignores it the same way.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            // Re-armed here, not only in SetUp: the framework restores the flag before
            // TearDown runs, and tearing a Projectile down is exactly when its un-pooled
            // Expire path calls Object.Destroy — which EditMode answers with an error.
            LogAssert.ignoreFailingMessages = true;
            GameEvents.Clear();
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        private Projectile CreateProjectile()
        {
            var go = new GameObject("ProjectileAttributionTest");
            _scene.Add(go);
            var p = go.AddComponent<Projectile>();
            var awake = typeof(Projectile).GetMethod("Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);
            awake?.Invoke(p, null);
            return p;
        }

        private GameObject CreateCaster()
        {
            var go = new GameObject("Caster");
            _scene.Add(go);
            return go;
        }

        private GameObject CreateVictim(int maxHp)
        {
            var go = new GameObject("Victim");
            _scene.Add(go);
            var health = go.AddComponent<Health>();
            health.Initialize(maxHp);
            go.AddComponent<BoxCollider2D>();
            return go;
        }

        private static void InvokeResolveHit(Projectile p, Collider2D other)
        {
            var m = typeof(Projectile).GetMethod("ResolveHit",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "ResolveHit method missing — fix scope or rename here");
            m.Invoke(p, new object[] { other });
        }

        [Test]
        public void KillingHit_AttributesDeathToTheBoundCaster()
        {
            var p = CreateProjectile();
            var caster = CreateCaster();
            var victim = CreateVictim(maxHp: 5);

            p.SetCaster(caster.transform);
            // Damage (10) exceeds the victim's 5 HP — this hit is the killing blow.
            // Layer mask: both projectile and victim default to layer 0 (Default).
            p.Initialize(Vector2.right, spd: 10f, dmg: 10f, life: 1f, rng: 5f, targets: 1);

            GameObject reportedKiller = null;
            GameEvents.OnEntityDied += (victimGo, killer) => reportedKiller = killer;

            InvokeResolveHit(p, victim.GetComponent<BoxCollider2D>());

            Assert.AreSame(caster, reportedKiller,
                "A kill landed by a projectile must attribute GameEvents.OnEntityDied's " +
                "killer to the bound caster, not leave it unattributed.");
        }

        [Test]
        public void NonKillingHit_StillAttributesDamageToTheBoundCaster()
        {
            var p = CreateProjectile();
            var caster = CreateCaster();
            var victim = CreateVictim(maxHp: 100);

            p.SetCaster(caster.transform);
            p.Initialize(Vector2.right, spd: 10f, dmg: 10f, life: 1f, rng: 5f, targets: 1);

            GameObject reportedAttacker = null;
            GameEvents.OnEntityDamaged += (victimGo, attacker, amount) => reportedAttacker = attacker;

            InvokeResolveHit(p, victim.GetComponent<BoxCollider2D>());

            Assert.AreSame(caster, reportedAttacker,
                "Every projectile hit — not only the killing one — must carry the caster " +
                "so direction-dependent feedback (camera hurt cue, hit-facing) has something " +
                "to point at.");
            Assert.AreEqual(90, victim.GetComponent<Health>().CurrentHp);
        }

        [Test]
        public void NoCasterBound_HitIsStillUnattributedRatherThanThrowing()
        {
            // Defensive: a projectile with no SetCaster call (shouldn't happen in
            // production — every executor calls it — but must degrade safely) must not
            // throw, and must pass a null attacker rather than fabricate one.
            var p = CreateProjectile();
            var victim = CreateVictim(maxHp: 100);
            p.Initialize(Vector2.right, spd: 10f, dmg: 10f, life: 1f, rng: 5f, targets: 1);

            GameObject reportedAttacker = victim; // sentinel != null
            bool fired = false;
            GameEvents.OnEntityDamaged += (victimGo, attacker, amount) =>
            {
                reportedAttacker = attacker;
                fired = true;
            };

            Assert.DoesNotThrow(() =>
                InvokeResolveHit(p, victim.GetComponent<BoxCollider2D>()));

            Assert.IsTrue(fired, "precondition: the hit landed.");
            Assert.IsNull(reportedAttacker);
        }
    }
}
