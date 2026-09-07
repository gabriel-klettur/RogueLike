using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// The three monsters cut from the wave13 sheets, and the ways a pixel pipeline can ship
    /// something that looks finished and is not.
    ///
    /// <para><b>THE SHEETS WERE NOT WHAT THEY LOOKED LIKE, three times over.</b> Two of the
    /// three monsters arrived as RGB PNGs with the editor's transparency CHECKERBOARD baked
    /// into the pixels, so they had no alpha at all. The frames LOOK like an even grid and are
    /// not — the figures are wider than their nominal cell and 148 components straddle a cut
    /// line, so grid-slicing puts a slice of the next barbol into this barbol's frame. And the
    /// keying's first version removed only border-connected plate, leaving 146 ENCLOSED
    /// pockets (the gap between an arm and a torso) welded on as solid white patches.</para>
    ///
    /// <para>None of that is visible from the ScriptableObjects, which is why these tests read
    /// the SPRITES: how many frames each state actually has, that a frame is a whole number of
    /// direction buckets, that the character is the size it was authored to be, and that no
    /// frame carries a white patch. A count is cheap to assert and would have caught every one
    /// of the three failures above.</para>
    /// </summary>
    public class Wave13MonsterRigTests
    {
        private const string MonsterDir = "Assets/_Project/Data/Catalogs/Monsters";

        /// <summary>Authored body height in pixels, against the NPC import PPU of 64.</summary>
        private static readonly Dictionary<string, float> ExpectedWorldHeight = new Dictionary<string, float>
        {
            { "barbol_muscle", 240f / 64f },   // 3.75 units
            { "barbol_young",  168f / 64f },   // 2.63
            { "red_dragon",    288f / 64f },   // 4.50
        };

        private static MonsterDefinition Load(string key)
            => AssetDatabase.LoadAssetAtPath<MonsterDefinition>($"{MonsterDir}/{key}.asset");

        private static IEnumerable<string> Keys => ExpectedWorldHeight.Keys;

        // ── The rig exists ───────────────────────────────────────────────────────

        [Test]
        public void EveryWave13MonsterExistsAndIsInTheCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>($"{MonsterDir}/MonsterCatalog.asset");
            Assert.IsNotNull(catalog);

            foreach (var key in Keys)
            {
                Assert.IsNotNull(Load(key), $"{key}.asset is missing — re-run " +
                    "Valkur > Monsters > Import Frame Sheets (Apply).");
                Assert.IsNotNull(catalog.GetByKey(key),
                    $"{key} exists on disk but is not in the catalog, so nothing in the game " +
                    "can spawn it — not a spawner, not the Entities editor, not `spawn`.");
            }
        }

        [Test]
        public void EveryStateIsAWholeNumberOfDirectionBuckets()
        {
            // DirectionalAnimator slices a linear list into EIGHT contiguous per-direction
            // buckets. A list that is not a multiple of eight is not one frame short of a
            // cycle, it is eight directions each silently holding the wrong frames.
            foreach (var key in Keys)
            {
                var ac = Load(key).assetConfig;
                foreach (var (name, list) in Sheets(ac))
                {
                    if (list == null || list.Count == 0) continue;
                    Assert.AreEqual(0, list.Count % 8,
                        $"{key}.{name} has {list.Count} frames, which is not a whole number of " +
                        "8-direction buckets.");
                }
            }
        }

        [Test]
        public void NoStateHoldsANullFrame()
        {
            // A null in the list is a sprite the manifest named and the importer could not
            // load. It reports it once at import time and then the animation simply blinks.
            foreach (var key in Keys)
            {
                var ac = Load(key).assetConfig;
                foreach (var (name, list) in Sheets(ac))
                    for (int i = 0; list != null && i < list.Count; i++)
                        Assert.IsNotNull(list[i], $"{key}.{name}[{i}] is null.");
            }
        }

        [Test]
        public void TheThreeMonstersAreTheSizeTheyWereAuthoredAt()
        {
            // Pixel height and PPU are a PAIR and neither means anything alone — CLAUDE.md
            // records the vampire rendering at 4.047 units instead of 2.667 because the PNGs
            // were rebuilt and the metas still held the old PPU. This asserts the COMPOSITION.
            foreach (var key in Keys)
            {
                var idle = Load(key).assetConfig.idleSheets;
                Assert.That(idle, Is.Not.Null.And.Not.Empty, $"{key} has no idle frames.");

                float worldHeight = idle[0].rect.height / idle[0].pixelsPerUnit;
                Assert.That(worldHeight, Is.EqualTo(ExpectedWorldHeight[key]).Within(0.06f),
                    $"{key} renders {worldHeight:0.00} units tall, expected " +
                    $"{ExpectedWorldHeight[key]:0.00}. Either the frames were rebuilt at a new " +
                    "pixel height, or the textures re-imported at a different PPU.");
            }
        }

        [Test]
        public void AllThreeAreBiggerThanThePlayer()
        {
            // The brief was that they be large. The dwarf stands 1.797 units; anything at or
            // under that is a monster that reads as another villager.
            const float DwarfHeight = 1.797f;
            foreach (var key in Keys)
            {
                var idle = Load(key).assetConfig.idleSheets;
                float h = idle[0].rect.height / idle[0].pixelsPerUnit;
                Assert.That(h, Is.GreaterThan(DwarfHeight),
                    $"{key} is {h:0.00} units against the player's {DwarfHeight}.");
            }
        }

        // ── The keying ───────────────────────────────────────────────────────────

        [Test]
        public void NoFrameCarriesAWhitePatchFromTheCheckerboard()
        {
            // The regression this exists for. Enclosed plate pockets came through as opaque
            // white welded to a hip or a hand: measured 547 such pixels on ONE frame before
            // the fix, 74 across every frame of both barbols after it. The budget below is per
            // FRAME and generous enough for the handful of legitimately pale pixels in this
            // art (an earring glint, a tooth) while a returned pocket is hundreds.
            const int MaxWhitePixelsPerFrame = 40;

            foreach (var key in new[] { "barbol_muscle", "barbol_young" })
            {
                var ac = Load(key).assetConfig;
                int worst = 0;
                string worstName = "";

                foreach (var (_, list) in Sheets(ac))
                {
                    if (list == null) continue;
                    foreach (var sprite in list.Distinct())
                    {
                        if (sprite == null) continue;
                        int white = CountOpaqueNearWhite(sprite);
                        if (white > worst) { worst = white; worstName = sprite.name; }
                    }
                }

                Assert.That(worst, Is.LessThanOrEqualTo(MaxWhitePixelsPerFrame),
                    $"{key}: {worstName} carries {worst} opaque near-white pixels. That is the " +
                    "transparency checkerboard coming back through — see " +
                    "tools/atlas/wave13/slice_wave13_sheets.py.");
            }
        }

        /// <summary>
        /// Opaque, near-white, colourless pixels: the signature of the baked plate. Reads the
        /// texture directly rather than the atlas, because a packed sprite's rect is in atlas
        /// space and `GetPixels` on it would sample its neighbours.
        /// </summary>
        private static int CountOpaqueNearWhite(Sprite sprite)
        {
            var texture = sprite.texture;
            if (texture == null || !texture.isReadable) return 0;

            var pixels = texture.GetPixels32();
            int count = 0;
            foreach (var p in pixels)
            {
                if (p.a <= 200) continue;
                int lo = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                int hi = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                if (lo >= 235 && hi - lo <= 8) count++;
            }
            return count;
        }

        // ── The design the importer deliberately does not write ──────────────────

        [Test]
        public void EveryWave13MonsterIsPlayable()
        {
            foreach (var key in Keys)
            {
                var def = Load(key);
                Assert.That(def.stats.hp, Is.GreaterThan(0), $"{key} spawns dead.");
                Assert.That(def.stats.meleeRange, Is.GreaterThan(0f), $"{key} cannot reach.");
                Assert.That(def.stats.meleeRange, Is.LessThanOrEqualTo(def.stats.aggroRange),
                    $"{key} reaches further than it can see.");
                Assert.That(def.stats.chasingSpeed, Is.GreaterThan(def.stats.speed),
                    $"{key} chases no faster than it patrols.");
                Assert.AreEqual("EVIL", def.stats.faction, $"{key} is not hostile.");
                Assert.That(def.fsmSet, Is.Not.Null.And.Not.Empty,
                    $"{key} names no FSM set, so it falls through to the hard-coded IdleState " +
                    "and stands still forever with data that reads correctly.");
                Assert.That(def.levelHpGrowth, Is.GreaterThan(0f),
                    $"{key} ignores encounter difficulty entirely.");
            }
        }

        [Test]
        public void EverySpellTheyCastResolves()
        {
            var spells = AssetDatabase.LoadAssetAtPath<SpellCatalog>(
                "Assets/_Project/Data/Catalogs/SpellCatalog.asset");
            Assert.IsNotNull(spells);

            foreach (var key in Keys)
            {
                var def = Load(key);
                if (!def.autoCast) continue;
                Assert.That(def.autoCastList, Is.Not.Null.And.Not.Empty,
                    $"{key} has autoCast on and nothing to cast.");
                foreach (var spellKey in def.autoCastList)
                    Assert.IsTrue(spells.TryGet(spellKey, out var s) && s != null,
                        $"{key} casts '{spellKey}', which is not in the catalog — " +
                        "ConfigureMonsterAutoCast skips it with a warning and wires the rest.");
            }
        }

        [Test]
        public void TheMovesetsAreActuallyDifferentMoves()
        {
            // knight_red shipped five visually distinct attacks that were mechanically
            // identical, because the variant carried no combat data. A moveset whose entries
            // all deal the same damage at the same reach is five animations over one hit.
            foreach (var key in new[] { "barbol_muscle", "barbol_young" })
            {
                var variants = Load(key).assetConfig.attackVariants;
                Assert.That(variants.Count, Is.GreaterThanOrEqualTo(3), $"{key} has no moveset.");

                var damages = variants.Select(v => v.damageMultiplier).Distinct().Count();
                Assert.That(damages, Is.GreaterThan(1),
                    $"{key}'s attack variants all deal the same damage.");

                Assert.IsTrue(variants.Any(v => v.minDistance > 0f || v.maxDistance > 0f),
                    $"{key} has no distance gate on any move, so AttackState picks purely by " +
                    "weight and a gap-closer can fire point blank.");
            }
        }

        [Test]
        public void EveryCastVariantIsReachable()
        {
            // A cast variant is chosen by SPELL KEY (NPCCastState.ResolveCastVariant). One
            // that reserves no spell can never be selected on a monster, because a monster
            // has no rotation to fall into - it is authored art nothing can play.
            foreach (var key in new[] { "barbol_muscle", "barbol_young" })
            {
                var def = Load(key);
                var castable = new HashSet<string>(def.autoCastList ?? new string[0]);

                foreach (var v in def.assetConfig.castVariants)
                {
                    Assert.That(v.spellKeys, Is.Not.Null.And.Not.Empty,
                        $"{key} cast variant '{v.key}' reserves no spell and can never play.");
                    foreach (var spellKey in v.spellKeys)
                        Assert.IsTrue(castable.Contains(spellKey),
                            $"{key} cast variant '{v.key}' reserves '{spellKey}', which is not " +
                            "in this monster's autoCastList.");
                }
            }
        }

        [Test]
        public void TheRawSheetsAreNotInsideTheAtlas()
        {
            // 44 source sheets at 62.8 Mpx were sitting under Art/NPC, which npc.spriteatlas
            // packs wholesale - fifteen atlas pages of art nothing references. They live in
            // staging/ at the repo root now, which is gitignored and outside Assets, exactly
            // as the player pipeline's sources do.
            Assert.IsFalse(AssetDatabase.IsValidFolder("Assets/_Project/Art/NPC/monsters/new"),
                "The raw wave13 sheets are back inside Assets/. They belong in " +
                "staging/monsters/wave13_src.");
        }

        private static IEnumerable<(string name, List<Sprite> list)> Sheets(EntityAssetConfig ac)
        {
            yield return ("idle", ac.idleSheets);
            yield return ("walk", ac.walkSheets);
            yield return ("chase", ac.chaseSheets);
            yield return ("attack", ac.attackSheets);
            yield return ("cast", ac.castSheets);
            yield return ("damage", ac.damageSheets);
            yield return ("death", ac.deathSheets);

            if (ac.attackVariants != null)
                foreach (var v in ac.attackVariants)
                    yield return ($"attackVariant:{v.key}", v.sheets);
            if (ac.castVariants != null)
                foreach (var v in ac.castVariants)
                    yield return ($"castVariant:{v.key}", v.sheets);
        }
    }
}
