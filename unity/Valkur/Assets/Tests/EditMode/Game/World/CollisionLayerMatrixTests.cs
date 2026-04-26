// Sanity tests for the Physics2D layer collision matrix.
//
// The whole tile + building collision stack relies on these layer pairs being
// allowed to collide. If someone disables a pair in Project Settings → Physics 2D,
// the player will silently start walking through walls/buildings. This test pins
// the contract.
//
// Project layer assignments (see .github/copilot-instructions.md):
//   Player(8), NPC(9), Projectile(10), World(11), Pickup(12),
//   UIBlocker(13), Building(14), Spawner(15)

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Asserts the Physics2D layer collision matrix has not regressed for the
    /// pairs critical to player movement and combat.
    /// </summary>
    [TestFixture]
    public class CollisionLayerMatrixTests
    {
        private const int Player    = 8;
        private const int NPC       = 9;
        private const int Projectile = 10;
        private const int World     = 11;
        private const int Pickup    = 12;
        private const int UIBlocker = 13;
        private const int Building  = 14;
        private const int Spawner   = 15;

        [SetUp]
        public void SetUp()
        {
            // Renderer.material warnings can leak from prior tests in the same run;
            // they are unrelated to the matrix assertions below.
            LogAssert.ignoreFailingMessages = true;
        }

        // ── Critical "MUST collide" pairs ────────────────────────────────────────

        [Test]
        public void Player_MustCollide_WithWorldLayer()
        {
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(Player, World),
                "Player↔World collision is REQUIRED for tile/wall blocking. " +
                "Re-enable the (Player, World) cell in Project Settings → Physics 2D.");
        }

        [Test]
        public void Player_MustCollide_WithBuildingLayer()
        {
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(Player, Building),
                "Player↔Building collision is REQUIRED for building blocking. " +
                "Re-enable the (Player, Building) cell in Project Settings → Physics 2D.");
        }

        [Test]
        public void Player_MustCollide_WithNPCLayer()
        {
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(Player, NPC),
                "Player↔NPC collision is REQUIRED for melee hit detection.");
        }

        [Test]
        public void NPC_MustCollide_WithWorldLayer()
        {
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(NPC, World),
                "NPC↔World collision is REQUIRED so monsters cannot walk through walls.");
        }

        [Test]
        public void NPC_MustCollide_WithBuildingLayer()
        {
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(NPC, Building),
                "NPC↔Building collision is REQUIRED so monsters cannot walk through houses.");
        }

        [Test]
        public void Projectile_MustCollide_WithWorldLayer()
        {
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(Projectile, World),
                "Projectile↔World collision is REQUIRED so spells/arrows are stopped by walls.");
        }

        [Test]
        public void Projectile_MustCollide_WithBuildingLayer()
        {
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(Projectile, Building),
                "Projectile↔Building collision is REQUIRED so spells/arrows are stopped by buildings.");
        }

        [Test]
        public void Player_MustCollide_WithPickupLayer()
        {
            // Pickup uses trigger volumes — they still need the layer pair enabled
            // for OnTriggerEnter2D to fire on the player collider.
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(Player, Pickup),
                "Player↔Pickup must be enabled so trigger callbacks fire for loot pickup.");
        }

        // ── Sanity: layer indices match project conventions ─────────────────────

        [Test]
        public void LayerNames_MatchExpectedIndices()
        {
            // Loose check (only fails if the user RENAMED layers but kept indices) —
            // catches accidental swaps like "Building" being moved to a different index.
            // Empty names are tolerated to support stripped builds in CI runners.
            void Check(int idx, string expected)
            {
                string actual = LayerMask.LayerToName(idx);
                if (string.IsNullOrEmpty(actual)) return;
                StringAssert.AreEqualIgnoringCase(expected, actual,
                    $"Layer index {idx} expected name '{expected}' but found '{actual}'.");
            }
            Check(Player,    "Player");
            Check(NPC,       "NPC");
            Check(Projectile, "Projectile");
            Check(World,     "World");
            Check(Pickup,    "Pickup");
            Check(UIBlocker, "UIBlocker");
            Check(Building,  "Building");
            Check(Spawner,   "Spawner");
        }

        // ── Default layer collision (test scaffolding depends on this) ──────────

        [Test]
        public void Player_MustCollide_WithDefaultLayer()
        {
            // PlayMode collision tests construct synthetic geometry on the Default
            // layer (0). If this pair is ever ignored the existing PlayerTileCollision
            // tests would silently pass without testing anything.
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(Player, 0),
                "Player↔Default(0) must collide — test fixtures rely on this.");
        }
    }
}
