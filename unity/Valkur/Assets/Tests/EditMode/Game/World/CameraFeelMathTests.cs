using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Feel;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// The camera solver, exercised without a camera.
    ///
    /// The system this replaces could only be observed by running the game and looking at it,
    /// which is why it shipped two live defects for months: an amplitude that ratcheted
    /// upward and never came back down, and a restore step that subtracted an offset the
    /// Cinemachine brain had already erased. Both are single-line properties of pure
    /// functions, and both have a test here.
    /// </summary>
    [TestFixture]
    public class CameraFeelMathTests
    {
        private const float DT = 1f / 60f;

        // ── Trauma ──────────────────────────────────────────────────────────

        [Test]
        public void AddTrauma_IsAdditiveAndClamped()
        {
            Assert.AreEqual(0.7f, CameraFeelMath.AddTrauma(0.4f, 0.3f), 1e-5f);
            Assert.AreEqual(1.0f, CameraFeelMath.AddTrauma(0.9f, 0.5f), 1e-5f,
                "Trauma saturates at 1; it must not run away.");
        }

        [Test]
        public void AddTrauma_DoesNotRatchet()
        {
            // The defect this pins: the old CameraShake kept Math.Max of the amplitude it
            // had been given and never lowered it, so one heavy hit permanently raised every
            // later shake in the session — including effects authored a quarter as strong.
            float trauma = CameraFeelMath.AddTrauma(0f, 0.8f);
            trauma = CameraFeelMath.DecayTrauma(trauma, 1.8f, 10f);
            Assert.AreEqual(0f, trauma, 1e-6f);

            trauma = CameraFeelMath.AddTrauma(trauma, 0.1f);
            Assert.AreEqual(0.1f, trauma, 1e-5f,
                "A small cue after a big one must be small.");
        }

        [Test]
        public void DecayTrauma_ReachesExactlyZero()
        {
            float trauma = 0.5f;
            for (int i = 0; i < 600; i++) trauma = CameraFeelMath.DecayTrauma(trauma, 1.8f, DT);
            Assert.AreEqual(0f, trauma, "Decay must land on exactly zero, never a denormal.");
        }

        [Test]
        public void TraumaToAmplitude_IsQuadratic()
        {
            float half = CameraFeelMath.TraumaToAmplitude(0.5f, 0.42f);
            float full = CameraFeelMath.TraumaToAmplitude(1.0f, 0.42f);
            Assert.AreEqual(0.25f, half / full, 1e-5f,
                "Quadratic is what keeps light hits subtle and heavy hits unmistakable.");
        }

        // ── Springs ─────────────────────────────────────────────────────────

        [Test]
        public void SpringStep_CriticallyDamped_NeverOvershoots()
        {
            Vector2 x = Vector2.zero;
            Vector2 v = new Vector2(1f, 0f);
            for (int i = 0; i < 200; i++)
            {
                CameraFeelMath.SpringStep(ref x, ref v, Vector2.zero, 26f, 1f, DT);
                Assert.GreaterOrEqual(x.x, -1e-4f,
                    "A critically damped kick must return to rest without crossing it.");
            }
        }

        [Test]
        public void SpringStep_Underdamped_OvershootsOnce()
        {
            // Crossings are counted only while the motion is still visible — two percent of
            // the peak, which at ortho 5 is a fifth of a screen pixel. Below that the spring
            // is mathematically still ringing and the player is looking at a still frame.
            Vector2 x = Vector2.zero;
            Vector2 v = new Vector2(1f, 0f);
            var trace = new System.Collections.Generic.List<float>(400);
            float peak = 0f;

            for (int i = 0; i < 400; i++)
            {
                CameraFeelMath.SpringStep(ref x, ref v, Vector2.zero, 15f, 0.65f, DT);
                trace.Add(x.x);
                peak = Mathf.Max(peak, Mathf.Abs(x.x));
            }

            float visible = peak * 0.02f;
            int signFlips = 0;
            float previous = 0f;

            foreach (float value in trace)
            {
                if (Mathf.Abs(value) < visible) continue;
                if (previous != 0f && Mathf.Sign(value) != Mathf.Sign(previous)) signFlips++;
                previous = value;
            }

            Assert.AreEqual(1, signFlips,
                "Taking a hit should wobble exactly once — that single visible overshoot is " +
                "what separates absorbing a blow from delivering one.");
        }

        [Test]
        public void SpringStep_IsStableAtLargeDt()
        {
            Vector2 x = Vector2.zero;
            Vector2 v = new Vector2(1f, 0f);
            float peak = 0f;

            for (int i = 0; i < 120; i++)
            {
                CameraFeelMath.SpringStep(ref x, ref v, Vector2.zero, 30f, 1f, 0.05f);
                peak = Mathf.Max(peak, x.magnitude);
                Assert.IsFalse(float.IsNaN(x.x) || float.IsInfinity(x.x));
            }

            Assert.Less(peak, 2f,
                "An editor hitch must not be able to launch the camera. The closed-form " +
                "solution is exact at any step size, so there is no stability limit to cross.");
            Assert.Less(x.magnitude, 0.01f, "It must still settle.");
        }

        [Test]
        public void SpringStep_ZeroDt_IsIdentity()
        {
            Vector2 x = new Vector2(0.3f, -0.2f);
            Vector2 v = new Vector2(1f, 2f);
            CameraFeelMath.SpringStep(ref x, ref v, Vector2.zero, 20f, 1f, 0f);
            Assert.AreEqual(new Vector2(0.3f, -0.2f), x);
            Assert.AreEqual(new Vector2(1f, 2f), v);
        }

        [Test]
        public void ImpulseGainForUnitPeak_ProducesPeakOfOne()
        {
            (float omega, float zeta)[] cases =
            {
                (26f, 1.00f), (15f, 0.65f), (30f, 1.00f), (18f, 0.85f), (22f, 1.00f),
            };

            foreach (var (omega, zeta) in cases)
            {
                Vector2 x = Vector2.zero;
                Vector2 v = new Vector2(CameraFeelMath.ImpulseGainForUnitPeak(omega, zeta), 0f);
                float peak = 0f;

                for (int i = 0; i < 600; i++)
                {
                    CameraFeelMath.SpringStep(ref x, ref v, Vector2.zero, omega, zeta, 1f / 240f);
                    peak = Mathf.Max(peak, Mathf.Abs(x.x));
                }

                Assert.AreEqual(1f, peak, 0.06f,
                    $"omega={omega} zeta={zeta}: an authored kick amplitude has to mean peak " +
                    "world units, or two cues with the same number and different damping " +
                    "would hit differently for no reason a designer can see.");
            }
        }

        // ── Lead ────────────────────────────────────────────────────────────

        [Test]
        public void ResolveAimVector_RemovesTheAppliedOffset()
        {
            Vector2 player = new Vector2(3f, -2f);
            Vector2 offset = new Vector2(0.8f, 0.4f);
            Vector2 cursorFromPlayer = new Vector2(4f, 0f);

            Vector2 aim = CameraFeelMath.ResolveAimVector(
                player + offset + cursorFromPlayer, player, offset, 0f);

            Assert.AreEqual(1f, aim.x, 1e-4f);
            Assert.AreEqual(0f, aim.y, 1e-4f,
                "Loop gain must be exactly zero: the aim the camera reads back has to be the " +
                "aim a rigid camera would have produced.");
        }

        [Test]
        public void ResolveAimVector_InsideDeadzone_IsZero()
        {
            Vector2 aim = CameraFeelMath.ResolveAimVector(
                new Vector2(0.9f, 0f), Vector2.zero, Vector2.zero, 1.2f);
            Assert.AreEqual(Vector2.zero, aim,
                "With the cursor on top of the player, every direction is a fixed point — " +
                "the lead has to switch off rather than pick one.");
        }

        [Test]
        public void ResolveLeadTarget_AimSurvivesStandingStill()
        {
            Vector2 lead = CameraFeelMath.ResolveLeadTarget(
                Vector2.zero, Vector2.right, 1.30f, 0.85f, 0.45f, 1.80f);

            Assert.AreEqual(0.85f, lead.magnitude, 1e-4f,
                "Scaling the aim term by movement speed would delete the ability to scan a " +
                "room with the mouse while standing still.");
        }

        [Test]
        public void ResolveLeadTarget_ClampsToMax()
        {
            Vector2 lead = CameraFeelMath.ResolveLeadTarget(
                Vector2.right, Vector2.right, 1.30f, 0.85f, 0.45f, 1.80f);
            Assert.LessOrEqual(lead.magnitude, 1.80f + 1e-4f);
        }

        [Test]
        public void ApplyLeadDeadzone_HoldsBelowThreshold()
        {
            const float wpp = 0.010417f;
            Vector2 current = new Vector2(1f, 1f);

            Vector2 held = CameraFeelMath.ApplyLeadDeadzone(
                current, current + new Vector2(0.5f * wpp, 0f), 0.75f, wpp);
            Assert.AreEqual(current, held, "Sub-pixel creep must not move the camera at all.");

            Vector2 moved = CameraFeelMath.ApplyLeadDeadzone(
                current, current + new Vector2(2f * wpp, 0f), 0.75f, wpp);
            Assert.AreNotEqual(current, moved);
        }

        // ── Noise ───────────────────────────────────────────────────────────

        [Test]
        public void ShakeSample_IsDeterministic()
        {
            Vector2 first = CameraFeelMath.ShakeSample(11.3f, 47.9f, 12.5f, 24f, 1.35f);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(first, CameraFeelMath.ShakeSample(11.3f, 47.9f, 12.5f, 24f, 1.35f));
        }

        [Test]
        public void ShakeSample_AxesAreDecorrelated()
        {
            const int n = 5000;
            double sx = 0, sy = 0, sxy = 0, sxx = 0, syy = 0;

            for (int i = 0; i < n; i++)
            {
                Vector2 s = CameraFeelMath.ShakeSample(11.3f, 47.9f, i * 0.013f, 24f, 1.35f);
                sx += s.x; sy += s.y; sxy += s.x * s.y; sxx += s.x * s.x; syy += s.y * s.y;
            }

            double cov = sxy / n - (sx / n) * (sy / n);
            double sdx = System.Math.Sqrt(sxx / n - (sx / n) * (sx / n));
            double sdy = System.Math.Sqrt(syy / n - (sy / n) * (sy / n));
            double corr = cov / (sdx * sdy);

            Assert.Less(System.Math.Abs(corr), 0.25,
                $"Correlated axes shake along a diagonal instead of shaking. corr={corr:F3}");
        }

        // ── Scaling and classification ──────────────────────────────────────

        [Test]
        public void ScaleByCombo_SaturatesAtCap()
        {
            float atCap = CameraFeelMath.ScaleByCombo(1f, 8, 8, 0.45f);
            float wayPast = CameraFeelMath.ScaleByCombo(1f, 80, 8, 0.45f);
            Assert.AreEqual(atCap, wayPast, 1e-5f);
            Assert.AreEqual(1.45f, atCap, 1e-5f);
        }

        [Test]
        public void SeverityFromDamage_ClampsAndIsMonotonic()
        {
            Assert.AreEqual(0f, CameraFeelMath.SeverityFromDamage(0, 100, 0.25f), 1e-5f);
            Assert.AreEqual(1f, CameraFeelMath.SeverityFromDamage(100, 100, 0.25f), 1e-5f);

            float previous = -1f;
            for (int dmg = 0; dmg <= 25; dmg++)
            {
                float s = CameraFeelMath.SeverityFromDamage(dmg, 100, 0.25f);
                Assert.Greater(s, previous - 1e-6f);
                previous = s;
            }
        }

        [Test]
        public void ScaleByDamage_NeverFallsBelowTheFloor()
        {
            Assert.AreEqual(0.55f, CameraFeelMath.ScaleByDamage(1f, 0, 40f), 1e-5f);
            Assert.AreEqual(1f, CameraFeelMath.ScaleByDamage(1f, 40, 40f), 1e-5f);
            Assert.AreEqual(1f, CameraFeelMath.ScaleByDamage(1f, 400, 40f), 1e-5f);
        }

        [Test]
        public void IsMeleeSwing_ClassifiesTheShippedCatalog()
        {
            Assert.IsTrue(CameraFeelMath.IsMeleeSwing(0f, 0f, 20f), "slash_regular");
            Assert.IsTrue(CameraFeelMath.IsMeleeSwing(0f, 0f, 26f), "slash_cleave");
            Assert.IsFalse(CameraFeelMath.IsMeleeSwing(15f, 0f, 20f), "fireball reaches");
            Assert.IsFalse(CameraFeelMath.IsMeleeSwing(0f, 4.5f, 0f), "dash travels and deals no damage");
        }

        [Test]
        public void IsHeavyCast_BoundaryIsInclusive()
        {
            Assert.IsTrue(CameraFeelMath.IsHeavyCast(0.20f, 0f, 0f, 0.20f, 3f, 25f));
            Assert.IsFalse(CameraFeelMath.IsHeavyCast(0.199f, 0f, 0f, 0.20f, 3f, 25f));
            Assert.IsTrue(CameraFeelMath.IsHeavyCast(0f, 3f, 0f, 0.20f, 3f, 25f));
            Assert.IsFalse(CameraFeelMath.IsHeavyCast(0f, 0.5f, 2f, 0.20f, 3f, 25f), "a fireball is not heavy");
        }

        // ── Geometry ────────────────────────────────────────────────────────

        [Test]
        public void SafeDirection_DegenerateInputReturnsFallback()
        {
            Vector2 fallback = Vector2.up;
            Vector2 d = CameraFeelMath.SafeDirection(Vector2.one, Vector2.one, fallback);
            Assert.AreEqual(fallback, d);
            Assert.IsFalse(float.IsNaN(d.x) || float.IsNaN(d.y));
        }

        [Test]
        public void IsTeleport_DetectsAWarp()
        {
            Assert.IsTrue(CameraFeelMath.IsTeleport(Vector2.zero, new Vector2(7f, 0f), 6f));
            Assert.IsFalse(CameraFeelMath.IsTeleport(Vector2.zero, new Vector2(0.4f, 0f), 6f),
                "Ordinary movement must not be mistaken for a warp.");
        }
    }
}
