using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Tests for <see cref="ProjectileExecutor.ResolveCasterCenter"/>.
    /// Guarantees that projectile spells originate from the visual middle of the
    /// caster sprite, not from the transform pivot (which sits at the feet for
    /// 2D characters with bottom-center pivot).
    /// </summary>
    public class ProjectileExecutorSpawnTests
    {
        [SetUp]
        public void SetUp()
        {
            // Renderer warnings can leak during sprite-bounds queries in EditMode.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>())
            {
                if (go.name.StartsWith("CasterTest_"))
                    Object.DestroyImmediate(go);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a 16×16 sprite whose pivot sits at the bottom-center, mirroring
        /// the player's setup: transform.position == feet, sprite renders upward.
        /// </summary>
        private static Sprite MakeBottomPivotSprite(int sizePx = 16, float ppu = 16f)
        {
            var tex = new Texture2D(sizePx, sizePx, TextureFormat.ARGB32, false);
            // pivot (0.5, 0) → bottom-center. With PPU=16 and size=16 the sprite
            // occupies +1 world unit upward from transform.position.
            return Sprite.Create(tex,
                new Rect(0, 0, sizePx, sizePx),
                new Vector2(0.5f, 0f),
                ppu);
        }

        // ── Null safety ──────────────────────────────────────────────────

        [Test]
        public void ResolveCasterCenter_NullTransform_ReturnsZero()
        {
            Assert.AreEqual(Vector3.zero, ProjectileExecutor.ResolveCasterCenter(null));
        }

        // ── Primary path: SpriteRenderer ────────────────────────────────

        [Test]
        public void ResolveCasterCenter_SpriteWithBottomPivot_ReturnsCenterAboveFeet()
        {
            var go = new GameObject("CasterTest_BottomPivot");
            go.transform.position = new Vector3(3f, 5f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = MakeBottomPivotSprite();

            var center = ProjectileExecutor.ResolveCasterCenter(go.transform);

            // Sprite is 16×16 px @ PPU=16 → 1 world unit tall, pivot at bottom.
            // Visual center should be 0.5 units above the transform position.
            Assert.AreEqual(3f, center.x, 1e-4f);
            Assert.AreEqual(5.5f, center.y, 1e-4f, "Center must sit above the feet pivot");
        }

        [Test]
        public void ResolveCasterCenter_SpriteOnChild_StillResolves()
        {
            var root = new GameObject("CasterTest_Root");
            root.transform.position = new Vector3(0f, 10f, 0f);

            var visual = new GameObject("CasterTest_Visual");
            visual.transform.SetParent(root.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = MakeBottomPivotSprite();

            var center = ProjectileExecutor.ResolveCasterCenter(root.transform);

            Assert.AreEqual(0f, center.x, 1e-4f);
            Assert.AreEqual(10.5f, center.y, 1e-4f);
        }

        [Test]
        public void ResolveCasterCenter_SpriteRendererPresentButNoSprite_FallsThroughToColliderWithMinLift()
        {
            var go = new GameObject("CasterTest_EmptySr");
            go.transform.position = new Vector3(0f, 0f, 0f);
            go.AddComponent<SpriteRenderer>(); // sprite is null

            var box = go.AddComponent<BoxCollider2D>();
            box.offset = new Vector2(0f, 0.4f);
            box.size   = new Vector2(0.5f, 0.6f);

            var center = ProjectileExecutor.ResolveCasterCenter(go.transform);

            // Collider center is 0.4, but minimum lift forces it to 0.5.
            Assert.AreEqual(0f, center.x, 1e-4f);
            Assert.AreEqual(0.5f, center.y, 1e-4f);
        }

        // ── Secondary path: Collider2D ──────────────────────────────────

        [Test]
        public void ResolveCasterCenter_NoSprite_UsesCollider()
        {
            var go = new GameObject("CasterTest_ColliderOnly");
            go.transform.position = new Vector3(2f, 2f, 0f);
            var box = go.AddComponent<BoxCollider2D>();
            box.offset = new Vector2(0f, 0.5f);
            box.size   = new Vector2(1f, 1f);

            var center = ProjectileExecutor.ResolveCasterCenter(go.transform);

            Assert.AreEqual(2f, center.x, 1e-4f);
            Assert.AreEqual(2.5f, center.y, 1e-4f);
        }

        [Test]
        public void ResolveCasterCenter_ColliderOnChild_LiftedToMinimum()
        {
            var root = new GameObject("CasterTest_RootCol");
            root.transform.position = new Vector3(7f, 7f, 0f);

            var child = new GameObject("CasterTest_ChildCol");
            child.transform.SetParent(root.transform, false);
            var box = child.AddComponent<BoxCollider2D>();
            box.offset = new Vector2(0f, 0.25f);
            box.size   = new Vector2(0.5f, 0.5f);

            var center = ProjectileExecutor.ResolveCasterCenter(root.transform);

            // Collider center is 7.25, lifted to 7.5 by minimum-lift safety.
            Assert.AreEqual(7f, center.x, 1e-4f);
            Assert.AreEqual(7.5f, center.y, 1e-4f);
        }

        // ── Regression: player-like setup (centered collider, no sprite) ────

        [Test]
        public void ResolveCasterCenter_PlayerLikeCenteredCollider_NeverSpawnsAtFeet()
        {
            // Mirrors the actual Player prefab: BoxCollider2D offset(0,0) size(0.5,0.3).
            // Without the minimum-lift safety this returned transform.position (the feet),
            // causing the laser to start at the player's feet (regression case).
            var go = new GameObject("CasterTest_PlayerLike");
            go.transform.position = new Vector3(4f, -2f, 0f);
            var box = go.AddComponent<BoxCollider2D>();
            box.offset = new Vector2(0f, 0f);
            box.size   = new Vector2(0.5f, 0.3f);

            var center = ProjectileExecutor.ResolveCasterCenter(go.transform);

            Assert.AreEqual(4f, center.x, 1e-4f);
            Assert.Greater(center.y, go.transform.position.y,
                "Resolved center must never coincide with the feet pivot, even when the" +
                " collider is centered on the transform");
            Assert.AreEqual(-1.5f, center.y, 1e-4f, "Should be lifted to position.y + MIN_LIFT (-2 + 0.5)");
        }

        // ── Last fallback: position + 0.5 Y ─────────────────────────────

        [Test]
        public void ResolveCasterCenter_NoSpriteNoCollider_AddsHalfUnitUp()
        {
            var go = new GameObject("CasterTest_Bare");
            go.transform.position = new Vector3(-1f, -2f, 0f);

            var center = ProjectileExecutor.ResolveCasterCenter(go.transform);

            Assert.AreEqual(-1f, center.x, 1e-4f);
            Assert.AreEqual(-1.5f, center.y, 1e-4f);
        }

        // ── Regression: sprite center is NOT the feet ───────────────────

        [Test]
        public void ResolveCasterCenter_AlwaysAboveOrEqualToFeet_ForBottomPivotSprite()
        {
            var go = new GameObject("CasterTest_Regression");
            go.transform.position = new Vector3(0f, 0f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = MakeBottomPivotSprite(32, 16f); // 32 px / 16 PPU = 2 units tall

            var center = ProjectileExecutor.ResolveCasterCenter(go.transform);

            Assert.Greater(center.y, go.transform.position.y,
                "Resolved center must be strictly above the feet pivot for a bottom-pivot sprite");
            Assert.AreEqual(1f, center.y, 1e-4f, "32 px @ PPU 16 → center is 1 unit up");
        }
    }
}
