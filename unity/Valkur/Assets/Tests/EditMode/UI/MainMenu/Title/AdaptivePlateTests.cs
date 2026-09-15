using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.MainMenu.Title;

namespace Valkur.Tests.EditMode.UI.MainMenu.Title
{
    /// <summary>
    /// The plate under the title is SOLVED against the frame behind it, not tuned.
    ///
    /// <para><b>What it replaces.</b> One constant under a carousel that changes every seven
    /// seconds. Measured on the shipped menu, the same word read at <b>7.5:1</b> over the dark
    /// arch and <b>4.2:1</b> over the bright barbarian — the logo's legibility was decided by a
    /// timer, and no value of a constant fixes both ends: raise it and the dark frames wear a
    /// black bar, lower it and the bright ones swallow the word.</para>
    ///
    /// <para>Everything here is arithmetic on a pure function, so it needs no scene and no
    /// rendered frame — which is exactly why this axis could be closed properly instead of being
    /// eyeballed against one screenshot.</para>
    /// </summary>
    public class AdaptivePlateTests
    {
        private const float Black = 0f;

        [Test]
        public void ABrighterGround_AsksForMorePlate()
        {
            float dark = BackdropLuminance.PlateAlphaFor(0.35f, 0.02f, Black, 6f, 0.1f, 0.95f);
            float mid = BackdropLuminance.PlateAlphaFor(0.35f, 0.10f, Black, 6f, 0.1f, 0.95f);
            float bright = BackdropLuminance.PlateAlphaFor(0.35f, 0.30f, Black, 6f, 0.1f, 0.95f);

            Assert.LessOrEqual(dark, mid, "a darker frame asked for more plate than a mid one");
            Assert.Less(mid, bright, "a brighter frame did not ask for more plate");
        }

        [Test]
        public void ADarkerInk_AsksForMorePlate_OnTheSameGround()
        {
            // The red word against the cream one, on one frame. This is the whole reason the
            // plate cannot be a per-menu constant: luminance weights red at 0.2126.
            float cream = BackdropLuminance.PlateAlphaFor(0.62f, 0.20f, Black, 6f, 0.1f, 0.95f);
            float red = BackdropLuminance.PlateAlphaFor(0.22f, 0.20f, Black, 6f, 0.1f, 0.95f);
            Assert.Less(cream, red, "the darker ink must be seated deeper, not the same");
        }

        [Test]
        public void TheAnswer_ActuallyHitsTheTarget()
        {
            // Not "more plate is more better": the solved alpha must LAND on the ratio asked for.
            const float ink = 0.30f, ground = 0.40f, target = 6f;
            float a = BackdropLuminance.PlateAlphaFor(ink, ground, Black, target, 0f, 1f);
            float seated = Mathf.Lerp(ground, Black, a);
            float ratio = (Mathf.Max(ink, seated) + 0.05f) / (Mathf.Min(ink, seated) + 0.05f);
            Assert.AreEqual(target, ratio, 0.25f,
                $"solved {a:F3} of plate and landed at {ratio:F2}:1 instead of {target}:1");
        }

        [Test]
        public void AGroundThatAlreadyClearsTheTarget_GetsTheFloorAndNoMore()
        {
            // A frame dark enough on its own must not be darkened further: that is how a title
            // ends up sitting in a black hole on the one background it looked best over.
            float a = BackdropLuminance.PlateAlphaFor(0.45f, 0.01f, Black, 6f, 0.2f, 0.95f);
            Assert.AreEqual(0.2f, a, 0.001f);
        }

        [Test]
        public void TheCeiling_StopsABlackBarBeingDrawnAcrossTheArt()
        {
            // An impossible demand — near-black ink on a white frame — must clamp rather than
            // ask for a plate that hides the picture the carousel exists to show.
            float a = BackdropLuminance.PlateAlphaFor(0.02f, 0.95f, Black, 9f, 0.2f, 0.9f);
            Assert.AreEqual(0.9f, a, 0.001f);
        }

        [Test]
        public void TheFloor_IsNeverCrossed_WhateverIsAsked()
        {
            foreach (float ground in new[] { 0f, 0.05f, 0.2f, 0.5f, 0.9f })
            {
                float a = BackdropLuminance.PlateAlphaFor(0.4f, ground, Black, 6f, 0.35f, 0.9f);
                Assert.GreaterOrEqual(a, 0.35f, $"ground {ground} dropped below the floor");
                Assert.LessOrEqual(a, 0.9f, $"ground {ground} went past the ceiling");
            }
        }

        [Test]
        public void EveryShippedLook_SeatsItselfOnBothEndsOfTheCarousel()
        {
            // The two frames actually measured off live captures of the shipped menu.
            const float darkFrame = 0.002f;
            const float brightFrame = 0.055f;

            foreach (var look in MenuStyle.Active.titleLooks)
            {
                float ink = BackdropLuminance.Luminance(look.Sample(0.5f));
                float plate = BackdropLuminance.Luminance(look.haloTint);

                foreach (var ground in new[] { darkFrame, brightFrame })
                {
                    float a = BackdropLuminance.PlateAlphaFor(ink, ground, plate,
                                                              look.contrastTarget,
                                                              look.haloStrength, look.haloCeiling);
                    float seated = Mathf.Lerp(ground, plate, a);
                    float ratio = (Mathf.Max(ink, seated) + 0.05f) / (Mathf.Min(ink, seated) + 0.05f);
                    Assert.GreaterOrEqual(ratio, 3.0f,
                        $"{look.name} reads at {ratio:F1}:1 on a frame of luminance {ground}");
                }
            }
        }

        [Test]
        public void MeasuringNothing_AnswersAMidGrey_RatherThanThrowing()
        {
            // A menu whose measurement fails must keep drawing: the plate falls back to where it
            // was before any of this existed.
            float value = BackdropLuminance.Measure(null);
            Assert.Greater(value, 0.05f);
            Assert.Less(value, 0.5f);
        }
    }
}
