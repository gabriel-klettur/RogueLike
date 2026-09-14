using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Player;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// The feet land where the body goes: measured loops, contact frames, the backpedal, the
    /// foot-preserving walk-to-run and the stride-derived rate. Findings and numbers:
    /// .github/LOCOMOTION_FOOT_SYNC_AUDIT_2026-09-14.md.
    /// </summary>
    [TestFixture]
    public class LocomotionCycleTests
    {
        private readonly List<Object> _created = new List<Object>();
        private LocomotionCycleCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<LocomotionCycleCatalog>();
            _created.Add(_catalog);
            _catalog.cycles.Add(new LocomotionCycle { key = "t_walk", state = "walk", loopStart = 0, contactFrames = new[] { 0, 4 }, strideUnits = 0.5f });
            _catalog.cycles.Add(new LocomotionCycle { key = "t_run", state = "chase", loopStart = 0, contactFrames = new[] { 2, 6 }, strideUnits = 0.8f });
            LocomotionCycleCatalog.OverrideForTests = _catalog;
        }

        [TearDown]
        public void TearDown()
        {
            LocomotionCycleCatalog.OverrideForTests = null;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private Sprite[] Frames(string sheet, int count)
        {
            var tex = new Texture2D(2, 2);
            _created.Add(tex);
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.zero);
                frames[i].name = $"{sheet}_e{i}";
                _created.Add(frames[i]);
            }
            return frames;
        }

        private static DirectionalAnimator.DirectionalSpriteSet All(Sprite[] f) => new DirectionalAnimator.DirectionalSpriteSet
        {
            south = f, southEast = f, east = f, northEast = f, north = f, northWest = f, west = f, southWest = f,
        };

        private DirectionalAnimator Rig()
        {
            var go = new GameObject("CycleRig");
            _created.Add(go);
            var anim = go.AddComponent<DirectionalAnimator>();
            var idle = All(Frames("t_idle", 4));
            anim.SetSpriteSets(idle, All(Frames("t_walk", 8)), All(Frames("t_run", 8)), idle, idle, idle, idle);
            return anim;
        }

        private static readonly System.Reflection.MethodInfo Advance = typeof(DirectionalAnimator).GetMethod(
            "AdvanceFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static List<int> Play(DirectionalAnimator anim, int ticks)
        {
            var shown = new List<int>();
            for (int i = 0; i < ticks; i++)
            {
                Advance.Invoke(anim, null);
                shown.Add(anim.DisplayedFrameIndex);
            }
            return shown;
        }

        [Test]
        public void AMeasuredLoop_PlaysFrameZero_WhereTheOldRuleSkippedIt()
        {
            var anim = Rig();
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            var shown = Play(anim, 12);
            CollectionAssert.Contains(shown, 0, "frame 0 is a foot contact in this cycle and must be in the loop");
        }

        [Test]
        public void StartingFromAStand_LandsOnAFootContact()
        {
            var anim = Rig();
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Assert.AreEqual(0, anim.DisplayedFrameIndex);
            Assert.AreEqual(0, anim.FramesToNextContact());
        }

        [Test]
        public void TheContactEvent_FiresOnEveryContact_AndOnlyThere()
        {
            var anim = Rig();
            var feet = new List<int>();
            anim.FootContact += f => feet.Add(f);
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            var shown = Play(anim, 16);

            int contacts = 0;
            foreach (int s in shown) if (s == 0 || s == 4) contacts++;
            Assert.AreEqual(contacts + 1, feet.Count, "one per contact shown, plus the planted start");
            for (int i = 1; i < feet.Count; i++) Assert.AreNotEqual(feet[i - 1], feet[i], "feet alternate");
        }

        [Test]
        public void TheBackpedal_PlaysTheCycleBackToFront()
        {
            var anim = Rig();
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Play(anim, 3);
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East, reversed: true);
            Assert.IsTrue(anim.IsLocomotionReversed);
            var shown = Play(anim, 4);
            for (int i = 1; i < shown.Count; i++)
                Assert.AreEqual((shown[i - 1] + 7) % 8, shown[i], "each tick steps one frame BACK");
        }

        [Test]
        public void WalkingIntoARun_KeepsTheSameFootAndProgress()
        {
            var anim = Rig();
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Play(anim, 5);   // shows 1,2,3,4,5 -> frame 5 = one frame after foot 1's contact (4)
            Assert.AreEqual(5, anim.DisplayedFrameIndex);

            anim.SetLocomotionState(DirectionalAnimator.AnimState.Chase, DirectionalAnimator.Direction.East);
            Assert.AreEqual(7, anim.DisplayedFrameIndex, "one frame after foot 1's run contact (6)");
        }

        [Test]
        public void FramesToNextContact_CountsForward()
        {
            var anim = Rig();
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Play(anim, 2);   // shows 1, 2
            Assert.AreEqual(2, anim.FramesToNextContact());
        }

        [Test]
        public void TheRate_ComesFromTheStride_CappedInFramesPerSecond()
        {
            var tuning = ScriptableObject.CreateInstance<LocomotionTuning>();
            _created.Add(tuning);
            var anim = Rig();
            anim.SetLocomotionState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);

            float interval = anim.BaseFrameIntervalFor(DirectionalAnimator.AnimState.Walk, -1);
            float drawn = 2f * 0.5f / (8 * interval);   // two steps of 0.5 u per 8-frame loop

            float planted = PlayerController.ResolveLocomotionRate(anim, DirectionalAnimator.AnimState.Walk,
                                                                   drawn * 1.2f, 5f, tuning);
            Assert.AreEqual(1.2f, planted, 1e-3f, "at 1.2x the drawn speed the cycle plays 1.2x");

            float capped = PlayerController.ResolveLocomotionRate(anim, DirectionalAnimator.AnimState.Walk,
                                                                  drawn * 10f, 5f, tuning);
            Assert.AreEqual(tuning.walkMaxFps * interval, capped, 1e-3f, "legs never blur past the fps cap");
        }

        [Test]
        public void AnUnmeasuredSheet_KeepsTheHistoricalSkip()
        {
            LocomotionCycleCatalog.OverrideForTests = ScriptableObject.CreateInstance<LocomotionCycleCatalog>();
            _created.Add(LocomotionCycleCatalog.OverrideForTests);
            var anim = Rig();
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            CollectionAssert.DoesNotContain(Play(anim, 20), 0);
        }

        [Test]
        public void BackpedallingNeverBuildsARun_AndSlowsTheWalk()
        {
            var tuning = ScriptableObject.CreateInstance<LocomotionTuning>();
            _created.Add(tuning);
            var gait = new LocomotionGait(tuning);
            for (int i = 0; i < 300; i++)
                gait.Step(new GaitInput { DeltaTime = 0.02f, Desired = Vector2.right, WalkSpeed = 5f,
                                          RealSpeed = 5f * gait.SpeedMultiplier, Energy01 = 1f, Backpedal = true });
            Assert.AreEqual(GaitState.Walk, gait.State);
            Assert.AreEqual(0f, gait.Momentum);
            Assert.AreEqual(tuning.walkSpeedFraction * tuning.backpedalSpeedFraction, gait.SpeedMultiplier, 1e-4f);
        }

        [Test]
        public void TurningToBackpedal_ThrowsARunAway()
        {
            var tuning = ScriptableObject.CreateInstance<LocomotionTuning>();
            _created.Add(tuning);
            var gait = new LocomotionGait(tuning);
            for (int i = 0; i < 150; i++)
                gait.Step(new GaitInput { DeltaTime = 0.02f, Desired = Vector2.right, WalkSpeed = 5f,
                                          RealSpeed = 5f * gait.SpeedMultiplier, Energy01 = 1f });
            Assert.AreEqual(GaitState.Run, gait.State);
            gait.Step(new GaitInput { DeltaTime = 0.02f, Desired = Vector2.right, WalkSpeed = 5f,
                                      RealSpeed = 5f * gait.SpeedMultiplier, Energy01 = 1f, Backpedal = true });
            Assert.AreEqual(GaitState.Walk, gait.State);
            Assert.AreEqual(GaitBreak.Backpedal, gait.LastBreak);
        }

        [Test]
        public void Footsteps_ListenToTheContactFrames()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/World/Ambience/FootstepEmitter.cs"));
            StringAssert.Contains("FootContact += HandleFootContact", src);
            StringAssert.Contains("FootContact -= HandleFootContact", src);
            StringAssert.Contains("if (ContactDriven) continue;", src);
        }

        // ── Shipped data ─────────────────────────────────────────────────────

        private const string CatalogPath = "Assets/_Project/Resources/Skills/LocomotionCycleCatalog.asset";

        [Test]
        public void EveryShippedPlayerCycle_IsMeasured_AndItsFramesExist()
        {
            LocomotionCycleCatalog.OverrideForTests = null;
            var shipped = UnityEditor.AssetDatabase.LoadAssetAtPath<LocomotionCycleCatalog>(CatalogPath);
            Assert.IsNotNull(shipped, "run Valkur > Players > Import Locomotion Cycles");
            Assert.GreaterOrEqual(shipped.cycles.Count, 25);

            foreach (var c in shipped.cycles)
            {
                string player = c.key.Substring(0, c.key.IndexOf('_'));
                string folder = c.key.Substring(player.Length + 1);
                string dir = Path.Combine(Application.dataPath, "_Project/Art/Characters", player, folder);
                Assert.IsTrue(Directory.Exists(dir), $"{c.key}: no frames at {dir}");
                int frames = Directory.GetFiles(dir, c.key + "_e*.png").Length;
                Assert.Greater(frames, 2, c.key);
                Assert.That(c.loopStart, Is.InRange(0, 1), c.key);
                Assert.AreEqual(2, c.contactFrames.Length, c.key + " has two feet");
                foreach (int f in c.contactFrames)
                    Assert.That(f, Is.InRange(c.loopStart, frames - 1), c.key + " contact inside the loop");
                Assert.Greater(c.strideUnits, 0.1f, c.key);
            }

            foreach (string player in new[] { "dwarf", "barbarian", "elven", "mague", "valkyrie", "vampire" })
            {
                bool hasWalk = false, hasRun = false;
                foreach (var c in shipped.cycles)
                {
                    if (!c.key.StartsWith(player + "_")) continue;
                    hasWalk |= c.state == "walk";
                    hasRun |= c.state == "chase";
                }
                Assert.IsTrue(hasWalk && hasRun, player + " must have a measured walk and run");
            }
        }
    }
}
