using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Gameplay.Enemies
{
    /// <summary>
    /// <see cref="SpawnBrain"/> — what the ENCOUNTER says about a spawn's behaviour, as
    /// opposed to what its shared <c>MonsterDefinition</c> says.
    ///
    /// <para>A spawner used to be able to influence exactly two facts about what it produced,
    /// <c>persistent</c> and the resolved level, because everything else came off the
    /// definition. Writing a leash or an FSM set onto that definition to make one camp defend
    /// its point would change every monster of that kind in the world — and, since Unity keeps
    /// a ScriptableObject edited in Play Mode until the next domain reload, would follow the
    /// author back into the Editor.</para>
    /// </summary>
    [TestFixture]
    public class SpawnBrainTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("spawned");

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        /// <summary>
        /// The property every legacy spawn path depends on: the console's <c>spawn</c>
        /// command, hand-placed entities, the summon executor, boss adds and every test
        /// double attach nothing and behave exactly as they did.
        /// </summary>
        [Test]
        public void NeutralValues_AttachNothing()
        {
            SpawnBrain.Stamp(_go, null, 0f);
            Assert.That(_go.GetComponent<SpawnBrain>(), Is.Null,
                "A component whose presence means 'no change' is a component whose presence " +
                "means nothing — and it costs a GetComponent on every monster in the world.");

            SpawnBrain.Stamp(_go, "", 0f);
            SpawnBrain.Stamp(_go, "   ", -1f);
            Assert.That(_go.GetComponent<SpawnBrain>(), Is.Null);
        }

        [Test]
        public void AnEmptyStamp_ReadsBackAsNoOverride()
        {
            Assert.That(SpawnBrain.FsmSetOf(_go), Is.Null);
            Assert.That(SpawnBrain.LeashOf(_go), Is.EqualTo(0f));
        }

        [Test]
        public void AnFsmSet_IsStampedAndReadBack()
        {
            SpawnBrain.Stamp(_go, "Monster_Caster", 0f);

            Assert.That(_go.GetComponent<SpawnBrain>(), Is.Not.Null);
            Assert.That(SpawnBrain.FsmSetOf(_go), Is.EqualTo("Monster_Caster"));
            Assert.That(SpawnBrain.LeashOf(_go), Is.EqualTo(0f));
        }

        [Test]
        public void ALeash_IsStampedAndReadBack()
        {
            SpawnBrain.Stamp(_go, null, 6.5f);

            Assert.That(SpawnBrain.FsmSetOf(_go), Is.Null,
                "An absent set must read as null, not as an empty string a resolver would try.");
            Assert.That(SpawnBrain.LeashOf(_go), Is.EqualTo(6.5f).Within(1e-4f));
        }

        [Test]
        public void TheSetIsTrimmed()
        {
            // Whitespace round an FSM set name is invisible in an input field and turns a
            // valid set into one that warns and falls through.
            SpawnBrain.Stamp(_go, "  Monster_Boss  ", 0f);

            Assert.That(SpawnBrain.FsmSetOf(_go), Is.EqualTo("Monster_Boss"));
        }

        [Test]
        public void RestampingReplacesRatherThanAccumulating()
        {
            SpawnBrain.Stamp(_go, "Monster_Caster", 6f);
            SpawnBrain.Stamp(_go, "Monster_Boss", 9f);

            Assert.That(_go.GetComponents<SpawnBrain>().Length, Is.EqualTo(1));
            Assert.That(SpawnBrain.FsmSetOf(_go), Is.EqualTo("Monster_Boss"));
            Assert.That(SpawnBrain.LeashOf(_go), Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void ReadersAreNullSafe()
        {
            Assert.That(SpawnBrain.FsmSetOf(null), Is.Null);
            Assert.That(SpawnBrain.LeashOf(null), Is.EqualTo(0f));
            Assert.DoesNotThrow(() => SpawnBrain.Stamp(null, "Monster_Boss", 1f));
        }
    }
}
