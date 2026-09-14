using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Guards the SUSTAINED CAST: a cast variant with a repeat stretch holds the gesture at its
    /// climax while the same spell keeps being cast, instead of cycling through its rest pose.
    ///
    /// <para>The defect it removes, measured on the mague: a held fireball fires every 0.43 s and
    /// an eight-frame cast lasts 1.2 s, so looping all eight frames put the caster back at rest
    /// (frames 6, 7 and 0 match) for half of every cycle — three fireballs in six left with his
    /// hands folded. These tests pin the four properties that fix it: a single cast is untouched,
    /// a sustained cast never leaves the stretch, a re-cast from the tail jumps back to the
    /// MIDDLE (never to rest), and a lapsed stretch plays its tail and says so.</para>
    ///
    /// <para>EditMode delivers no Update and no clock, so the frame tick is invoked by reflection
    /// and the repeat window runs on <c>RepeatClockForTests</c>.</para>
    /// </summary>
    public class DirectionalAnimatorRepeatTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int Frames = 8;
        private const int From = 3;
        private const int Count = 3;   // stretch = frames 3, 4, 5

        private readonly List<Object> _created = new List<Object>();
        private float _now;

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private DirectionalAnimator.DirectionalSpriteSet SetOf(string prefix)
        {
            var texture = new Texture2D(8 * Frames, 1);
            _created.Add(texture);
            var frames = new List<Sprite>(8 * Frames);
            for (int i = 0; i < 8 * Frames; i++)
            {
                var s = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                s.name = $"{prefix}_{i}";
                frames.Add(s);
                _created.Add(s);
            }
            return DirectionalAnimator.CreateSetFromLinearFrames(frames);
        }

        private DirectionalAnimator Create(int repeatFrom = From, int repeatCount = Count)
        {
            var go = new GameObject("RepeatAnimator");
            _created.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            var anim = go.AddComponent<DirectionalAnimator>();
            typeof(DirectionalAnimator).GetField("targetRenderer", Instance).SetValue(anim, renderer);

            anim.SetSpriteSets(SetOf("idle"), SetOf("walk"), SetOf("chase"), SetOf("cast"),
                               SetOf("attack"), SetOf("damage"), SetOf("death"));
            anim.SetVariants(DirectionalAnimator.AnimState.Cast,
                new List<DirectionalAnimator.DirectionalSpriteSet> { SetOf("spell") }, null,
                new[] { new DirectionalAnimator.VariantPacing
                        { SpeedMultiplier = 1f, RepeatFrom = repeatFrom, RepeatFrameCount = repeatCount } });

            _now = 100f;
            anim.RepeatClockForTests = () => _now;
            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East, 0);
            return anim;
        }

        private static void Tick(DirectionalAnimator anim)
            => typeof(DirectionalAnimator).GetMethod("AdvanceFrame", Instance).Invoke(anim, null);

        /// <summary>Ticks <paramref name="n"/> frames and returns the frame shown after each.</summary>
        private static List<int> Run(DirectionalAnimator anim, int n)
        {
            var shown = new List<int>(n);
            for (int i = 0; i < n; i++) { Tick(anim); shown.Add(anim.DisplayedFrameIndex); }
            return shown;
        }

        [Test]
        public void ASingleCast_PlaysTheWholeGestureOnce_AndReportsItsTailFinished()
        {
            var anim = Create();
            Assert.AreEqual(0, anim.DisplayedFrameIndex, "a fresh cast opens on frame 0");

            List<int> shown = Run(anim, Frames);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 7 }, shown,
                "unsustained, the stretch changes nothing: the gesture plays through and the last " +
                "frame holds rather than wrapping back to 0 mid-window");
            Assert.IsTrue(anim.RepeatTailFinished,
                "the last frame has been on screen for a full tick, so the owner may end the pose");
        }

        [Test]
        public void TheTailSignal_WaitsForTheLastFrameToBeShownForAFullTick()
        {
            var anim = Create();
            Run(anim, Frames - 1);   // now showing frame 7 for the first time
            Assert.AreEqual(Frames - 1, anim.DisplayedFrameIndex);
            Assert.IsFalse(anim.RepeatTailFinished,
                "ending the pose on the tick that FIRST draws the last frame would cut it to nothing");
        }

        [Test]
        public void ASustainedCast_NeverLeavesTheStretch()
        {
            var anim = Create();
            anim.SustainRepeat(10f);   // the re-cast arrives on frame 0

            List<int> shown = Run(anim, 20);
            // Lead-in 1,2 then the stretch, round and round. Never 6, 7 or 0: the rest pose.
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 3, 4, 5, 3, 4, 5, 3, 4, 5, 3, 4, 5, 3, 4, 5 },
                                      shown);
            Assert.IsFalse(anim.RepeatTailFinished);
        }

        [Test]
        public void AReCastDuringTheTail_JumpsBackIntoTheMiddle_NotToRest()
        {
            var anim = Create();
            Run(anim, 6);                         // showing frame 6: the tail
            Assert.AreEqual(6, anim.DisplayedFrameIndex);

            Assert.IsTrue(anim.SustainRepeat(1f));
            Assert.AreEqual(From, anim.DisplayedFrameIndex,
                "the spell just fired again; the pose that goes with it is the stretch, drawn now");
        }

        [Test]
        public void AReCastInsideTheStretch_DoesNotPopAFrame()
        {
            var anim = Create();
            anim.SustainRepeat(10f);
            Run(anim, 4);                          // 1,2,3,4
            Assert.AreEqual(4, anim.DisplayedFrameIndex);

            anim.SustainRepeat(10f);
            Assert.AreEqual(4, anim.DisplayedFrameIndex, "a steady cadence must not restart the stretch");
            Assert.AreEqual(5, Run(anim, 1)[0]);
        }

        [Test]
        public void WhenTheWindowLapses_TheTailPlaysOut_AndTheSignalFires()
        {
            var anim = Create();
            anim.SustainRepeat(1f);
            Run(anim, 7);                          // 1,2,3,4,5,3,4
            _now += 2f;                            // the player let go

            List<int> shown = Run(anim, 4);
            CollectionAssert.AreEqual(new[] { 5, 6, 7, 7 }, shown,
                "released, the gesture finishes from where it was and returns to rest once");
            Assert.IsTrue(anim.RepeatTailFinished);
        }

        [Test]
        public void AVariantWithNoStretch_IsUntouched()
        {
            var anim = Create(repeatFrom: 0, repeatCount: 0);
            Assert.IsFalse(anim.SustainRepeat(10f), "nothing to sustain");

            List<int> shown = Run(anim, Frames);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 0 }, shown,
                "the default loop, exactly as every animation without a stretch has always played");
            Assert.IsFalse(anim.RepeatTailFinished);
        }

        [Test]
        public void ChangingState_DisarmsTheStretch()
        {
            var anim = Create();
            anim.SustainRepeat(10f);
            Assert.IsTrue(anim.RepeatArmed);

            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            Assert.IsFalse(anim.RepeatArmed,
                "a repeat belongs to one cast; left armed it would hold the NEXT cast in a stretch");
        }

        [Test]
        public void ReversedPlayback_DoesNotRepeat()
        {
            var anim = Create();
            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East, 0, true);
            Assert.IsFalse(anim.SustainRepeat(10f), "a sheathe is not a sustained cast");
        }

        [Test]
        public void AStretchPastTheEndOfTheFrames_IsClampedNotThrown()
        {
            var anim = Create(repeatFrom: 6, repeatCount: 10);
            anim.SustainRepeat(10f);
            Assert.DoesNotThrow(() => Run(anim, 20));
            Assert.That(anim.DisplayedFrameIndex, Is.InRange(0, Frames - 1));
        }
    }
}
