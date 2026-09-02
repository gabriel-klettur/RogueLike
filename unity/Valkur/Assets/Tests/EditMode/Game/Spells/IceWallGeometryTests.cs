using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Pins the ice wall's SHAPE and the things that make it real.
    ///
    /// <para>The bug these exist for shipped and survived: <c>wallWidth</c>/<c>wallHeight</c>
    /// were divided by 32 on the way out of the asset, so the authored 12.5 x 3.125 became a
    /// barrier 0.78 units wide and 0.049 tall — collider included. Nothing failed; the wall
    /// simply was not there. A unit convention that only one side of a conversion knows is
    /// exactly the class of defect CLAUDE.md's spawner-drift note describes, so it gets a
    /// test that asserts the composition rather than either half.</para>
    /// </summary>
    public class IceWallGeometryTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            // In Edit Mode a component added by AddComponent never receives Awake, and Unity
            // skips the matching OnDestroy for the same reason — so WallController's own
            // unregister never runs here and the registry would leak a wall into the next
            // test, where an in-range leftover changes how many obstacles a swing reports.
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                var controller = go.GetComponent<WallController>();
                if (controller != null) DestructibleObstacleRegistry.Unregister(controller);
            }

            SpellEffectRegistry.ClearAll();
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static SpellDefinition MakeWallSpell(float width, float height,
            bool blockProjectiles = true, bool blockUnits = true)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = "wall_ice_test";
            spell.type = SpellType.Wall;
            spell.wallWidth = width;
            spell.wallHeight = height;
            spell.wallHP = 100f;
            spell.duration = 6f;
            spell.distance = 2f;
            spell.maxInstances = 1;
            spell.blockProjectiles = blockProjectiles;
            spell.blockUnits = blockUnits;
            return spell;
        }

        private GameObject Cast(SpellDefinition spell, Vector2 direction)
        {
            var caster = new GameObject("Caster");
            _spawned.Add(caster);

            var before = new HashSet<WallController>(Object.FindObjectsOfType<WallController>());

            new WallExecutor().Execute(new SpellContext
            {
                Spell = spell,
                Caster = caster.transform,
                Direction = direction,
            });

            // By identity rather than by name: a leaked wall from an earlier test would win a
            // GameObject.Find and the assertions would be measuring the wrong object.
            WallController spawned = null;
            foreach (var candidate in Object.FindObjectsOfType<WallController>())
                if (!before.Contains(candidate)) { spawned = candidate; break; }

            Assert.IsNotNull(spawned, "WallExecutor produced no wall.");
            _spawned.Add(spawned.gameObject);
            return spawned.gameObject;
        }

        [Test]
        public void WallWidth_IsWorldUnits_NotPixels()
        {
            var wall = Cast(MakeWallSpell(width: 6f, height: 2f), Vector2.right);
            var collider = wall.GetComponentInChildren<BoxCollider2D>();

            Assert.AreEqual(6f, collider.size.x, 0.001f,
                "wallWidth must reach the collider as world units. A /32 conversion would give 0.19.");
            Assert.Greater(collider.size.y, 0.3f,
                "The footprint must be walkable-scale, not sub-pixel.");
            Assert.LessOrEqual(collider.size.y, 2f,
                "The footprint is a fraction of the drawn height, never the whole silhouette.");
        }

        [Test]
        public void Wall_StandsAcrossTheCastDirection()
        {
            var wall = Cast(MakeWallSpell(6f, 2f), Vector2.right);
            var collider = wall.GetComponentInChildren<BoxCollider2D>();

            // The box's local +X runs along the barrier, so casting east must leave it
            // pointing north/south.
            Vector2 barrier = collider.transform.right;
            Assert.AreEqual(0f, Vector2.Dot(barrier.normalized, Vector2.right), 0.01f,
                "The barrier must be perpendicular to the cast, not aligned with it.");
        }

        [Test]
        public void WallRoot_IsNeverScaledOrRotated()
        {
            var wall = Cast(MakeWallSpell(6f, 2f), new Vector2(0.7f, 0.7f));

            Assert.AreEqual(Vector3.one, wall.transform.localScale,
                "A scaled root re-scales any Light2D parented under it — the trap WorldLightLoader " +
                "counter-scales to undo. IceWallVisual sizes every child absolutely instead.");
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, wall.transform.rotation), 0.01f,
                "The crystals stand up on SCREEN whichever way the barrier runs, so the root " +
                "carries no rotation; only the collider child does.");
        }

        [Test]
        public void Wall_SitsOnTheBuildingLayer_SoItBlocksThePlayerToo()
        {
            var wall = Cast(MakeWallSpell(6f, 2f), Vector2.up);
            var collider = wall.GetComponentInChildren<BoxCollider2D>();

            int building = LayerMask.NameToLayer("Building");
            Assert.AreEqual(building, collider.gameObject.layer);
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(LayerMask.NameToLayer("Player"), building),
                "The wall is meant to block its own caster.");
            Assert.AreEqual(0, collider.excludeLayers.value,
                "With both block flags set nothing may be excluded.");
        }

        [Test]
        public void BlockFlags_AreRead_NotJustSerialised()
        {
            var wall = Cast(MakeWallSpell(6f, 2f, blockProjectiles: false, blockUnits: false), Vector2.up);
            var collider = wall.GetComponentInChildren<BoxCollider2D>();

            int excluded = collider.excludeLayers.value;
            Assert.AreNotEqual(0, excluded & (1 << LayerMask.NameToLayer("Projectile")),
                "blockProjectiles: false must actually let projectiles through.");
            Assert.AreNotEqual(0, excluded & (1 << LayerMask.NameToLayer("Player")),
                "blockUnits: false must actually let units through.");
        }

        [Test]
        public void Wall_RegistersAsDestructible_AndTakesDamage()
        {
            var wall = Cast(MakeWallSpell(6f, 2f), Vector2.up);
            var controller = wall.GetComponent<WallController>();
            var health = wall.GetComponent<Health>();

            Assert.Greater(DestructibleObstacleRegistry.Count, 0,
                "Nothing in the project can damage a Building-layer collider through a mask, " +
                "which is why the wall's Health used to be unreachable code.");

            int before = health.CurrentHp;
            int struck = DestructibleObstacleRegistry.DamageInArc(
                wall.transform.position, radius: 8f, direction: Vector2.up,
                arcDegrees: 360f, damage: 25, attacker: null, element: null);

            Assert.AreEqual(1, struck);
            Assert.Less(health.CurrentHp, before, "The blow must reduce the wall's HP.");
            Assert.IsTrue(controller.AcceptsDamage, "A damaged wall is still a live wall.");
        }

        [Test]
        public void DamageInArc_IgnoresAnObstacleOutOfReach()
        {
            var wall = Cast(MakeWallSpell(6f, 2f), Vector2.up);
            var health = wall.GetComponent<Health>();
            int before = health.CurrentHp;

            int struck = DestructibleObstacleRegistry.DamageInArc(
                wall.transform.position + new Vector3(50f, 0f, 0f), radius: 1f,
                direction: Vector2.right, arcDegrees: 90f, damage: 25, attacker: null, element: null);

            Assert.AreEqual(0, struck);
            Assert.AreEqual(before, health.CurrentHp);
        }

        [Test]
        public void Dissipating_UnregistersAndStopsBlocking()
        {
            var wall = Cast(MakeWallSpell(6f, 2f), Vector2.up);
            var controller = wall.GetComponent<WallController>();
            var collider = wall.GetComponentInChildren<BoxCollider2D>();
            int registeredBefore = DestructibleObstacleRegistry.Count;

            Assert.IsTrue(controller.BeginDissipate(0.25f),
                "wall_ice ships maxInstances: 1, so eviction by the next cast is the COMMON exit.");
            Assert.IsFalse(collider.enabled, "A wall that is visibly going must stop blocking.");
            Assert.AreEqual(registeredBefore - 1, DestructibleObstacleRegistry.Count);
            Assert.IsFalse(controller.AcceptsDamage);
        }
    }
}
