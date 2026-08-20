using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Feel;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// The Camera Editor builds its entire UI from <see cref="CameraFeelProfile.Tunables"/>
    /// and reads and writes through <c>GetTunable</c> / <c>SetTunable</c>.
    ///
    /// That shape trades twenty-four hand-written slider rows for three switch statements,
    /// which is a good trade — but its failure mode is a tunable that appears in the editor
    /// and silently does nothing, because one of the three lists was not updated. Nothing at
    /// compile time catches a missing switch case. These sweeps do.
    /// </summary>
    [TestFixture]
    public class CameraFeelTunableTests
    {
        private CameraFeelProfile _profile;

        [SetUp]
        public void SetUp() => _profile = CameraFeelProfile.CreateDefault();

        [TearDown]
        public void TearDown()
        {
            if (_profile != null) UnityEngine.Object.DestroyImmediate(_profile);
        }

        private static IEnumerable<CameraFeelTunable> AllTunables()
            => Enum.GetValues(typeof(CameraFeelTunable)).Cast<CameraFeelTunable>();

        [Test]
        public void EveryTunableHasAnInfoEntry()
        {
            var missing = AllTunables()
                .Where(id => string.IsNullOrEmpty(CameraFeelProfile.GetInfo(id).Label))
                .ToList();

            Assert.IsEmpty(missing,
                "These tunables have no entry in CameraFeelProfile.Tunables, so they would " +
                "never appear in the Camera Editor at all.\n\n  " +
                string.Join("\n  ", missing));
        }

        [Test]
        public void EveryTunableRoundTrips()
        {
            var broken = new List<string>();

            foreach (var id in AllTunables())
            {
                var info = CameraFeelProfile.GetInfo(id);
                float probe = Mathf.Lerp(info.Min, info.Max, 0.375f);

                _profile.SetTunable(id, probe);
                float read = _profile.GetTunable(id);

                if (Mathf.Abs(read - probe) > 1e-4f)
                    broken.Add($"{id}: wrote {probe:0.####}, read back {read:0.####}");
            }

            Assert.IsEmpty(broken,
                "A tunable that does not round-trip is a slider that moves and changes " +
                "nothing — the exact failure a switch statement with a missing case " +
                "produces, and one nothing else would catch.\n\n  " +
                string.Join("\n  ", broken));
        }

        [Test]
        public void EveryTunableIsIndependent()
        {
            // Two ids sharing a backing field is a copy-paste error in the switch that
            // round-tripping alone cannot see: both would appear to work.
            var collisions = new List<string>();

            foreach (var id in AllTunables())
            {
                var fresh = CameraFeelProfile.CreateDefault();
                try
                {
                    var before = AllTunables().ToDictionary(t => t, t => fresh.GetTunable(t));
                    var info = CameraFeelProfile.GetInfo(id);

                    // A value guaranteed to differ from the default, inside the range.
                    float target = Mathf.Approximately(before[id], info.Max)
                        ? Mathf.Lerp(info.Min, info.Max, 0.25f)
                        : info.Max;
                    fresh.SetTunable(id, target);

                    foreach (var other in AllTunables())
                    {
                        if (other == id) continue;
                        if (Mathf.Abs(fresh.GetTunable(other) - before[other]) > 1e-5f)
                            collisions.Add($"setting {id} also changed {other}");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(fresh); }
            }

            Assert.IsEmpty(collisions,
                "Two tunables share a backing field.\n\n  " + string.Join("\n  ", collisions));
        }

        [Test]
        public void EveryTunableIsClampedToItsDeclaredRange()
        {
            foreach (var id in AllTunables())
            {
                var info = CameraFeelProfile.GetInfo(id);

                _profile.SetTunable(id, info.Min - 1000f);
                Assert.AreEqual(info.Min, _profile.GetTunable(id), 1e-4f,
                    $"{id} accepted a value below its declared minimum.");

                _profile.SetTunable(id, info.Max + 1000f);
                Assert.AreEqual(info.Max, _profile.GetTunable(id), 1e-4f,
                    $"{id} accepted a value above its declared maximum.");
            }
        }

        [Test]
        public void EveryShippedDefaultLiesInsideItsOwnRange()
        {
            var outside = new List<string>();

            foreach (var id in AllTunables())
            {
                var info = CameraFeelProfile.GetInfo(id);
                float value = _profile.GetTunable(id);
                if (value < info.Min - 1e-4f || value > info.Max + 1e-4f)
                    outside.Add($"{id} = {value:0.###}, range [{info.Min}, {info.Max}]");
            }

            Assert.IsEmpty(outside,
                "A shipped value outside its own slider range is silently clamped the first " +
                "time anyone touches that slider, changing the tuning by opening the editor.\n\n  " +
                string.Join("\n  ", outside));
        }

        [Test]
        public void EveryTunableHasHelpText()
        {
            var bare = AllTunables()
                .Where(id => string.IsNullOrWhiteSpace(CameraFeelProfile.GetInfo(id).Help))
                .ToList();

            Assert.IsEmpty(bare,
                "Camera tuning is the one place where a number's consequence is not " +
                "self-evident. A knob with no explanation gets moved on a hunch.\n\n  " +
                string.Join("\n  ", bare));
        }

        [Test]
        public void EveryRangeIsOrdered()
        {
            foreach (var info in CameraFeelProfile.Tunables)
                Assert.Less(info.Min, info.Max, $"{info.Id} has an empty or inverted range.");
        }

        [Test]
        public void EveryTunableBelongsToExactlyOneGroup()
        {
            var duplicated = CameraFeelProfile.Tunables
                .GroupBy(i => i.Id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.ToString())
                .ToList();

            Assert.IsEmpty(duplicated,
                "A tunable listed twice renders two sliders that fight each other.\n\n  " +
                string.Join("\n  ", duplicated));
        }

        // ── Cues ────────────────────────────────────────────────────────────

        [Test]
        public void EveryCueRoundTripsThroughSetCue()
        {
            var broken = new List<string>();

            foreach (CameraFeelCue cue in Enum.GetValues(typeof(CameraFeelCue)))
            {
                var probe = new FeelCue
                {
                    traumaAdd = 0.11f,
                    traumaDecayPerSecond = 2.22f,
                    shakeFrequencyHz = 33f,
                    kickAmplitudeWu = 0.44f,
                    kickOmega = 5.5f,
                    kickZeta = 0.66f,
                    leadFreezeSeconds = 0.77f,
                    hitStopSeconds = 0.088f,
                    minIntervalSeconds = 0.99f,
                };

                _profile.SetCue(cue, probe);
                FeelCue read = _profile.GetCue(cue);

                if (!Mathf.Approximately(read.traumaAdd, probe.traumaAdd) ||
                    !Mathf.Approximately(read.kickOmega, probe.kickOmega) ||
                    !Mathf.Approximately(read.minIntervalSeconds, probe.minIntervalSeconds))
                    broken.Add(cue.ToString());
            }

            Assert.IsEmpty(broken,
                "These cues cannot be written, so the Camera Editor's cue panel would edit " +
                "them into a void.\n\n  " + string.Join("\n  ", broken));
        }

        [Test]
        public void SettingOneCueLeavesTheOthersAlone()
        {
            var probe = new FeelCue { traumaAdd = 0.999f, kickOmega = 39f };
            _profile.SetCue(CameraFeelCue.Hurt, probe);

            Assert.AreEqual(0.30f, _profile.GetCue(CameraFeelCue.AttackConnect).traumaAdd, 1e-4f,
                "Writing one cue must not disturb another — a shared case in the switch.");
            Assert.AreEqual(0.999f, _profile.GetCue(CameraFeelCue.Hurt).traumaAdd, 1e-4f);
        }

        [Test]
        public void ResetToDefaults_RestoresEveryTunable()
        {
            foreach (var id in AllTunables())
                _profile.SetTunable(id, CameraFeelProfile.GetInfo(id).Max);

            _profile.ResetToDefaults();

            var reference = CameraFeelProfile.CreateDefault();
            try
            {
                foreach (var id in AllTunables())
                    Assert.AreEqual(reference.GetTunable(id), _profile.GetTunable(id), 1e-4f,
                        $"{id} was not restored, so the editor's RESET button lies about it.");
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }
    }
}
