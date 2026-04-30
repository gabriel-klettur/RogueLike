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
            // Walking requires the full 8×5 = 40 frames for Python parity.
            // Allow ≥40 (extra trailing frames are harmless — binder ignores them).
            foreach (var def in AllPlayerDefs())
            {
                int valid = ValidSpriteCount(def.assetConfig.walkSheets);
                Assert.GreaterOrEqual(valid, 40,
                    $"'{def.name}' walkSheets has only {valid} valid frames " +
                    "(need ≥40 for 8 directions × 5 frames matching Python parity).");
            }
        }

        // ---- Binder produces 5 frames per direction (the actual bug) -----

        [Test]
        public void EveryPlayerDefinition_WalkBinder_Produces5FramesPerDirection()
        {
            // The original bug: 17 valid frames / 8 = 2 frames per direction.
            // After fix: 40 valid frames / 8 = 5 frames per direction.
            foreach (var def in AllPlayerDefs())
            {
                var clean = StripNulls(def.assetConfig.walkSheets);
                Assert.GreaterOrEqual(clean.Count, 40,
                    $"'{def.name}' walkSheets must have ≥40 non-null entries.");

                var set = DirectionalAnimator.CreateSetFromLinearFrames(clean);

                AssertDirectionHasFrames(def.name, "south",     set.south,     5);
                AssertDirectionHasFrames(def.name, "southEast", set.southEast, 5);
                AssertDirectionHasFrames(def.name, "east",      set.east,      5);
                AssertDirectionHasFrames(def.name, "northEast", set.northEast, 5);
                AssertDirectionHasFrames(def.name, "north",     set.north,     5);
                AssertDirectionHasFrames(def.name, "northWest", set.northWest, 5);
                AssertDirectionHasFrames(def.name, "west",      set.west,      5);
                AssertDirectionHasFrames(def.name, "southWest", set.southWest, 5);
            }
        }

        [Test]
        public void EveryPlayerDefinition_IdleBinder_Produces5FramesPerDirection()
        {
            foreach (var def in AllPlayerDefs())
            {
                var clean = StripNulls(def.assetConfig.idleSheets);
                Assert.GreaterOrEqual(clean.Count, 40,
                    $"'{def.name}' idleSheets must have ≥40 non-null entries.");

                var set = DirectionalAnimator.CreateSetFromLinearFrames(clean);

                AssertDirectionHasFrames(def.name, "south",     set.south,     5);
                AssertDirectionHasFrames(def.name, "east",      set.east,      5);
                AssertDirectionHasFrames(def.name, "north",     set.north,     5);
                AssertDirectionHasFrames(def.name, "west",      set.west,      5);
                AssertDirectionHasFrames(def.name, "northEast", set.northEast, 5);
                AssertDirectionHasFrames(def.name, "northWest", set.northWest, 5);
                AssertDirectionHasFrames(def.name, "southEast", set.southEast, 5);
                AssertDirectionHasFrames(def.name, "southWest", set.southWest, 5);
            }
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
