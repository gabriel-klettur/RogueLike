using System.Collections.Generic;
using NUnit.Framework;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// The contract of <see cref="PlayerStance"/> itself: the default, the transition event,
    /// and the fact that a redundant set is silent.
    ///
    /// <para>The default matters more than it looks. War is what makes the whole feature
    /// additive — a fresh session behaves exactly as it did before the stance existed, so a
    /// regression hunt never has to suspect it. A default of Peace would mean a new player
    /// cannot attack and has nothing on screen explaining why.</para>
    /// </summary>
    [TestFixture]
    public class PlayerStanceTests
    {
        [SetUp]
        public void SetUp() => Reset();

        [TearDown]
        public void TearDown() => Reset();

        /// <summary>
        /// Domain Reload is off, so the stance and its subscriber list both survive between
        /// fixtures inside one Editor session. Without this a test that leaves Peace behind
        /// fails an unrelated fixture later, for a reason nothing in its name mentions.
        /// </summary>
        private static void Reset()
        {
            PlayerStance.ResetForTests();
        }

        [Test]
        public void Default_IsWar()
        {
            Assert.AreEqual(Stance.War, PlayerStance.Current);
            Assert.IsTrue(PlayerStance.IsWar);
            Assert.IsFalse(PlayerStance.IsPeace);
        }

        [Test]
        public void Toggle_AlternatesBothWays()
        {
            PlayerStance.Toggle();
            Assert.AreEqual(Stance.Peace, PlayerStance.Current);
            Assert.IsTrue(PlayerStance.IsPeace);

            PlayerStance.Toggle();
            Assert.AreEqual(Stance.War, PlayerStance.Current);
            Assert.IsTrue(PlayerStance.IsWar);
        }

        [Test]
        public void OnChanged_FiresOnTransitionsOnly()
        {
            var seen = new List<Stance>();
            PlayerStance.OnChanged += seen.Add;

            PlayerStance.Set(Stance.War);    // no-op, already War
            PlayerStance.Set(Stance.Peace);
            PlayerStance.Set(Stance.Peace);  // no-op
            PlayerStance.Set(Stance.War);

            CollectionAssert.AreEqual(new[] { Stance.Peace, Stance.War }, seen);
        }

        /// <summary>
        /// The handler must see the NEW value already committed. PlayerController's
        /// OnStanceChanged cuts a live beam and a held charge off the back of this event, and
        /// anything reading Current from inside it while the field still held the old stance
        /// would take the opposite branch.
        /// </summary>
        [Test]
        public void OnChanged_ObservesCurrentAlreadyUpdated()
        {
            Stance observed = Stance.War;
            PlayerStance.OnChanged += _ => observed = PlayerStance.Current;

            PlayerStance.Set(Stance.Peace);

            Assert.AreEqual(Stance.Peace, observed);
        }
    }
}
