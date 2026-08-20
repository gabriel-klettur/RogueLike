using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data.Feel;
using Valkur.Gameplay.Feel;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// The shipped tuning, checked against the constraints the runtime actually imposes.
    ///
    /// The binding one is the pixel lattice: <c>CameraPixelSnap</c> rounds the final camera
    /// position to the screen-pixel grid, so any effect smaller than a pixel is not subtle,
    /// it is erased. An amplitude authored below that threshold is a cue that does nothing
    /// and reports nothing.
    /// </summary>
    [TestFixture]
    public class CameraFeelProfileDefaultsTests
    {
        private const string ASSET_PATH = "Assets/_Project/Resources/CameraFeelProfile.asset";

        /// <summary>Ortho 5 on a 960 px viewport — the shipped framing.</summary>
        private const float WORLD_UNITS_PER_PIXEL = (5f * 2f) / 960f;

        private CameraFeelProfile _asset;

        [SetUp]
        public void SetUp()
        {
            _asset = AssetDatabase.LoadAssetAtPath<CameraFeelProfile>(ASSET_PATH);
            Assert.IsNotNull(_asset,
                $"{ASSET_PATH} is missing. The director falls back to CreateDefault() without " +
                "it, so the game still runs — which is exactly why its absence needs a test.");
        }

        private static IEnumerable<CameraFeelCue> AllCues()
            => Enum.GetValues(typeof(CameraFeelCue)).Cast<CameraFeelCue>();

        [Test]
        public void ShippedAssetMatchesTheCodeDefaults()
        {
            var reference = CameraFeelProfile.CreateDefault();
            try
            {
                foreach (CameraFeelTunable id in Enum.GetValues(typeof(CameraFeelTunable)))
                    Assert.AreEqual(reference.GetTunable(id), _asset.GetTunable(id), 1e-4f,
                        $"{id} differs between the asset and CreateDefault(). A missing asset " +
                        "must degrade to the tuned numbers, not to a different camera.");
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        [Test]
        public void EveryVisibleCueIsAboveThePixelQuantum()
        {
            var invisible = new List<string>();

            foreach (var cue in AllCues())
            {
                FeelCue t = _asset.GetCue(cue);
                if (t.traumaAdd <= 0f && t.kickAmplitudeWu <= 0f) continue;   // deliberately silent

                float shake = CameraFeelMathProbe.TraumaToAmplitude(t.traumaAdd, _asset.MaxShakeWu);
                float biggest = Mathf.Max(shake, t.kickAmplitudeWu);

                if (biggest < 2f * WORLD_UNITS_PER_PIXEL)
                    invisible.Add($"{cue}: peak {biggest:0.####} wu = " +
                                  $"{biggest / WORLD_UNITS_PER_PIXEL:0.#} px");
            }

            Assert.IsEmpty(invisible,
                "CameraPixelSnap rounds the camera to the pixel grid, so these cues are " +
                "rounded away entirely. A cue that fires and moves nothing is worse than no " +
                "cue: it reads as the effect being broken.\n\n  " +
                string.Join("\n  ", invisible));
        }

        [Test]
        public void NoCueThrowsTheCameraAcrossTheScreen()
        {
            foreach (var cue in AllCues())
            {
                FeelCue t = _asset.GetCue(cue);
                float total = CameraFeelMathProbe.TraumaToAmplitude(t.traumaAdd, _asset.MaxShakeWu)
                            + t.kickAmplitudeWu;
                Assert.Less(total, 1.0f,
                    $"{cue} displaces the camera {total:0.##} wu, a fifth of the visible " +
                    "height at the tightest zoom. Past that the player loses their character.");
            }
        }

        [Test]
        public void EveryRepeatableCueIsRateLimited()
        {
            var unlimited = new[]
            {
                CameraFeelCue.AttackConnect, CameraFeelCue.Hurt,
                CameraFeelCue.ImpactLight, CameraFeelCue.ImpactMedium,
                CameraFeelCue.ImpactHeavy, CameraFeelCue.ImpactMassive,
                CameraFeelCue.LevelUp,
            }.Where(c => _asset.GetCue(c).minIntervalSeconds <= 0f).ToList();

            Assert.IsEmpty(unlimited,
                "Beams and cones report a hit per tick per victim and explosions report every " +
                "target in one frame. Without a minimum interval these pin the screen at full " +
                "shake.\n\n  " + string.Join("\n  ", unlimited));
        }

        [Test]
        public void ImpactCuesEscalate()
        {
            FeelCue light = _asset.GetCue(CameraFeelCue.ImpactLight);
            FeelCue medium = _asset.GetCue(CameraFeelCue.ImpactMedium);
            FeelCue heavy = _asset.GetCue(CameraFeelCue.ImpactHeavy);
            FeelCue massive = _asset.GetCue(CameraFeelCue.ImpactMassive);

            Assert.Less(light.traumaAdd, medium.traumaAdd);
            Assert.Less(medium.traumaAdd, heavy.traumaAdd);
            Assert.Less(heavy.traumaAdd, massive.traumaAdd);

            Assert.Less(light.kickAmplitudeWu, medium.kickAmplitudeWu);
            Assert.Less(medium.kickAmplitudeWu, heavy.kickAmplitudeWu);
            Assert.Less(heavy.kickAmplitudeWu, massive.kickAmplitudeWu);

            // Heavier impacts are LOWER in frequency. Amplitude alone cannot express weight —
            // a loud fast rattle reads as a rendering fault, not as a meteor.
            Assert.Greater(light.shakeFrequencyHz, massive.shakeFrequencyHz,
                "A massive impact must be slower than a light one, not just bigger.");
        }

        [Test]
        public void TakingDamageDiffersFromDealingItInCharacterNotJustSize()
        {
            FeelCue connect = _asset.GetCue(CameraFeelCue.AttackConnect);
            FeelCue hurt = _asset.GetCue(CameraFeelCue.Hurt);

            Assert.Less(hurt.shakeFrequencyHz, connect.shakeFrequencyHz,
                "A blow you absorb is slower than one you deliver.");
            Assert.Less(hurt.kickZeta, connect.kickZeta,
                "Taking damage overshoots once; dealing it does not. That single wobble is " +
                "the whole difference in feel.");
            Assert.Greater(hurt.leadFreezeSeconds, 0f,
                "Being hit must stop the camera anticipating — that is what makes it read as " +
                "an interruption rather than as a bump.");
            Assert.AreEqual(0f, connect.leadFreezeSeconds, 1e-5f,
                "Landing a hit must NOT interrupt your own momentum.");
        }

        [Test]
        public void RewardCuesNeverPunch()
        {
            foreach (var cue in new[] { CameraFeelCue.LevelUp, CameraFeelCue.ComboPayoff,
                                        CameraFeelCue.BossPhase, CameraFeelCue.Death })
                Assert.AreEqual(0f, _asset.GetCue(cue).kickAmplitudeWu, 1e-5f,
                    $"{cue} has a directional kick. A reward that punches the frame reads as " +
                    "damage; these must swell, not shove.");
        }

        [Test]
        public void OnlyThePlayersOwnActionsFreezeTime()
        {
            var freezing = AllCues().Where(c => _asset.GetCue(c).hitStopSeconds > 0f).ToList();
            var allowed = new[] { CameraFeelCue.AttackConnect, CameraFeelCue.ImpactMassive,
                                  CameraFeelCue.BossPhase };

            var unexpected = freezing.Except(allowed).ToList();
            Assert.IsEmpty(unexpected,
                "Hit-stop is a global time freeze. Granting it broadly is how one NPC swinging " +
                "a sword froze the whole session.\n\n  " + string.Join("\n  ", unexpected));

            foreach (var cue in freezing)
                Assert.Less(_asset.GetCue(cue).hitStopSeconds, 0.16f,
                    $"{cue} freezes for longer than a player reads as impact rather than lag.");
        }

        [Test]
        public void TheCameraLeadsRatherThanTrails()
        {
            // A critically damped follow spring settles 2*speed/omega behind a walking
            // player, and that lag subtracts from the forward lead. Get this backwards and
            // the camera trails the character while appearing, in the Inspector, to lead it.
            const float walkSpeed = 4f;
            float lag = _asset.FollowOmega > 0f ? 2f * walkSpeed / _asset.FollowOmega : 0f;
            float net = _asset.MoveLeadWu - lag;

            Assert.Greater(net, 0.05f,
                $"Follow lag is {lag:0.00} wu and the move lead is {_asset.MoveLeadWu:0.00} wu, " +
                $"so the camera sits {net:0.00} wu from the player — it is not leading. Raise " +
                "followOmega or moveLeadWu.");
            Assert.Less(net, _asset.MaxLeadWu,
                "The net lead must stay inside the clamp or the clamp is doing the tuning.");
        }

        [Test]
        public void AimLeadIsOffSoTheCameraFollowsTheCharacter()
        {
            Assert.AreEqual(0f, _asset.AimLeadIdleWu, 1e-5f);
            Assert.AreEqual(0f, _asset.AimLeadMovingWu, 1e-5f,
                "The camera follows the character, not the cursor. Both aim terms ship at zero " +
                "and the director skips reading the mouse entirely while they are.");
        }
    }

    /// <summary>
    /// The one piece of solver maths this fixture needs, restated rather than reached for.
    /// <c>CameraFeelMath</c> is internal to the gameplay assembly and this fixture is about
    /// the DATA — restating the curve keeps the test honest if the two ever disagree.
    /// </summary>
    internal static class CameraFeelMathProbe
    {
        public static float TraumaToAmplitude(float trauma, float maxShakeWu)
            => trauma * trauma * maxShakeWu;
    }
}
