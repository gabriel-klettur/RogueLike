using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Buildings
{
    /// <summary>
    /// Pins <see cref="InteriorExit"/> — the way out of a swapped-in overlay.
    ///
    /// The whole design rests on ARMING. The exit is dropped on the tile the player arrives
    /// on, which is what removes every scrap of authoring burden (an interior is a hand-drawn
    /// tile matrix with no components in it), but it also means the player starts standing
    /// inside their own exit. An exit that worked on contact would bounce them straight back
    /// out on the frame they arrived. So: inert until they have walked away from it once.
    ///
    /// The failure path matters as much: a spent exit inside a sealed room is a soft-lock, so
    /// a refused trip back has to leave the exit usable.
    /// </summary>
    [TestFixture]
    public class InteriorExitTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private static void ResetTransitionStatics()
        {
            typeof(WorldTransitionService)
                .GetMethod("ResetStaticsOnPlayModeEnter", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, null);
        }

        [SetUp]
        public void SetUp() => ResetTransitionStatics();

        [TearDown]
        public void TearDown()
        {
            ResetTransitionStatics();
            LogAssert.ignoreFailingMessages = false;

            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();

            WorldTransitionService.DespawnInteriorExit();
        }

        private InteriorExit MakeExit(Vector3 position)
        {
            var go = new GameObject("TestInteriorExit");
            go.transform.position = position;
            _spawned.Add(go);
            return go.AddComponent<InteriorExit>();
        }

        // ── Geometry ────────────────────────────────────────────────────────────

        [Test]
        public void ExitRect_IsCentredOnTheArrivalTile()
        {
            var exit = MakeExit(new Vector3(7.5f, 5.5f, 0f));

            var r = exit.ExitRect;

            Assert.AreEqual(7.5f, r.center.x, 1e-4f);
            Assert.AreEqual(5.5f, r.center.y, 1e-4f);
            Assert.AreEqual(InteriorExit.EXIT_HALF_EXTENT_WORLD * 2f, r.width,  1e-4f);
            Assert.AreEqual(InteriorExit.EXIT_HALF_EXTENT_WORLD * 2f, r.height, 1e-4f);
        }

        [Test]
        public void ArmingDistance_IsLargerThanTheExitItself()
        {
            // Otherwise a player shuffling on the spot arms the exit and re-enters it in the
            // same motion, which reads as the interior spitting them straight back out.
            Assert.Greater(InteriorExit.ARMING_DISTANCE_WORLD,
                           InteriorExit.EXIT_HALF_EXTENT_WORLD,
                           "The arming radius must clear the exit rect.");
        }

        [Test]
        public void ANewExit_IsNotArmed()
        {
            Assert.IsFalse(MakeExit(Vector3.zero).IsArmed,
                "The player arrives standing on the exit; a live one would fire immediately.");
        }

        // ── The trip back ───────────────────────────────────────────────────────

        [Test]
        public void LeavingWithNothingToReturnTo_IsRefusedAndTheExitStaysUsable()
        {
            var exit = MakeExit(Vector3.zero);

            LogAssert.Expect(LogType.Warning, new Regex("no return point was recorded"));
            LogAssert.Expect(LogType.Warning, new Regex("exit stays usable"));

            Assert.IsFalse(exit.Leave(player: null));
        }

        [Test]
        public void ARefusedTripBack_LeavesTheReturnPointArmed()
        {
            Assume.That(Object.FindObjectOfType<WorldLoader>() == null,
                "Another fixture left a WorldLoader in the scene; this case needs none.");

            var exit = MakeExit(Vector3.zero);
            WorldTransitionService.RecordReturnPoint("", new Vector2(12f, 34f));

            LogAssert.Expect(LogType.Error, new Regex("cannot rebuild"));
            LogAssert.Expect(LogType.Warning, new Regex("exit stays usable"));

            Assert.IsFalse(exit.Leave(player: null));

            Assert.IsTrue(WorldTransitionService.HasReturnPoint,
                "A trip back that could not happen must not consume the way home — the player " +
                "would be sealed in a room whose only exit had gone inert.");
            Assert.IsTrue(WorldTransitionService.TryConsumeReturnPoint(out var point));
            Assert.AreEqual(new Vector2(12f, 34f), point.WorldPosition,
                "And the way home must still point at the same place.");
        }

        // ── Service-level spawn / despawn ───────────────────────────────────────

        [Test]
        public void DespawnInteriorExit_IsSafeWhenThereIsNone()
        {
            Assert.DoesNotThrow(() => WorldTransitionService.DespawnInteriorExit());
        }

        [Test]
        public void DespawnInteriorExit_RemovesTheOneInTheScene()
        {
            MakeExit(new Vector3(3f, 3f, 0f));
            Assume.That(Object.FindObjectOfType<InteriorExit>() != null);

            WorldTransitionService.DespawnInteriorExit();

            Assert.IsNull(Object.FindObjectOfType<InteriorExit>(),
                "An exit left behind after the world changed would send the player back to a " +
                "return point recorded for a different trip.");
        }
    }
}
