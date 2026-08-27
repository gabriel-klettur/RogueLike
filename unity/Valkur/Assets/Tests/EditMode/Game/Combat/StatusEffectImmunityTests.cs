using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// EditMode tests for <see cref="StatusEffectManager"/>'s immunity gate: an entity
    /// authored with a <see cref="StatusEffectKind"/> in its immunity list must refuse
    /// that effect outright, before <c>OnApply</c> ever runs.
    /// </summary>
    [TestFixture]
    public class StatusEffectImmunityTests
    {
        private StatusEffectManager CreateManager()
        {
            var go = new GameObject("ImmunityTestEntity");
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<SpriteRenderer>();
            return go.AddComponent<StatusEffectManager>();
        }

        private static void Cleanup(StatusEffectManager mgr)
        {
            if (mgr != null) Object.DestroyImmediate(mgr.gameObject);
        }

        [Test]
        public void ImmuneEntity_IgnoresTheMatchingStatus()
        {
            var mgr = CreateManager();
            try
            {
                mgr.SetImmunities(new[] { StatusEffectKind.Stun });

                mgr.Apply(new StunEffect(5f));

                Assert.IsFalse(mgr.HasEffect<StunEffect>(),
                    "An entity immune to Stun must never carry a live StunEffect.");
                Assert.IsFalse(mgr.IsStunned);
            }
            finally { Cleanup(mgr); }
        }

        [Test]
        public void ImmuneEntity_FiresOnEffectImmune_NotOnEffectApplied()
        {
            var mgr = CreateManager();
            try
            {
                mgr.SetImmunities(new[] { StatusEffectKind.Freeze });

                StatusEffectKind? immuneKind = null;
                bool appliedFired = false;
                mgr.OnEffectImmune += k => immuneKind = k;
                mgr.OnEffectApplied += _ => appliedFired = true;

                mgr.Apply(new FreezeEffect(3f));

                Assert.AreEqual(StatusEffectKind.Freeze, immuneKind);
                Assert.IsFalse(appliedFired,
                    "A refused effect must not also fire the normal applied event.");
            }
            finally { Cleanup(mgr); }
        }

        [Test]
        public void ImmunityIsPerKind_OtherEffectsStillApply()
        {
            var mgr = CreateManager();
            try
            {
                mgr.SetImmunities(new[] { StatusEffectKind.Stun });

                mgr.Apply(new SlowEffect(3f, 0.5f));

                Assert.IsTrue(mgr.HasEffect<SlowEffect>(),
                    "Immunity to Stun must not accidentally block unrelated effects.");
            }
            finally { Cleanup(mgr); }
        }

        [Test]
        public void NoImmunities_IsTheDefault_EveryEffectApplies()
        {
            var mgr = CreateManager();
            try
            {
                mgr.Apply(new StunEffect(5f));
                Assert.IsTrue(mgr.HasEffect<StunEffect>(),
                    "A StatusEffectManager that never calls SetImmunities must behave " +
                    "exactly as before this field existed — immune to nothing.");
            }
            finally { Cleanup(mgr); }
        }

        [Test]
        public void SetImmunities_Null_ClearsToNoImmunities()
        {
            var mgr = CreateManager();
            try
            {
                mgr.SetImmunities(new[] { StatusEffectKind.Poison });

                mgr.SetImmunities(null);
                mgr.Apply(new PoisonEffect(4f));

                Assert.IsTrue(mgr.HasEffect<PoisonEffect>(),
                    "SetImmunities(null) must clear to 'immune to nothing', not throw or " +
                    "leave the previous list in place.");
            }
            finally { Cleanup(mgr); }
        }
    }
}
