using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Gameplay.World.Navigation
{
    /// <summary>
    /// <see cref="LineOfSight"/> is the one geometry query the whole AI rests on: aggro
    /// acquisition, NPC melee, the A* string-pulling pass, the retreat fan and the chase's
    /// sight memory all call it. It had NO test at all — which is how it could have shipped
    /// with either of the two failure modes this fixture pins, both silent in game.
    ///
    /// <para>The first is the start epsilon. <c>Physics2D.queriesStartInColliders</c> defaults
    /// to TRUE, so an entity standing ON a painted collision cell hits its own cell at
    /// distance 0 and would be permanently blind — every monster spawned on a collider would
    /// simply never acquire anything, with nothing logged.</para>
    ///
    /// <para>The second is the direction of the answer. <c>IsClear</c> and <c>IsBlocked</c>
    /// are used interchangeably across the states, and inverting one of them turns aggro into
    /// "only see through walls" — which reads as the monsters being broken in an interesting
    /// way rather than as an inverted boolean.</para>
    /// </summary>
    public class LineOfSightTests
    {
        /// <summary>The World layer, from the project's layer map (Player 8, NPC 9,
        /// Projectile 10, World 11, ...). Blocking geometry lives here.</summary>
        private const int WorldLayer = 11;

        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            Physics2D.SyncTransforms();
        }

        private GameObject Wall(Vector2 centre, Vector2 size)
        {
            var go = new GameObject("Wall") { layer = WorldLayer };
            go.transform.position = centre;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            _scene.Add(go);

            // EditMode does not step physics, and the colliders were created after the last
            // transform sync — without this every query below sees an empty scene and the
            // whole fixture passes for the wrong reason.
            Physics2D.SyncTransforms();
            return go;
        }

        [Test]
        public void EmptySpace_IsClear()
        {
            Assert.IsTrue(LineOfSight.IsClear(new Vector2(-5f, 0f), new Vector2(5f, 0f)));
            Assert.IsFalse(LineOfSight.IsBlocked(new Vector2(-5f, 0f), new Vector2(5f, 0f)));
        }

        [Test]
        public void AWallBetweenTwoPoints_BlocksTheLine()
        {
            Wall(Vector2.zero, new Vector2(1f, 6f));

            Assert.IsFalse(LineOfSight.IsClear(new Vector2(-5f, 0f), new Vector2(5f, 0f)),
                "A wall standing between the two points is exactly the case this exists for.");
            Assert.IsTrue(LineOfSight.IsBlocked(new Vector2(-5f, 0f), new Vector2(5f, 0f)),
                "IsBlocked must be the exact inverse — the two are used interchangeably " +
                "across the states.");
        }

        [Test]
        public void AWallOffToTheSide_DoesNotBlock()
        {
            Wall(new Vector2(0f, 5f), new Vector2(1f, 1f));

            Assert.IsTrue(LineOfSight.IsClear(new Vector2(-5f, 0f), new Vector2(5f, 0f)));
        }

        [Test]
        public void StandingOnGeometry_DoesNotBlindTheSeeker()
        {
            // The seeker is INSIDE this collider. queriesStartInColliders defaults to true,
            // so without LineOfSight's start epsilon this hit registers at distance ~0 and
            // the entity can never see anything again.
            Wall(new Vector2(-5f, 0f), new Vector2(2f, 2f));

            Assert.IsTrue(LineOfSight.IsClear(new Vector2(-5f, 0f), new Vector2(5f, 0f)),
                "A collider the seeker is standing on is not a wall between it and its target.");
        }

        [Test]
        public void AWallTheTargetIsPressedAgainst_StillBlocks()
        {
            // The other end of the epsilon trade: only hits very close to the ORIGIN are
            // forgiven. A wall at the far end is a wall.
            Wall(new Vector2(4.5f, 0f), new Vector2(1f, 4f));

            Assert.IsTrue(LineOfSight.IsBlocked(new Vector2(-5f, 0f), new Vector2(5f, 0f)));
        }
    }
}
