using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.Tests.EditMode.UI.MainMenu.Title
{
    /// <summary>
    /// The title's looks: that the shipped one is preserved EXACTLY, that a fire look really is
    /// hotter at its foot than at its crown, and that a red word still clears the carousel.
    ///
    /// <para><b>Why a red title needs a test and a cream one did not.</b> Relative luminance
    /// weights green at 0.7152 and red at 0.2126, so the same colour value in red reaches the eye
    /// at under a third of the light. Measured on the shipped cream word over the menu's own
    /// carousel, contrast already swings from <b>7.5:1</b> on the dark frame to <b>4.2:1</b> on the
    /// bright one — a red word starts below that and can only get back by carrying more plate
    /// under it. That is why <see cref="TitleLook.haloStrength"/> belongs to the LOOK: one shared
    /// constant would be tuned for whichever look was authored last.</para>
    /// </summary>
    public class TitleLookTests
    {
        /// <summary>
        /// The brightest ground the carousel puts under the word, measured off a live capture of
        /// the shipped menu (the rainbow frame): relative luminance 0.055.
        /// </summary>
        private const float BrightCarouselLuminance = 0.055f;

        private static float Luminance(Color c) => MenuUIKit.Luminance(c);

        /// <summary>What the ground becomes once the halo is laid over it.</summary>
        private static float GroundUnderHalo(TitleLook look)
        {
            float tint = Luminance(look.haloTint);
            return Mathf.Lerp(BrightCarouselLuminance, tint, Mathf.Clamp01(look.haloStrength));
        }

        private static float Contrast(float a, float b)
        {
            float hi = Mathf.Max(a, b), lo = Mathf.Min(a, b);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        // ── The shipped look is preserved ────────────────────────────────────

        [Test]
        public void AStyleWithNoLooks_FallsBackToTheShippedFields()
        {
            var style = ScriptableObject.CreateInstance<MenuStyle>();
            try
            {
                style.titleLooks = new TitleLook[0];
                var look = style.ResolveTitleLook();
                Assert.AreEqual(style.titleCore, look.hot);
                Assert.AreEqual(style.titleMid, look.warm);
                Assert.AreEqual(style.titleEdge, look.cool);
                Assert.AreEqual(1f, look.coolAcross, 0.0001f, "the shipped word cools ACROSS its stroke");
                Assert.AreEqual(0f, look.coolUpward, 0.0001f, "the shipped word does not cool upward");
                Assert.AreEqual(0f, look.flickerAmount, 0.0001f, "the shipped word does not flicker");
            }
            finally { Object.DestroyImmediate(style); }
        }

        [Test]
        public void TheShippedAsset_StillCarriesTheOriginalLook_UnderTheNameAscua()
        {
            // Looked up BY NAME rather than by pointing the shipped asset at it. Writing
            // MenuStyle.Active.titleLook here left "ascua" in the Editor's in-memory copy for
            // the rest of the session — a domain reload does not reload assets — so every menu
            // opened after a test run drew the cream word whatever the asset on disk said. The
            // defect was invisible from inside the suite and visible only on screen.
            var style = MenuStyle.Active;
            var legacy = style.LegacyTitleLook();
            var look = LookNamed("ascua");

            Assert.AreEqual("ascua", look.name);
            AssertSameColour(legacy.hot, look.hot, "hot");
            AssertSameColour(legacy.warm, look.warm, "warm");
            AssertSameColour(legacy.cool, look.cool, "cool");
            Assert.AreEqual(legacy.coolAcross, look.coolAcross, 0.001f);
            Assert.AreEqual(legacy.coolUpward, look.coolUpward, 0.001f);
            Assert.AreEqual(legacy.flickerAmount, look.flickerAmount, 0.001f);
            Assert.AreEqual(legacy.shimmerAmplitude, look.shimmerAmplitude, 0.001f);
            Assert.AreEqual(legacy.haloStrength, look.haloStrength, 0.001f);
            Assert.AreEqual(1f, look.glowGain, 0.001f, "the shipped word is not overdriven");
        }

        [Test]
        public void AnUnknownName_FallsBackRatherThanDrawingNothing()
        {
            // A SCRATCH instance, never MenuStyle.Active. Restoring the field in a finally is
            // correct and still leaves the shipped object dirty, which is one AssetDatabase.
            // SaveAssets away from writing a fixture's value over the shipped data — the shape
            // that cost this project 216 building templates.
            var style = ScriptableObject.CreateInstance<MenuStyle>();
            try
            {
                style.titleLooks = MenuStyle.Active.titleLooks;
                style.titleLook = "no-such-look";
                var look = style.ResolveTitleLook();
                Assert.IsNotNull(look, "a title that cannot resolve its look must still draw");
                AssertSameColour(style.titleCore, look.hot, "hot");
            }
            finally { Object.DestroyImmediate(style); }
        }

        // ── The temperature model ────────────────────────────────────────────

        [Test]
        public void WithNoVerticalCooling_HeightChangesNothing()
        {
            var look = MenuStyle.Active.LegacyTitleLook();
            var foot = look.Sample(look.TemperatureOf(0.2f, 0f, 0f));
            var crown = look.Sample(look.TemperatureOf(0.2f, 1f, 0f));
            AssertSameColour(foot, crown, "a flat word must not change with height");
        }

        [Test]
        public void AFireLook_IsHotterAtItsFootThanAtItsCrown()
        {
            foreach (var name in new[] { "volcan", "lava", "colada", "forja" })
            {
                var look = LookNamed(name);
                float foot = look.TemperatureOf(0.2f, 0f, 0f);
                float crown = look.TemperatureOf(0.2f, 1f, 0f);
                Assert.Greater(foot, crown,
                    name + " cools the wrong way: fire is hottest where it is burning");
                Assert.Greater(Luminance(look.Sample(foot)), Luminance(look.Sample(crown)),
                    name + "'s foot must also be BRIGHTER, or the ramp says one thing and the " +
                    "eye reads another");
            }
        }

        [Test]
        public void EveryLook_RampsFromCoolToHot()
        {
            foreach (var look in MenuStyle.Active.titleLooks)
            {
                float cold = Luminance(look.Sample(0f));
                float mid = Luminance(look.Sample(0.5f));
                float hot = Luminance(look.Sample(1f));
                Assert.Less(cold, hot, look.name + ": its hot tone is darker than its cold one");
                Assert.LessOrEqual(cold, mid + 0.001f, look.name + ": the ramp dips in the middle");
                Assert.LessOrEqual(mid, hot + 0.001f, look.name + ": the ramp dips near the top");
            }
        }

        /// <summary>
        /// Flicker may not push a mote off the end of the ramp in a way that loses the word: the
        /// temperature is clamped when sampled, so the test is that the shipped amounts stay
        /// inside a range where the ramp still MOVES rather than saturating at one tone.
        /// </summary>
        [Test]
        public void Flicker_StaysInsideTheRamp()
        {
            foreach (var look in MenuStyle.Active.titleLooks)
            {
                if (look.flickerAmount <= 0f) continue;
                float mid = look.TemperatureOf(0.5f, 0.5f, 0f);
                float up = look.TemperatureOf(0.5f, 0.5f, look.flickerAmount);
                float down = look.TemperatureOf(0.5f, 0.5f, -look.flickerAmount);
                Assert.Less(up, 1.35f, look.name + ": its flicker pins the word at white");
                Assert.Greater(down, -0.35f, look.name + ": its flicker pins the word at black");
                Assert.AreNotEqual(Luminance(look.Sample(up)), Luminance(look.Sample(down)),
                    look.name + ": a flicker that does not change the colour is not a flicker");
                Assert.Greater(mid, -0.35f);
            }
        }

        // ── Legibility, which is what a red word puts at risk ────────────────

        [Test]
        public void EveryLook_ClearsTheBrightestFrameOfTheCarousel()
        {
            foreach (var look in MenuStyle.Active.titleLooks)
            {
                // What the word READS as is its middle tone: most motes land near it.
                float ink = Luminance(look.Sample(0.5f));
                float ground = GroundUnderHalo(look);
                float ratio = Contrast(ink, ground);
                Assert.GreaterOrEqual(ratio, 3.0f,
                    look.name + " reads at " + ratio.ToString("F1") + ":1 over the brightest " +
                    "carousel frame. A title is not body text, but below 3:1 the word stops " +
                    "being separable from a painted face behind it.");
            }
        }

        /// <summary>
        /// A RED look states a higher contrast target and a heavier floor than a cream one.
        ///
        /// <para><b>This replaces an assertion that was measuring an accident.</b> The first
        /// version read "the fire look is the darker ink, so it must carry the heavier plate" —
        /// true when it was written and false the moment the volcano was overdriven
        /// (<c>glowGain 1.45</c>), because the intensity dial on an additive surface IS the
        /// colour. The general rule it was reaching for is arithmetic and is proved without any
        /// look at all in <c>AdaptivePlateTests.ADarkerInk_AsksForMorePlate</c>; what belongs
        /// here is the DESIGN statement, which does not move when somebody retunes a swatch:
        /// red carries 0.2126 of the luminance weight against green's 0.7152, so a look built on
        /// it has to ask for more contrast and sit on more plate than one built on cream.</para>
        /// </summary>
        [Test]
        public void AFireLook_AsksForMoreContrast_ThanTheCreamOne()
        {
            var ascua = LookNamed("ascua");
            foreach (var name in new[] { "volcan", "lava", "colada" })
            {
                var fire = LookNamed(name);
                Assert.GreaterOrEqual(fire.contrastTarget, ascua.contrastTarget,
                    name + " asks for no more contrast than the cream look, on a hue that " +
                    "reaches the eye at under a third of the light");
                Assert.GreaterOrEqual(fire.haloStrength, ascua.haloStrength,
                    name + " sits on no more plate than the cream look");
            }
        }

        /// <summary>
        /// The crown of a fire look is REDDER than anything the cream look draws.
        ///
        /// <para><b>Stated as a RATIO between channels, and that is the whole point.</b> This is
        /// the second assertion in this fixture to have been written as an absolute luminance and
        /// failed for the same reason: <c>glowGain</c> multiplies every channel, so any claim of
        /// the form "this look is darker than that one" is a claim about the intensity dial
        /// rather than about the colour. A ratio is invariant under it — scaling (0.85, 0.13,
        /// 0.03) by 1.45 changes the luminance and leaves G/R and B/R exactly where they were.
        /// Two tuning-dependent assertions in a row is the signal that the property being reached
        /// for was not a property.</para>
        /// </summary>
        [Test]
        public void TheCrownOfAFireLook_IsRedderThanTheCreamWord()
        {
            var creamCold = LookNamed("ascua").Sample(0f);
            float creamGreen = creamCold.g / Mathf.Max(0.0001f, creamCold.r);

            foreach (var name in new[] { "volcan", "lava", "colada" })
            {
                var cold = LookNamed(name).Sample(0f);
                float green = cold.g / Mathf.Max(0.0001f, cold.r);
                Assert.Less(green, creamGreen,
                    name + "'s coldest tone carries as much green as the cream look's, so it is " +
                    "not a red ramp — it is an orange one at another brightness");
                Assert.Less(green, 0.45f,
                    name + "'s crown is not red enough to read as the cold end of a fire");
            }
        }

        [Test]
        public void TheFireLooks_Rise_AndTheShippedOneDoesNot()
        {
            Assert.AreEqual(0f, LookNamed("ascua").riseBias, 0.001f,
                "the shipped word is heat off a bar, and a bar does not rise");
            Assert.Greater(LookNamed("volcan").riseBias, 0f, "fire rises");
            Assert.Greater(LookNamed("volcan").driftAspect, 1f,
                "a flame's drift leans VERTICAL; below 1 it reads as a shiver, not a flame");
        }

        // ── The dials added for the lava word ────────────────────────────────

        /// <summary>
        /// A halo that is not BIGGER than the mote it sits behind is a brightness change wearing
        /// the name of an atmosphere — it adds light exactly where there already was some and
        /// reaches nowhere.
        /// </summary>
        [Test]
        public void AGlowingLook_ReachesPastItsOwnBody()
        {
            foreach (var look in MenuStyle.Active.titleLooks)
            {
                if (look.glowStrength <= 0f) continue;
                Assert.Greater(look.glowScale, 1.5f,
                    look.name + "'s halo is barely wider than its mote, so it cannot read as " +
                    "anything reaching past the letter");
                Assert.Less(look.glowStrength, 0.6f,
                    look.name + "'s halo is strong enough to compete with the body it is " +
                    "supposed to sit behind; on an additive surface that dissolves the stroke");
            }
        }

        /// <summary>
        /// Dripping is a statement that the word is MOLTEN, so only a red look may make it.
        /// A cream word shedding drops is a word melting for no reason the player can see.
        /// </summary>
        [Test]
        public void OnlyAMoltenLook_Drips()
        {
            foreach (var look in MenuStyle.Active.titleLooks)
            {
                if (look.dripRate <= 0f) continue;
                var cold = look.Sample(0f);
                Assert.Less(cold.g / Mathf.Max(0.0001f, cold.r), 0.45f,
                    look.name + " drips without being a red look");
                Assert.LessOrEqual(look.dripRate, 4f,
                    look.name + " drips often enough to be an animation rather than an accent");
            }
        }

        /// <summary>
        /// The look the game actually SHIPS has to use every dial it was given, or the dial is
        /// the authored-and-inert shape this project has shipped a dozen times: a control with a
        /// reader, a tooltip and nobody proving it does anything.
        /// </summary>
        [Test]
        public void TheShippedLook_UsesTheDialsThatWereAddedForIt()
        {
            var look = MenuStyle.Active.ResolveTitleLook();
            Assert.Greater(look.glowStrength, 0f, look.name + " draws no halo");
            Assert.Greater(look.flickerAsymmetry, 0f, look.name + " flickers like a lamp");
            Assert.Greater(look.emberHeat, 0f, look.name + " throws embers colder than its fire");
            Assert.Greater(look.dripRate, 0f, look.name + " never drips");
        }

        /// <summary>
        /// And the four looks that were here first carry NONE of them. Each is a preserved
        /// alternative, and an alternative that silently grew a halo the day somebody added one
        /// is not preserved — it is a different look under the same name.
        /// </summary>
        [Test]
        public void ThePreservedLooks_CarryNoneOfTheNewDials()
        {
            foreach (var name in new[] { "ascua", "volcan", "colada", "forja" })
            {
                var look = LookNamed(name);
                Assert.AreEqual(0f, look.glowStrength, 0.0001f, name + " grew a halo");
                Assert.AreEqual(0f, look.flickerAsymmetry, 0.0001f, name + " grew an asymmetry");
                Assert.AreEqual(0f, look.emberHeat, 0.0001f, name + " grew a hot ember");
                Assert.AreEqual(0f, look.dripRate, 0.0001f, name + " grew a drip");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static TitleLook LookNamed(string name)
        {
            foreach (var look in MenuStyle.Active.titleLooks)
                if (look.name == name) return look;
            Assert.Fail("the shipped style no longer carries the look '" + name + "'");
            return null;
        }

        private static void AssertSameColour(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 0.002f, what + ".r");
            Assert.AreEqual(expected.g, actual.g, 0.002f, what + ".g");
            Assert.AreEqual(expected.b, actual.b, 0.002f, what + ".b");
        }
    }
}
