using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode
{
    public class CombatTests
    {
        // --- MeleeCombat ---

        [Test]
        public void MeleeCombat_Initialize_SetsStats()
        {
            var go = new GameObject("Attacker");
            var melee = go.AddComponent<MeleeCombat>();
            melee.Initialize(15, 0.8f, 2f);
            Assert.AreEqual(15, melee.Damage);
            Assert.AreEqual(0.8f, melee.CooldownTotal, 0.001f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MeleeCombat_CanAttack_TrueInitially()
        {
            var go = new GameObject("Attacker");
            var melee = go.AddComponent<MeleeCombat>();
            melee.Initialize(10, 1f, 1f);
            Assert.IsTrue(melee.CanAttack);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MeleeCombat_CooldownRemaining_ZeroWhenReady()
        {
            var go = new GameObject("Attacker");
            var melee = go.AddComponent<MeleeCombat>();
            melee.Initialize(10, 1f, 1f);
            Assert.AreEqual(0f, melee.CooldownRemaining, 0.01f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MeleeCombat_Damage_MatchesInitialized()
        {
            var go = new GameObject("Attacker");
            var melee = go.AddComponent<MeleeCombat>();
            melee.Initialize(25, 0.5f, 1.5f);
            Assert.AreEqual(25, melee.Damage);
            Object.DestroyImmediate(go);
        }

        // --- DashAbility ---

        [Test]
        public void DashAbility_CanDash_TrueInitially()
        {
            var go = new GameObject("Dasher");
            go.AddComponent<Rigidbody2D>();
            var dash = go.AddComponent<DashAbility>();
            Assert.IsTrue(dash.CanDash);
            Assert.IsFalse(dash.IsDashing);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void DashAbility_CooldownRemaining_ZeroWhenReady()
        {
            var go = new GameObject("Dasher");
            go.AddComponent<Rigidbody2D>();
            var dash = go.AddComponent<DashAbility>();
            Assert.AreEqual(0f, dash.CooldownRemaining, 0.01f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void DashAbility_TryDash_ZeroDirection_Fails()
        {
            var go = new GameObject("Dasher");
            go.AddComponent<Rigidbody2D>();
            var dash = go.AddComponent<DashAbility>();
            bool result = dash.TryDash(Vector2.zero);
            Assert.IsFalse(result);
            Assert.IsFalse(dash.IsDashing);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void DashAbility_TryDash_ValidDirection_Succeeds()
        {
            var go = new GameObject("Dasher");
            go.AddComponent<Rigidbody2D>();
            var dash = go.AddComponent<DashAbility>();
            bool result = dash.TryDash(Vector2.right);
            Assert.IsTrue(result);
            Assert.IsTrue(dash.IsDashing);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void DashAbility_TryDash_WhileDashing_Fails()
        {
            var go = new GameObject("Dasher");
            go.AddComponent<Rigidbody2D>();
            var dash = go.AddComponent<DashAbility>();
            dash.TryDash(Vector2.right);
            bool second = dash.TryDash(Vector2.left);
            Assert.IsFalse(second);
            Object.DestroyImmediate(go);
        }

        // --- Health + MeleeCombat integration (event wiring) ---

        [Test]
        public void MeleeCombat_OnHitTarget_EventCanBeSubscribed()
        {
            var go = new GameObject("Attacker");
            var melee = go.AddComponent<MeleeCombat>();
            melee.Initialize(10, 1f, 1f);

            bool eventFired = false;
            melee.OnHitTarget += (target, dmg) => eventFired = true;

            // Event won't fire without actual physics, but subscription should not throw
            Assert.IsFalse(eventFired);
            Object.DestroyImmediate(go);
        }
    }
}
