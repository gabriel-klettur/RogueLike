using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Player;

namespace Valkur.Tests.EditMode.Gameplay.Player.Locomotion
{
    /// <summary>
    /// The run art finally plays, at a rate tied to the body's speed, without the lead foot
    /// hopping back to the start of the stride when the walk becomes a run.
    /// </summary>
    [TestFixture]
    public class LocomotionAnimationTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private Sprite[] Frames(int count)
        {
            var tex = new Texture2D(2, 2);
            _created.Add(tex);
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.zero);
                _created.Add(frames[i]);
            }
            return frames;
        }

        private static DirectionalAnimator.DirectionalSpriteSet AllDirections(Sprite[] frames) =>
            new DirectionalAnimator.DirectionalSpriteSet
            {
                south = frames, southEast = frames, east = frames, northEast = frames,
                north = frames, northWest = frames, west = frames, southWest = frames,
            };

        private DirectionalAnimator MakeAnimator(int walkFrames, int runFrames)
        {
            var go = new GameObject("LocomotionAnimTest");
            _created.Add(go);
            var anim = go.AddComponent<DirectionalAnimator>();
            var idle = AllDirections(Frames(4));
            anim.SetSpriteSets(idle, AllDirections(Frames(walkFrames)), AllDirections(Frames(runFrames)),
                               idle, idle, idle, idle);
            return anim;
        }

        private static void Advance(DirectionalAnimator anim, int ticks)
        {
            var m = typeof(DirectionalAnimator).GetMethod("AdvanceFrame",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            for (int i = 0; i < ticks; i++) m.Invoke(anim, null);
        }

        [Test]
        public void TheLocomotionRate_PacesWalkAndRun_AndNothingElse()
        {
            var anim = MakeAnimator(9, 9);
            float walk = anim.FrameIntervalFor(DirectionalAnimator.AnimState.Walk, -1);
            float idle = anim.FrameIntervalFor(DirectionalAnimator.AnimState.Idle, -1);

            anim.SetLocomotionRate(2f);
            Assert.AreEqual(walk / 2f, anim.FrameIntervalFor(DirectionalAnimator.AnimState.Walk, -1), 1e-5f);
            Assert.AreEqual(walk / 2f, anim.FrameIntervalFor(DirectionalAnimator.AnimState.Chase, -1), 1e-5f);
            Assert.AreEqual(idle, anim.FrameIntervalFor(DirectionalAnimator.AnimState.Idle, -1), 1e-5f,
                "an authored idle must not breathe faster because the body ran");
        }

        [Test]
        public void TheLocomotionRate_ComposesWithAnAuthoredStateSpeed()
        {
            var anim = MakeAnimator(9, 9);
            anim.SetStateSpeed(DirectionalAnimator.AnimState.Walk, 0.5f);
            float authored = anim.FrameIntervalFor(DirectionalAnimator.AnimState.Walk, -1);
            anim.SetLocomotionRate(1.25f);
            Assert.AreEqual(authored / 1.25f, anim.FrameIntervalFor(DirectionalAnimator.AnimState.Walk, -1), 1e-5f);
            Assert.AreEqual(0.5f, anim.StateSpeedOf(DirectionalAnimator.AnimState.Walk), 1e-5f);
        }

        [Test]
        public void WalkingIntoARun_KeepsThePlaceInTheStride()
        {
            var anim = MakeAnimator(9, 9);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Advance(anim, 4);
            int before = anim.DisplayedFrameIndex;
            Assert.AreEqual(5, before);

            anim.SetLocomotionState(DirectionalAnimator.AnimState.Chase, DirectionalAnimator.Direction.East);
            Assert.AreEqual(DirectionalAnimator.AnimState.Chase, anim.CurrentState);
            Assert.AreEqual(before, anim.DisplayedFrameIndex, "same fraction of the stride, not frame 1");
        }

        [Test]
        public void ThePhase_IsAFraction_WhenTheCyclesDifferInLength()
        {
            var anim = MakeAnimator(9, 5);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Advance(anim, 4);   // frame 5 of 1..8: halfway
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Chase, DirectionalAnimator.Direction.East);
            Assert.AreEqual(3, anim.DisplayedFrameIndex, "halfway through 1..4");
        }

        [Test]
        public void AnyOtherTransition_StillRestartsTheAnimation()
        {
            var anim = MakeAnimator(9, 9);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Advance(anim, 4);
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            Assert.AreEqual(DirectionalAnimator.AnimState.Idle, anim.CurrentState);
            Assert.AreEqual(0, anim.DisplayedFrameIndex);
        }

        [Test]
        public void ThePlayerController_ChoosesTheRunCycle_FromTheGait()
        {
            // A structural pin: the two places the controller used to hard-code Walk-or-Idle both
            // go through the gait now. Reverting either is the one-line change that silently
            // retires every run animation the roster ships.
            string src = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Player/PlayerController.Movement.cs"));
            StringAssert.DoesNotContain("IsMoving ? DirectionalAnimator.AnimState.Walk", src);
            Assert.GreaterOrEqual(Regex(src, "ResolveLocomotionAnimState()"), 2);
            StringAssert.Contains("StepLocomotion(", src);
        }

        private static int Regex(string haystack, string needle)
        {
            int count = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
            return count;
        }

        [Test]
        public void TheClassSelectorsResistencia_IsTheEnergyPool()
        {
            var tuning = ScriptableObject.CreateInstance<LocomotionTuning>();
            _created.Add(tuning);
            Assert.AreEqual(70f, tuning.MaxEnergyFor(25f), 1e-3f, "the mague");
            Assert.AreEqual(104f, tuning.MaxEnergyFor(110f), 1e-3f, "the vampire");
            Assert.AreEqual(StatKind.MaxEnergy, ParseOrFail("stamina"));
        }

        private static StatKind ParseOrFail(string name)
        {
            Assert.IsTrue(StatCatalog.TryParse(name, out var stat));
            return stat;
        }
    }
}
