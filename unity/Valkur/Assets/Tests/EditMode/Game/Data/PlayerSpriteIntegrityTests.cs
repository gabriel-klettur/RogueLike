using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Regression guards for the player walk/idle animation bug.
    ///
    /// Bug history (do not regress):
    ///  - PlayerDefinition.idleSheets / walkSheets / chaseSheets / castSheets /
    ///    attackSheets / damageSheets / deathSheets are List&lt;Sprite&gt;
    ///    references into a sliced character texture. After the importer's
    ///    maxTextureSize was raised from 2048 → 8192 to fit the full 5120px
    ///    walking strip, the EXISTING references in the asset became stale
    ///    nulls (because the sprite sub-asset names no longer matched).
    ///  - 23 of 40 walkSheets entries on every player asset were null.
    ///    EntityAnimationBinder filtered nulls → only 17 valid frames →
    ///    BuildEightDirectionalSet split that into 2 frames per direction
    ///    instead of 5, so walking animation looked broken (only 2 frames
    ///    cycling per direction).
    ///  - The fix was to re-run "Valkur > Characters > Rebuild Player
    ///    Character Assets". These tests assert the data is still healthy
    ///    so we never ship a build with stale sprite refs again.
    /// </summary>
    public class PlayerSpriteIntegrityTests
    {
        private const string PlayerCatalog = "Assets/_Project/Data/Catalogs/Players";

        private static readonly string[] ExpectedPlayers =
        {
            "barbarian", "dwarf", "elven", "mague", "valkyrie"
        };

        private static IEnumerable<PlayerDefinition> AllPlayerDefs()
        {
            foreach (var key in ExpectedPlayers)
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(
                    $"{PlayerCatalog}/{key}.asset");
                Assert.IsNotNull(def, $"PlayerDefinition '{key}.asset' should exist.");
                Assert.IsNotNull(def.assetConfig, $"'{key}' assetConfig must not be null.");
                yield return def;
            }
        }

        private static int CountNulls(IList<Sprite> list)
        {
            if (list == null) return 0;
            int nulls = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == null) nulls++;
            return nulls;
        }

        // ---- Per-sheet null guards -----------------------------------------

        [TestCase("idleSheets")]
        [TestCase("walkSheets")]
        [TestCase("chaseSheets")]
        [TestCase("castSheets")]
        [TestCase("attackSheets")]
        [TestCase("damageSheets")]
        [TestCase("deathSheets")]
        public void EveryPlayerDefinition_HasNoNullSpriteRefsInSheet(string sheetField)
        {
            var fi = typeof(EntityAssetConfig).GetField(sheetField);
            Assert.IsNotNull(fi, $"EntityAssetConfig.{sheetField} field must exist.");

            foreach (var def in AllPlayerDefs())
            {
                var list = fi.GetValue(def.assetConfig) as List<Sprite>;
                if (list == null || list.Count == 0)
                    continue;  // Empty is allowed (some classes lack some states); nulls in a populated list are not.

                int nulls = CountNulls(list);
                Assert.AreEqual(0, nulls,
                    $"PlayerDefinition '{def.name}' has {nulls} null sprite refs in '{sheetField}' " +
                    $"(of {list.Count} entries). Re-run 'Valkur > Characters > Rebuild Player Character Assets'.");
            }
        }

        // ---- Minimum frame count per state --------------------------------

        [Test]
        public void EveryPlayerDefinition_HasEnoughIdleFrames_For8DirSplit()
        {
            // The 8-direction binder needs at least 8 frames to give each
            // direction a unique frame. We assert ≥ 24 (3 frames per dir)
            // because below that the animation looks like a stutter.
            foreach (var def in AllPlayerDefs())
            {
                int valid = ValidSpriteCount(def.assetConfig.idleSheets);
                Assert.GreaterOrEqual(valid, 24,
                    $"'{def.name}' idleSheets has only {valid} valid frames " +
                    "(need ≥24 for 8 directions × 3 frames).");
            }
        }

        [Test]
        public void EveryPlayerDefinition_HasEnoughWalkFrames_For8DirSplit()
        {
            // 8 directions x 3 frames is the floor below which a walk reads as a
            // stutter. This used to demand 40 (8 x 5) "for Python parity", which was
            // really a restatement of the single sheet layout that shipped at the
            // time; the wave3 characters walk on 8 frames per direction, and the
            // legacy five still clear this bar with their 40.
            foreach (var def in AllPlayerDefs())
            {
                int valid = ValidSpriteCount(def.assetConfig.walkSheets);
                Assert.GreaterOrEqual(valid, 24,
                    $"'{def.name}' walkSheets has only {valid} valid frames " +
                    "(need at least 8 directions x 3 frames).");
            }
        }

        // ---- Binder splits the frame list evenly across all eight directions ----

        [Test]
        public void EveryPlayerDefinition_WalkBinder_SplitsEvenlyAcrossAllEightDirections()
        {
            foreach (var def in AllPlayerDefs())
                AssertEvenEightWaySplit(def.name, "walkSheets", def.assetConfig.walkSheets);
        }

        [Test]
        public void EveryPlayerDefinition_IdleBinder_SplitsEvenlyAcrossAllEightDirections()
        {
            foreach (var def in AllPlayerDefs())
                AssertEvenEightWaySplit(def.name, "idleSheets", def.assetConfig.idleSheets);
        }

        /// <summary>
        /// The bug this guards is a SHORT, UNEVEN split, not a specific frame count.
        ///
        /// Originally: 40 entries of which 23 were stale nulls, leaving 17 valid, which
        /// BuildEightDirectionalSet floored to 2 frames per direction - the walk visibly
        /// stuttered. The assertion was written as "== 5" because every player then shipped
        /// one 5120x128 sheet holding 8 directions x 5 frames, so 5 was the only right answer.
        ///
        /// It is no longer. dwarf, barbarian and elven are built by
        /// <c>tools/atlas/wave3/build_player_frames.py</c> out of side-view art drawn in ONE
        /// direction and mirrored, and their states carry however many frames the source
        /// animation actually has - 4 for a hurt, 6 for an idle, 7 for a death, 8 for a walk.
        /// Hardcoding 5 would force every future animation to be padded or truncated to match
        /// an artefact of whichever sheets happened to ship first.
        ///
        /// So assert the invariant rather than the number: every direction gets the same count,
        /// that count is <c>valid / 8</c>, and it is at least 3 - below which the animation reads
        /// as a stutter, the same floor the sibling frame-count tests use. The original
        /// 17-valid-frame bug still fails this, at 17 / 8 = 2.
        /// </summary>
        private static void AssertEvenEightWaySplit(string playerName, string sheetField,
                                                    IList<Sprite> sheets)
        {
            var clean = StripNulls(sheets);
            Assert.GreaterOrEqual(clean.Count, 24,
                $"'{playerName}' {sheetField} has only {clean.Count} non-null entries " +
                "(need at least 8 directions x 3 frames).");
            // Integer division on purpose, and NOT also asserted to divide evenly. The legacy
            // strips are 5248 px = 41 frames of 128, so mague, valkyrie, dwarf-as-was and
            // friends carry a 41st frame that BuildEightDirectionalSet floors away. That
            // trailing frame has never rendered and nothing depends on it; failing the shipped
            // data over it would be this guard inventing a rule rather than protecting the
            // stutter bug it exists for.
            int expected = clean.Count / 8;
            var set = DirectionalAnimator.CreateSetFromLinearFrames(clean);

            AssertDirectionHasFrames(playerName, "south",     set.south,     expected);
            AssertDirectionHasFrames(playerName, "southEast", set.southEast, expected);
            AssertDirectionHasFrames(playerName, "east",      set.east,      expected);
            AssertDirectionHasFrames(playerName, "northEast", set.northEast, expected);
            AssertDirectionHasFrames(playerName, "north",     set.north,     expected);
            AssertDirectionHasFrames(playerName, "northWest", set.northWest, expected);
            AssertDirectionHasFrames(playerName, "west",      set.west,      expected);
            AssertDirectionHasFrames(playerName, "southWest", set.southWest, expected);
        }

        // ---- Helpers -------------------------------------------------------

        private static int ValidSpriteCount(IList<Sprite> list)
        {
            if (list == null) return 0;
            int valid = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) valid++;
            return valid;
        }

        private static List<Sprite> StripNulls(IList<Sprite> list)
        {
            var result = new List<Sprite>();
            if (list == null) return result;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) result.Add(list[i]);
            return result;
        }

        private static void AssertDirectionHasFrames(string playerName, string dirName,
                                                     Sprite[] frames, int expected)
        {
            Assert.IsNotNull(frames,
                $"'{playerName}' direction '{dirName}' is null.");
            Assert.AreEqual(expected, frames.Length,
                $"'{playerName}' direction '{dirName}' has {frames.Length} frames; expected {expected}. " +
                "This is the regression for the 2-frames-per-direction walking bug.");
            for (int i = 0; i < frames.Length; i++)
                Assert.IsNotNull(frames[i],
                    $"'{playerName}' direction '{dirName}' frame [{i}] is null.");
        }

        [Test]
        public void PlayerDefinitionSanitizer_DoesNotPadFourDirectionalSheetsIntoWrong8DirLayout()
        {
            var def = ScriptableObject.CreateInstance<PlayerDefinition>();
            var created = new List<Object> { def };

            try
            {
                var frames = CreateFrames(16, created);
                def.assetConfig = new EntityAssetConfig
                {
                    idleSheets = new List<Sprite>(frames)
                };
                def.assetConfig.idleSheets.Insert(5, null);

                var sanitize = typeof(PlayerDefinition).GetMethod(
                    "SanitizeAssetConfig",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(sanitize);

                sanitize.Invoke(def, null);

                Assert.AreEqual(16, def.assetConfig.idleSheets.Count,
                    "Sanitizing must strip nulls but preserve a 4-direction sheet count; padding to 40 corrupts direction mapping.");
                for (int i = 0; i < frames.Count; i++)
                    Assert.AreSame(frames[i], def.assetConfig.idleSheets[i]);
            }
            finally
            {
                for (int i = 0; i < created.Count; i++)
                {
                    if (created[i] != null)
                        Object.DestroyImmediate(created[i]);
                }
            }
        }

        private static List<Sprite> CreateFrames(int count, List<Object> created)
        {
            var texture = new Texture2D(count, 1);
            created.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                frames.Add(sprite);
                created.Add(sprite);
            }

            return frames;
        }
    }
}
