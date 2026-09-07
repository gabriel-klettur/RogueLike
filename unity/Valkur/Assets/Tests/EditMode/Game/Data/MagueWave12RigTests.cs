using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Guards the mague as the wave12 import shipped him.
    ///
    /// He is the first character built from TWO nearly complete loadouts — a bare-handed
    /// wizard and a staff wizard — and the first to use per-state variants, so most of what
    /// this pins is a COMPOSITION rather than a value: which art is base, which is the
    /// loadout, and which rotations belong to which. Each half reads correctly on its own and
    /// only the pairing can be wrong, which is the shape <c>SPAWNER_COORDINATE_SPACE_DRIFT</c>
    /// records and the reason these are asserted together.
    ///
    /// Sprites are matched by NAME, never by <c>AssetDatabase.GetAssetPath</c>: the mague is
    /// packed into <c>characters.spriteatlas</c> (he is baked at 256 px / PPU 96 rather than
    /// the roster's shared 115 / 64, so <c>CharacterAtlasBuilder</c> leaves him to the
    /// remainder group), and a packed sprite's asset path is the EMPTY STRING — a path-based
    /// check here would inspect nothing and report success, which is the vacuous-fixture trap
    /// <c>PlayerFramesManifestBindingTests</c> was already caught by.
    /// The builder names every frame <c>mague_&lt;state folder&gt;_&lt;e|w&gt;&lt;index&gt;</c>,
    /// so the folder is recoverable from the name alone.
    /// </summary>
    public class MagueWave12RigTests
    {
        private const string DefinitionPath = "Assets/_Project/Data/Catalogs/Players/mague.asset";

        private static PlayerDefinition Load()
        {
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(DefinitionPath);
            Assert.IsNotNull(def, $"No PlayerDefinition at {DefinitionPath}.");
            Assert.IsNotNull(def.assetConfig, "mague has no assetConfig.");
            return def;
        }

        /// <summary>The state folder a frame was written into, read back out of its name.</summary>
        private static string FolderOf(Sprite sprite)
        {
            Assert.IsNotNull(sprite, "a null frame reached the shipped asset");
            string n = sprite.name;
            Assert.IsTrue(n.StartsWith("mague_"), $"'{n}' is not a mague frame");
            // Trim the "mague_" head and the "_e3" / "_w12" tail the builder appends.
            string body = n.Substring("mague_".Length);
            int cut = body.LastIndexOf('_');
            Assert.Greater(cut, 0, $"'{n}' carries no facing suffix");
            return body.Substring(0, cut);
        }

        private static HashSet<string> FoldersOf(IEnumerable<Sprite> frames)
            => new HashSet<string>(frames.Select(FolderOf));

        private static void AssertAllFrom(List<Sprite> frames, string folder, string what)
        {
            Assert.IsNotNull(frames, $"{what} has no frames");
            Assert.Greater(frames.Count, 0, $"{what} has no frames");
            // A state is framesPerDirection * 8 — eight contiguous per-direction buckets — and
            // the frame count varies per sheet (the mague's states run 4 to 8 frames each), so
            // the invariant is the DIVISIBILITY, never a particular total.
            Assert.AreEqual(0, frames.Count % 8,
                $"{what} has {frames.Count} frames, which is not a whole number of " +
                "8-direction buckets");
            CollectionAssert.AreEquivalent(new[] { folder }, FoldersOf(frames),
                $"{what} should be drawn entirely from '{folder}'");
        }

        // ---- The base character is the BARE-HANDED wizard --------------------

        [Test]
        public void EveryBaseState_IsFilled_AndDrawnFromItsOwnFolder()
        {
            EntityAssetConfig c = Load().assetConfig;

            // idle is the one slot that is NOT bare-handed, and it is deliberate: the wave
            // contains no unarmed idle at all, and EntityAnimationBinder.ApplyVisuals refuses
            // to bind a character whose idle has no frames. So the staff idle wears the base
            // slot and the staff is visible while the character stands still with the `armed`
            // loadout off. Six frames of a wizard standing still, empty-handed, closes it —
            // and this assertion is what will fail on the day they arrive.
            AssertAllFrom(c.idleSheets, "staff_idle", "base idle");

            AssertAllFrom(c.walkSheets, "walk", "base walk");
            AssertAllFrom(c.chaseSheets, "run", "base chase");
            AssertAllFrom(c.castSheets, "spellcast_1", "base cast");
            AssertAllFrom(c.attackSheets, "punch", "base attack");
            AssertAllFrom(c.damageSheets, "hit", "base damage");
            AssertAllFrom(c.deathSheets, "die", "base death");
            AssertAllFrom(c.recoverSheets, "knockdown_recovery", "base recover");
        }

        [Test]
        public void TheSheetLayout_IsDeclared_NotInferred()
        {
            // Auto infers the layout from the frame COUNT. Every count this pipeline produces
            // happens to fall outside its 4-directional window, and leaning on that
            // coincidence is how a future 12- or 16-frame state binds as 4-directional.
            Assert.AreEqual(EntitySheetDirectionLayout.EightDirectional,
                            Load().assetConfig.directionLayout);
        }

        // ---- Per-state rotations --------------------------------------------

        [Test]
        public void TheAlternateTakes_ShipAsStateVariants()
        {
            EntityAssetConfig c = Load().assetConfig;

            var byState = new Dictionary<string, int>();
            foreach (StateVariantGroup g in c.stateVariants)
                byState[g.state.ToLowerInvariant()] = g.variants.Count;

            // Three walks, two hurts, three deaths — every alternate take in the wave, and the
            // state's OWN sheet is variant 0 rather than an extra. A state that carries
            // variants never renders its base set, so a group listing only the alternates
            // would silently retire the default cycle.
            Assert.AreEqual(3, byState.GetValueOrDefault("walk"), "walk cycles");
            Assert.AreEqual(2, byState.GetValueOrDefault("damage"), "hurt reactions");
            Assert.AreEqual(3, byState.GetValueOrDefault("death"), "deaths");

            // idle and chase draw once and are absent rather than listed with one entry.
            CollectionAssert.IsNotSubsetOf(new[] { "idle", "chase", "recover", "attack", "cast" },
                                           byState.Keys);
        }

        [Test]
        public void StateVariants_NeverNameAttackOrCast()
        {
            // Those two are chosen by an ACTION and live in attackVariants / castVariants.
            // Declaring one here means whichever install ran last wins, silently.
            foreach (StateVariantGroup g in Load().assetConfig.stateVariants)
            {
                string s = g.state.ToLowerInvariant();
                Assert.AreNotEqual("attack", s);
                Assert.AreNotEqual("cast", s);
            }
        }

        [Test]
        public void EveryVariantFrame_BelongsToItsOwnFolder()
        {
            EntityAssetConfig c = Load().assetConfig;
            foreach (StateVariantGroup g in c.stateVariants)
            {
                foreach (StateVariant v in g.variants)
                    AssertAllFrom(v.sheets, v.key, $"{g.state} variant '{v.key}'");
            }
        }

        // ---- The bare-handed rotations --------------------------------------

        [Test]
        public void TheStaffSwing_IsReserved_SoAnOrdinarySwingStaysEmptyHanded()
        {
            EntityAssetConfig c = Load().assetConfig;
            var byKey = c.attackVariants.ToDictionary(v => v.key, v => v);

            CollectionAssert.AreEquivalent(new[] { "punch", "kick", "staff_swing" }, byKey.Keys);

            // punch and kick ARE the unarmed melee, so they rotate. staff_swing plays only for
            // slash_regular — the one slash that runs through AnimState.Attack — because a
            // slash is the action that must never render empty-handed, and because rotating it
            // would make the staff appear on every third bare-handed swing.
            Assert.IsFalse(byKey["punch"].IsReservedForSpell);
            Assert.IsFalse(byKey["kick"].IsReservedForSpell);
            Assert.IsTrue(byKey["staff_swing"].ClaimsSpell("slash_regular"));
        }

        [Test]
        public void TheFiveBareHandedCasts_Rotate_AndTheTwoSpecialsAreReserved()
        {
            EntityAssetConfig c = Load().assetConfig;
            var byKey = c.castVariants.ToDictionary(v => v.key, v => v);

            foreach (string k in new[] { "spell_1", "spell_2", "spell_3", "spell_4", "spell_5" })
            {
                Assert.IsTrue(byKey.ContainsKey(k), $"missing cast variant '{k}'");
                Assert.IsFalse(byKey[k].IsReservedForSpell, $"'{k}' should stay in the rotation");
            }

            // The DRAW. Without this reservation weapon_toggle animates as an ordinary
            // spellcast and the staff appears out of nothing.
            Assert.IsTrue(byKey["armed_equip"].ClaimsSpell("weapon_toggle"));

            // The one sheet in the wave that casts while running, which is what a dash is.
            // Compressed and held for the reason the dwarf's `charge` is: DashExecutor moves
            // the body in a single MovePosition and its wake lasts 0.14 s against eight frames
            // that would otherwise run for 1.2 s.
            Assert.IsTrue(byKey["run_cast"].ClaimsSpell("dash"));
            Assert.Greater(byKey["run_cast"].animationSpeedMultiplier, 1f);
            Assert.IsTrue(byKey["run_cast"].holdLastFrame);
        }

        // ---- The staff loadout ----------------------------------------------

        [Test]
        public void TheArmedLoadout_OverridesEveryStateTheStaffHasArtFor()
        {
            EntityAssetConfig c = Load().assetConfig;
            Assert.AreEqual(1, c.loadouts.Count, "the wave ships exactly one loadout");

            Loadout armed = c.FindLoadout("armed");
            Assert.IsNotNull(armed, "the loadout must be keyed 'armed': weapon_toggle names " +
                                    "that key, and PlayerLoadoutController treats PUTTING a " +
                                    "loadout ON as the draw and taking it OFF as the stow, " +
                                    "which only reads correctly with the staff as the loadout");

            var byState = armed.states.ToDictionary(s => s.state.ToLowerInvariant(), s => s);
            CollectionAssert.AreEquivalent(
                new[] { "idle", "walk", "chase", "cast", "attack" }, byState.Keys);

            AssertAllFrom(byState["idle"].sheets, "staff_idle", "armed idle");
            AssertAllFrom(byState["walk"].sheets, "staff_walk", "armed walk");
            AssertAllFrom(byState["chase"].sheets, "staff_run", "armed chase");
            AssertAllFrom(byState["cast"].sheets, "staff_spellcast", "armed cast");
            AssertAllFrom(byState["attack"].sheets, "staff_attack", "armed attack");

            // damage, death and recover are NOT overridden: no staff art exists for them, and
            // falling back to the base set puts the character in the right POSE with the wrong
            // hands, which is strictly better than the idle pose with the right ones.
            CollectionAssert.DoesNotContain(byState.Keys, "damage");
            CollectionAssert.DoesNotContain(byState.Keys, "death");
            CollectionAssert.DoesNotContain(byState.Keys, "recover");
        }

        [Test]
        public void TheArmedWalk_CarriesItsOwnFourCycles()
        {
            Loadout armed = Load().assetConfig.FindLoadout("armed");
            LoadoutStateSheets walk = armed.states.First(s => s.state == "walk");

            // Four staff cycles, replacing the three bare-handed ones. A rotation that stayed
            // global would put the staff back in his empty hands one step in four.
            Assert.AreEqual(4, walk.variants.Count);
            CollectionAssert.AreEquivalent(
                new[] { "staff_walk", "staff_walk_2", "staff_walk_4", "staff_walk_5" },
                walk.variants.Select(v => v.key).ToArray());
        }

        [Test]
        public void TheArmedLoadout_ReDeclaresTheSheathe()
        {
            Loadout armed = Load().assetConfig.FindLoadout("armed");

            var casts = armed.castVariants.ToDictionary(v => v.key, v => v);
            // The STOW is cast from inside the loadout. A loadout whose cast list forgets
            // weapon_toggle stows the staff to whatever pose the rotation landed on.
            Assert.IsTrue(casts.ContainsKey("armed_equip"), "the armed cast list must re-declare the draw");
            Assert.IsTrue(casts["armed_equip"].ClaimsSpell("weapon_toggle"));

            // Three staff casts rotate, and the staff slash is reserved for the same reason
            // the bare-handed staff_swing is.
            foreach (string k in new[] { "staff_cast_1", "staff_cast_2", "staff_cast_5" })
            {
                Assert.IsTrue(casts.ContainsKey(k), $"missing armed cast '{k}'");
                Assert.IsFalse(casts[k].IsReservedForSpell);
            }
            Assert.IsTrue(casts["staff_slash"].ClaimsSpell("slash"));

            var swings = armed.attackVariants.ToDictionary(v => v.key, v => v);
            CollectionAssert.AreEquivalent(new[] { "staff_swing", "staff_thrust" }, swings.Keys);
        }

        // ---- Nothing is half-imported ---------------------------------------

        [Test]
        public void NoFrameIsNull_AnywhereInTheRig()
        {
            EntityAssetConfig c = Load().assetConfig;
            var lists = new List<(string what, List<Sprite> frames)>
            {
                ("idle", c.idleSheets), ("walk", c.walkSheets), ("chase", c.chaseSheets),
                ("cast", c.castSheets), ("attack", c.attackSheets), ("damage", c.damageSheets),
                ("death", c.deathSheets), ("recover", c.recoverSheets),
            };
            foreach (StateVariantGroup g in c.stateVariants)
                foreach (StateVariant v in g.variants) lists.Add(($"{g.state}/{v.key}", v.sheets));
            foreach (AttackVariant v in c.attackVariants) lists.Add(($"attack/{v.key}", v.sheets));
            foreach (CastVariant v in c.castVariants) lists.Add(($"cast/{v.key}", v.sheets));
            foreach (Loadout l in c.loadouts)
            {
                foreach (LoadoutStateSheets s in l.states)
                {
                    lists.Add(($"{l.key}/{s.state}", s.sheets));
                    foreach (StateVariant v in s.variants) lists.Add(($"{l.key}/{s.state}/{v.key}", v.sheets));
                }
                foreach (AttackVariant v in l.attackVariants) lists.Add(($"{l.key}/attack/{v.key}", v.sheets));
                foreach (CastVariant v in l.castVariants) lists.Add(($"{l.key}/cast/{v.key}", v.sheets));
            }

            var broken = new List<string>();
            foreach ((string what, List<Sprite> frames) in lists)
            {
                if (frames == null || frames.Count == 0) { broken.Add($"{what}: empty"); continue; }
                if (frames.Count % 8 != 0) broken.Add($"{what}: {frames.Count} frames is not 8 buckets");
                for (int i = 0; i < frames.Count; i++)
                {
                    if (frames[i] == null) { broken.Add($"{what}: frame {i} is null"); break; }
                }
            }

            Assert.IsEmpty(broken, string.Join("\n", broken));
        }
    }
}
