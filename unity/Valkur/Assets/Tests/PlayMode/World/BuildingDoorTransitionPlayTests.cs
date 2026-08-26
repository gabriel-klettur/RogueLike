using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Tests.PlayMode.World
{
    /// <summary>
    /// The half of the doorway feature EditMode cannot reach: an actual world swap, with a
    /// real <see cref="WorldGridBuilder"/>, real tiles painted from the shipped interior, a
    /// real player transform, and the exit that comes back out.
    ///
    /// EditMode pins the geometry, the persistence pair and every refusal path. What only a
    /// live scene can show is that the destination actually PAINTS, that the player lands
    /// inside it, and that the exit arms once they step away and takes them back when they
    /// step on it — the three things that decide whether a player is in a room or in a void.
    ///
    /// The base world is deliberately NOT rebuilt here: that path needs a full
    /// <c>WorldLoader</c> and would turn this fixture into a whole-world load. It is covered
    /// by driving the return into a SECOND overlay instead, which exercises the same
    /// consume-and-transition logic without the world rebuild.
    /// </summary>
    [TestFixture]
    public class BuildingDoorTransitionPlayTests
    {
        private const string INTERIOR = "Interiors/house_interior_small.overlay.json";
        private const string OTHER    = "lobby.overlay.json";

        private GameObject _builderGo;
        private GameObject _playerGo;
        private WorldGridBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            // Renderer/material chatter from a bare scene is not what this fixture is about.
            LogAssert.ignoreFailingMessages = true;

            _builderGo = new GameObject("TestWorldGridBuilder");
            _builder   = _builderGo.AddComponent<WorldGridBuilder>();

            _playerGo = new GameObject("TestPlayer");
            _playerGo.transform.position = new Vector3(-999f, -999f, 0f);
            var body = _playerGo.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.velocity     = new Vector2(3f, -4f); // residual motion, on purpose
            EntityRegistry.RegisterPlayer(_playerGo);

            WorldTransitionService.ClearReturnPoint();
            WorldTransitionService.DespawnInteriorExit();
        }

        [TearDown]
        public void TearDown()
        {
            WorldTransitionService.ClearReturnPoint();
            WorldTransitionService.DespawnInteriorExit();

            if (_playerGo  != null) Object.Destroy(_playerGo);
            if (_builderGo != null) Object.Destroy(_builderGo);

            LogAssert.ignoreFailingMessages = false;
        }

        private int PaintedTileCount()
        {
            int painted = 0;
            foreach (var tm in _builder.Grid.GetComponentsInChildren<Tilemap>())
            {
                var bounds = tm.cellBounds;
                foreach (var pos in bounds.allPositionsWithin)
                    if (tm.GetTile(pos) != null) painted++;
            }
            return painted;
        }

        // ── Entering ────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator EnteringTheShippedInterior_PaintsItAndPutsThePlayerInside()
        {
            yield return null; // let WorldGridBuilder.Awake build the grid

            Assume.That(_builder.Grid != null, "The grid was not built.");
            Assume.That(WorldTransitionService.IsOverlayLoadable(INTERIOR),
                "The shipped interior is missing or unloadable.");

            var spawn = new Vector2(7.5f, 5.5f);
            bool ok = WorldTransitionService.EnterOverlay(
                INTERIOR, spawn, useDefaultSpawn: false, _playerGo, _builder);

            Assert.IsTrue(ok, "The transition was refused.");
            yield return null;

            Assert.Greater(PaintedTileCount(), 0,
                "The destination painted nothing — the player is standing in a void, which is " +
                "exactly the state the pre-clear validation exists to prevent.");

            Assert.AreEqual(spawn.x, _playerGo.transform.position.x, 1e-3f, "Player X.");
            Assert.AreEqual(spawn.y, _playerGo.transform.position.y, 1e-3f, "Player Y.");
            Assert.AreEqual(INTERIOR, WorldTransitionService.CurrentOverlay);
        }

        [UnityTest]
        public IEnumerator Entering_ZeroesResidualVelocityAndWakesTheBody()
        {
            yield return null;

            Assume.That(WorldTransitionService.EnterOverlay(
                INTERIOR, new Vector2(7.5f, 5.5f), false, _playerGo, _builder));

            var body = _playerGo.GetComponent<Rigidbody2D>();
            Assert.AreEqual(0f, body.velocity.magnitude, 1e-3f,
                "A teleport that keeps the walking velocity slides the player off the arrival " +
                "tile and, in a sealed room, straight into a wall.");
            Assert.IsTrue(body.IsAwake(),
                "A sleeping Dynamic body starts no new contacts, and the exit under the player " +
                "polls rather than triggers precisely because that is not reliable.");
        }

        [UnityTest]
        public IEnumerator Entering_DropsAnExitOnTheArrivalTile_Disarmed()
        {
            yield return null;

            var spawn = new Vector2(7.5f, 5.5f);
            Assume.That(WorldTransitionService.EnterOverlay(INTERIOR, spawn, false, _playerGo, _builder));
            yield return null;

            var exit = Object.FindObjectOfType<InteriorExit>();
            Assert.IsNotNull(exit, "No way out was placed — the interior is a trap.");
            Assert.AreEqual(spawn.x, exit.transform.position.x, 1e-3f);
            Assert.AreEqual(spawn.y, exit.transform.position.y, 1e-3f);
            Assert.IsFalse(exit.IsArmed,
                "The player arrives standing on the exit; a live one fires on the arrival frame.");
        }

        [UnityTest]
        public IEnumerator TheExit_ArmsOnlyAfterThePlayerWalksAway()
        {
            yield return null;

            var spawn = new Vector2(7.5f, 5.5f);
            Assume.That(WorldTransitionService.EnterOverlay(INTERIOR, spawn, false, _playerGo, _builder));
            yield return null;

            var exit = Object.FindObjectOfType<InteriorExit>();
            Assume.That(exit != null && !exit.IsArmed);

            // Standing still does nothing, however long you wait.
            yield return null;
            yield return null;
            Assert.IsFalse(exit.IsArmed, "The exit armed without the player moving.");

            // Step off it.
            _playerGo.transform.position =
                new Vector3(spawn.x + InteriorExit.ARMING_DISTANCE_WORLD + 0.5f, spawn.y, 0f);
            yield return null;

            Assert.IsTrue(exit.IsArmed, "Walking away must arm the way out.");
        }

        // ── Leaving ─────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SteppingBackOnTheArmedExit_TakesThePlayerBackWhereTheyCameFrom()
        {
            yield return null;

            // Pretend the player came from another overlay rather than the base world, so the
            // trip back is a plain overlay swap and needs no full WorldLoader rebuild.
            Assume.That(WorldTransitionService.IsOverlayLoadable(OTHER), $"{OTHER} is not loadable.");
            var home = new Vector2(20f, 20f);
            WorldTransitionService.RecordReturnPoint(OTHER, home);

            var spawn = new Vector2(7.5f, 5.5f);
            Assume.That(WorldTransitionService.EnterOverlay(INTERIOR, spawn, false, _playerGo, _builder));
            yield return null;

            var exit = Object.FindObjectOfType<InteriorExit>();
            Assume.That(exit != null);

            // Entering recorded a NEW return point (this fixture is not going through a
            // BuildingDoor), so re-arm the one we care about before leaving.
            WorldTransitionService.RecordReturnPoint(OTHER, home);

            _playerGo.transform.position =
                new Vector3(spawn.x + InteriorExit.ARMING_DISTANCE_WORLD + 0.5f, spawn.y, 0f);
            yield return null;
            Assume.That(exit.IsArmed);

            _playerGo.transform.position = new Vector3(spawn.x, spawn.y, 0f);
            yield return null;
            yield return null;

            Assert.AreEqual(OTHER, WorldTransitionService.CurrentOverlay,
                "Stepping back onto the armed exit did not swap the world back.");
            Assert.AreEqual(home.x, _playerGo.transform.position.x, 1e-3f, "Landed at the wrong X.");
            Assert.AreEqual(home.y, _playerGo.transform.position.y, 1e-3f, "Landed at the wrong Y.");
            Assert.IsFalse(WorldTransitionService.HasReturnPoint,
                "The way home is used once.");
        }

        [UnityTest]
        public IEnumerator TheOldExit_IsRemovedWhenTheWorldChangesAgain()
        {
            yield return null;

            Assume.That(WorldTransitionService.EnterOverlay(INTERIOR, new Vector2(7.5f, 5.5f), false, _playerGo, _builder));
            yield return null;
            Assume.That(Object.FindObjectOfType<InteriorExit>() != null);

            Assume.That(WorldTransitionService.EnterOverlay(OTHER, new Vector2(20f, 20f), false, _playerGo, _builder));
            yield return null;

            var exits = Object.FindObjectsOfType<InteriorExit>();
            Assert.AreEqual(1, exits.Length,
                "Each world gets exactly one exit. A leftover from the previous one sends the " +
                "player to a return point recorded for a different trip.");
        }

        // ── Refusals leave the world alone ──────────────────────────────────────

        [UnityTest]
        public IEnumerator AnUnloadableDestination_LeavesTheWorldUntouched()
        {
            yield return null;

            Assume.That(WorldTransitionService.EnterOverlay(INTERIOR, new Vector2(7.5f, 5.5f), false, _playerGo, _builder));
            yield return null;
            int paintedBefore = PaintedTileCount();
            Vector3 whereTheyWere = _playerGo.transform.position;
            Assume.That(paintedBefore > 0);

            LogAssert.Expect(LogType.Error, new Regex("missing, unparsable, or has no 'layers'"));

            bool ok = WorldTransitionService.EnterOverlay(
                "definitely_not_a_room.overlay.json", new Vector2(1f, 1f), false, _playerGo, _builder);
            yield return null;

            Assert.IsFalse(ok);
            Assert.AreEqual(paintedBefore, PaintedTileCount(),
                "The world was cleared for a destination that could not load — that is the " +
                "black-void failure the pre-clear validation exists to prevent.");
            Assert.AreEqual(whereTheyWere, _playerGo.transform.position, "The player was moved anyway.");
            Assert.AreEqual(INTERIOR, WorldTransitionService.CurrentOverlay,
                "A refused transition must not claim the new overlay is loaded.");
        }
    }
}
