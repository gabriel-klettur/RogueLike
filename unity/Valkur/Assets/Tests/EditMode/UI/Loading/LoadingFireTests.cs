using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.Loading;

namespace Valkur.Tests.EditMode.UI.Loading
{
    /// <summary>
    /// The embers over the dragon's painted fire.
    ///
    /// <para>What these fixtures can and cannot see is the usual EditMode split, and it is worth
    /// stating because it decides every assertion below. uGUI runs no layout pass here, so a
    /// rect is whatever was written into it and never what a layout would have made of it; no
    /// component added here receives <c>Awake</c>, so nothing starts itself; and
    /// <c>Object.Destroy</c> is an outright error, so every teardown is immediate. What IS
    /// checkable is the arithmetic — where the anchors are, that the pool is a ceiling, and that
    /// a frame lasting three seconds is integrated as one step rather than as three seconds.</para>
    /// </summary>
    public class LoadingFireTests
    {
        private const string ShippedArt = "background_ini";

        private GameObject _root;
        private RectTransform _art;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("LoadingFireTestRoot", typeof(RectTransform), typeof(Canvas));
            var artGo = new GameObject("Background", typeof(RectTransform));
            artGo.transform.SetParent(_root.transform, false);
            _art = (RectTransform)artGo.transform;
            // Anchored to a point rather than stretched, so sizeDelta IS the rect with no layout
            // pass to run — the size the painting would have at 1600x800 under EnvelopeParent.
            _art.anchorMin = _art.anchorMax = new Vector2(0.5f, 0.5f);
            _art.sizeDelta = new Vector2(1600f, 1066f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // ── The anchors ──────────────────────────────────────────────────────

        [Test]
        public void TheShippedPainting_CarriesAMeasuredJet()
        {
            var a = LoadingArtAnchors.FireFor(ShippedArt);
            Assert.IsTrue(a.IsValid, "the shipped loading art lost its measurement");

            foreach (var p in new[] { a.Mouth, a.Tip })
            {
                Assert.That(p.x, Is.InRange(0f, 1f), "an anchor left the painting");
                Assert.That(p.y, Is.InRange(0f, 1f), "an anchor left the painting");
            }

            // The dragon is on the right and breathes down and to the left. Both halves are
            // stated, because a sign flip in either is a jet pointing into the creature's own
            // face and every other assertion here would still pass.
            Assert.Less(a.Tip.x, a.Mouth.x, "the jet does not run away from the mouth");
            Assert.Less(a.Tip.y, a.Mouth.y, "the jet does not fall toward the ground");

            // A jet SPREADS. Getting this backwards makes the embers converge to a point at the
            // far end, which reads as them being sucked in.
            Assert.Greater(a.TipHalfWidth, a.MouthHalfWidth, "the jet narrows as it travels");
            Assert.Greater(a.MouthHalfWidth, 0f);
        }

        [Test]
        public void AnUnmeasuredPainting_AnswersNothingRatherThanAGuess()
        {
            Assert.IsFalse(LoadingArtAnchors.FireFor("no_such_art").IsValid);
            Assert.IsFalse(LoadingArtAnchors.FireFor(null).IsValid);
            Assert.IsFalse(LoadingArtAnchors.FireFor(string.Empty).IsValid);
        }

        [Test]
        public void TheAxis_RunsFromTheMouthToTheLanding()
        {
            var a = LoadingArtAnchors.FireFor(ShippedArt);
            Assert.AreEqual(a.Mouth, a.Axis(0f));
            Assert.AreEqual(a.Tip, a.Axis(1f));
            Assert.AreEqual(a.MouthHalfWidth, a.HalfWidthAt(0f), 1e-5f);
            Assert.AreEqual(a.TipHalfWidth, a.HalfWidthAt(1f), 1e-5f);
            Assert.AreEqual(1f, a.Direction.magnitude, 1e-4f);
        }

        // ── The effect ───────────────────────────────────────────────────────

        [Test]
        public void TheFire_RefusesToAttachWhereNothingWasMeasured()
        {
            Assert.IsNull(LoadingFireFX.Attach(_art, "no_such_art", MenuStyle.Active),
                "a painting with no anchor must draw nothing, not a jet guessed at the middle");
            Assert.IsNull(LoadingFireFX.Attach(null, ShippedArt, MenuStyle.Active),
                "no painting, no fire — the black fallback background is exactly this case");
        }

        [Test]
        public void TheFire_BurnsOverTheShippedPainting_AndStaysInsideItsPool()
        {
            var fire = LoadingFireFX.Attach(_art, ShippedArt, MenuStyle.Active);
            Assert.IsNotNull(fire);
            Assert.IsTrue(fire.IsLive);
            Assert.AreEqual(0, fire.EmberCount, "nothing is alive before the first tick");

            for (int i = 0; i < 200; i++) fire.Tick(1f / 60f);

            Assert.Greater(fire.EmberCount, 0, "three seconds of jet produced no ember at all");
            Assert.Greater(fire.SmokeCount, 0, "the fire is landing on nothing");
            // The pool is a ceiling and an emit past it is dropped, never grown. A rate that
            // outruns the pool is how a loading screen starts allocating while the main thread
            // is busy building a world.
            Assert.LessOrEqual(fire.EmberCount, 96);
            Assert.LessOrEqual(fire.SmokeCount, 24);

            fire.Dispose();
        }

        /// <summary>
        /// A long frame is paid IN FULL, and that is the stutter fix.
        ///
        /// <para>The first version capped the step at 50 ms, which also capped the emission debt
        /// (<c>rate x dt</c>). Measured on a real boot — 49 frames in 4201 ms, some of 145 ms —
        /// that emitted 56 % of the intended embers, and the shortfall tracked the frame length:
        /// short frames at full rate, long ones cut to a third, so the jet thinned and refilled
        /// with the boot's own pacing. Sub-stepping pays the whole frame in bounded pieces.</para>
        /// </summary>
        [Test]
        public void ALongFrame_IsSimulatedInFull_NotClampedToOneStep()
        {
            var fire = LoadingFireFX.Attach(_art, ShippedArt, MenuStyle.Active);
            fire.Tick(0.145f);
            Assert.AreEqual(0.145f, fire.SimulatedSeconds, 0.001f,
                "a 145 ms boot frame was simulated as " + fire.SimulatedSeconds + " s — the jet " +
                "falls behind real time on exactly the frames the boot makes long, which is the stutter");
            Assert.Greater(fire.EmberCount, 0);
            fire.Dispose();
        }

        /// <summary>
        /// A STALL is paid only up to the catch-up budget, and the rest is dropped.
        ///
        /// <para>A three-second frame is a stall, not a frame to replay. Simulating all of it at once
        /// fills the 96-deep pool from one frame (46 a second x 3 s = 138) and draws a jet that was
        /// never seen travelling. Six sub-steps of 50 ms is the ceiling.</para>
        /// </summary>
        [Test]
        public void AStall_IsPaidOnlyUpToTheCatchUpBudget()
        {
            var fire = LoadingFireFX.Attach(_art, ShippedArt, MenuStyle.Active);
            fire.Tick(3f);
            Assert.AreEqual(0.30f, fire.SimulatedSeconds, 0.001f,
                "a stall was chased instead of dropped");
            Assert.Less(fire.EmberCount, 20,
                "a three-second frame emitted " + fire.EmberCount + " embers: the stall reached the pool");
            Assert.Greater(fire.EmberCount, 0, "the capped catch-up emitted nothing at all");
            fire.Dispose();
        }

        [Test]
        public void ANonPositiveStep_ChangesNothing()
        {
            var fire = LoadingFireFX.Attach(_art, ShippedArt, MenuStyle.Active);
            fire.Tick(0f);
            fire.Tick(-1f);
            Assert.AreEqual(0, fire.EmberCount);
            fire.Dispose();
        }

        /// <summary>
        /// THE resolution test. The decoration is drawn over a PAINTING, so where it belongs is
        /// a fact about the painting and not about the window — and the first version got this
        /// wrong in a way that was invisible at the size it was authored at.
        ///
        /// <para>Measured before the fix, on the shipped anchor: the muzzle sat exactly on the
        /// dragon's mouth at 1600x1066 and drifted to (0.515, 0.517) at 3840x2560 and to
        /// (0.555, 0.564) at 1024x683, while its diameter ran from 3.3 % of the width to 1.4 %
        /// and then to 5.1 %. The wash moved a QUARTER of the painting. Every number came from
        /// an <c>anchoredPosition</c> and a <c>sizeDelta</c> computed once against whatever rect
        /// existed at build time.</para>
        ///
        /// <para>It asserts the REALIZED rect and not only the anchors, because anchors that are
        /// correct with a non-zero offset still drift — the offset is the pixel, and a pixel is
        /// the thing that cannot survive a change of resolution.</para>
        /// </summary>
        [Test]
        public void TheDecoration_KeepsItsPlaceOnThePainting_AtEveryResolution()
        {
            var fire = LoadingFireFX.Attach(_art, ShippedArt, MenuStyle.Active);
            Assert.IsNotNull(fire);
            fire.Tick(1f / 60f);          // the first tick is what lays it out against a real rect

            var a = LoadingArtAnchors.FireFor(ShippedArt);
            var sizes = new[]
            {
                new Vector2(1600f, 1066f),     // the window this was authored at
                new Vector2(3840f, 2560f),     // 4K
                new Vector2(1024f, 683f),      // a small window
                new Vector2(2560f, 1706f),
            };

            Vector2 firstMuzzle = Vector2.zero, firstWash = Vector2.zero;
            float firstMuzzleWidth = 0f, firstWashWidth = 0f;

            for (int i = 0; i < sizes.Length; i++)
            {
                _art.sizeDelta = sizes[i];
                fire.Tick(1f / 60f);

                var muzzle = Fraction("FireMuzzle", out float muzzleW, out float muzzleH);
                var wash = Fraction("FireWash", out float washW, out float washH);

                if (i == 0)
                {
                    firstMuzzle = muzzle; firstWash = wash;
                    firstMuzzleWidth = muzzleW; firstWashWidth = washW;

                    // The muzzle is ON the mouth, and the wash is PAST the landing — the two
                    // facts the whole anchor table exists to state.
                    Assert.AreEqual(a.Mouth.x, muzzle.x, 0.002f, "the muzzle left the dragon's mouth");
                    Assert.AreEqual(a.Mouth.y, muzzle.y, 0.002f, "the muzzle left the dragon's mouth");
                    Assert.Less(wash.x, a.Tip.x, "the wash is not past where the fire lands");
                }
                else
                {
                    string at = " at " + sizes[i].x + "x" + sizes[i].y;
                    Assert.AreEqual(firstMuzzle.x, muzzle.x, 0.002f, "the muzzle drifted across" + at);
                    Assert.AreEqual(firstMuzzle.y, muzzle.y, 0.002f, "the muzzle drifted up or down" + at);
                    Assert.AreEqual(firstWash.x, wash.x, 0.002f, "the wash drifted across" + at);
                    Assert.AreEqual(firstWash.y, wash.y, 0.002f, "the wash drifted up or down" + at);
                    Assert.AreEqual(firstMuzzleWidth, muzzleW, 0.002f, "the muzzle changed size" + at);
                    Assert.AreEqual(firstWashWidth, washW, 0.002f, "the wash changed size" + at);
                }

                // Square at every size: the painting's aspect is constant under EnvelopeParent,
                // and a blob that stretches with the window means that assumption broke.
                Assert.AreEqual(1f, muzzleH / Mathf.Max(0.0001f, muzzleW), 0.02f,
                    "the muzzle is an ellipse at " + sizes[i].x + "x" + sizes[i].y);
                Assert.AreEqual(1f, washH / Mathf.Max(0.0001f, washW), 0.02f,
                    "the wash is an ellipse at " + sizes[i].x + "x" + sizes[i].y);
            }

            fire.Dispose();
        }

        /// <summary>
        /// A blob stays a CIRCLE even if the painting's own proportion changes.
        ///
        /// <para>It cannot today — <c>EnvelopeParent</c> keeps the shipped art at 3:2 whatever
        /// the window does — and that is exactly why it is worth pinning: the placement derives
        /// its vertical half-extent from the LIVE aspect, and the day a second loading painting
        /// arrives with another shape, the alternative is every blob silently becoming an
        /// ellipse. A guarantee that holds only because of a constant nobody has changed yet is
        /// not a guarantee.</para>
        /// </summary>
        [Test]
        public void ABlob_StaysCircular_WhateverThePaintingsProportion()
        {
            var fire = LoadingFireFX.Attach(_art, ShippedArt, MenuStyle.Active);
            var a = LoadingArtAnchors.FireFor(ShippedArt);

            foreach (var size in new[] { new Vector2(1600f, 1066f),    // the shipped 3:2
                                         new Vector2(1600f, 900f),     // 16:9
                                         new Vector2(1200f, 1200f) })  // square
            {
                _art.sizeDelta = size;
                fire.Tick(1f / 60f);

                var centre = Fraction("FireMuzzle", out float w, out float h);
                string at = " on a " + (size.x / size.y).ToString("F2") + " painting";
                Assert.AreEqual(1f, h / Mathf.Max(0.0001f, w), 0.02f, "the muzzle is an ellipse" + at);
                Assert.AreEqual(a.Mouth.x, centre.x, 0.002f, "the muzzle left the mouth" + at);
                Assert.AreEqual(a.Mouth.y, centre.y, 0.002f, "the muzzle left the mouth" + at);
            }

            fire.Dispose();
        }

        /// <summary>
        /// A child's centre as a fraction of the painting, plus its width and height BOTH as
        /// fractions of the painting's width — so the blob is square exactly when the two come
        /// back equal, whatever the painting's own aspect happens to be.
        /// </summary>
        private Vector2 Fraction(string childName, out float widthFraction, out float heightFraction)
        {
            var rt = _art.Find(childName) as RectTransform;
            Assert.IsNotNull(rt, childName + " is not under the painting any more");
            var parent = _art.rect.size;
            widthFraction = rt.rect.width / parent.x;
            heightFraction = rt.rect.height / parent.x;
            // localPosition is the centre relative to the parent's pivot, which is the middle.
            var centre = (Vector2)rt.localPosition + parent * 0.5f;
            return new Vector2(centre.x / parent.x, centre.y / parent.y);
        }

        // ── The rule, read off the source ────────────────────────────────────

        /// <summary>
        /// A source scan, because the rule is about what the code DOES NOT do and no runtime
        /// assertion can see a <c>ParticleSystem</c> that was never written. A particle system is
        /// a world renderer: it sorts either in front of the progress bar or behind the painting,
        /// never between them, and this project has now recorded that four times.
        /// </summary>
        [Test]
        public void NothingInTheLoadingScreen_UsesAParticleSystem()
        {
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/UI/Loading");
            Assert.IsTrue(Directory.Exists(dir), dir + " is not where the loading screen lives any more");

            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string source = StripComments(File.ReadAllText(file));
                Assert.IsFalse(Regex.IsMatch(source, @"\bParticleSystem\b"),
                    Path.GetFileName(file) + " reaches for a ParticleSystem, which cannot sort " +
                    "against the Graphics of an overlay canvas");
            }
        }

        /// <summary>
        /// Comments are stripped first, because this very file's prose NAMES the thing the scan
        /// forbids — the trap that turned three source-scanning fixtures red the last time this
        /// project moved a call one level down.
        /// </summary>
        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(source, @"//[^\n]*", string.Empty);
        }
    }
}
