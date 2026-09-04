using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The shipped data for the 27-spell expansion.
    ///
    /// <para>Every assertion here guards one specific way this project has previously shipped
    /// a spell that looked correct in the asset and was wrong on screen: a length authored in
    /// the Python build's pixel scale (five prior sightings), a <c>range</c> left at zero so
    /// <c>Projectile</c>'s default of 20 silently truncated the flight (two prior sightings), a
    /// <c>particleColor</c> left on the opaque-white "nobody authored this" sentinel (ten
    /// spells still are), and a persistent field whose cooldown is shorter than its own
    /// duration, which turns a zoning tool into permanent area denial (three prior sightings).</para>
    ///
    /// <para>It reads the SHIPPED assets rather than the seed table, because a table that
    /// agrees with itself proves nothing about what is on disk.</para>
    /// </summary>
    public class SpellExpansionDataTests
    {
        private const string SpellFolder = "Assets/_Project/Data/Catalogs/Spells";

        /// <summary>The 27 keys this expansion adds. Named literally so a spell quietly
        /// dropped from the seeder is a red test rather than a smaller loop.</summary>
        private static readonly string[] ExpansionKeys =
        {
            "frost_nova", "ice_lance", "glacial_step", "frozen_ward", "blizzard",
            "thorn_burst", "entangle", "barkskin", "spore_cloud", "summon_wolf",
            "shadow_step", "void_lance", "curse_of_frailty", "raise_thrall",
            "radiant_burst", "blessing", "sanctuary", "guardian_light",
            "seeking_shard", "thunderclap", "static_field",
            "scatter_volley", "war_cry", "leap_slam",
            "charged_bolt", "cinder_trail",
            "arcane_barrier",
        };

        private static SpellDefinition Load(string key)
            => AssetDatabase.LoadAssetAtPath<SpellDefinition>($"{SpellFolder}/{key}.asset");

        private static IEnumerable<SpellDefinition> All()
            => ExpansionKeys.Select(Load).Where(s => s != null);

        [Test]
        public void AllTwentySeven_ExistOnDisk()
        {
            var missing = ExpansionKeys.Where(k => Load(k) == null).ToList();
            Assert.IsEmpty(missing,
                "Missing expansion spell assets: " + string.Join(", ", missing) +
                ". Run Valkur > Spells > Seed Expansion Spells.");
        }

        [Test]
        public void EveryOne_AuthorsItsParticleColour()
        {
            foreach (var s in All())
            {
                // Opaque white is the project's "nobody authored this" sentinel, tested
                // BEFORE saturation everywhere it is read: a real grey is a deliberate
                // request for the absence of colour, and checking saturation first catches
                // white in the grey branch.
                Assert.IsFalse(KiPalette.IsUnauthored(s.particleColor),
                    $"'{s.spellKey}' leaves particleColor on the unauthored sentinel. Ten " +
                    "shipped spells are still in that state and this expansion must not add " +
                    "to them -- the swatch drives the cast flourish, the buff aura and every " +
                    "procedural rig.");
            }
        }

        [Test]
        public void EveryRangedSpell_AuthorsItsRange()
        {
            foreach (var s in All())
            {
                bool needsRange = s.type == SpellType.Projectile
                               || s.type == SpellType.Teleport
                               || s.spawnAtMouse;
                if (!needsRange) continue;

                Assert.Greater(s.range, 0f,
                    $"'{s.spellKey}' leaves range at 0. Projectile.range then falls back to " +
                    "20, which silently cut the boomerang's entire return leg and the " +
                    "firework's whole flight.");
            }
        }

        [Test]
        public void NothingIsAuthoredInThePythonPixelScale()
        {
            // The camera is 33.33 x 16.67 world units. Anything past 20 on a size or a reach
            // is a pixel value that escaped -- the tell in all five prior sightings was a
            // number tens of times larger than the screen it had to fit on.
            const float Ceiling = 20f;

            foreach (var s in All())
            {
                Assert.LessOrEqual(s.radius, Ceiling, $"'{s.spellKey}'.radius");
                Assert.LessOrEqual(s.range, Ceiling, $"'{s.spellKey}'.range");
                Assert.LessOrEqual(s.wallWidth, Ceiling, $"'{s.spellKey}'.wallWidth");
                Assert.LessOrEqual(s.wallHeight, Ceiling, $"'{s.spellKey}'.wallHeight");
                Assert.LessOrEqual(s.coneLength, Ceiling, $"'{s.spellKey}'.coneLength");
                Assert.LessOrEqual(s.distance, Ceiling, $"'{s.spellKey}'.distance");
            }
        }

        [Test]
        public void EveryPersistentField_HasACooldownLongerThanItsOwnDuration()
        {
            foreach (var s in All())
            {
                if (s.maxInstances != 1 || s.duration <= 0f) continue;
                // A buff is exempt: it is on the CASTER, so a recast that refreshes it early
                // is a mana cost, not free area denial.
                if (s.type == SpellType.Buff) continue;

                Assert.Greater(s.cooldownDuration, s.duration,
                    $"'{s.spellKey}' runs {s.duration}s on a {s.cooldownDuration}s cooldown " +
                    "with maxInstances 1, so the player always has one out AND can evict " +
                    "their own to reposition it. That is permanent area denial -- recorded " +
                    "three times already (both vortices and arcane_flame).");
            }
        }

        [Test]
        public void EveryOne_IsPlayerAudience()
        {
            foreach (var s in All())
                Assert.IsTrue(s.audience.HasFlag(SpellAudience.Player),
                    $"'{s.spellKey}' is not tagged as player content, so it will not appear " +
                    "in the F4 editor's player tab.");
        }

        // ── The mechanics each one is supposed to demonstrate ────────────────

        [Test]
        public void ThePiercingSpells_ActuallyPierce()
        {
            foreach (var key in new[] { "ice_lance", "void_lance" })
            {
                var s = Load(key);
                if (s == null) continue;
                Assert.Greater(s.pierceCount, 0,
                    $"'{key}' exists to demonstrate piercing and authors none, which would " +
                    "make it an ordinary projectile with a different name.");
            }
        }

        [Test]
        public void TheSeekingShard_AuthorsBOTHHomingFields()
        {
            var s = Load("seeking_shard");
            if (s == null) return;

            Assert.Greater(s.homingStrength, 0f, "seeking_shard.homingStrength");
            Assert.Greater(s.homingRange, 0f,
                "A turn rate with no acquisition radius finds nothing and flies straight, " +
                "which looks like the field not working rather than like a spell that missed.");
        }

        [Test]
        public void TheVolley_FiresMoreThanOneShotAcrossARealFan()
        {
            var s = Load("scatter_volley");
            if (s == null) return;

            Assert.Greater(s.projectileCount, 1, "scatter_volley.projectileCount");
            Assert.Greater(s.spreadDegrees, 0f,
                "Five shots at zero spread is one shot drawn five times.");
        }

        [Test]
        public void TheChargedBolt_IsChargeableAndActuallyRewardsTheHold()
        {
            var s = Load("charged_bolt");
            if (s == null) return;

            Assert.IsTrue(s.IsChargeable, "charged_bolt must author chargeMaxSeconds.");
            Assert.Greater(s.chargeDamageMultiplier, 1f,
                "A charge that does not raise the damage is a delay.");
            Assert.Less(s.chargeMinFraction, 1f,
                "A minimum fraction of 1 means a snap cast is identical to a full hold, " +
                "which removes the decision the mechanic exists for.");
        }

        [Test]
        public void EveryBuff_AuthorsModifiersAndADuration()
        {
            foreach (var s in All())
            {
                if (s.type != SpellType.Buff) continue;

                Assert.IsNotNull(s.statModifiers, $"'{s.spellKey}'.statModifiers");
                Assert.IsNotEmpty(s.statModifiers,
                    $"'{s.spellKey}' is a Buff with no modifiers, so the cast would spend " +
                    "mana, play its flourish and change nothing -- the authored-and-inert " +
                    "failure this project has recorded eleven times.");
                Assert.Greater(s.duration, 0f,
                    $"'{s.spellKey}' has no duration. TimedBuffSource refuses that silently, " +
                    "because a permanent stat change belongs in a layer with an owner who can " +
                    "remove it and the Buff layer's owner is a clock.");
            }
        }

        [Test]
        public void RaiseThrall_MarksRatherThanDamages()
        {
            var s = Load("raise_thrall");
            if (s == null) return;

            Assert.AreEqual(0f, s.damage,
                "The bolt is a claim on a death, not a source of one. Damage on it would " +
                "make the spell able to land the very kill it is betting on, which is a " +
                "different and much weaker design.");

            var marked = s.statusApplications?.FirstOrDefault(a => a.type == StatusEffectKind.Marked);
            Assert.IsNotNull(s.statusApplications);
            Assert.IsTrue(s.statusApplications.Any(a => a.type == StatusEffectKind.Marked),
                "raise_thrall must apply StatusEffectKind.Marked or it does nothing at all.");
            Assert.Greater(marked.Value.duration, 0f, "The mark needs a window.");
            Assert.Greater(marked.Value.magnitude, 0f,
                "magnitude is how long the raised thrall SERVES -- a second clock beside the " +
                "mark's own window.");
        }

        [Test]
        public void TheArcaneBarrier_StopsShotsAndLetsBodiesThrough()
        {
            var s = Load("arcane_barrier");
            if (s == null) return;

            Assert.IsTrue(s.blockProjectiles, "It is cover or it is nothing.");
            Assert.IsFalse(s.blockUnits,
                "Blocking units too would make it wall_ice with a different colour; the " +
                "inverse is the whole point of the spell.");
            Assert.Greater(s.wallWidth, 1f,
                "wall_ice shipped 12.5 x 3.125 against an executor dividing by 32 and " +
                "resolved to 0.78 u by 0.049 u. These are WORLD units.");
        }
    }
}
