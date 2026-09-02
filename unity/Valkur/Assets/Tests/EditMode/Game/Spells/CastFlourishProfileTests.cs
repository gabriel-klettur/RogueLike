using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Pins which gesture each kind of spell casts with, and the invariants that make the
    /// nine families READ as nine different things.
    ///
    /// <para>The failure this guards against is silent and gradual: a new spell type is added,
    /// nothing routes it, it falls through to Hurl, and a summoned totem is announced by the
    /// caster throwing something. Nothing errors — it just stops meaning anything, which is
    /// the state the first version of this effect was in when every spell in the game shared
    /// one gesture and differed only in colour.</para>
    /// </summary>
    public class CastFlourishProfileTests
    {
        private static CastFlourishProfile For(SpellType type)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = type.ToString().ToLowerInvariant();
            spell.type = type;
            return CastFlourishProfile.Build(spell);
        }

        [Test]
        public void EachKindOfSpellCastsWithItsOwnGesture()
        {
            Assert.AreEqual("Hurl", For(SpellType.Projectile).FamilyName);
            Assert.AreEqual("Hurl", For(SpellType.Boomerang).FamilyName);
            Assert.AreEqual("Edge", For(SpellType.Slash).FamilyName);
            Assert.AreEqual("Conjure", For(SpellType.Wall).FamilyName);
            Assert.AreEqual("Conjure", For(SpellType.Totem).FamilyName);
            Assert.AreEqual("Conjure", For(SpellType.Summon).FamilyName);
            Assert.AreEqual("Invoke", For(SpellType.Meteor).FamilyName);
            Assert.AreEqual("Invoke", For(SpellType.Lightning).FamilyName);
            Assert.AreEqual("Ward", For(SpellType.Aura).FamilyName);
            Assert.AreEqual("Ward", For(SpellType.SphereMagicShield).FamilyName);
            Assert.AreEqual("Surge", For(SpellType.Dash).FamilyName);
            Assert.AreEqual("Vanish", For(SpellType.Teleport).FamilyName);
            Assert.AreEqual("Channel", For(SpellType.Beam).FamilyName);
            Assert.AreEqual("Channel", For(SpellType.ArcaneFlame).FamilyName);
        }

        [Test]
        public void EveryFamilyIsReachable_AndNoTwoAreTheSameShape()
        {
            var byName = new Dictionary<string, string>();

            foreach (SpellType type in System.Enum.GetValues(typeof(SpellType)))
            {
                var profile = CastFlourishProfile.Build(Spell(type));
                // The shape signature is what the eye actually reads: where the motes come
                // from, where they go, what the circle does, where the light points.
                string signature = profile.Sigil + "/" + profile.Approach + "/" +
                                   profile.Departure + "/" + profile.Lance + "/" + profile.Burst;

                if (byName.TryGetValue(profile.FamilyName, out string existing))
                    Assert.AreEqual(existing, signature,
                        profile.FamilyName + " resolved to two different shapes.");
                else
                    byName[profile.FamilyName] = signature;
            }

            Assert.AreEqual(9, byName.Count, "One of the nine families became unreachable.");

            var signatures = new HashSet<string>(byName.Values);
            Assert.AreEqual(byName.Count, signatures.Count,
                "Two families collapsed onto the same shape and would look identical in play.");
        }

        private static SpellDefinition Spell(SpellType type)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = type.ToString().ToLowerInvariant();
            spell.type = type;
            return spell;
        }

        [Test]
        public void AWardHasNoDirection_BecauseNothingLeavesTheCaster()
        {
            var ward = For(SpellType.Aura);
            Assert.AreEqual(LanceAim.None, ward.Lance,
                "A lance is the piece that says WHERE the spell went; a ward goes nowhere.");
            Assert.AreEqual(MoteDeparture.Linger, ward.Departure);
            Assert.AreEqual(BurstOrigin.Body, ward.Burst, "A ward blooms out of the character.");
            Assert.IsFalse(ward.HandAnchored);
        }

        [Test]
        public void AnImplosionThrowsNothing()
        {
            var vanish = For(SpellType.Teleport);
            Assert.AreEqual(MoteDeparture.PullInward, vanish.Departure);
            Assert.AreEqual(BurstOrigin.None, vanish.Burst,
                "A shockwave leaving is the opposite statement to a caster imploding.");
            Assert.AreEqual(LanceAim.None, vanish.Lance);
        }

        [Test]
        public void ASummonsPointsAtTheSky()
        {
            var invoke = For(SpellType.Meteor);
            Assert.AreEqual(LanceAim.Up, invoke.Lance);
            Assert.AreEqual(MoteApproach.RiseFromGround, invoke.Approach);
            Assert.AreEqual(MoteDeparture.ThrowUp, invoke.Departure);
            Assert.Greater(invoke.Gather, For(SpellType.Slash).Gather * 4f,
                "The answer is coming from far away, so the wind-up is the longest of any family.");
        }

        [Test]
        public void ASwingConjuresNothing()
        {
            var edge = For(SpellType.Slash);
            Assert.AreEqual(SigilMotion.None, edge.Sigil,
                "A circle on the ground says something is being summoned. A cut summons nothing.");
            Assert.Less(edge.Duration, 0.35f,
                "A slash that glows after it lands reads as a spell rather than as a cut.");
        }

        [Test]
        public void ConjuringPushesOutward_AndThrowingDrawsIn()
        {
            // The two circles are the same sprite doing opposite things, and which way it
            // moves is the whole difference between taking power in and putting it down.
            Assert.AreEqual(SigilMotion.Expand, For(SpellType.Wall).Sigil);
            Assert.AreEqual(SigilMotion.Contract, For(SpellType.Projectile).Sigil);
            Assert.AreEqual(SigilMotion.Pulse, For(SpellType.Beam).Sigil,
                "A channel is a hold: its circle breathes instead of resolving.");
        }

        [Test]
        public void FamiliesSizeThemselvesOffTheSpellsOwnData()
        {
            var narrow = ScriptableObject.CreateInstance<SpellDefinition>();
            narrow.type = SpellType.Slash;
            narrow.arcRangeDegrees = 40f;

            var wide = ScriptableObject.CreateInstance<SpellDefinition>();
            wide.type = SpellType.Slash;
            wide.arcRangeDegrees = 300f;

            Assert.Less(CastFlourishProfile.Build(narrow).MoteCount,
                        CastFlourishProfile.Build(wide).MoteCount,
                "A whirl throws sparks all the way round; a thrust barely disturbs the air.");

            var small = ScriptableObject.CreateInstance<SpellDefinition>();
            small.type = SpellType.Aura;
            small.radius = 1.2f;

            var large = ScriptableObject.CreateInstance<SpellDefinition>();
            large.type = SpellType.Aura;
            large.radius = 2.8f;

            Assert.Less(CastFlourishProfile.Build(small).SigilRadius,
                        CastFlourishProfile.Build(large).SigilRadius,
                "A ward's circle is the ward's own radius, not a constant.");
        }

        [Test]
        public void EveryFamilyHasAUsableClock()
        {
            foreach (SpellType type in System.Enum.GetValues(typeof(SpellType)))
            {
                var profile = For(type);
                Assert.Greater(profile.Duration, 0f, type + " has no duration");
                Assert.Greater(profile.Release, 0f, type + " has no release");
                // The tail is Duration - Gather - Release and it divides the falloff, so a
                // family whose beats overran its own duration would fall off in zero seconds.
                Assert.Greater(profile.Duration - profile.Gather - profile.Release, 0.05f,
                    type + ": the gather and release leave no room for the falloff.");
                Assert.Greater(profile.MoteCount, 0, type + " has no motes");
            }
        }
    }
}
