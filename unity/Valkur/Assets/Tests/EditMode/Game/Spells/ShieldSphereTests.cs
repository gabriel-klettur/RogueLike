using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Pins the magic shield sphere: that its authored radius reaches the drawn shell, that a
    /// blocked blow is announced, and that a spell's own swatch reaches its cast flourish.
    ///
    /// <para>Every one of these guards a failure that produced NO error and NO warning. The
    /// radius was divided by 16 and then overwritten by a hard-coded constant, so the dial did
    /// nothing and looked fine. The invincibility check returned in silence, so a shield could
    /// absorb a whole fight without a frame of feedback. And 39 spells authored a colour the
    /// flourish never read.</para>
    /// </summary>
    public class ShieldSphereTests
    {
        private const string ShieldPath =
            "Assets/_Project/Data/Catalogs/Spells/sphere_magic_shield.asset";

        private static SpellDefinition LoadShield()
            => AssetDatabase.LoadAssetAtPath<SpellDefinition>(ShieldPath);

        // ── the asset ───────────────────────────────────────────────────────────────

        [Test]
        public void TheShieldShipsAsASphereWithAnAuthoredSwatch()
        {
            var spell = LoadShield();
            Assert.IsNotNull(spell, "sphere_magic_shield is missing from " + ShieldPath);
            Assert.AreEqual(SpellType.SphereMagicShield, spell.type);
            Assert.Greater(spell.duration, 0f, "a shield with no duration is not a shield");
            Assert.AreEqual(1, spell.maxInstances,
                "two overlapping shields share one invincibility flag, and the first to expire "
                + "drops it for both");

            Assert.IsFalse(KiPalette.IsUnauthored(spell.particleColor),
                "The shield derives its whole palette from this one swatch. Left at the "
                + "unauthored white it falls back to a generic pale blue and the F4 colour "
                + "picker appears to do nothing.");
        }

        [Test]
        public void TheRadiusIsInWorldUnitsNotPixels()
        {
            var spell = LoadShield();

            // The regression this guards is specific and it has happened twice in this project:
            // a value authored in PIXELS surviving from the Python build, divided by 16 (or 32)
            // on the way in. wall_ice shipped for months as a barrier 0.78 units wide that way.
            // A sphere has to clear the body it encloses and must not fill the viewport, which
            // is 10 world units tall.
            Assert.Greater(spell.radius, 0.6f,
                "radius " + spell.radius + " would not clear a character — this is the pixel bug");
            Assert.Less(spell.radius, 4f,
                "radius " + spell.radius + " is most of the screen; the camera sees 10 units");
        }

        // ── the geometry the rim constant rests on ──────────────────────────────────

        [Test]
        public void TheRingSpriteIsExactlyOneWorldUnit()
        {
            // ShieldSphereFX pins the rim to the authored radius with `scale = radius / 0.39`,
            // which is only correct because Ring is 1 world unit across at scale 1 and its
            // bright band peaks at normalized radius 0.78. If the sprite's PPU ever stops
            // matching its texture size that arithmetic silently draws the boundary somewhere
            // else — invisible in code, and the exact failure that left the arcane flame's only
            // hard contour 40 % inside the circle that actually hurt.
            ElementalSprites.EnsureAll();
            var ring = ElementalSprites.Ring;
            Assert.IsNotNull(ring);

            float worldWidth = ring.rect.width / ring.pixelsPerUnit;
            float worldHeight = ring.rect.height / ring.pixelsPerUnit;
            Assert.AreEqual(1f, worldWidth, 0.001f, "Ring is no longer 1 world unit wide");
            Assert.AreEqual(1f, worldHeight, 0.001f, "Ring is no longer 1 world unit tall");
        }

        [Test]
        public void TheShieldHasItsOwnExecutorAndCanDissipate()
        {
            var executor = SpellCaster.GetExecutor(SpellType.SphereMagicShield);
            Assert.IsNotNull(executor,
                "With no executor the caster silently falls back to Projectile and fires a bolt.");
            Assert.IsInstanceOf<ShieldExecutor>(executor);

            Assert.IsTrue(typeof(ISpellEffectDissipates).IsAssignableFrom(typeof(ShieldController)),
                "maxInstances is 1, so recasting EVICTS the live shield. Without this the "
                + "registry destroys it outright and the shell vanishes mid-frame.");
        }

        [Test]
        public void TheAuthoringSurfaceIsReachableInTheEditor()
        {
            var spell = LoadShield();
            Assert.IsTrue(SpellFieldRelevance.Applies(spell, "radius"));
            Assert.IsTrue(SpellFieldRelevance.Applies(spell, "duration"));
            Assert.IsTrue(SpellFieldRelevance.Applies(spell, "particleColor"));
        }

        [Test]
        public void TheShieldIsTrackedBeforeItClaimsInvincibility()
        {
            // Tracking is what EVICTS the previous shield, and eviction restores the
            // invincibility flag that shield had claimed. Run in the other order and the
            // sequence goes backwards: the new shield claims the flag, then the old one's
            // teardown puts it back — the player stands unprotected inside a shell that has
            // just visibly closed around them. Measured on a double cast before this was
            // fixed: IsInvincible came back False with two spheres on screen.
            //
            // A source-order check because the real thing needs the registry, a live caster
            // and a frame; the ordering is the whole contract and it is one line to get wrong.
            string path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Spells/Executors/ShieldExecutor.cs");
            Assert.IsTrue(System.IO.File.Exists(path), path + " not found");

            string source = System.IO.File.ReadAllText(path);
            int track = source.IndexOf("SpellEffectRegistry.Track", System.StringComparison.Ordinal);
            int initialize = source.IndexOf("controller.Initialize", System.StringComparison.Ordinal);

            Assert.Greater(track, 0, "the shield is not tracked at all — maxInstances is unenforced");
            Assert.Greater(initialize, 0, "the controller is never initialized");
            Assert.Less(track, initialize,
                "Track must run BEFORE Initialize, or recasting leaves the caster unprotected.");
        }

        // ── the blocked-damage seam ─────────────────────────────────────────────────

        [Test]
        public void AnInvincibleEntityAnnouncesTheBlowItTurnedAway()
        {
            var go = new GameObject("BlockedProbe");
            try
            {
                var health = go.AddComponent<Health>();
                health.Initialize(100);          // Awake does not run in Edit Mode
                health.SetInvincible(true);

                int calls = 0;
                int reported = 0;
                GameObject reportedAttacker = null;
                var attacker = new GameObject("Attacker");

                health.OnDamageBlocked += (amount, source) =>
                {
                    calls++;
                    reported = amount;
                    reportedAttacker = source;
                };

                health.TakeDamage(37, attacker);

                Assert.AreEqual(1, calls, "a blocked blow must be announced exactly once");
                Assert.AreEqual(37, reported, "the shield sizes its ripple off this number");
                Assert.AreSame(attacker, reportedAttacker,
                    "the ripple starts where the hit came from; without the attacker it cannot");
                Assert.AreEqual(100, health.CurrentHp, "the hit must still be refused");

                Object.DestroyImmediate(attacker);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NothingIsAnnouncedForABlowThatWasNeverGoingToLand()
        {
            var go = new GameObject("BlockedProbe");
            try
            {
                var health = go.AddComponent<Health>();
                health.Initialize(100);

                int calls = 0;
                health.OnDamageBlocked += (amount, source) => calls++;

                // Not invincible: this one lands, and landing is not blocking.
                health.TakeDamage(10, null);
                Assert.AreEqual(0, calls, "a hit that connected is not a hit that was blocked");

                health.SetInvincible(true);
                health.TakeDamage(0, null);
                Assert.AreEqual(0, calls,
                    "\"blocked\" has to mean a blow was turned away, or a listener flashing on "
                    + "it flashes at nothing");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── the flourish retint ─────────────────────────────────────────────────────

        [Test]
        public void ASpellsOwnSwatchMovesItsFlourishHueAndNothingElse()
        {
            var basePalette = ElementPalette.For(SpellElement.Arcane);
            var tinted = basePalette.RecolouredTo(LoadShield().particleColor);

            AssertValueAndAlphaSurvive(basePalette.hotCore, tinted.hotCore, "hotCore");
            AssertValueAndAlphaSurvive(basePalette.core, tinted.core, "core");
            AssertValueAndAlphaSurvive(basePalette.glow, tinted.glow, "glow");
            AssertValueAndAlphaSurvive(basePalette.halo, tinted.halo, "halo");
            AssertValueAndAlphaSurvive(basePalette.accent, tinted.accent, "accent");
            AssertValueAndAlphaSurvive(basePalette.lightColor, tinted.lightColor, "lightColor");

            // And it did actually move — a retint that changes nothing would pass every
            // assertion above.
            Color.RGBToHSV(tinted.core, out float hue, out _, out _);
            Color.RGBToHSV(LoadShield().particleColor, out float wanted, out _, out _);
            Assert.AreEqual(wanted, hue, 0.02f, "the flourish did not take the spell's hue");
        }

        // AnUnauthoredSwatchLeavesTheElementPaletteAlone, the near-black guard and the
        // achromatic case live in CastFlourishColourTests: they are contracts of the retint
        // itself, not of the shield. Only the shield's own swatch is asserted here.

        private static void AssertValueAndAlphaSurvive(Color before, Color after, string field)
        {
            Color.RGBToHSV(before, out _, out _, out float valueBefore);
            Color.RGBToHSV(after, out _, out _, out float valueAfter);

            // The per-field VALUE is the palette's tuning: hotCore is near-white and halo is
            // dim, and that spread is what makes a flourish read as a hot centre inside a soft
            // bloom rather than as six sprites of one colour.
            Assert.AreEqual(valueBefore, valueAfter, 0.01f,
                field + ": the retint must move the hue and leave the brightness alone");
            Assert.AreEqual(before.a, after.a, 0.001f, field + ": alpha is tuning too");
        }
    }
}
