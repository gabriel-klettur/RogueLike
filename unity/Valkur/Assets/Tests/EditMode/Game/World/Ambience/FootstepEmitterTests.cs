using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World.Ambience;

namespace Valkur.Tests.EditMode.Game.World.Ambience
{
    /// <summary>
    /// The stride clock and the ground probe, driven by hand.
    ///
    /// A puff per stride of ground covered, whatever the frame rate; none while standing;
    /// the kind of puff decided by what the foot lands on, through a probe the fixture can
    /// replace — the default reads the snow clock and the Ground tilemap, neither of which
    /// exists here. Every puff sorts on the body's own layer, under its shadow.
    /// </summary>
    [TestFixture]
    public class FootstepEmitterTests
    {
        private readonly List<GameObject> _spawned = new();
        private bool _footstepsWere;

        [SetUp]
        public void Snapshot()
        {
            _footstepsWere = WorldLookSettings.Footsteps;
            WorldLookSettings.Footsteps = true;
            FootstepEmitter.GroundProbe = _ => GroundKind.Dust;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            FootstepDust.DestroyAllForTests();
            WorldLookSettings.Footsteps = _footstepsWere;
            FootstepEmitter.GroundProbe = null;   // the reset hook restores the default on Play
        }

        private FootstepEmitter Walker()
        {
            var go = new GameObject("walker");
            _spawned.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            var tex = new Texture2D(16, 32);
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 16, 32), new Vector2(0.5f, 0f), 16f);
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = 4000;
            return go.AddComponent<FootstepEmitter>();
        }

        [Test]
        public void OnePuffPerStride_WhateverTheFrameRate()
        {
            var e = Walker();
            // 3 u/s for one second, in 60 frames: 3 units, i.e. 4 strides of 0.62.
            for (int i = 0; i < 60; i++) e.Tick(1f / 60f, new Vector2(3f, 0f));
            int atSixty = e.Emitted;

            var e2 = Walker();
            for (int i = 0; i < 6; i++) e2.Tick(1f / 6f, new Vector2(3f, 0f));
            Assert.That(atSixty, Is.EqualTo(Mathf.FloorToInt(3f / FootstepEmitter.Stride)));
            Assert.That(e2.Emitted, Is.EqualTo(atSixty), "A stride is a length on the ground, not a frame.");
        }

        [Test]
        public void StandingStill_KicksUpNothing_AndResetsTheStride()
        {
            var e = Walker();
            e.Tick(0.1f, new Vector2(3f, 0f));          // 0.3 u, under a stride
            e.Tick(1f, Vector2.zero);                    // stop: the half-stride is forgotten
            e.Tick(0.1f, new Vector2(3f, 0f));          // another 0.3 u
            Assert.That(e.Emitted, Is.EqualTo(0), "Two half-strides with a stop between them are not a step.");
        }

        [Test]
        public void ThePuff_SortsUnderTheBodyOnItsOwnLayer()
        {
            var e = Walker();
            var body = e.GetComponent<SpriteRenderer>();
            e.Tick(1f, new Vector2(1f, 0f));            // one stride
            Assert.That(FootstepDust.LiveCount, Is.EqualTo(1));

            var puff = Object.FindObjectOfType<FootstepPuff>();
            var sr = puff.GetComponent<SpriteRenderer>();
            Assert.That(sr.sortingLayerID, Is.EqualTo(body.sortingLayerID));
            Assert.That(sr.sortingOrder, Is.LessThan(body.sortingOrder - 2),
                "Under the projected shadow (-1) and the contact blob (-2): dust is under the feet.");
        }

        [Test]
        public void TheGround_DecidesThePuff()
        {
            FootstepEmitter.GroundProbe = _ => GroundKind.Snow;
            var e = Walker();
            e.Tick(1f, new Vector2(1f, 0f));
            var puff = Object.FindObjectOfType<FootstepPuff>();
            Assert.That(puff.Kind, Is.EqualTo(GroundKind.Snow));
            Assert.That(puff.BornColour.r, Is.GreaterThan(0.9f), "Snow is white.");
            Assert.That(FootstepDust.ColourFor(GroundKind.Dust).r, Is.GreaterThan(FootstepDust.ColourFor(GroundKind.Dust).b),
                "Dust is warm.");
        }

        [Test]
        public void APuff_LivesLessThanASecond_AndGoesBackToThePool()
        {
            var e = Walker();
            e.Tick(1f, new Vector2(1f, 0f));
            var puff = Object.FindObjectOfType<FootstepPuff>();
            Assert.IsTrue(puff.IsLive);
            for (int i = 0; i < 70; i++) puff.Tick(1f / 60f);
            Assert.IsFalse(puff.IsLive, "Dust that outlives a second is fog.");
            Assert.That(FootstepDust.LiveCount, Is.EqualTo(0));

            // The pool hands the same object back for the next step. The first tick left
            // 0.38 u on the stride clock, so 0.3 u more is exactly one more stride.
            e.Tick(0.3f, new Vector2(1f, 0f));
            Assert.That(FootstepDust.LiveCount, Is.EqualTo(1));
            Assert.IsTrue(puff.IsLive, "Pooled, not re-created.");
        }

        [Test]
        public void SwitchedOff_NothingIsKickedUp()
        {
            WorldLookSettings.Footsteps = false;
            var e = Walker();
            e.Tick(2f, new Vector2(2f, 0f));
            Assert.That(e.Emitted, Is.EqualTo(0));
            Assert.That(FootstepDust.LiveCount, Is.EqualTo(0));
        }
    }
}
