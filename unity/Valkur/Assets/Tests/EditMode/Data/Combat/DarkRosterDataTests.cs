using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.Combat
{
    /// <summary>
    /// The six DARK hostiles, and the three ways this feature can rot without anything failing.
    ///
    /// <para>ONE: THE ART DRIFTS. A dark twin wears its player class's animation, copied at
    /// generation time. Every wave of <c>build_player_frames.py</c> plus
    /// <c>PlayerFramesImporter</c> rewrites that class's <c>EntityAssetConfig</c> — and the
    /// copy does not follow. The monster goes on rendering a set of frames the class no longer
    /// has: nothing throws, nothing logs, the enemy simply stops being the same character.
    /// <see cref="EveryDarkEntity_WearsItsSourceClassCurrentArt"/> compares them sprite for
    /// sprite, so the drift is a red test rather than something noticed a wave later.</para>
    ///
    /// <para>TWO: A SPELL KEY GOES STALE. <c>EntitySetup.ConfigureMonsterAutoCast</c> skips an
    /// unresolved key with a warning and wires the rest, so a renamed spell costs the monster
    /// one of its three answers and leaves it looking merely passive.</para>
    ///
    /// <para>THREE: THE FSM SET AND THE ASSETS DISAGREE. <c>fsmSet</c> is a STRING, matched at
    /// load against <c>StreamingAssets/FSM/sets.json</c>. A typo, or a set deleted from the FSM
    /// editor, silently falls the monster through to the hard-coded IdleState — a hostile that
    /// stands still forever with perfectly correct-looking data. And the set must actually
    /// declare DodgeState, because the allowed-state whitelist is the structural half of the
    /// dodge gate: without it every one of these entities is tuned to dodge and cannot.</para>
    /// </summary>
    public class DarkRosterDataTests
    {
        private const string DarkDir = "Assets/_Project/Data/Catalogs/Monsters/Dark";
        private const string CatalogPath = "Assets/_Project/Data/Catalogs/Monsters/MonsterCatalog.asset";
        private const string PlayersDir = "Assets/_Project/Data/Catalogs/Players";
        private const string SpellCatalogPath = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const string SetsJson = "StreamingAssets/FSM/sets.json";
        private const string FsmSet = "Monster_Dark";

        private static readonly string[] SourceClasses =
            { "dwarf", "barbarian", "elven", "mague", "valkyrie", "vampire" };

        private static List<MonsterDefinition> _dark;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            _dark = AssetDatabase.FindAssets("t:MonsterDefinition", new[] { DarkDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null)
                .OrderBy(m => m.monsterKey)
                .ToList();
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) + "/Assets";

        private static MonsterDefinition Dark(string playerKey)
            => _dark.FirstOrDefault(m => m.monsterKey == "dark_" + playerKey);

        private static PlayerDefinition Source(string playerKey)
            => AssetDatabase.LoadAssetAtPath<PlayerDefinition>($"{PlayersDir}/{playerKey}.asset");

        // ── Existence and identity ───────────────────────────────────────────────

        [Test]
        public void EveryPlayableClass_HasADarkTwin()
        {
            foreach (var key in SourceClasses)
                Assert.IsNotNull(Dark(key),
                    $"dark_{key} is missing. Re-run Valkur > Monsters > Build Dark Roster. " +
                    "The roster is generated FROM the player classes, so a new class silently " +
                    "has no dark counterpart until somebody runs it.");
        }

        [Test]
        public void EveryDarkEntity_IsInTheMonsterCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "MonsterCatalog missing.");

            foreach (var m in _dark)
                Assert.IsNotNull(catalog.GetByKey(m.monsterKey),
                    $"{m.monsterKey} exists on disk but is not in the catalog, so nothing in " +
                    "the game can spawn it — not the spawners, not the Entities editor, not " +
                    "the `spawn` console command. An asset nobody can reach is not content.");
        }

        [Test]
        public void EveryDarkEntity_IsHostile()
        {
            foreach (var m in _dark)
                Assert.AreEqual("EVIL", m.stats.faction,
                    $"{m.monsterKey}: the faction string gates the loot roll in DeathDropSystem. " +
                    "It is NOT what makes the entity fight — no state class reads it — which is " +
                    "exactly why it is easy to leave wrong.");
        }

        // ── The tint ─────────────────────────────────────────────────────────────

        [Test]
        public void EveryDarkEntity_IsTintedBlack_WithAnOpaqueAlpha()
        {
            foreach (var m in _dark)
            {
                var tint = m.assetConfig.scaleConfig.tint;
                Assert.That(tint.r, Is.EqualTo(0f).Within(0.001f), $"{m.monsterKey} red");
                Assert.That(tint.g, Is.EqualTo(0f).Within(0.001f), $"{m.monsterKey} green");
                Assert.That(tint.b, Is.EqualTo(0f).Within(0.001f), $"{m.monsterKey} blue");

                // The alpha is the half that is easy to get wrong and impossible to see in the
                // Inspector's swatch: EntityAnimationBinder reads (0,0,0,0) as "nobody authored
                // a tint" and substitutes WHITE, so an alpha-zero black is not a dark entity,
                // it is an untinted one.
                Assert.That(tint.a, Is.GreaterThan(0.99f),
                    $"{m.monsterKey}: an alpha-zero black is read as 'no tint' and renders as " +
                    "the ordinary player character.");
            }
        }

        [Test]
        public void TheTintIsAnLdrValue_SoTheHitFlashSurvivesIt()
        {
            // Above 1 on any channel, EntityAnimationBinder routes the tint into the material's
            // _Color instead of onto the renderer — and that product is downstream of
            // SpriteTintStack, so a near-black there annihilates every flash, burn, freeze and
            // death fade. The design only works because black is LDR.
            foreach (var m in _dark)
            {
                var t = m.assetConfig.scaleConfig.tint;
                Assert.That(Mathf.Max(t.r, Mathf.Max(t.g, t.b)), Is.LessThanOrEqualTo(1f),
                    $"{m.monsterKey}: an HDR tint takes the material path and would make every " +
                    "combat feedback effect on this entity invisible.");
            }
        }

        // ── The art ──────────────────────────────────────────────────────────────

        [Test]
        public void EveryDarkEntity_WearsItsSourceClassCurrentArt()
        {
            foreach (var key in SourceClasses)
            {
                var dark = Dark(key);
                var player = Source(key);
                if (dark == null || player == null) continue;

                AssertSameSprites($"dark_{key}.idle", player.assetConfig.idleSheets, dark.assetConfig.idleSheets);
                AssertSameSprites($"dark_{key}.walk", player.assetConfig.walkSheets, dark.assetConfig.walkSheets);
                AssertSameSprites($"dark_{key}.chase", player.assetConfig.chaseSheets, dark.assetConfig.chaseSheets);
                AssertSameSprites($"dark_{key}.attack", player.assetConfig.attackSheets, dark.assetConfig.attackSheets);
                AssertSameSprites($"dark_{key}.cast", player.assetConfig.castSheets, dark.assetConfig.castSheets);
                AssertSameSprites($"dark_{key}.death", player.assetConfig.deathSheets, dark.assetConfig.deathSheets);

                Assert.AreEqual(player.assetConfig.attackVariants.Count,
                                dark.assetConfig.attackVariants.Count,
                    $"dark_{key}: attack variants drifted from the player class. The moveset is " +
                    "what AttackState weighs and gates, so a stale copy is a different fight.");
                Assert.AreEqual(player.assetConfig.castVariants.Count,
                                dark.assetConfig.castVariants.Count,
                    $"dark_{key}: cast variants drifted from the player class.");
            }
        }

        private static void AssertSameSprites(string what, List<Sprite> expected, List<Sprite> actual)
        {
            int e = expected != null ? expected.Count : 0;
            int a = actual != null ? actual.Count : 0;
            Assert.AreEqual(e, a, $"{what}: frame count drifted ({e} on the class, {a} here). " +
                                  "Re-run Valkur > Monsters > Build Dark Roster.");
            for (int i = 0; i < e; i++)
                Assert.AreSame(expected[i], actual[i], $"{what}: frame {i} is a different sprite.");
        }

        [Test]
        public void EveryDarkEntity_HasSomethingToRender()
        {
            foreach (var m in _dark)
                Assert.That(m.assetConfig.idleSheets, Is.Not.Null.And.Not.Empty,
                    $"{m.monsterKey} has no idle frames, so EntityAnimationBinder binds nothing " +
                    "and the entity spawns as an invisible collider that can still kill you.");
        }

        // ── The AI ───────────────────────────────────────────────────────────────

        [Test]
        public void EveryDarkEntity_UsesTheDarkFsmSet()
        {
            foreach (var m in _dark)
                Assert.AreEqual(FsmSet, m.fsmSet,
                    $"{m.monsterKey}: fsmSet is a STRING resolved against sets.json. A mismatch " +
                    "falls the monster through to the hard-coded IdleState — a hostile that " +
                    "stands still forever, with data that reads correctly everywhere.");
        }

        [Test]
        public void TheDarkFsmSet_ExistsAndDeclaresTheStatesItsBehaviourNeeds()
        {
            string json = File.ReadAllText(ProjectRoot + "/" + SetsJson);

            Assert.That(json, Does.Contain("\"" + FsmSet + "\""),
                $"{FsmSet} is missing from sets.json, so every dark entity boots into IdleState.");

            // The whitelist is the structural half of the dodge gate and of the cast path: a set
            // that does not name a state can never enter it, however the monster is tuned.
            foreach (var state in new[] { "DodgeState", "NPCCastState", "ChaseState",
                                          "AttackState", "AlertChaseState", "SearchState" })
                Assert.That(json, Does.Contain("\"" + state + "\""),
                    $"{state} must be declared somewhere in sets.json for {FsmSet} to use it.");
        }

        [Test]
        public void EveryDarkEntity_CanActuallyDodge()
        {
            foreach (var m in _dark)
                Assert.That(m.aiTuning.dodgeChance, Is.GreaterThan(0f),
                    $"{m.monsterKey}: dodge_chance 0 makes FSMDodge return on its first line. " +
                    "Evading is the roster's whole identity — an unset chance is an entity that " +
                    "looks like the others and plays like a barbol.");
        }

        [Test]
        public void EveryDarkEntity_HasAFieldOfViewItCanBeFlankedThrough()
        {
            foreach (var m in _dark)
                Assert.That(m.aiTuning.fovDegrees, Is.GreaterThan(0f).And.LessThan(360f),
                    $"{m.monsterKey}: 0 publishes nothing and falls back to the omniscient 360 " +
                    "default. Something this fast that also sees behind itself has no answer " +
                    "except out-damaging it.");
        }

        [Test]
        public void AStandoffIsAuthored_OnlyWhereTheSpellsCanUseIt()
        {
            foreach (var m in _dark)
            {
                if (m.aiTuning.desiredRange <= 0f) continue;

                Assert.That(m.aiTuning.desiredRange, Is.GreaterThan(m.stats.meleeRange),
                    $"{m.monsterKey}: a standoff inside its own reach is not a standoff.");
                Assert.That(m.aiTuning.desiredRange, Is.LessThan(m.stats.aggroRange),
                    $"{m.monsterKey}: a standoff outside the aggro ring makes the monster back " +
                    "out of its own perception and de-aggro on the spot.");
            }
        }

        // ── The spells ───────────────────────────────────────────────────────────

        [Test]
        public void EveryAutoCastKey_ResolvesInTheSpellCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellCatalogPath);
            Assert.IsNotNull(catalog, $"SpellCatalog missing at {SpellCatalogPath}.");

            foreach (var m in _dark)
            {
                Assert.IsTrue(m.autoCast, $"{m.monsterKey}: autoCast is false, so " +
                    "ConfigureMonsterAutoCast returns before adding NPCAutoCast and the whole " +
                    "spell list below is inert — including the animation it would have played.");

                foreach (var key in m.autoCastList)
                    Assert.IsTrue(catalog.TryGet(key, out var spell) && spell != null,
                        $"{m.monsterKey} casts '{key}', which is not in the catalog. " +
                        "ConfigureMonsterAutoCast skips it with a warning and wires the rest, " +
                        "so the monster silently loses one of its answers.");
            }
        }

        [Test]
        public void NoDarkEntity_AuthorsMoreSpellsThanTheCasterHasSlots()
        {
            // SpellCaster ships four slots and ConfigureMonsterAutoCast only creates an
            // NPCAutoCast entry while `registered < slotCount`. A fifth key is registered in the
            // spell book and never fires — authored, round-tripped and inert.
            foreach (var m in _dark)
                Assert.That(m.autoCastList.Length, Is.LessThanOrEqualTo(4),
                    $"{m.monsterKey} authors {m.autoCastList.Length} spells; only the first four " +
                    "are ever cast.");
        }

        // ── Balance sanity beyond the shared monster rules ───────────────────────

        [Test]
        public void EveryDarkEntity_ChasesFasterThanItPatrols()
        {
            foreach (var m in _dark)
                Assert.That(m.stats.chasingSpeed, Is.GreaterThan(m.stats.speed),
                    $"{m.monsterKey}: running is the point. A chase no faster than the patrol " +
                    "is a monster the player simply walks away from.");
        }

        [Test]
        public void EveryDarkEntity_TelegraphsAWindupLongEnoughToRead()
        {
            // AttackState refuses to draw a telegraph below 0.15 s: a tell that appears and
            // resolves in the same instant is noise. Authoring one below that is a promise the
            // code declines to keep.
            foreach (var m in _dark)
            {
                if (!m.useAttackTelegraph) continue;
                Assert.That(m.stats.attackWindupSeconds, Is.GreaterThanOrEqualTo(0.15f),
                    $"{m.monsterKey}: telegraph authored on a {m.stats.attackWindupSeconds}s " +
                    "windup, which AttackState will not draw.");
            }
        }
    }
}
