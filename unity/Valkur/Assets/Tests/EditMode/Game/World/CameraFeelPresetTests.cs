using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Feel;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// The Camera Editor's whole-camera starting points.
    ///
    /// A preset is a destination, not a delta: pressing one twice, or pressing two in a row,
    /// must land in the same place both times. Written as "start from defaults, then
    /// override", so the failure this guards against is a preset that quietly inherits
    /// whatever the previous one left behind.
    /// </summary>
    [TestFixture]
    public class CameraFeelPresetTests
    {
        private static IEnumerable<CameraFeelPreset> AllPresets()
            => Enum.GetValues(typeof(CameraFeelPreset)).Cast<CameraFeelPreset>();

        private static IEnumerable<CameraFeelTunable> AllTunables()
            => Enum.GetValues(typeof(CameraFeelTunable)).Cast<CameraFeelTunable>();

        private static CameraFeelProfile Make() => CameraFeelProfile.CreateDefault();

        private static void Kill(params CameraFeelProfile[] profiles)
        {
            foreach (var p in profiles)
                if (p != null) UnityEngine.Object.DestroyImmediate(p);
        }

        [Test]
        public void EveryPresetIsIdempotent()
        {
            foreach (var preset in AllPresets())
            {
                var once = Make();
                var twice = Make();
                try
                {
                    once.ApplyPreset(preset);
                    twice.ApplyPreset(preset);
                    twice.ApplyPreset(preset);

                    foreach (var id in AllTunables())
                        Assert.AreEqual(once.GetTunable(id), twice.GetTunable(id), 1e-4f,
                            $"{preset} is not idempotent on {id}.");
                }
                finally { Kill(once, twice); }
            }
        }

        [Test]
        public void PresetsDoNotInheritFromEachOther()
        {
            // The failure mode: a preset written as a delta rather than as a destination.
            // Coming from Cinematic must land in exactly the same place as coming from fresh.
            foreach (var preset in AllPresets())
            {
                var direct = Make();
                var viaOther = Make();
                try
                {
                    direct.ApplyPreset(preset);

                    viaOther.ApplyPreset(CameraFeelPreset.Cinematic);
                    viaOther.ApplyPreset(CameraFeelPreset.Rigid);
                    viaOther.ApplyPreset(preset);

                    foreach (var id in AllTunables())
                        Assert.AreEqual(direct.GetTunable(id), viaOther.GetTunable(id), 1e-4f,
                            $"{preset} inherited state from the preset applied before it ({id}).");
                }
                finally { Kill(direct, viaOther); }
            }
        }

        [Test]
        public void DefaultPresetEqualsTheShippedTuning()
        {
            var applied = Make();
            var reference = Make();
            try
            {
                applied.ApplyPreset(CameraFeelPreset.Default);
                foreach (var id in AllTunables())
                    Assert.AreEqual(reference.GetTunable(id), applied.GetTunable(id), 1e-4f,
                        $"Default preset differs from CreateDefault() on {id}.");
            }
            finally { Kill(applied, reference); }
        }

        [Test]
        public void RigidIsTheHonestBaseline()
        {
            var p = Make();
            try
            {
                p.ApplyPreset(CameraFeelPreset.Rigid);
                Assert.AreEqual(0f, p.FollowOmega, 1e-5f, "Rigid must weld the camera.");
                Assert.AreEqual(0f, p.MoveLeadWu, 1e-5f, "Rigid must not lead.");
                Assert.AreEqual(0f, p.AimLeadIdleWu, 1e-5f);
                Assert.AreEqual(0f, p.AimLeadMovingWu, 1e-5f);
                Assert.Greater(p.MasterIntensity01, 0f,
                    "Rigid is about MOVEMENT. Shake and kick stay on, or it stops being a " +
                    "comparison against how the camera used to move and becomes a comparison " +
                    "against nothing happening at all.");
            }
            finally { Kill(p); }
        }

        [Test]
        public void MovementOnlySilencesTheTransientLayerAndNothingElse()
        {
            var p = Make();
            var reference = Make();
            try
            {
                p.ApplyPreset(CameraFeelPreset.MovementOnly);
                Assert.AreEqual(0f, p.MasterIntensity01, 1e-5f);

                Assert.AreEqual(reference.FollowOmega, p.FollowOmega, 1e-4f);
                Assert.AreEqual(reference.MoveLeadWu, p.MoveLeadWu, 1e-4f,
                    "MovementOnly must leave the movement exactly as it was — that is the " +
                    "point of it.");
            }
            finally { Kill(p, reference); }
        }

        [Test]
        public void EveryPresetLeadsRatherThanTrails()
        {
            // The trap this system makes easy: a follow spring soft enough that its lag
            // cancels the lead, leaving the camera behind the character while the Inspector
            // still shows a positive lead.
            const float walkSpeed = 4f;

            foreach (var preset in AllPresets())
            {
                var p = Make();
                try
                {
                    p.ApplyPreset(preset);

                    if (p.FollowOmega <= 0f) continue;   // welded: no lag, nothing to check
                    float lag = 2f * walkSpeed / p.FollowOmega;
                    float net = p.MoveLeadWu - lag;

                    Assert.GreaterOrEqual(net, -0.05f,
                        $"{preset}: follow lag {lag:0.00} wu exceeds its {p.MoveLeadWu:0.00} wu " +
                        $"lead, so the camera trails the player by {-net:0.00} wu.");
                }
                finally { Kill(p); }
            }
        }

        [Test]
        public void EveryPresetStaysInsideEveryDeclaredRange()
        {
            foreach (var preset in AllPresets())
            {
                var p = Make();
                try
                {
                    p.ApplyPreset(preset);
                    foreach (var id in AllTunables())
                    {
                        var info = CameraFeelProfile.GetInfo(id);
                        float v = p.GetTunable(id);
                        Assert.That(v, Is.InRange(info.Min - 1e-4f, info.Max + 1e-4f),
                            $"{preset} sets {id} to {v:0.###}, outside [{info.Min}, {info.Max}]. " +
                            "Opening the editor would silently clamp it and change the tuning.");
                    }
                }
                finally { Kill(p); }
            }
        }

        [Test]
        public void PresetsLeaveTheCueTableAlone()
        {
            // A preset is about the camera's movement. Silently rewriting fifteen hand-tuned
            // impact cues because someone wanted a looser follow would be a nasty surprise.
            var reference = Make();
            foreach (var preset in AllPresets())
            {
                var p = Make();
                try
                {
                    p.ApplyPreset(preset);
                    foreach (CameraFeelCue cue in Enum.GetValues(typeof(CameraFeelCue)))
                        Assert.AreEqual(reference.GetCue(cue).traumaAdd,
                                        p.GetCue(cue).traumaAdd, 1e-4f,
                            $"{preset} changed the {cue} cue.");
                }
                finally { Kill(p); }
            }
            Kill(reference);
        }
    }
}
