using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Buildings
{
    /// <summary>
    /// Pins <see cref="WorldTransitionService"/>: the single owner of the same-scene overlay
    /// swap, and the return point that lets an interior find its way back out.
    ///
    /// The return point is why an interior does not hardcode the coordinates of the building
    /// it belongs to — without it, dragging that building in F10 silently breaks the exit.
    /// The refusal cases matter just as much: a transition that cannot complete must change
    /// NOTHING, because the alternative is a player standing in a half-cleared world.
    /// </summary>
    [TestFixture]
    public class WorldTransitionServiceTests
    {
        private static void ResetStatics()
        {
            var m = typeof(WorldTransitionService).GetMethod(
                "ResetStaticsOnPlayModeEnter", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "Reflection: WorldTransitionService.ResetStaticsOnPlayModeEnter not " +
                                "found. Domain Reload is OFF in this project, so that reset is not " +
                                "optional — if it was removed, restore it rather than this test.");
            m.Invoke(null, null);
        }

        [SetUp]
        public void SetUp() => ResetStatics();

        [TearDown]
        public void TearDown()
        {
            ResetStatics();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Static reset (Domain Reload is OFF) ─────────────────────────────────

        [Test]
        public void PlayModeReset_ClearsAReturnPointLeftBehindByAPreviousSession()
        {
            WorldTransitionService.RecordReturnPoint("old.overlay.json", new Vector2(5f, 6f));

            ResetStatics();

            Assert.IsFalse(WorldTransitionService.HasReturnPoint,
                "A return point recorded in the previous Play session would send the player to a " +
                "position in a world that is no longer loaded.");
            Assert.AreEqual(string.Empty, WorldTransitionService.CurrentOverlay);
        }

        // ── Return point ────────────────────────────────────────────────────────

        [Test]
        public void NoReturnPointRecorded_ConsumeReportsNothing()
        {
            Assert.IsFalse(WorldTransitionService.HasReturnPoint);
            Assert.IsFalse(WorldTransitionService.TryConsumeReturnPoint(out _));
        }

        [Test]
        public void RecordedReturnPoint_ComesBackVerbatim()
        {
            WorldTransitionService.RecordReturnPoint("lobby.overlay.json", new Vector2(12.5f, -3f));

            Assert.IsTrue(WorldTransitionService.HasReturnPoint);
            Assert.IsTrue(WorldTransitionService.TryConsumeReturnPoint(out var point));
            Assert.AreEqual("lobby.overlay.json", point.Overlay);
            Assert.AreEqual(new Vector2(12.5f, -3f), point.WorldPosition);
            Assert.IsFalse(point.IsBaseWorld);
        }

        [Test]
        public void ConsumingAReturnPoint_ClearsIt()
        {
            WorldTransitionService.RecordReturnPoint("a.overlay.json", Vector2.one);

            Assert.IsTrue(WorldTransitionService.TryConsumeReturnPoint(out _));

            Assert.IsFalse(WorldTransitionService.HasReturnPoint,
                "A return point must be used once. Leaving it armed teleports the player on the " +
                "next unrelated exit.");
            Assert.IsFalse(WorldTransitionService.TryConsumeReturnPoint(out _));
        }

        [Test]
        public void EmptyOverlayName_MeansTheBaseWorld_WhichIsAValidDestination()
        {
            WorldTransitionService.RecordReturnPoint("", new Vector2(40f, 40f));

            Assert.IsTrue(WorldTransitionService.TryConsumeReturnPoint(out var point));
            Assert.IsTrue(point.IsBaseWorld,
                "The outdoor world is assembled from per-zone overlays and has no single overlay " +
                "name; empty is how a door records 'the player came from outside'.");
            Assert.AreEqual(new Vector2(40f, 40f), point.WorldPosition);
        }

        [Test]
        public void RecordingTwice_KeepsTheMostRecentTrip()
        {
            WorldTransitionService.RecordReturnPoint("first.overlay.json", Vector2.zero);
            WorldTransitionService.RecordReturnPoint("second.overlay.json", new Vector2(9f, 9f));

            Assert.IsTrue(WorldTransitionService.TryConsumeReturnPoint(out var point));
            Assert.AreEqual("second.overlay.json", point.Overlay,
                "Overwrite, not stack: an unfinished trip must not leak into the next one.");
        }

        [Test]
        public void ClearReturnPoint_DisarmsWithoutConsuming()
        {
            WorldTransitionService.RecordReturnPoint("a.overlay.json", Vector2.one);

            WorldTransitionService.ClearReturnPoint();

            Assert.IsFalse(WorldTransitionService.HasReturnPoint);
        }

        [Test]
        public void NullOverlayName_IsNormalisedToEmpty()
        {
            WorldTransitionService.RecordReturnPoint(null, Vector2.zero);

            Assert.IsTrue(WorldTransitionService.TryConsumeReturnPoint(out var point));
            Assert.AreEqual(string.Empty, point.Overlay, "Callers concatenate this — it must never be null.");
            Assert.IsTrue(point.IsBaseWorld);
        }

        // ── Transition refusals ─────────────────────────────────────────────────

        [Test]
        public void BlankDestination_IsRefusedAndChangesNothing()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no destination overlay"));

            bool ok = WorldTransitionService.EnterOverlay(
                "   ", Vector2.zero, useDefaultSpawn: false, player: null);

            Assert.IsFalse(ok);
            Assert.AreEqual(string.Empty, WorldTransitionService.CurrentOverlay,
                "A refused transition must not claim an overlay is loaded.");
        }

        [Test]
        public void NoWorldGridBuilderInScene_IsRefusedRatherThanHalfApplied()
        {
            Assume.That(Object.FindObjectOfType<WorldGridBuilder>() == null,
                "Another fixture left a WorldGridBuilder in the scene; this case needs none.");

            LogAssert.Expect(LogType.Error, new Regex("No WorldGridBuilder"));

            bool ok = WorldTransitionService.EnterOverlay(
                "somewhere.overlay.json", new Vector2(3f, 4f), useDefaultSpawn: false, player: null);

            Assert.IsFalse(ok);
            Assert.AreEqual(string.Empty, WorldTransitionService.CurrentOverlay);
        }

        [Test]
        public void ResolveOverlayPath_PointsIntoTheMapsFolder()
        {
            string path = WorldTransitionService.ResolveOverlayPath("x.overlay.json")
                                                .Replace('\\', '/');

            StringAssert.Contains("/Maps/x.overlay.json", path,
                "A doorway target is a filename resolved against StreamingAssets/Maps, the same " +
                "value ZonePortal.destinationOverlay consumes.");
        }

        [Test]
        public void AMissingDestination_IsNotLoadable()
        {
            // The check that lets EnterOverlay refuse BEFORE tearing the world down. Without
            // it, OverlayLoader logs and returns, leaving the player in a cleared world with
            // no tiles, no buildings and no way back.
            Assert.IsFalse(WorldTransitionService.IsOverlayLoadable("definitely_not_a_room.overlay.json"));
            Assert.IsFalse(WorldTransitionService.IsOverlayLoadable(""));
            Assert.IsFalse(WorldTransitionService.IsOverlayLoadable(null));
        }

        // \u2500\u2500 The trip back \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        [Test]
        public void ReturnToCaller_WithNothingRecorded_IsRefused()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no return point was recorded"));

            Assert.IsFalse(WorldTransitionService.ReturnToCaller(player: null));
        }

        [Test]
        public void ReturnToCaller_WhenTheBaseWorldCannotBeRebuilt_PutsTheReturnPointBack()
        {
            Assume.That(Object.FindObjectOfType<WorldLoader>() == null,
                "Another fixture left a WorldLoader in the scene; this case needs none.");

            WorldTransitionService.RecordReturnPoint("", new Vector2(41f, 9f));

            LogAssert.Expect(LogType.Error, new Regex("cannot rebuild"));

            Assert.IsFalse(WorldTransitionService.ReturnToCaller(player: null));

            Assert.IsTrue(WorldTransitionService.HasReturnPoint,
                "A consumed-then-failed return is a soft-lock: the player is in a sealed room " +
                "whose exit no longer knows where home is.");
            Assert.IsTrue(WorldTransitionService.TryConsumeReturnPoint(out var point));
            Assert.AreEqual(new Vector2(41f, 9f), point.WorldPosition);
        }

        // \u2500\u2500 World-content suspension \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private static void ForceSuspended(bool value)
        {
            typeof(WorldTransitionService)
                .GetProperty("IsBaseWorldContentSuspended",
                             BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, value, null);
        }

        [Test]
        public void OutsideATransition_WorldContentWritesAreAllowed()
        {
            Assert.IsFalse(WorldTransitionService.IsBaseWorldContentSuspended);
            Assert.IsFalse(WorldTransitionService.RefuseWorldContentWrite("buildings"),
                "The guard must be inert in normal play, or every ordinary edit stops saving.");
        }

        [Test]
        public void WhileTheWorldIsTornDown_ContentWritesAreRefused()
        {
            // The window this closes is real and cost 188 placed emitters: inside an interior
            // the scene legitimately holds no buildings, lights or particles, so an editor
            // autosave serialises nothing and overwrites the authored world with it. The
            // editors' own guards infer intent from counts; this states the fact.
            ForceSuspended(true);
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("Refusing a buildings save"));
                Assert.IsTrue(WorldTransitionService.RefuseWorldContentWrite("buildings"));

                LogAssert.Expect(LogType.Warning, new Regex("Refusing a particles save"));
                Assert.IsTrue(WorldTransitionService.RefuseWorldContentWrite("particles"));
            }
            finally
            {
                ForceSuspended(false);
            }
        }

        [Test]
        public void PlayModeReset_ClearsAStuckSuspension()
        {
            // Domain Reload is OFF. A session that ended inside an interior would otherwise
            // hand the next one a permanent "no saves allowed" state with no way to clear it.
            ForceSuspended(true);

            ResetStatics();

            Assert.IsFalse(WorldTransitionService.IsBaseWorldContentSuspended);
            Assert.IsFalse(WorldTransitionService.RefuseWorldContentWrite("buildings"));
        }

        [Test]
        public void DefaultSpawn_IsAKnownConstantRatherThanTheOrigin()
        {
            // (0, 0) is the corner of the map — a "default" spawn that lands there reads as
            // the player being flung out of the world.
            Assert.AreNotEqual(Vector2.zero, WorldTransitionService.DEFAULT_SPAWN);
        }
    }
}
