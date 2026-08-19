using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers the charges that travel along the laser from the caster to the impact point.
    ///
    /// The first attempt at this scrolled a texture along a full-length line and rendered
    /// completely static. Two independent reasons, either of which alone was fatal:
    ///
    /// 1. URP's particle shader ignores the ST transform. It has no <c>_MainTex</c>, and its
    ///    forward pass never applies <c>_BaseMap_ST</c> — <c>GetParticleTexcoords</c> assigns
    ///    <c>outputTexcoord = inputTexcoords.xy</c> and that raw UV0 is what gets sampled.
    ///    So every scroll write was a no-op.
    /// 2. <c>LineTextureMode.Tile</c> derives U from length, so the "single packet" repeated
    ///    dozens of times and read as flat white.
    ///
    /// On top of that the beam was additively saturated past 1.0 before the packet contributed
    /// anything, so it had no headroom to be brighter in.
    ///
    /// The charge is geometry now — a short line whose endpoints slide — which depends on no
    /// shader feature at all. These tests pin the parts that make that read as motion.
    /// </summary>
    [TestFixture]
    public class BeamPacketTests
    {
        private const float SOFTNESS = 0.5f;   // the value LaserBeamController authors with
        private const int SAMPLES = 512;

        private static float Centre(float along) =>
            BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Packet, 0f, along, SOFTNESS);

        // ── Motion ───────────────────────────────────────────────────────────────

        [Test]
        public void TheChargeStartsAtTheCasterAndEndsAtTheImpactPoint()
        {
            const float L = 10f, P = 1.7f;

            LaserBeamController.ResolvePacketSpan(0.02f, L, P, out float earlyFrom, out float earlyTo);
            LaserBeamController.ResolvePacketSpan(0.98f, L, P, out float lateFrom, out float lateTo);

            Assert.AreEqual(0f, earlyFrom, 1e-4f, "It must emerge from the muzzle, not mid-beam.");
            Assert.Less(earlyTo, L * 0.5f);
            Assert.AreEqual(L, lateTo, 1e-4f, "It must reach the impact point.");
            Assert.Greater(lateFrom, L * 0.5f);
        }

        [Test]
        public void TheChargeAdvancesMonotonicallyOutward()
        {
            const float L = 10f, P = 1.7f;
            float prevFrom = -1f, prevTo = -1f;
            int visibleSamples = 0;

            for (int i = 0; i <= 200; i++)
            {
                if (!LaserBeamController.ResolvePacketSpan(i / 200f, L, P, out float from, out float to))
                    continue;

                visibleSamples++;
                Assert.GreaterOrEqual(from, prevFrom - 1e-4f, "The trailing end went backwards.");
                Assert.GreaterOrEqual(to, prevTo - 1e-4f, "The leading end went backwards.");
                prevFrom = from;
                prevTo = to;
            }

            Assert.Greater(visibleSamples, 150,
                "The charge should be on screen for most of its cycle, not flicker in and out.");
        }

        [Test]
        public void TheChargeGrowsOutOfTheMuzzleAndIsAbsorbedAtTheTip()
        {
            const float L = 10f, P = 1.7f;

            // Mid-flight it is exactly its authored length; at the two ends it is clipped by
            // the beam, which is what makes it emerge and land rather than pop.
            LaserBeamController.ResolvePacketSpan(0.5f, L, P, out float midFrom, out float midTo);
            Assert.AreEqual(P, midTo - midFrom, 1e-3f);

            LaserBeamController.ResolvePacketSpan(0.05f, L, P, out float aFrom, out float aTo);
            Assert.Less(aTo - aFrom, P, "Emerging: still partly inside the caster.");

            LaserBeamController.ResolvePacketSpan(0.97f, L, P, out float bFrom, out float bTo);
            Assert.Less(bTo - bFrom, P, "Landing: partly past the impact point.");
        }

        [Test]
        public void ADegenerateSpanIsReportedInsteadOfDrawn()
        {
            // A LineRenderer given two identical points draws a dot at the origin, which reads
            // as a bead stuck on the player rather than as nothing.
            Assert.IsFalse(LaserBeamController.ResolvePacketSpan(0f, 10f, 1.7f, out _, out _));
            Assert.IsFalse(LaserBeamController.ResolvePacketSpan(1f, 10f, 1.7f, out _, out _));
            Assert.IsFalse(LaserBeamController.ResolvePacketSpan(0.5f, 0f, 1.7f, out _, out _));
            Assert.IsFalse(LaserBeamController.ResolvePacketSpan(0.5f, 10f, 0f, out _, out _));
        }

        [Test]
        public void ItWorksOnABeamShorterThanTheCharge()
        {
            // Point blank against a wall. The charge must stay inside the beam rather than
            // overshoot past the impact point.
            const float L = 0.8f, P = 1.7f;

            for (int i = 0; i <= 100; i++)
            {
                if (!LaserBeamController.ResolvePacketSpan(i / 100f, L, P, out float from, out float to))
                    continue;

                Assert.GreaterOrEqual(from, 0f);
                Assert.LessOrEqual(to, L + 1e-4f);
            }
        }

        [Test]
        public void TheChargesAreStaggeredRatherThanStacked()
        {
            Assert.GreaterOrEqual(LaserBeamController.PACKET_COUNT, 2,
                "One charge leaves a visible gap between trips; two keep the flow reading as steady.");

            // Phases are spaced 1/N apart, so at any moment no two occupy the same span.
            const float L = 10f;
            float p0 = 0.2f;
            float p1 = Mathf.Repeat(p0 + (1f / LaserBeamController.PACKET_COUNT), 1f);

            LaserBeamController.ResolvePacketSpan(p0, L, LaserBeamController.PACKET_LENGTH, out float a0, out float b0);
            LaserBeamController.ResolvePacketSpan(p1, L, LaserBeamController.PACKET_LENGTH, out float a1, out float b1);

            bool overlap = a1 < b0 && a0 < b1;
            Assert.IsFalse(overlap, "Staggered charges must not sit on top of each other.");
        }

        // ── The charge's shape ───────────────────────────────────────────────────

        [Test]
        public void BothEndsFadeToNothing()
        {
            // This line has hard geometric ends. Residual alpha there is a cut edge that
            // travels along the beam — more distracting than no packet at all.
            Assert.AreEqual(0f, Centre(0f), 1e-3f);
            Assert.AreEqual(0f, Centre(1f), 1e-3f);
        }

        [Test]
        public void ThereIsExactlyOneHead()
        {
            int maxima = 0;
            for (int i = 1; i < SAMPLES - 1; i++)
            {
                float prev = Centre((i - 1) / (float)SAMPLES);
                float cur = Centre(i / (float)SAMPLES);
                float next = Centre((i + 1) / (float)SAMPLES);
                if (cur > prev && cur >= next) maxima++;
            }

            Assert.AreEqual(1, maxima, "Two heads read as two charges crammed into one line.");
        }

        [Test]
        public void TheHeadLeadsAndTheTailFollows()
        {
            int peakIdx = 0;
            float peak = -1f;
            for (int i = 0; i < SAMPLES; i++)
            {
                float v = Centre(i / (float)SAMPLES);
                if (v > peak) { peak = v; peakIdx = i; }
            }

            float peakAlong = peakIdx / (float)SAMPLES;
            Assert.Greater(peakAlong, 0.6f,
                "The bright head must sit toward the LEADING end (U=1). A centred blob has no " +
                "direction and reads as a throb rather than as something being fired.");

            // Brightness at equal distances either side of the head. This is the asymmetry
            // itself, rather than the head's own width at half-max — the streak is deliberately
            // dimmer than the head, so it never crosses that threshold and measuring there
            // reports on the gaussian instead of on the tail.
            const float OFFSET = 0.15f;
            float behind = Centre(peakAlong - OFFSET);
            float ahead = Centre(peakAlong + OFFSET);

            Assert.Greater(behind, ahead * 3f,
                $"At {OFFSET:0.00} either side of the head the charge is {behind:0.000} behind " +
                $"and {ahead:0.000} ahead. Without a pronounced streak the eye has nothing to " +
                "infer a direction of travel from.");
        }

        [Test]
        public void TheChargesMassSitsBehindItsHead()
        {
            // The same asymmetry stated as a whole-shape property, so retuning the head or the
            // tail independently cannot satisfy the point-sample check above while leaving the
            // charge looking symmetric overall.
            float weighted = 0f, total = 0f, peak = -1f, peakAlong = 0f;
            for (int i = 0; i < SAMPLES; i++)
            {
                float along = i / (float)SAMPLES;
                float v = Centre(along);
                weighted += along * v;
                total += v;
                if (v > peak) { peak = v; peakAlong = along; }
            }

            Assert.Greater(total, 0f);
            float centroid = weighted / total;

            Assert.Less(centroid, peakAlong - 0.05f,
                $"Centre of mass {centroid:0.000} against a head at {peakAlong:0.000}. The mass " +
                "must sit clearly behind the head, which is what a trailing streak means.");
        }

        [Test]
        public void ItStillFallsOffAcrossItsWidth()
        {
            float atCentre = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Packet, 0f, 0.82f, SOFTNESS);
            float atEdge = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Packet, 0.95f, 0.82f, SOFTNESS);

            Assert.Greater(atCentre, atEdge * 2f);
            Assert.AreEqual(0f, BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Packet, 1f, 0.82f, SOFTNESS));
        }

        // ── Headroom ─────────────────────────────────────────────────────────────

        [Test]
        public void TheBeamLeavesRoomForTheChargeToBeBrighter()
        {
            // Additive blending accumulates dst += rgb * a. If the core and glow already sum
            // past 1.0 on the centreline, the beam is clipped white and no charge can read,
            // however well it is animated. That is precisely why the first version was
            // invisible, so the budget is asserted rather than left to drift.
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts",
                "Gameplay", "Spells", "Controllers", "LaserBeamController.cs"));

            float coreAlpha = ParseConst(src, "CORE_ALPHA");
            float glowAlpha = ParseConst(src, "GLOW_ALPHA");
            float packetAlpha = ParseConst(src, "PACKET_ALPHA");

            float coreTex = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Core, 0f, 0.5f, 0.25f);
            float glowTex = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Glow, 0f, 0.5f, 0.80f);

            float baseline = (coreTex * coreAlpha) + (glowTex * glowAlpha);

            Assert.Less(baseline, 0.85f,
                $"Baseline centreline luminance is {baseline:0.000}. Above ~0.85 the beam is " +
                "effectively clipped and a travelling charge cannot be seen at all.");

            float peakTexEarly = 0f;
            for (int i = 0; i <= SAMPLES; i++)
                peakTexEarly = Mathf.Max(peakTexEarly, Centre(i / (float)SAMPLES));
            float chargeAdds = peakTexEarly * packetAlpha;

            // Stated against the charge rather than as an absolute floor. The upper bound alone
            // would let the beam be dimmed indefinitely to buy headroom, trading the saturation
            // bug for a beam that no longer reads as incandescent. But "0.40" was a number
            // picked out of the air, and any legitimate retune of the alphas would have tripped
            // it. What actually matters is the RATIO: if the steady beam is dim relative to what
            // a passing charge adds, the eye stops seeing a laser that pulses and starts seeing
            // a string of pulses with nothing between them.
            Assert.Greater(baseline, chargeAdds * 0.5f,
                $"Baseline centreline luminance is {baseline:0.000} against a charge that adds " +
                $"{chargeAdds:0.000}. The beam has become a carrier for the charges rather than " +
                "a beam they travel along.");

            // The true maximum, not the head's centre — the tail is one-sided and worth
            // nothing exactly at the head, so sampling there understates the charge by a third.
            float peakTex = 0f;
            for (int i = 0; i <= SAMPLES; i++)
                peakTex = Mathf.Max(peakTex, Centre(i / (float)SAMPLES));
            float contrast = peakTex * packetAlpha;

            Assert.Greater(contrast, 0.25f,
                $"The charge only adds {contrast:0.000} over the baseline — too little to read " +
                "as an event travelling down the beam.");
        }

        private static float ParseConst(string source, string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                source, @"\b" + name + @"\s*=\s*([0-9.]+)f");
            Assert.IsTrue(m.Success, $"{name} not found — the alpha budget test cannot verify anything.");
            return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        [Test]
        public void TheChargesLeaveAGapForTheEyeToMeasureTravelAgainst()
        {
            // Coverage, not a constant product: what matters is how much of the beam is lit by
            // a charge at once. Past roughly two thirds the charges merge into a continuous
            // bright rope and there is no dark stretch left for the eye to judge motion
            // against — the beam goes back to looking uniformly on.
            const float L = LaserBeamController.DEFAULT_RANGE;
            float worst = 0f;

            for (int step = 0; step <= 200; step++)
            {
                float phase = step / 200f;
                float covered = 0f;

                for (int i = 0; i < LaserBeamController.PACKET_COUNT; i++)
                {
                    float p = Mathf.Repeat(phase + (i / (float)LaserBeamController.PACKET_COUNT), 1f);
                    if (LaserBeamController.ResolvePacketSpan(p, L, LaserBeamController.PACKET_LENGTH,
                                                              out float from, out float to))
                        covered += to - from;
                }

                worst = Mathf.Max(worst, covered / L);
            }

            Assert.Less(worst, 0.66f,
                $"At its busiest the charges cover {worst:P0} of the beam. Raising PACKET_COUNT " +
                "or PACKET_LENGTH past this point removes the gap that makes the travel legible.");
        }

        // ── Wiring ───────────────────────────────────────────────────────────────

        [Test]
        public void ThePacketLinesStretchTheirTextureExactlyOnce()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts",
                "Gameplay", "Spells", "Controllers", "LaserBeamController.Visual.cs"));

            Assert.IsTrue(src.Contains("textureMode = LineTextureMode.Stretch"),
                "Tile derives U from world length, so the charge's head, tail and end-fades " +
                "repeat many times over and collapse into uniform white. Stretch is what makes " +
                "one copy span the charge.");
        }

        [Test]
        public void ThePacketLinesAreTornDownWithTheBeam()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts",
                "Gameplay", "Spells", "Controllers", "LaserBeamController.cs"));

            Assert.IsTrue(src.Contains("if (p != null) Destroy(p.gameObject);"),
                "The beam parents its visuals to the caster, so anything not destroyed on " +
                "teardown survives as a stuck line on the player.");
        }

        [Test]
        public void NothingTriesToScrollATextureAnyMore()
        {
            // The scroll path was a silent no-op on this shader. Leaving a caller behind would
            // read as a working mechanism to the next person to touch the file.
            string dir = Path.Combine(Application.dataPath, "_Project", "Scripts");
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    string t = line.TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;

                    Assert.IsFalse(t.Contains("_MainTex_ST") || t.Contains("_BaseMap_ST"),
                        $"{file}: URP's particle shader applies neither. Animating them looks " +
                        "like it works — it compiles, the value lands in the property block, " +
                        "and nothing on screen moves.");
                }
            }
        }
    }
}
