using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Player;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// What running teaches, and how fast. The simulation uses the SHIPPED skill asset, so a
    /// retune of the curve is checked against the pace the design promises: about half an hour of
    /// running to 50 % and about five hours to 100 %.
    /// </summary>
    [TestFixture]
    public class AthleticsTrainerTests
    {
        private const string SkillPath = "Assets/_Project/Data/Catalogs/Skills/GS_athletics.asset";

        private LocomotionTuning _tuning;

        [SetUp]
        public void SetUp() => _tuning = ScriptableObject.CreateInstance<LocomotionTuning>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_tuning);

        [Test]
        public void OneRoll_PerDistanceRun()
        {
            var trainer = new AthleticsTrainer(_tuning);
            float every = _tuning.gainEveryRunDistance;
            Assert.AreEqual(0, trainer.Accumulate(every * 0.5f, 0f));
            Assert.AreEqual(1, trainer.Accumulate(every * 0.5f, 0.1f));
            Assert.AreEqual(3, trainer.Accumulate(every * 3f, 0.2f));
        }

        [Test]
        public void NoMoreThanTheCap_InAnyMinute_AndTheExcessIsNotBanked()
        {
            var trainer = new AthleticsTrainer(_tuning);
            int cap = _tuning.maxGainRollsPerMinute;
            int got = trainer.Accumulate(_tuning.gainEveryRunDistance * cap * 3, 1f);
            Assert.AreEqual(cap, got);

            Assert.AreEqual(0, trainer.Accumulate(_tuning.gainEveryRunDistance, 30f), "still inside the minute");
            Assert.AreEqual(1, trainer.Accumulate(_tuning.gainEveryRunDistance, 61.5f), "the window rolled on");
        }

        [Test]
        public void NothingRun_TeachesNothing()
        {
            var trainer = new AthleticsTrainer(_tuning);
            Assert.AreEqual(0, trainer.Accumulate(0f, 0f));
            Assert.AreEqual(0, trainer.Accumulate(-5f, 0f));
        }

        [Test]
        public void TheShippedCurve_TakesAboutHalfAnHourTo50_AndAboutFiveHoursToMaster()
        {
            var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(SkillPath);
            Assert.IsNotNull(skill, "GS_athletics was not seeded (Valkur > Skills > Seed Skill Content)");
            Assert.AreEqual(SkillCategory.Physical, skill.category);
            Assert.AreEqual(LocomotionTuning.SkillKey, skill.skillKey);

            // A realistic run: a mid-speed class at a mid-skill run multiplier covers far more
            // than the cap needs, so the cap is what paces the climb.
            float runSpeed = 5.5f * _tuning.RunMultiplier(0.5f);
            float rollsPerHour = Mathf.Min(_tuning.maxGainRollsPerMinute * 60f,
                                           runSpeed * 3600f / _tuning.gainEveryRunDistance);

            double toHalf = 0d, toMax = 0d;
            for (int t = 0; t < SkillDefinition.MaxTenths; t += Mathf.Max(1, skill.gainTenths))
            {
                int difficulty = Mathf.RoundToInt(SkillDefinition.ToPercent(t));
                double rolls = 1d / skill.GainChance(t, difficulty, false);
                if (t < 500) toHalf += rolls;
                toMax += rolls;
            }

            double hoursHalf = toHalf / rollsPerHour;
            double hoursMax = toMax / rollsPerHour;
            Assert.That(hoursHalf, Is.InRange(0.25d, 0.8d), $"50 % took {hoursHalf:0.00} h of running");
            Assert.That(hoursMax, Is.InRange(3.5d, 7d), $"100 % took {hoursMax:0.00} h of running");
        }
    }
}
