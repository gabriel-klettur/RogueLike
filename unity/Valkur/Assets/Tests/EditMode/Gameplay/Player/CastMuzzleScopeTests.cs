using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Gameplay.Player
{
    /// <summary>
    /// Which authored muzzle answers for a given cast.
    ///
    /// <para>The resolution is most-specific-first over three OPTIONAL discriminators — state,
    /// variant, spell — and the weights (1, 2, 4) are what make the ordering a property of the
    /// numbers rather than of the comparison. That is the half worth pinning: every individual
    /// match looks right on its own, and the thing that breaks silently is the ORDER between
    /// two points that both apply.</para>
    ///
    /// <para>Pure data, so no scene and no binder: <see cref="EntityAssetConfig"/> owns the
    /// resolution and <c>CastMuzzle</c> only re-states it against the animator. The runtime
    /// half — that the component is installed at all, that only Hands and Head consult it, that
    /// the two mirrored halves share one number — is <c>CastMuzzleTests</c>, and the two
    /// deliberately do not overlap.</para>
    /// </summary>
    public class CastMuzzleScopeTests
    {
        private static CastMuzzlePoint Point(string state, string variant, Vector2 offset,
                                             params string[] spells)
        {
            var point = new CastMuzzlePoint
            {
                key = state + "/" + variant,
                state = state,
                variantKey = variant,
                offset = offset,
            };
            if (spells != null)
                for (int i = 0; i < spells.Length; i++) point.spellKeys.Add(spells[i]);
            return point;
        }

        private static EntityAssetConfig ConfigWith(params CastMuzzlePoint[] points)
        {
            var config = new EntityAssetConfig();
            config.castMuzzlePoints.Clear();
            for (int i = 0; i < points.Length; i++) config.castMuzzlePoints.Add(points[i]);
            return config;
        }

        // -- Matching --------------------------------------------------------------

        [Test]
        public void AnEmptyField_MatchesAnything()
        {
            var any = Point(null, null, new Vector2(0.5f, 0.1f));

            Assert.That(any.SpecificityFor("Cast", "bite", "flame_breath"), Is.EqualTo(0),
                "All three discriminators empty is the creature-wide answer: it must apply, " +
                "and score lowest so anything more specific outranks it.");
        }

        [Test]
        public void AWrongState_DoesNotApplyAtAll()
        {
            var cast = Point("Cast", null, Vector2.one);

            Assert.That(cast.SpecificityFor("Attack", null, null), Is.EqualTo(-1));
            Assert.That(cast.SpecificityFor("Cast", null, null), Is.EqualTo(1));
        }

        [Test]
        public void MatchingIsCaseInsensitive_BecauseTheStateIsAStringifiedEnum()
        {
            // EntityAssetConfig lives in Valkur.Data and may not reference AnimState, so the
            // state is a STRING -- and a string typed by an author in the Inspector will not
            // always carry the enum's own casing.
            var cast = Point("cast", "BITE", Vector2.one);

            Assert.That(cast.SpecificityFor("Cast", "bite", null), Is.EqualTo(3));
        }

        [Test]
        public void ASpellMatch_OutranksAnyCombinationOfTheOtherTwo()
        {
            var animation = Point("Cast", "bite", new Vector2(0.2f, 0f));
            var spellOnly = Point(null, null, new Vector2(0.9f, 0f), "flame_breath");

            int animationScore = animation.SpecificityFor("Cast", "bite", "flame_breath");
            int spellScore = spellOnly.SpecificityFor("Cast", "bite", "flame_breath");

            Assert.That(spellScore, Is.GreaterThan(animationScore),
                "A point named for the spell is the author saying 'this one, specifically'. " +
                "If state+variant could outscore it, a per-spell exception would be " +
                "unreachable on any creature that also scoped its animation.");
        }

        [Test]
        public void AVariantMatch_OutranksAStateMatch()
        {
            var state = Point("Cast", null, Vector2.zero);
            var variant = Point("Cast", "bite", Vector2.one);

            Assert.That(variant.SpecificityFor("Cast", "bite", null),
                        Is.GreaterThan(state.SpecificityFor("Cast", "bite", null)));
        }

        // -- Resolution ------------------------------------------------------------

        [Test]
        public void ResolvePicksTheMostSpecific_NotTheFirst()
        {
            var general = Point(null, null, new Vector2(0.1f, 0f));
            var scoped = Point("Cast", "bite", new Vector2(0.8f, 0.2f));
            var config = ConfigWith(general, scoped);

            var resolved = config.ResolveMuzzlePoint("Cast", "bite", null);

            Assert.That(resolved, Is.SameAs(scoped),
                "List order must not decide between points of different specificity, or the " +
                "answer would depend on the order rows happen to have been added in.");
        }

        [Test]
        public void ResolveFallsBackWhenTheSpellHasNoPointOfItsOwn()
        {
            var animation = Point("Cast", "bite", new Vector2(0.8f, 0.2f));
            var config = ConfigWith(animation);

            var resolved = config.ResolveMuzzlePoint("Cast", "bite", "meteor_shower");

            Assert.That(resolved, Is.SameAs(animation),
                "A spell with no point of its own still fires from wherever its ANIMATION " +
                "does. Refusing here would send it back to the vertical anchor, which is the " +
                "thing the muzzle exists to replace.");
        }

        [Test]
        public void ResolveAnswersNullWhenNothingApplies()
        {
            var config = ConfigWith(Point("Attack", null, Vector2.one));

            Assert.That(config.ResolveMuzzlePoint("Cast", null, null), Is.Null,
                "Null is what sends the caller to the creature-wide pair, and then to the " +
                "shared anchor. A best-effort match would put the cast somewhere nobody chose.");
        }

        [Test]
        public void TiesFallToListOrder_WhichIsTheAuthorsOwn()
        {
            var first = Point("Cast", null, new Vector2(0.3f, 0f));
            var second = Point("Cast", null, new Vector2(0.7f, 0f));
            var config = ConfigWith(first, second);

            Assert.That(config.ResolveMuzzlePoint("Cast", null, null), Is.SameAs(first),
                "Two points of equal specificity is an authoring mistake, and the first one " +
                "is the only tie-break that does not depend on something invisible.");
        }

        // -- Per-frame refinement --------------------------------------------------

        [Test]
        public void APointsOwnFrameRow_OverridesItsOffset()
        {
            var point = Point("Cast", null, new Vector2(0.5f, 0.1f));
            point.frames.Add(new CastMuzzleFrame
            {
                frame = "red_dragon_cast_e6",
                offset = new Vector2(0.9f, 0.6f),
                handTuned = true,
            });

            Assert.That(point.OffsetForFrame("red_dragon_cast_e6"), Is.EqualTo(new Vector2(0.9f, 0.6f)));
            Assert.That(point.OffsetForFrame("red_dragon_cast_e1"), Is.EqualTo(new Vector2(0.5f, 0.1f)),
                "A frame the refinement does not name keeps the point's own offset -- placing " +
                "one animation must not require placing every frame of it.");
        }

        [Test]
        public void FrameNamesAreMatchedExactly_BecauseAnAtlasSpriteIsIdentifiedByName()
        {
            var point = Point("Cast", null, Vector2.zero);
            point.frames.Add(new CastMuzzleFrame { frame = "Red_Dragon_Cast_E6", offset = Vector2.one });

            Assert.That(point.OffsetForFrame("red_dragon_cast_e6"), Is.EqualTo(Vector2.zero),
                "AssetDatabase.GetAssetPath returns EMPTY for an atlas-packed sprite, so the " +
                "NAME is the only identity a frame has -- and it is the name Unity stored, " +
                "not one a human retyped. Loosening this would let two frames answer as one.");
        }

        // -- Composition -----------------------------------------------------------

        [Test]
        public void APointWithNoRow_KeepsTheCreaturesBakedSweep()
        {
            // The dragon's mouth travels as it rears, which is why 60 rows were baked. A point
            // that simply won outright would flatten all of it -- making the ONE creature this
            // feature was built for worse, silently, the moment an author used the picker.
            var point = Point("Cast", null, new Vector2(0.60f, 0.30f));
            Vector2 pair = new Vector2(0.80f, 0.08f);
            Vector2 row = new Vector2(0.95f, 0.50f);        // this frame rears: +0.15 / +0.42

            Vector2 composed = EntityAssetConfig.ComposeMuzzleOffset(
                point, "red_dragon_cast_e6", pair, true, row, true);

            Assert.That(composed.x, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(composed.y, Is.EqualTo(0.72f).Within(0.0001f));
        }

        [Test]
        public void APointsOwnRow_WinsOutrightOverTheSweep()
        {
            var point = Point("Cast", null, new Vector2(0.60f, 0.30f));
            point.frames.Add(new CastMuzzleFrame
            {
                frame = "red_dragon_cast_e6", offset = new Vector2(0.1f, 0.1f), handTuned = true,
            });

            Vector2 composed = EntityAssetConfig.ComposeMuzzleOffset(
                point, "red_dragon_cast_e6", new Vector2(0.8f, 0.08f), true,
                new Vector2(0.95f, 0.50f), true);

            Assert.That(composed, Is.EqualTo(new Vector2(0.1f, 0.1f)),
                "A hand-placed row means 'not there, and not derived from there either'. " +
                "On cast_e6 the dragon's FORELEG reaches further forward than its snout, so " +
                "the measured sweep is exactly what must not be applied.");
        }

        [Test]
        public void WithNoCreaturePair_ThereIsNothingToDisplaceFrom()
        {
            var point = Point("Cast", null, new Vector2(0.60f, 0.30f));

            Vector2 composed = EntityAssetConfig.ComposeMuzzleOffset(
                point, "frame", Vector2.zero, creatureHasPair: false,
                new Vector2(0.95f, 0.50f), creatureHasFrameRow: true);

            Assert.That(composed, Is.EqualTo(new Vector2(0.60f, 0.30f)),
                "The sweep is a displacement FROM the pair. With no pair the row is a raw " +
                "measurement, and adding it would throw the muzzle most of a body-length away.");
        }

        [Test]
        public void WithNoPoint_TheCreatureAnswersExactlyAsItAlwaysDid()
        {
            Vector2 pair = new Vector2(0.80f, 0.08f);
            Vector2 row = new Vector2(0.95f, 0.50f);

            Assert.That(EntityAssetConfig.ComposeMuzzleOffset(null, "f", pair, true, row, true),
                        Is.EqualTo(row), "Baked row for a covered frame.");
            Assert.That(EntityAssetConfig.ComposeMuzzleOffset(null, "f", pair, true, pair, false),
                        Is.EqualTo(pair), "The pair for a frame the bake did not cover.");
        }

        [Test]
        public void PlacingThenReadingBack_IsAnIdentityOnTheFrameItWasPlacedOn()
        {
            // What the picker stores is the INVERSE of the composition, so the crosshair lands
            // under the cursor on the frame being looked at. Without it the mark jumps away
            // from the click the instant the creature has baked rows.
            Vector2 pair = new Vector2(0.80f, 0.08f);
            Vector2 row = new Vector2(0.95f, 0.50f);
            Vector2 clicked = new Vector2(0.70f, 0.18f);

            var point = Point("Cast", null, clicked - (row - pair));
            Vector2 readBack = EntityAssetConfig.ComposeMuzzleOffset(point, "f", pair, true, row, true);

            Assert.That(readBack.x, Is.EqualTo(clicked.x).Within(0.0001f));
            Assert.That(readBack.y, Is.EqualTo(clicked.y).Within(0.0001f));
        }

        [Test]
        public void TryFrameOffset_SeparatesHavingARowFromMatchingTheOffset()
        {
            var point = Point("Cast", null, new Vector2(0.5f, 0.5f));
            point.frames.Add(new CastMuzzleFrame { frame = "f", offset = new Vector2(0.5f, 0.5f) });

            Assert.That(point.TryFrameOffset("f", out _), Is.True);
            Assert.That(point.TryFrameOffset("other", out _), Is.False,
                "A row that happens to equal the point's offset is still a row: it says 'do " +
                "not displace this one', and collapsing the two would re-apply the sweep.");
        }

        // -- The sentinel ----------------------------------------------------------

        [Test]
        public void APointAloneIsEnoughToCountAsAuthored()
        {
            var config = ConfigWith(Point("Cast", null, new Vector2(0.8f, 0.2f)));

            Assert.That(config.castMuzzle, Is.EqualTo(Vector2.zero),
                "Precondition: this creature declares no creature-wide pair.");
            Assert.That(config.HasCastMuzzle, Is.True,
                "'The cast animation fires from the mouth' with no creature-wide fallback is " +
                "the NORMAL shape now. Reading HasCastMuzzle off the loose pair alone would " +
                "leave the component uninstalled and the point unreachable.");
        }

        [Test]
        public void AnEmptyConfigStillReadsAsUnauthored()
        {
            var config = new EntityAssetConfig();

            Assert.That(config.HasCastMuzzle, Is.False,
                "(0,0) with no points is the 'nobody authored one' sentinel, and it is safe " +
                "because the sprite's own centre is never a mouth: a spell that really wants " +
                "the body authors SpellCastAnchor.Center.");
        }
    }
}
