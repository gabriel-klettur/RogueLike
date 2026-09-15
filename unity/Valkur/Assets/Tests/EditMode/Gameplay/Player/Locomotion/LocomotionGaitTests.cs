using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Player;

namespace Valkur.Tests.EditMode.Gameplay.Player.Locomotion
{
    /// <summary>
    /// Walking into a run, driven step by step on a synthetic clock. The gait is pure, so every
    /// rule of the feel is a number here rather than something only a player could notice.
    /// </summary>
    [TestFixture]
    public class LocomotionGaitTests
    {
        private const float Dt = 0.02f;
        private const float Walk = 5f;

        private LocomotionTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _tuning = ScriptableObject.CreateInstance<LocomotionTuning>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_tuning);
        }

        /// <summary>Steps with the body keeping up with whatever the gait asked for.</summary>
        private static void Run(LocomotionGait gait, Vector2 dir, float seconds, float skill = 0f,
                                float energy = 1f, float realSpeedFraction = 1f)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                gait.Step(new GaitInput
                {
                    DeltaTime = Dt,
                    Desired = dir,
                    WalkSpeed = Walk,
                    RealSpeed = dir == Vector2.zero ? 0f : Walk * gait.SpeedMultiplier * realSpeedFraction,
                    Energy01 = energy,
                    Skill01 = skill,
                });
            }
        }

        private static float SecondsToRun(LocomotionGait gait, float skill)
        {
            for (int i = 0; i < 1000; i++)
            {
                Run(gait, Vector2.right, Dt, skill);
                if (gait.State == GaitState.Run) return (i + 1) * Dt;
            }
            return float.PositiveInfinity;
        }

        [Test]
        public void SustainedWalking_BreaksIntoARun_AfterTheSkillsStartTime()
        {
            float beginner = SecondsToRun(new LocomotionGait(_tuning), 0f);
            float master = SecondsToRun(new LocomotionGait(_tuning), 1f);

            Assert.AreEqual(_tuning.StartSeconds(0f), beginner, 0.05f, "a beginner walks ~3 steps first");
            Assert.AreEqual(_tuning.StartSeconds(1f), master, 0.05f, "a master breaks into a run on the first stride");
            Assert.Less(master, beginner);
        }

        [Test]
        public void PushingIntoAWall_NeverRuns()
        {
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 5f, realSpeedFraction: 0f);
            Assert.AreEqual(GaitState.Walk, gait.State);
            Assert.AreEqual(0f, gait.Momentum, 1e-4f, "momentum is earned by displacement, not by input");
        }

        [Test]
        public void SlidingAlongAnEdge_StillBuildsMomentum()
        {
            // The void clamp zeroes one axis of a diagonal: 0.707 of the speed asked for.
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 3f, realSpeedFraction: 0.707f);
            Assert.AreEqual(GaitState.Run, gait.State);
        }

        [Test]
        public void ATapBetweenDirections_IsNotAStop_ButLettingGoIs()
        {
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 3f);
            Assert.AreEqual(GaitState.Run, gait.State);

            Run(gait, Vector2.zero, 0.06f);
            Run(gait, Vector2.right, Dt);
            Assert.AreEqual(GaitState.Run, gait.State, "a 60 ms gap is inside the grace");

            Run(gait, Vector2.zero, 0.5f);
            Assert.AreEqual(GaitState.Idle, gait.State);
            Assert.AreEqual(0f, gait.Momentum);
            Assert.AreEqual(GaitBreak.Stopped, gait.LastBreak);
        }

        [Test]
        public void AFullReversal_ThrowsTheRunAway()
        {
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 3f, 1f);
            Run(gait, Vector2.left, Dt, 1f);
            Assert.AreNotEqual(GaitState.Run, gait.State);
            Assert.AreEqual(GaitBreak.Turned, gait.LastBreak);
        }

        [Test]
        public void ARightAngle_DropsABeginnerToAWalk_ButNotAMaster()
        {
            var beginner = new LocomotionGait(_tuning);
            Run(beginner, Vector2.right, 3f, 0f);
            Run(beginner, Vector2.up, Dt, 0f);
            Assert.AreEqual(GaitState.Walk, beginner.State);

            var master = new LocomotionGait(_tuning);
            Run(master, Vector2.right, 3f, 1f);
            Run(master, Vector2.up, Dt, 1f);
            Assert.AreEqual(GaitState.Run, master.State, "the skill buys turning without losing the run");
        }

        [Test]
        public void ADiagonalAdjustment_CostsABeginnerNothingThatMatters()
        {
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 3f, 0f);
            Run(gait, new Vector2(1f, 1f), Dt, 0f);
            Assert.AreEqual(GaitState.Run, gait.State);
        }

        [Test]
        public void ABlow_ThrowsMomentumAway()
        {
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 3f);
            gait.Break(GaitBreak.Hit);
            Assert.AreEqual(GaitState.Walk, gait.State);
            Assert.AreEqual(0f, gait.Momentum);
            Assert.IsTrue(gait.BrokeThisStep);
            Assert.AreEqual(GaitBreak.Hit, gait.LastBreak);
        }

        [Test]
        public void BlockedWhileRunning_DropsToAWalk()
        {
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 3f);
            Run(gait, Vector2.right, 0.5f, realSpeedFraction: 0f);
            Assert.AreEqual(GaitState.Walk, gait.State);
            Assert.AreEqual(GaitBreak.Blocked, gait.LastBreak);
        }

        [Test]
        public void RunningSpendsEnergy_AndStandingGivesItBackAfterABreath()
        {
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 3f);
            Run(gait, Vector2.right, Dt);
            Assert.Less(gait.EnergyDelta, 0f);
            Assert.AreEqual(-_tuning.DrainPerSecond(0f) * Dt, gait.EnergyDelta, 1e-4f);

            Run(gait, Vector2.zero, 0.3f);
            Assert.AreEqual(0f, gait.EnergyDelta, "no regeneration inside the delay");
            Run(gait, Vector2.zero, 1f);
            Assert.AreEqual(_tuning.RegenIdle(0f) * Dt, gait.EnergyDelta, 1e-4f);
        }

        [Test]
        public void AnEmptyPool_Winds_AndLocksMomentumUntilTheRecoverFraction()
        {
            var gait = new LocomotionGait(_tuning);
            Run(gait, Vector2.right, 3f);
            Assert.AreEqual(GaitState.Run, gait.State);

            Run(gait, Vector2.right, Dt, energy: 0f);
            Assert.IsTrue(gait.IsWinded);
            Assert.AreEqual(GaitBreak.Winded, gait.LastBreak);
            Assert.AreEqual(GaitState.Walk, gait.State);

            Run(gait, Vector2.right, 5f, energy: _tuning.windedRecoverFraction - 0.05f);
            Assert.AreEqual(GaitState.Walk, gait.State, "hysteresis: a sliver of energy is not a run");
            Assert.AreEqual(0f, gait.Momentum);

            Run(gait, Vector2.right, 3f, energy: _tuning.windedRecoverFraction + 0.01f);
            Assert.IsFalse(gait.IsWinded);
            Assert.AreEqual(GaitState.Run, gait.State);
        }

        [Test]
        public void TheSpeedEasesIn_WithNoStep()
        {
            var gait = new LocomotionGait(_tuning);
            float previous = _tuning.walkSpeedFraction;
            float largest = 0f;
            for (int i = 0; i < 400; i++)
            {
                Run(gait, Vector2.right, Dt, 1f);
                largest = Mathf.Max(largest, Mathf.Abs(gait.SpeedMultiplier - previous));
                previous = gait.SpeedMultiplier;
            }
            Assert.AreEqual(_tuning.RunMultiplier(1f), gait.SpeedMultiplier, 1e-3f);
            Assert.Less(largest, 0.11f, "one physics step must never jump the speed by more than a tenth");
        }

        [Test]
        public void TheSkill_BuysRunSpeed()
        {
            // Walking is 60 % of the class speed; a beginner runs at the class speed (what used to
            // be the walk) and a master 30 % above it.
            Assert.AreEqual(0.6f, _tuning.walkSpeedFraction, 1e-4f);
            Assert.AreEqual(1.0f, _tuning.RunMultiplier(0f), 1e-4f);
            Assert.AreEqual(1.3f, _tuning.RunMultiplier(1f), 1e-4f);
            Assert.Greater(_tuning.RunEndurance(1f, 100f), _tuning.RunEndurance(0f, 100f) * 2f);
        }
    }
}
