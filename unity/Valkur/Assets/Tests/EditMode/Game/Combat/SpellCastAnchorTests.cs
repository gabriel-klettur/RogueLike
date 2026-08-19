using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the per-spell cast origin: <see cref="SpellDefinition.castAnchor"/> picks the
    /// point on the caster's body a spell is born from, and
    /// <see cref="SpellDefinition.castForwardOffset"/> how far in front of it.
    ///
    /// The anchor is expressed as a fraction of the caster's half-height rather than in
    /// world units, so a single setting reads the same on a rat and on a boss. These tests
    /// assert that relationship rather than absolute coordinates, so they keep their meaning
    /// if the sprite conventions ever change.
    /// </summary>
    [TestFixture]
    public class SpellCastAnchorTests
    {
        private GameObject _caster;
        private Sprite _sprite;
        private SpellDefinition _spell;

        private static readonly Vector2 Right = Vector2.right;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _caster = new GameObject("Caster");
            var renderer = _caster.AddComponent<SpriteRenderer>();
            _sprite = MakeSprite(32, 16f);        // 2 x 2 world units, half-height 1
            renderer.sprite = _sprite;

            _spell = ScriptableObject.CreateInstance<SpellDefinition>();
            _spell.spellKey = "anchor_probe";
        }

        [TearDown]
        public void TearDown()
        {
            if (_caster != null) Object.DestroyImmediate(_caster);
            if (_spell != null) Object.DestroyImmediate(_spell);
            if (_sprite != null)
            {
                var tex = _sprite.texture;
                Object.DestroyImmediate(_sprite);
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }

        private static Sprite MakeSprite(int size, float pixelsPerUnit)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }

        private float OriginY(SpellCastAnchor anchor)
            => ProjectileExecutor.ResolveCastOrigin(_caster.transform, anchor).y;

        // ── Backwards compatibility ─────────────────────────────────────────

        [Test]
        public void HandsIsTheZeroValueSoOldAssetsKeepTheirOrigin()
        {
            Assert.AreEqual(0, (int)SpellCastAnchor.Hands,
                "Every SpellDefinition asset authored before castAnchor existed deserializes " +
                "the field as 0. If Hands stops being 0, ~45 spells silently move their " +
                "origin the next time the project is opened.");

            var fresh = ScriptableObject.CreateInstance<SpellDefinition>();
            try
            {
                Assert.AreEqual(SpellCastAnchor.Hands, fresh.castAnchor);
                Assert.AreEqual(0f, fresh.castForwardOffset,
                    "An unauthored clearance reads as exactly 0, which is the sentinel for " +
                    "'use the system default'. Treating 0 literally would spawn every legacy " +
                    "spell inside the caster's own collider.");
            }
            finally { Object.DestroyImmediate(fresh); }
        }

        [Test]
        public void ADefaultSpellReproducesTheLegacyOrigin()
        {
            Vector3 legacy = ProjectileExecutor.ResolveCastStart(_caster.transform, Right);
            Vector3 viaSpell = ProjectileExecutor.ResolveCastStart(_caster.transform, Right, _spell);

            Assert.AreEqual(legacy.x, viaSpell.x, 0.0001f);
            Assert.AreEqual(legacy.y, viaSpell.y, 0.0001f,
                "A spell that sets nothing must land exactly where every spell landed before " +
                "the anchor existed.");
        }

        // ── The ladder up the body ──────────────────────────────────────────

        [Test]
        public void TheAnchorsClimbTheBody()
        {
            float feet   = OriginY(SpellCastAnchor.Feet);
            float centre = OriginY(SpellCastAnchor.Center);
            float hands  = OriginY(SpellCastAnchor.Hands);
            float head   = OriginY(SpellCastAnchor.Head);

            Assert.Less(feet, centre, "Feet must sit below the body centre.");
            Assert.Less(centre, hands, "Hands must sit above the body centre.");
            Assert.Less(hands, head, "Head must sit above the hands.");
        }

        [Test]
        public void FeetAndHeadSitAFullHalfHeightEitherSideOfTheCentre()
        {
            float centre = OriginY(SpellCastAnchor.Center);
            float halfHeight = _caster.GetComponent<SpriteRenderer>().bounds.extents.y;

            // Measured from the resolved centre, not from the raw sprite bounds:
            // ResolveCasterCenter keeps a minimum lift above the transform pivot so a
            // centre-pivot sprite never resolves down at the character's feet.
            Assert.AreEqual(centre - halfHeight, OriginY(SpellCastAnchor.Feet), 0.001f,
                "Feet is a full half-height below the resolved centre.");
            Assert.AreEqual(centre + halfHeight, OriginY(SpellCastAnchor.Head), 0.001f,
                "Head is a full half-height above the resolved centre.");
        }

        [Test]
        public void HandsSitPartWayUpRatherThanAtAFixedDistance()
        {
            float centre = OriginY(SpellCastAnchor.Center);
            float toHead = OriginY(SpellCastAnchor.Head) - centre;
            float toHands = OriginY(SpellCastAnchor.Hands) - centre;

            Assert.Greater(toHead, 0f, "Sanity: the sprite has height.");
            Assert.AreEqual(0.45f, toHands / toHead, 0.001f,
                "Hand height is a proportion of the caster, not a magic world-space number. " +
                "That proportion is what lets one setting work on a rat and on a boss.");
        }

        [Test]
        public void TheAnchorScalesWithTheCasterSize()
        {
            float smallHead = OriginY(SpellCastAnchor.Head) - OriginY(SpellCastAnchor.Center);

            var bigSprite = MakeSprite(32, 4f);   // same pixels, quarter the PPU → 4x the size
            _caster.GetComponent<SpriteRenderer>().sprite = bigSprite;
            try
            {
                float bigHead = OriginY(SpellCastAnchor.Head) - OriginY(SpellCastAnchor.Center);
                Assert.Greater(bigHead, smallHead * 2f,
                    "A larger caster must push every anchor further from its centre. If this " +
                    "stops holding, the anchor has been baked into world units again.");
            }
            finally
            {
                var tex = bigSprite.texture;
                _caster.GetComponent<SpriteRenderer>().sprite = _sprite;
                Object.DestroyImmediate(bigSprite);
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }

        // ── Forward clearance ───────────────────────────────────────────────

        [Test]
        public void TheForwardOffsetPushesAlongTheCastDirection()
        {
            _spell.castForwardOffset = 3f;

            Vector3 origin = ProjectileExecutor.ResolveCastOrigin(_caster.transform, _spell.castAnchor);
            Vector3 start = ProjectileExecutor.ResolveCastStart(_caster.transform, Right, _spell);

            Assert.AreEqual(origin.x + 3f, start.x, 0.001f);
            Assert.AreEqual(origin.y, start.y, 0.001f,
                "Forward clearance runs along the cast direction only — it must not also lift " +
                "the spell, or the anchor stops meaning what it says.");
        }

        [Test]
        public void TheForwardOffsetFollowsTheDirectionRatherThanTheAxis()
        {
            _spell.castForwardOffset = 2f;

            Vector3 origin = ProjectileExecutor.ResolveCastOrigin(_caster.transform, _spell.castAnchor);
            Vector3 up = ProjectileExecutor.ResolveCastStart(_caster.transform, Vector2.up, _spell);

            Assert.AreEqual(origin.x, up.x, 0.001f);
            Assert.AreEqual(origin.y + 2f, up.y, 0.001f);
        }

        [Test]
        public void AnUnsetForwardOffsetUsesTheSystemDefault()
        {
            Assert.AreEqual(ProjectileExecutor.CAST_FORWARD_OFFSET,
                ProjectileExecutor.ResolveCastForwardOffset(_spell), 0.0001f);

            _spell.castForwardOffset = 0f;
            Assert.AreEqual(ProjectileExecutor.CAST_FORWARD_OFFSET,
                ProjectileExecutor.ResolveCastForwardOffset(_spell), 0.0001f,
                "Exactly 0 is the 'system default' sentinel. Treating it literally would " +
                "spawn every legacy spell inside the caster's own collider.");

            _spell.castForwardOffset = 1.25f;
            Assert.AreEqual(1.25f, ProjectileExecutor.ResolveCastForwardOffset(_spell), 0.0001f);
        }

        [Test]
        public void ANegativeOffsetIsTakenLiterallyAndPlacesTheSpellBehind()
        {
            _spell.castForwardOffset = -1.5f;

            Assert.AreEqual(-1.5f, ProjectileExecutor.ResolveCastForwardOffset(_spell), 0.0001f,
                "A negative clearance must survive the default-sentinel check. Some spells " +
                "are born behind the caster, not in front of it.");

            Vector3 origin = ProjectileExecutor.ResolveCastOrigin(_caster.transform, _spell.castAnchor);
            Vector3 start = ProjectileExecutor.ResolveCastStart(_caster.transform, Right, _spell);

            Assert.AreEqual(origin.x - 1.5f, start.x, 0.001f,
                "Facing right, a negative offset must land the spell to the left of the anchor.");
            Assert.AreEqual(origin.y, start.y, 0.001f,
                "A negative offset still runs along the cast direction only — it must not " +
                "drop the spell down the body as well.");
        }

        [Test]
        public void ATinyOffsetIsHonouredRatherThanTreatedAsUnset()
        {
            // The 0 sentinel costs the ability to author a literal zero. A very small
            // value is the escape hatch, so it must not be rounded back to the default.
            _spell.castForwardOffset = 0.01f;
            Assert.AreEqual(0.01f, ProjectileExecutor.ResolveCastForwardOffset(_spell), 0.0001f);
        }

        [Test]
        public void ANullSpellStillResolves()
        {
            Assert.AreEqual(ProjectileExecutor.CAST_FORWARD_OFFSET,
                ProjectileExecutor.ResolveCastForwardOffset(null), 0.0001f);

            Vector3 start = ProjectileExecutor.ResolveCastStart(_caster.transform, Right, (SpellDefinition)null);
            Assert.AreEqual(ProjectileExecutor.ResolveCastStart(_caster.transform, Right).y, start.y, 0.001f,
                "NPC and debug paths cast without a definition; they must not throw or land at the origin.");
        }
    }
}
