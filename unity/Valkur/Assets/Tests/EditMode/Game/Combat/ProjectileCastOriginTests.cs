using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Where a projectile leaves the caster.
    ///
    /// ResolveCasterCenter returns the geometric middle of the sprite, which on a humanoid
    /// with a feet pivot is the waist — so a fireball spawned there visibly came out of the
    /// character's stomach. ResolveCastOrigin lifts that to hand height.
    ///
    /// The lift is a fraction of the caster's own half-height rather than a fixed number,
    /// so it holds for a rat and for a boss without either being retuned. These tests pin
    /// that relationship, not the constant.
    /// </summary>
    [TestFixture]
    public class ProjectileCastOriginTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>A caster whose sprite spans <paramref name="height"/> units above its pivot.</summary>
        private Transform MakeCaster(float height, Vector3 position = default)
        {
            var go = new GameObject("CastOriginCaster");
            _created.Add(go);
            go.transform.position = position;

            var tex = new Texture2D(8, 8);
            tex.hideFlags = HideFlags.DontSave;
            // Pivot at the bottom centre, the 2D convention for characters in this project.
            var sprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0f), 8f / height);
            sprite.hideFlags = HideFlags.DontSave;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            return go.transform;
        }

        [Test]
        public void CastOrigin_IsAboveTheBodyCentre()
        {
            var caster = MakeCaster(2f);

            float center = ProjectileExecutor.ResolveCasterCenter(caster).y;
            float origin = ProjectileExecutor.ResolveCastOrigin(caster).y;

            Assert.Greater(origin, center,
                "Spawning at the body centre is what made the fireball look like it came out " +
                "of the character's stomach.");
        }

        [Test]
        public void CastOrigin_StaysInsideTheSprite()
        {
            var caster = MakeCaster(2f);
            var sr = caster.GetComponent<SpriteRenderer>();

            float origin = ProjectileExecutor.ResolveCastOrigin(caster).y;

            Assert.Less(origin, sr.bounds.max.y,
                "Hand height, not above the head — a projectile born off the top of the sprite " +
                "reads as detached from the caster.");
            Assert.Greater(origin, sr.bounds.min.y);
        }

        [Test]
        public void CastOrigin_ScalesWithCasterSize()
        {
            var small = MakeCaster(1f, Vector3.zero);
            var large = MakeCaster(4f, Vector3.zero);

            float smallLift = ProjectileExecutor.ResolveCastOrigin(small).y
                            - ProjectileExecutor.ResolveCasterCenter(small).y;
            float largeLift = ProjectileExecutor.ResolveCastOrigin(large).y
                            - ProjectileExecutor.ResolveCasterCenter(large).y;

            Assert.Greater(largeLift, smallLift,
                "Expressed as a fraction of the caster's height so one constant suits a rat " +
                "and a boss. A fixed offset would put the rat's cast above its head.");
        }

        [Test]
        public void CastOrigin_IsHorizontallyUnchanged()
        {
            var caster = MakeCaster(2f, new Vector3(3.5f, 1.25f, 0f));

            var center = ProjectileExecutor.ResolveCasterCenter(caster);
            var origin = ProjectileExecutor.ResolveCastOrigin(caster);

            Assert.AreEqual(center.x, origin.x, 1e-4f,
                "Only the height changes; the forward offset along the aim direction is applied " +
                "separately by the executor.");
        }

        [Test]
        public void CastOrigin_NullCaster_IsSafe()
        {
            Assert.AreEqual(Vector3.zero, ProjectileExecutor.ResolveCastOrigin(null));
        }

        [Test]
        public void CastOrigin_CasterWithNoSpriteOrCollider_DoesNotThrow()
        {
            var go = new GameObject("BareCaster");
            _created.Add(go);

            Vector3 origin = Vector3.zero;
            Assert.DoesNotThrow(() => origin = ProjectileExecutor.ResolveCastOrigin(go.transform));
            Assert.Greater(origin.y, go.transform.position.y,
                "Even with nothing to measure, a cast must not originate at the feet.");
        }
    }
}
