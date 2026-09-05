using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// A spell aimed at the cursor has to PASS THROUGH the cursor.
    ///
    /// The player's facing is measured from the sprite's centre
    /// (<c>PlayerController.ResolveFacingOrigin</c>) while the spell is born from the hands —
    /// <c>ProjectileExecutor.ResolveCastOrigin</c> lifts the origin by <c>castAnchor</c> times
    /// the caster's half-height. Each half is right on its own; the composition is not. The
    /// shot leaves PARALLEL to the line the aim was measured on, from a point above it, so it
    /// misses by that lift and the miss does NOT close with distance — it converges on the
    /// lift instead of shrinking. Measured on the shipped player sprite (1.86 units tall,
    /// Hands at 0.45 of the half-height): 0.379 units off at 2 units of range and 0.416 at 8,
    /// against a 0.4185 lift. A quarter of the character, at every range a player fights at.
    ///
    /// It survived because it is invisible in code: both origins are correct, both are
    /// already tested, and only their composition disagrees with the screen. Same shape as
    /// SPAWNER_COORDINATE_SPACE_DRIFT, and it needs the same answer — assert the COMPOSITION,
    /// not either half.
    /// </summary>
    [TestFixture]
    public class SpellAimFromCastOriginTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            var camGo = new GameObject("AimTestCamera");
            _created.Add(camGo);
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 5f;
        }

        [TearDown]
        public void TearDown()
        {
            // The override is STATIC, so leaving it set would follow every fixture that runs
            // after this one — the same class of leak as a PlayerPrefs key left behind.
            MouseInputManager.SetTestMousePosition(null);
            foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            _camera = null;
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>A Player-tagged caster whose sprite spans <paramref name="height"/> above its pivot.</summary>
        private Transform MakeCaster(float height, Vector3 position)
        {
            var go = new GameObject("AimCaster");
            _created.Add(go);
            go.tag = "Player";
            go.transform.position = position;

            var tex = new Texture2D(8, 8);
            tex.hideFlags = HideFlags.DontSave;
            var sprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0f), 8f / height);
            sprite.hideFlags = HideFlags.DontSave;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            return go.transform;
        }

        /// <summary>Put the cursor on a world point and hand that point back.</summary>
        private Vector2 AimCursorAt(Vector2 world)
        {
            Vector3 screen = _camera.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            MouseInputManager.SetTestMousePosition(new Vector2(screen.x, screen.y));
            return world;
        }

        /// <summary>Perpendicular distance from <paramref name="point"/> to the ray.</summary>
        private static float MissDistance(Vector2 origin, Vector2 direction, Vector2 point)
        {
            Vector2 d = direction.normalized;
            Vector2 rel = point - origin;
            return Mathf.Abs(rel.x * d.y - rel.y * d.x);
        }

        [Test]
        public void TheShotPassesThroughTheCursor()
        {
            var caster = MakeCaster(1.86f, Vector3.zero);
            Vector2 cursor = AimCursorAt(new Vector2(4f, 0.5f));

            // What the player controller hands the executor: measured from the body centre.
            Vector2 centre = caster.GetComponent<SpriteRenderer>().bounds.center;
            Vector2 facing = (cursor - centre).normalized;

            Vector2 aim = SpellTargeting.ResolveAimDirection(caster, facing, null);
            Vector3 spawn = ProjectileExecutor.ResolveCastStart(caster, aim, (SpellDefinition)null);

            Assert.Less(MissDistance(spawn, aim, cursor), 0.01f,
                "The projectile must fly through the point the player clicked.");
        }

        [Test]
        public void AimingFromTheBodyCentreMissesByTheHandLift()
        {
            // The defect itself, pinned so the fix cannot be undone silently. Deliberately
            // measures the OLD composition rather than trusting the new one is different.
            var caster = MakeCaster(1.86f, Vector3.zero);
            Vector2 cursor = AimCursorAt(new Vector2(4f, 0.5f));

            Vector2 centre = caster.GetComponent<SpriteRenderer>().bounds.center;
            Vector2 facing = (cursor - centre).normalized;
            Vector3 spawn = ProjectileExecutor.ResolveCastStart(caster, facing, (SpellDefinition)null);

            float lift = ProjectileExecutor.ResolveCastOrigin(caster).y - centre.y;

            Assert.Greater(lift, 0.3f, "The hand lift is what the miss is made of.");
            Assert.Greater(MissDistance(spawn, facing, cursor), 0.3f,
                "Aiming from the centre and firing from the hands misses by the lift.");
        }

        [Test]
        public void TheMissDoesNotCloseWithDistance()
        {
            // Not an angular error — which is why it never read as a targeting bug, and why
            // standing further back does not help.
            var caster = MakeCaster(1.86f, Vector3.zero);
            Vector2 centre = caster.GetComponent<SpriteRenderer>().bounds.center;

            Vector2 near = AimCursorAt(new Vector2(2f, 0f));
            Vector2 nearFacing = (near - centre).normalized;
            float nearMiss = MissDistance(
                ProjectileExecutor.ResolveCastStart(caster, nearFacing, (SpellDefinition)null),
                nearFacing, near);

            Vector2 far = AimCursorAt(new Vector2(8f, 0f));
            Vector2 farFacing = (far - centre).normalized;
            float farMiss = MissDistance(
                ProjectileExecutor.ResolveCastStart(caster, farFacing, (SpellDefinition)null),
                farFacing, far);

            float lift = ProjectileExecutor.ResolveCastOrigin(caster).y - centre.y;

            // It does not shrink with range: it CONVERGES ON THE LIFT from below. The small
            // gap at 2 units is the forward clearance, which is spent along an aim that tilts
            // more the closer the cursor is — measured 0.379 at 2 units against 0.416 at 8,
            // both a hair under the 0.4185 lift. An angular error would do the opposite and
            // grow without bound.
            Assert.Greater(nearMiss, 0.35f, "Already most of the lift at point-blank range.");
            Assert.GreaterOrEqual(farMiss, nearMiss, "Distance does not help.");
            Assert.Less(farMiss, lift + 0.001f);
            Assert.Greater(farMiss, lift - 0.01f, "At range the miss IS the hand lift.");
        }

        [Test]
        public void ACasterWithNoCursorKeepsItsFacing()
        {
            // A monster has no pointer and must go on aiming with its facing. That is the
            // fallback every caster used before, not an error path.
            var monster = MakeCaster(1.86f, Vector3.zero);
            monster.gameObject.tag = "Untagged";
            AimCursorAt(new Vector2(4f, 0.5f));

            var facing = new Vector2(0f, -1f);

            Assert.AreEqual(facing, SpellTargeting.ResolveAimDirection(monster, facing, null));
        }

        [Test]
        public void ACursorOnTheOriginKeepsTheFacing()
        {
            // No heading to give. Answering (0,0) would send the projectile nowhere.
            var caster = MakeCaster(1.86f, Vector3.zero);
            AimCursorAt(ProjectileExecutor.ResolveCastOrigin(caster));

            var facing = new Vector2(1f, 0f);

            Assert.AreEqual(facing, SpellTargeting.ResolveAimDirection(caster, facing, null));
        }
    }
}
