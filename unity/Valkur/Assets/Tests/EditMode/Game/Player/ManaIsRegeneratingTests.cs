using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Covers the <see cref="Mana.IsRegenerating"/> gate that drives the
    /// mana-regen visuals (particle aura + silhouette halo).
    ///
    /// Two state machines collapse into the property:
    ///   1) "Pool below max"      — needs <see cref="Mana.TryConsume"/>.
    ///   2) "Regen-delay elapsed" — needs the post-consume grace window
    ///      to have passed since <c>_lastConsumeTime</c>.
    ///
    /// Tests force (2) deterministically by reaching into the private
    /// <c>_lastConsumeTime</c> field via reflection — waiting on real
    /// <see cref="Time.time"/> would either be slow (real wait) or flaky.
    /// </summary>
    public class ManaIsRegeneratingTests
    {
        private GameObject _go;
        private Mana _mana;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ManaTestEntity");
            _mana = _go.AddComponent<Mana>();
            _mana.Initialize(max: 100, regen: 5f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // Push _lastConsumeTime into the past so the regen-delay window is
        // already elapsed, regardless of Mana's regenDelay configuration.
        private void ForceRegenDelayElapsed()
        {
            var field = typeof(Mana).GetField("_lastConsumeTime",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "Mana._lastConsumeTime must exist; visuals depend on it.");
            field.SetValue(_mana, Time.time - 10f);
        }

        [Test]
        public void IsRegenerating_AtFullMana_ReturnsFalse()
        {
            Assert.AreEqual(_mana.MaxMana, _mana.CurrentMana, "Sanity: Initialize fills to max.");
            Assert.IsFalse(_mana.IsRegenerating,
                "At full mana, IsRegenerating must be false — visuals must not light up.");
        }

        [Test]
        public void IsRegenerating_DuringRegenDelay_ReturnsFalse()
        {
            _mana.TryConsume(40);
            // _lastConsumeTime was just set to Time.time, so Time.time - _lastConsumeTime ~ 0,
            // which is well under the 1.5s default regenDelay.
            Assert.IsFalse(_mana.IsRegenerating,
                "Inside the post-cast regen-delay grace window, IsRegenerating must be false.");
        }

        [Test]
        public void IsRegenerating_BelowMaxAndDelayElapsed_ReturnsTrue()
        {
            _mana.TryConsume(40);
            ForceRegenDelayElapsed();

            Assert.Less(_mana.CurrentMana, _mana.MaxMana, "Sanity: TryConsume reduced the pool.");
            Assert.IsTrue(_mana.IsRegenerating,
                "Below max + delay elapsed must drive IsRegenerating true.");
        }

        [Test]
        public void IsRegenerating_AfterRestoreToFull_ReturnsFalse()
        {
            _mana.TryConsume(30);
            ForceRegenDelayElapsed();
            Assert.IsTrue(_mana.IsRegenerating, "Sanity: regenerating before restore.");

            _mana.Restore(1000);

            Assert.AreEqual(_mana.MaxMana, _mana.CurrentMana, "Restore must clamp to max.");
            Assert.IsFalse(_mana.IsRegenerating,
                "Once fully restored, IsRegenerating must drop back to false.");
        }

        [Test]
        public void IsRegenerating_AfterIncreaseMaxMana_RequiresDelayPath()
        {
            // IncreaseMaxMana grows current 1:1 with max so the gap stays zero.
            // IsRegenerating must remain false even with an elapsed delay,
            // because there's nothing to recover.
            ForceRegenDelayElapsed();
            _mana.IncreaseMaxMana(50);

            Assert.AreEqual(_mana.MaxMana, _mana.CurrentMana,
                "IncreaseMaxMana keeps current pinned to the new max.");
            Assert.IsFalse(_mana.IsRegenerating,
                "Growing the cap must not mistakenly trigger the regen visual.");
        }

        [Test]
        public void IsRegenerating_FailedConsume_DoesNotResetDelayWindow()
        {
            // TryConsume(amount > current) must return false and NOT touch
            // _lastConsumeTime — otherwise an out-of-mana attempt would
            // reset the delay timer and stutter the visual.
            _mana.TryConsume(40);
            ForceRegenDelayElapsed();

            bool consumed = _mana.TryConsume(_mana.CurrentMana + 1);

            Assert.IsFalse(consumed, "Cannot consume more than current — guard rail.");
            Assert.IsTrue(_mana.IsRegenerating,
                "Failed consume must leave the delay window intact.");
        }
    }
}
