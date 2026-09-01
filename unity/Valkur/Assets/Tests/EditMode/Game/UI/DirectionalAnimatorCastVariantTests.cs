using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Guards the cast-variant RESERVATION: a <see cref="CastVariant"/> may claim one or more
    /// spell keys, and a spell that is claimed always plays that animation instead of taking
    /// the next step of the generic rotation.
    ///
    /// The reservation exists because a casting pose is drawn for a particular spell — the
    /// dwarf's <c>spellcasting_3</c> is the fireball wind-up — and a rotation hands it out to
    /// whatever happens to be cast next. Two halves have to hold and they are independent:
    /// the claimed spell must FIND its variant, and the claimed variant must LEAVE the pool,
    /// or one cast in five of every other spell borrows the fireball pose.
    ///
    /// The lookup lives on the animator rather than in the caller because the binder DROPS
    /// authored variants that resolved to no frames, so an index computed from the authored
    /// list slides off the moment one is empty. Installing both lists in one call is what
    /// makes that impossible, and the last test here is the one that pins it.
    ///
    /// Awake never runs in EditMode, so the renderer is wired by reflection — the same shape
    /// as <see cref="DirectionalAnimatorAttackVariantTests"/>.
    /// </summary>
    public class DirectionalAnimatorCastVariantTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int FramesPerDirection = 4;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        // ---- Helpers --------------------------------------------------------

        private DirectionalAnimator CreateAnimator()
        {
            var go = new GameObject("TestCastAnimator");
            _created.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            var anim = go.AddComponent<DirectionalAnimator>();
            typeof(DirectionalAnimator).GetField("targetRenderer", Instance).SetValue(anim, renderer);
            return anim;
        }

        private List<Sprite> CreateFrames(string prefix, int count)
        {
            var texture = new Texture2D(count, 1);
            _created.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                sprite.name = $"{prefix}_{i}";
                frames.Add(sprite);
                _created.Add(sprite);
            }
            return frames;
        }

        /// <summary>Stands in for the Update tick, which EditMode never delivers.</summary>
        private static void Tick(DirectionalAnimator anim)
            => typeof(DirectionalAnimator).GetMethod("AdvanceFrame", Instance).Invoke(anim, null);

        private static int FrameIndex(DirectionalAnimator anim)
            => (int)typeof(DirectionalAnimator).GetField("_frameIndex", Instance).GetValue(anim);

        private DirectionalAnimator.DirectionalSpriteSet SetOf(string prefix)
            => DirectionalAnimator.CreateSetFromLinearFrames(CreateFrames(prefix, 8 * FramesPerDirection));

        /// <summary>
        /// An animator carrying <paramref name="prefixes"/>.Length cast variants, with the
        /// reservations given by <paramref name="spellKeys"/> (a null row = unreserved).
        /// </summary>
        private DirectionalAnimator WithCastVariants(string[] prefixes, string[][] spellKeys)
        {
            var anim = CreateAnimator();
            anim.SetSpriteSets(SetOf("idle"), SetOf("walk"), SetOf("chase"), SetOf("cast"),
                               SetOf("attack"), SetOf("damage"), SetOf("death"));

            var variants = new List<DirectionalAnimator.DirectionalSpriteSet>();
            foreach (string p in prefixes) variants.Add(SetOf(p));

            List<IReadOnlyList<string>> keys = null;
            if (spellKeys != null)
            {
                keys = new List<IReadOnlyList<string>>(spellKeys.Length);
                foreach (string[] row in spellKeys) keys.Add(row);
            }

            anim.SetVariants(DirectionalAnimator.AnimState.Cast, variants, keys);
            return anim;
        }

        // ---- Reservation lookup ---------------------------------------------

        [Test]
        public void VariantForSpell_ReturnsTheVariantThatClaimsTheKey()
        {
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_2", "spell_3" },
                new[] { null, null, new[] { "fireball" } });

            Assert.AreEqual(2, anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball"),
                "The variant that claims 'fireball' is index 2 — the third cast animation.");
        }

        [Test]
        public void VariantForSpell_IsCaseInsensitive()
        {
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_2" },
                new[] { null, new[] { "Fireball" } });

            Assert.AreEqual(1, anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball"),
                "A spell key is typed by hand in several places; a casing slip must not " +
                "silently fall back to the rotation.");
        }

        [Test]
        public void VariantForSpell_ReturnsMinusOne_ForAnUnclaimedSpell()
        {
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_2", "spell_3" },
                new[] { null, null, new[] { "fireball" } });

            Assert.AreEqual(-1, anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "iceball"),
                "-1 is what tells the caller to use its own rotation.");
            Assert.AreEqual(-1, anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, null));
        }

        [Test]
        public void VariantForSpell_ReturnsMinusOne_WhenNoVariantReservesAnything()
        {
            var anim = WithCastVariants(new[] { "spell_1", "spell_2" }, null);

            Assert.AreEqual(-1, anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball"));
            Assert.IsFalse(anim.IsVariantReserved(DirectionalAnimator.AnimState.Cast, 0));
            Assert.IsFalse(anim.IsVariantReserved(DirectionalAnimator.AnimState.Cast, 1));
        }

        [Test]
        public void IsVariantReserved_IsTrueOnlyForTheClaimedIndex()
        {
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_2", "spell_3" },
                new[] { null, null, new[] { "fireball" } });

            var cast = DirectionalAnimator.AnimState.Cast;
            Assert.IsFalse(anim.IsVariantReserved(cast, 0));
            Assert.IsFalse(anim.IsVariantReserved(cast, 1));
            Assert.IsTrue(anim.IsVariantReserved(cast, 2),
                "A reserved variant must leave the generic rotation, or every other spell " +
                "borrows the pose drawn for this one.");
            Assert.IsFalse(anim.IsVariantReserved(cast, 99), "Out of range is not reserved.");
        }

        [Test]
        public void Reservations_DoNotLeakAcrossStates()
        {
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_2" },
                new[] { null, new[] { "fireball" } });

            Assert.AreEqual(-1, anim.VariantForSpell(DirectionalAnimator.AnimState.Attack, "fireball"),
                "Attack carries its own variants and none of them claim anything here.");
        }

        // ---- Routing --------------------------------------------------------

        [Test]
        public void SelectingTheReservedVariant_RendersThatVariantsFrames()
        {
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_2", "spell_3" },
                new[] { null, null, new[] { "fireball" } });

            int variant = anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball");
            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East, variant);

            Sprite rendered = anim.GetComponent<SpriteRenderer>().sprite;
            Assert.IsNotNull(rendered, "The reserved variant must actually reach the renderer.");
            StringAssert.StartsWith("spell_3_", rendered.name,
                "Casting fireball must draw the variant it reserved, not the rotation's next step.");
        }

        [Test]
        public void GetStateLength_ReportsTheReservedVariantsOwnFrameCount()
        {
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_2", "spell_3" },
                new[] { null, null, new[] { "fireball" } });

            int variant = anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball");
            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East, variant);

            // Measured AFTER SetState, because GetStateLength reports the CURRENT direction's
            // frame count — the same ordering PlayerController.TriggerCastAnimation relies on
            // to size the cast window against the animation that is actually playing.
            float length = anim.GetStateLength(DirectionalAnimator.AnimState.Cast, variant);
            Assert.Greater(length, 0f,
                "A zero length would let the cast window fall back to its floor and cut the " +
                "animation off part-way, which is the bug this whole path exists to fix.");
        }

        // ---- Pacing ---------------------------------------------------------

        /// <summary>Installs pacing alongside the variants, the way the binder does.</summary>
        private DirectionalAnimator WithPacedVariants(string[] prefixes,
                                                      DirectionalAnimator.VariantPacing[] pacing)
        {
            var anim = CreateAnimator();
            anim.SetSpriteSets(SetOf("idle"), SetOf("walk"), SetOf("chase"), SetOf("cast"),
                               SetOf("attack"), SetOf("damage"), SetOf("death"));

            var variants = new List<DirectionalAnimator.DirectionalSpriteSet>();
            foreach (string p in prefixes) variants.Add(SetOf(p));
            anim.SetVariants(DirectionalAnimator.AnimState.Cast, variants, null, pacing);
            return anim;
        }

        [Test]
        public void AVariantsSpeedMultiplier_ShortensItsMeasuredLength()
        {
            var anim = WithPacedVariants(
                new[] { "normal", "fast" },
                new[]
                {
                    DirectionalAnimator.VariantPacing.Default,
                    new DirectionalAnimator.VariantPacing { SpeedMultiplier = 4f },
                });

            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East, 0);
            float normal = anim.GetStateLength(DirectionalAnimator.AnimState.Cast, 0);
            float fast = anim.GetStateLength(DirectionalAnimator.AnimState.Cast, 1);

            Assert.Greater(normal, 0f);
            Assert.AreEqual(normal / 4f, fast, 1e-4f,
                "A 4x variant must measure a quarter as long, or the cast window sized from " +
                "GetStateLength holds the pose four times longer than the animation runs — " +
                "which is the exact mismatch the dash's compression exists to remove.");
        }

        [Test]
        public void PacingOf_DefaultsToNeutral_WhenNoTableWasInstalled()
        {
            var anim = WithCastVariants(new[] { "spell_1", "spell_2" }, null);

            var pacing = anim.PacingOf(DirectionalAnimator.AnimState.Cast, 0);
            Assert.AreEqual(1f, pacing.SpeedMultiplier, 1e-4f);
            Assert.IsFalse(pacing.HoldLastFrame);

            Assert.AreEqual(1f, anim.PacingOf(DirectionalAnimator.AnimState.Cast, 99).SpeedMultiplier,
                1e-4f, "Out of range must read as neutral, not as zero — a zero interval " +
                "would divide the frame clock by nothing.");
        }

        [Test]
        public void AZeroSpeedMultiplier_IsReadAsNeutral()
        {
            // What an asset authored before the field existed deserializes to.
            var anim = WithPacedVariants(
                new[] { "legacy" },
                new[] { new DirectionalAnimator.VariantPacing { SpeedMultiplier = 0f } });

            Assert.AreEqual(1f, anim.PacingOf(DirectionalAnimator.AnimState.Cast, 0).SpeedMultiplier,
                1e-4f);
        }

        [Test]
        public void HoldLastFrame_StopsTheAnimationRepeating()
        {
            var anim = WithPacedVariants(
                new[] { "held" },
                new[] { new DirectionalAnimator.VariantPacing
                        { SpeedMultiplier = 1f, HoldLastFrame = true } });

            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East, 0);

            // Tick well past the end of the cycle; the cursor must stop at the last frame
            // rather than wrapping to 0.
            for (int i = 0; i < FramesPerDirection * 3; i++)
                Tick(anim);

            Assert.AreEqual(FramesPerDirection - 1, FrameIndex(anim),
                "A move that ends in a pose must stay there. Wrapping reads as a stutter: the " +
                "dash would re-lunge a second time after the body had already arrived.");
        }

        // ---- Reversed playback ----------------------------------------------

        /// <summary>Ticks a state to its final frame and returns the sprite names seen, so a
        /// reversed pass can be compared against the forward one rather than against
        /// hard-coded bucket indices.</summary>
        private static List<string> PlayThrough(DirectionalAnimator anim, SpriteRenderer sr, int steps)
        {
            var seen = new List<string> { sr.sprite.name };
            for (int i = 0; i < steps; i++)
            {
                Tick(anim);
                seen.Add(sr.sprite.name);
            }
            return seen;
        }

        [Test]
        public void ReversedPlayback_RunsTheSameFramesBackToFront()
        {
            var anim = WithPacedVariants(new[] { "equip" },
                                         new[] { DirectionalAnimator.VariantPacing.Default });
            var sr = anim.GetComponent<SpriteRenderer>();
            var cast = DirectionalAnimator.AnimState.Cast;

            anim.SetState(cast, DirectionalAnimator.Direction.East, 0, false);
            List<string> forward = PlayThrough(anim, sr, FramesPerDirection - 1);

            anim.SetState(cast, DirectionalAnimator.Direction.East, 0, true);
            List<string> backward = PlayThrough(anim, sr, FramesPerDirection - 1);

            backward.Reverse();
            CollectionAssert.AreEqual(forward, backward,
                "The sheathe is the draw run backwards — one sheet, one motion. If these two " +
                "differ by anything but order, the reversed read is not reading the same frames.");
        }

        [Test]
        public void SwitchingPlaybackDirection_RestartsInsteadOfBeingSwallowed()
        {
            var anim = WithPacedVariants(new[] { "equip" },
                                         new[] { DirectionalAnimator.VariantPacing.Default });
            var sr = anim.GetComponent<SpriteRenderer>();
            var cast = DirectionalAnimator.AnimState.Cast;
            var east = DirectionalAnimator.Direction.East;

            anim.SetState(cast, east, 0, false);
            string forwardFirst = sr.sprite.name;
            for (int i = 0; i < FramesPerDirection - 1; i++) Tick(anim);
            string forwardLast = sr.sprite.name;
            Assert.AreNotEqual(forwardFirst, forwardLast, "The probe set must have >1 frame.");

            // Same state, same direction, same variant — only the playback direction differs.
            // Without reversedChanged counting as a change, SetState returns early here and
            // stowing silently keeps playing the draw.
            anim.SetState(cast, east, 0, true);
            Assert.AreEqual(forwardLast, sr.sprite.name,
                "A reversed pass must open on the frame the forward pass ended on.");

            anim.SetState(cast, east, 0, false);
            Assert.AreEqual(forwardFirst, sr.sprite.name,
                "And going forward again must restart at the front.");
        }

        [Test]
        public void ReversedPlayback_WithHoldLastFrame_SettlesOnTheFirstFrame()
        {
            var anim = WithPacedVariants(
                new[] { "equip" },
                new[] { new DirectionalAnimator.VariantPacing
                        { SpeedMultiplier = 1f, HoldLastFrame = true } });
            var sr = anim.GetComponent<SpriteRenderer>();
            var cast = DirectionalAnimator.AnimState.Cast;
            var east = DirectionalAnimator.Direction.East;

            anim.SetState(cast, east, 0, false);
            string forwardFirst = sr.sprite.name;

            anim.SetState(cast, east, 0, true);
            for (int i = 0; i < FramesPerDirection * 3; i++) Tick(anim);

            Assert.AreEqual(forwardFirst, sr.sprite.name,
                "Held and reversed, the move ends where the forward one began — the weapon " +
                "back in its sheath. Holding the forward last frame here would freeze the " +
                "character mid-draw with the weapon out.");
        }

        [Test]
        public void ADirectionChangeMidReversedPlay_KeepsReadingBackwards()
        {
            var anim = WithPacedVariants(new[] { "equip" },
                                         new[] { DirectionalAnimator.VariantPacing.Default });
            var sr = anim.GetComponent<SpriteRenderer>();
            var cast = DirectionalAnimator.AnimState.Cast;

            anim.SetState(cast, DirectionalAnimator.Direction.East, 0, true);
            string firstEast = sr.sprite.name;

            // A direction-only change goes through RefreshCurrentFrame, which resolves the
            // frame itself. Unmapped it would land on the mirror-image cursor position and
            // snap the sheathe back to its start every time the facing sector changed.
            anim.SetState(cast, DirectionalAnimator.Direction.North, 0, true);
            string firstNorth = sr.sprite.name;

            Assert.AreNotEqual(firstEast, firstNorth, "The two facings draw different sprites.");
            StringAssert.StartsWith("equip_", firstNorth);

            // Frame 0 of North's bucket is what a FORWARD read would show; a reversed read at
            // cursor 0 must show that bucket's LAST frame instead.
            anim.SetState(cast, DirectionalAnimator.Direction.North, 0, false);
            Assert.AreNotEqual(firstNorth, sr.sprite.name,
                "Reversed and forward must not resolve the same sprite at cursor 0.");
        }

        // ---- Index alignment ------------------------------------------------

        [Test]
        public void ReservationIndex_TracksTheInstalledList_NotTheAuthoredOne()
        {
            // The binder installs only the variants that resolved to frames. Reservations are
            // appended in the same pass, so index 1 here is the SECOND INSTALLED variant even
            // though it was the third authored one. Simulated by installing two sets whose
            // reservation rows were built alongside them.
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_4" },
                new[] { null, new[] { "fireball" } });

            Assert.AreEqual(1, anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball"));

            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East, 1);
            StringAssert.StartsWith("spell_4_", anim.GetComponent<SpriteRenderer>().sprite.name);
        }

        [Test]
        public void ReinstallingVariantsWithoutReservations_ClearsThePreviousTable()
        {
            var anim = WithCastVariants(
                new[] { "spell_1", "spell_2" },
                new[] { null, new[] { "fireball" } });
            Assert.AreEqual(1, anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball"));

            var fresh = new List<DirectionalAnimator.DirectionalSpriteSet> { SetOf("a"), SetOf("b") };
            anim.SetVariants(DirectionalAnimator.AnimState.Cast, fresh);

            Assert.AreEqual(-1, anim.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball"),
                "A stale reservation surviving a rebind would pin a spell to art that is gone.");
        }
    }
}
