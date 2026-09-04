using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the arithmetic every blow in the world goes through, whether it arrived as a
    /// combat swing or as a shift at a rock face.
    ///
    /// <para>The point of the resolver is that there is ONE answer to "what did that blow
    /// amount to". These tests are what stops the two callers drifting apart again: they
    /// exercise the resolver directly, so a change that only fixes one call site fails here.</para>
    /// </summary>
    [TestFixture]
    public class HarvestBlowResolverTests
    {
        private DestructionResistanceTable _table;

        [SetUp]
        public void SetUp()
        {
            _table = ScriptableObject.CreateInstance<DestructionResistanceTable>();
            _table.SeedShippedMatrix();
            HarvestBlowResolver.OverrideTable(_table);
        }

        [TearDown]
        public void TearDown()
        {
            // Restore the load-from-Resources path, or every later fixture in the run would
            // silently measure against this synthetic matrix.
            HarvestBlowResolver.OverrideTable(null);
            if (_table != null) Object.DestroyImmediate(_table);
        }

        private static DestructionProfile Profile(MaterialClass material, int requiredTier,
            float chipFraction)
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            profile.material = material;
            profile.requiredToolTier = requiredTier;
            profile.chipDamageFraction = chipFraction;
            return profile;
        }

        // Scale ----------------------------------------------------------------------

        [Test]
        public void Scale_NeverRoundsARealMultiplierDownToZero()
        {
            // Bare hands against wood is 0.10 in the shipped matrix, so a 5-damage blow floors
            // to 0. Letting it stay 0 makes the material read as unbreakable rather than as
            // hard, and the player concludes the thing cannot be chopped at all.
            Assert.That(HarvestBlowResolver.Scale(5, 0.10f), Is.EqualTo(1));
            Assert.That(HarvestBlowResolver.Scale(1, 0.02f), Is.EqualTo(1));
        }

        [Test]
        public void Scale_TreatsAZeroMultiplierAsDeliberateImmunity()
        {
            Assert.That(HarvestBlowResolver.Scale(1000, 0f), Is.EqualTo(0));
        }

        [Test]
        public void Scale_IsProportionalOnceItClearsTheFloor()
        {
            Assert.That(HarvestBlowResolver.Scale(100, 0.45f), Is.EqualTo(45));
            Assert.That(HarvestBlowResolver.Scale(40, 1.40f), Is.EqualTo(56));
        }

        [Test]
        public void Scale_RefusesANonPositiveAmount()
        {
            Assert.That(HarvestBlowResolver.Scale(0, 1f), Is.EqualTo(0));
            Assert.That(HarvestBlowResolver.Scale(-5, 1f), Is.EqualTo(0));
        }

        // Resolve --------------------------------------------------------------------

        [Test]
        public void Resolve_BareHandedAttackerIsTheNoneClass()
        {
            var profile = Profile(MaterialClass.Stone, requiredTier: 0, chipFraction: 0.15f);
            var attacker = new GameObject("Attacker");
            try
            {
                var blow = HarvestBlowResolver.Resolve(profile, attacker, element: null);
                Assert.That(blow.DamageClass, Is.EqualTo(DamageClass.None));
                Assert.That(blow.ToolTier, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(attacker);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Resolve_MagicIsNeverTierGated()
        {
            // There is no such thing as the wrong tier of fireball. A profile demanding a
            // tier-3 tool must still burn if it burns at all, or "requires a better pick"
            // silently becomes "requires a better pick to cast fire at".
            var profile = Profile(MaterialClass.Wood, requiredTier: 3, chipFraction: 0f);
            try
            {
                var blow = HarvestBlowResolver.Resolve(profile, null, SpellElement.Fire);
                Assert.That(blow.DamageClass, Is.EqualTo(DamageClass.Fire));
                Assert.That(blow.WrongTool, Is.False);
                Assert.That(blow.Immune, Is.False,
                    "Fire is 1.40 against wood in the shipped matrix; the tier gate must not touch it.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Resolve_APhysicalBlowBelowTheRequiredTierIsChipped()
        {
            var profile = Profile(MaterialClass.Stone, requiredTier: 1, chipFraction: 0.15f);
            var attacker = new GameObject("Attacker");
            try
            {
                var blow = HarvestBlowResolver.Resolve(profile, attacker, element: null);
                Assert.That(blow.WrongTool, Is.True);

                // Stone vs bare hands is 0.02; the chip fraction multiplies it, it does not
                // replace it. Both halves have to be applied or a tier gate on a material the
                // tool cannot touch anyway would read as generous.
                Assert.That(blow.Multiplier, Is.LessThan(0.02f).And.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(attacker);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Resolve_AZeroChipFractionMakesTheWrongToolTrulyImmune()
        {
            var profile = Profile(MaterialClass.Stone, requiredTier: 2, chipFraction: 0f);
            var attacker = new GameObject("Attacker");
            try
            {
                var blow = HarvestBlowResolver.Resolve(profile, attacker, element: null);
                Assert.That(blow.Immune, Is.True);
                Assert.That(HarvestBlowResolver.Scale(999, blow.Multiplier), Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(attacker);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Resolve_ANullProfileIsInertRatherThanThrowing()
        {
            var blow = HarvestBlowResolver.Resolve(null, null, element: null);
            Assert.That(blow.Immune, Is.True);
        }
    }
}
