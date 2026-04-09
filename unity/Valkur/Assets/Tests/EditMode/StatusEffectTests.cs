using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode
{
    public class StatusEffectTests
    {
        private StatusEffectManager CreateManager()
        {
            var go = new GameObject("TestEntity");
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<SpriteRenderer>();
            return go.AddComponent<StatusEffectManager>();
        }

        private void Cleanup(StatusEffectManager mgr)
        {
            Object.DestroyImmediate(mgr.gameObject);
        }

        // --- StunEffect ---

        [Test]
        public void StunEffect_Apply_SetsHasEffect()
        {
            var mgr = CreateManager();
            mgr.Apply(new StunEffect(5f));
            Assert.IsTrue(mgr.HasEffect<StunEffect>());
            Assert.IsTrue(mgr.IsStunned);
            Cleanup(mgr);
        }

        [Test]
        public void StunEffect_Remove_ClearsEffect()
        {
            var mgr = CreateManager();
            mgr.Apply(new StunEffect(5f));
            mgr.Remove<StunEffect>();
            Assert.IsFalse(mgr.HasEffect<StunEffect>());
            Assert.IsFalse(mgr.IsStunned);
            Cleanup(mgr);
        }

        // --- SlowEffect ---

        [Test]
        public void SlowEffect_Apply_SetsHasEffect()
        {
            var mgr = CreateManager();
            mgr.Apply(new SlowEffect(3f, 0.5f));
            Assert.IsTrue(mgr.HasEffect<SlowEffect>());
            Cleanup(mgr);
        }

        [Test]
        public void SlowEffect_SlowFactor_IsCorrect()
        {
            var slow = new SlowEffect(2f, 0.3f);
            Assert.AreEqual(0.3f, slow.SlowFactor, 0.001f);
        }

        [Test]
        public void SlowEffect_Remove_ClearsEffect()
        {
            var mgr = CreateManager();
            mgr.Apply(new SlowEffect(3f));
            mgr.Remove<SlowEffect>();
            Assert.IsFalse(mgr.HasEffect<SlowEffect>());
            Cleanup(mgr);
        }

        // --- FreezeEffect ---

        [Test]
        public void FreezeEffect_Apply_SetsHasEffect()
        {
            var mgr = CreateManager();
            mgr.Apply(new FreezeEffect(4f));
            Assert.IsTrue(mgr.HasEffect<FreezeEffect>());
            Cleanup(mgr);
        }

        [Test]
        public void FreezeEffect_Remove_ClearsEffect()
        {
            var mgr = CreateManager();
            mgr.Apply(new FreezeEffect(4f));
            mgr.Remove<FreezeEffect>();
            Assert.IsFalse(mgr.HasEffect<FreezeEffect>());
            Cleanup(mgr);
        }

        // --- ClearAll ---

        [Test]
        public void ClearAll_RemovesAllEffects()
        {
            var mgr = CreateManager();
            mgr.Apply(new StunEffect(5f));
            mgr.Apply(new SlowEffect(3f));
            mgr.Apply(new FreezeEffect(4f));
            mgr.ClearAll();
            Assert.IsFalse(mgr.HasEffect<StunEffect>());
            Assert.IsFalse(mgr.HasEffect<SlowEffect>());
            Assert.IsFalse(mgr.HasEffect<FreezeEffect>());
            Cleanup(mgr);
        }

        // --- Replace semantics ---

        [Test]
        public void Apply_SameType_ReplacesExisting()
        {
            var mgr = CreateManager();
            mgr.Apply(new SlowEffect(3f, 0.5f));
            mgr.Apply(new SlowEffect(5f, 0.7f));
            Assert.IsTrue(mgr.HasEffect<SlowEffect>());
            // Only one effect of each type
            var snapshot = mgr.GetSnapshot();
            int slowCount = 0;
            foreach (var s in snapshot)
                if (s.typeName == "SlowEffect") slowCount++;
            Assert.AreEqual(1, slowCount);
            Cleanup(mgr);
        }

        // --- Mixed effects coexist ---

        [Test]
        public void DifferentEffects_Coexist()
        {
            var mgr = CreateManager();
            mgr.Apply(new StunEffect(5f));
            mgr.Apply(new SlowEffect(3f));
            mgr.Apply(new FreezeEffect(4f));
            Assert.IsTrue(mgr.HasEffect<StunEffect>());
            Assert.IsTrue(mgr.HasEffect<SlowEffect>());
            Assert.IsTrue(mgr.HasEffect<FreezeEffect>());
            Cleanup(mgr);
        }
    }
}
