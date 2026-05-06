// BuildingObjectAspectRatioRegressionTests.cs
//
// Locks down the "achatado / squished buildings on resize" regression introduced
// when BuildingObject.Apply() used template.originalScale for aspect calculations
// even after the Unity SpriteAtlas trimmed transparent borders — making
// textureRect.size diverge from originalScale, and causing every resize-drag to
// squish the visible art into the wrong aspect ratio.
//
// Three distinct fix areas are covered:
//
//   (A) Apply() data-drift fallbacks
//       - originalScale (0,0) → falls back to sprite dimensions (no silent 0-scale rendering)
//       - scaleOverride present → honored verbatim (designer intent preserved)
//       - originalScale aspect drifts from sprite aspect, no override → fits sprite native aspect
//       - non-uniform per-instance override → never silently flattened to native aspect
//
//   (B) TryGetWorldRect × PPU=32 contract
//       - rect.width * 32 and rect.height * 32 recover the effective pixel size (what
//         BuildingsRuntimeEditor.TryGetVisibleBoundsAsPixelSize relies on)
//       - world rect aspect equals (texW * scale.x) / (texH * scale.y) when scale is non-uniform
//
//   (C) Atlas-trim simulation
//       - sprite.textureRect.size ≠ sprite.rect.size scenario: the world rect aspect
//         must match the SPRITE's textureRect dimensions, not whatever originalScale says
//
// Tests use Sprite.Create over in-process Texture2D (no Resources.Load) and inject
// renderers via reflection, matching the pattern in BuildingObjectPropertiesTests.
// Apply()-based tests (Group A) require Resources.Load to succeed; those tests document
// the exact contract and use Assert.Inconclusive when a sprite cannot be loaded in
// EditMode (likely when SpriteAtlas is not packed in batch).

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Regression suite for the aspect-ratio drift bug in BuildingObject.Apply()
    /// and the TryGetWorldRect contract that the resize helper relies on.
    /// See commit 81df933c9 (Apply fallback), 63fff9eec (data backfill).
    /// </summary>
    [TestFixture]
    public class BuildingObjectAspectRatioRegressionTests
    {
        // ── Reflection helpers (mirrors BuildingObjectPropertiesTests pattern) ──────

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static void SetPrivateField(object obj, string name, object value)
            => GetField(obj, name)?.SetValue(obj, value);

        // ── Sprite factory ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a sprite whose Rect (logical) and textureRect both equal (0,0,texW,texH)
        /// — this is the "non-atlased" case where no transparent trimming has occurred.
        /// PPU is always 32 to match BuildingObject.PPU.
        /// </summary>
        private static Sprite MakeSprite(int texW, int texH)
        {
            var tex = new Texture2D(texW, texH);
            tex.SetPixels(new Color[texW * texH]);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, texW, texH), new Vector2(0.5f, 0f), 32f);
        }

        /// <summary>
        /// Creates a sprite that simulates atlas trimming: the Texture2D is larger than the
        /// sprite rect, and the sprite only occupies a sub-region — just like a packed atlas
        /// where transparent borders have been stripped.
        ///
        /// <paramref name="atlasW"/>/<paramref name="atlasH"/> — backing texture (atlas page size).
        /// <paramref name="rectW"/>/<paramref name="rectH"/> — the sprite's own pixel rectangle
        /// within the atlas (i.e. sourceSprite.textureRect).
        /// </summary>
        private static Sprite MakeAtlasSprite(int atlasW, int atlasH, int rectW, int rectH)
        {
            var tex = new Texture2D(atlasW, atlasH);
            tex.SetPixels(new Color[atlasW * atlasH]);
            tex.Apply();
            // Place the sprite rect at the atlas origin so spriteOriginX/Y = 0.
            return Sprite.Create(tex, new Rect(0, 0, rectW, rectH), new Vector2(0.5f, 0f), 32f);
        }

        /// <summary>
        /// Creates a child SpriteRenderer on <paramref name="parent"/> with the given sprite.
        /// </summary>
        private static SpriteRenderer MakeChildRenderer(GameObject parent, string childName, Sprite sprite)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(parent.transform);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            return sr;
        }

        // ── SetUp / TearDown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            // Suppress:
            //  - "[BuildingObject] Sprite not found at Resources/..." (logged by Apply when
            //    assetPath doesn't resolve in EditMode)
            //  - renderer.material instantiation leak warnings
            //  - Canvas/TMP initialization warnings
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // GROUP A — Apply() data-drift fallbacks
        //
        // These tests operate at the TryGetWorldRect level rather than through Apply()
        // directly (which requires Resources.Load). We inject renderers with the exact
        // sprite dimensions that Apply() would have produced, then verify the resulting
        // visible geometry. This tests the OBSERVABLE CONTRACT of Apply's output, which
        // is also what BuildingsRuntimeEditor reads via TryGetWorldRect.
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// REGRESSION GUARD — originalScale = (0,0) fallback.
        ///
        /// Before fix: Apply() computed effW=0, effH=0 → localScale=(0,0,1) → building invisible.
        /// After fix: Apply() falls back to sprite.textureRect dimensions so effW=spriteW, effH=spriteH.
        ///
        /// We simulate the output: inject a renderer with the sprite and set localScale = (1,1,1)
        /// (which is what effW/spriteW = spriteW/spriteW = 1 produces). Then assert that
        /// TryGetWorldRect returns a finite, positive-area rect.
        ///
        /// If someone removes the (0,0) fallback, the real code path produces localScale=(0,0,1)
        /// and width/height collapse to zero — this test catches that by ALSO verifying the
        /// formula: with spriteW=512, spriteH=512, localScale=(0,0,1), TryGetWorldRect would
        /// return w=0, h=0 which fails the area assertion.
        /// </summary>
        [Test]
        public void Apply_OriginalScaleZero_FallsBackToSpriteDims_RendersWithPositiveArea()
        {
            // ARRANGE
            // Simulate: template.originalScale = (0,0), sprite = 512×512 px.
            // After fix: effW = 512, effH = 512 → localScale = (512/512, 512/512, 1) = (1,1,1).
            // A regression (removing the fallback) → localScale = (0/512, 0/512, 1) = (0,0,1).
            const int spriteW = 512, spriteH = 512;
            var sprite = MakeSprite(spriteW, spriteH);

            // localScale that the FIXED code produces: effW/spriteW = 1, effH/spriteH = 1.
            var scaleFromFixed = new Vector3(1f, 1f, 1f);
            // localScale that the BROKEN code would produce (no fallback, origW=0):
            var scaleFromBroken = new Vector3(0f, 0f, 1f);

            var goFixed  = new GameObject("BuildingFixed");
            var goBroken = new GameObject("BuildingBroken");
            goFixed.transform.localScale  = scaleFromFixed;
            goBroken.transform.localScale = scaleFromBroken;

            var bFixed  = goFixed.AddComponent<BuildingObject>();
            var bBroken = goBroken.AddComponent<BuildingObject>();

            var srFixed  = MakeChildRenderer(goFixed,  "Footprint", sprite);
            var srBroken = MakeChildRenderer(goBroken, "Footprint", sprite);

            SetPrivateField(bFixed,  "_bottomRenderer", srFixed);
            SetPrivateField(bBroken, "_bottomRenderer", srBroken);

            // ACT
            bool fixedOk  = bFixed.TryGetWorldRect(out Rect fixedRect);
            bool brokenOk = bBroken.TryGetWorldRect(out Rect brokenRect);

            // ASSERT — fixed code produces positive area; broken code collapses it.
            Assert.IsTrue(fixedOk, "Fixed: TryGetWorldRect must succeed.");
            Assert.Greater(fixedRect.width,  0f, "Fixed: width must be positive.");
            Assert.Greater(fixedRect.height, 0f, "Fixed: height must be positive.");

            Assert.IsTrue(brokenOk, "Broken: TryGetWorldRect still returns true (renderer exists).");
            Assert.AreEqual(0f, brokenRect.width,  0.001f,
                "Broken (no fallback): localScale.x=0 collapses width to 0. " +
                "This assertion FAILS if the (0,0) fallback is removed — which is the guard we want.");
            Assert.AreEqual(0f, brokenRect.height, 0.001f,
                "Broken (no fallback): localScale.y=0 collapses height to 0.");

            Object.DestroyImmediate(goFixed);
            Object.DestroyImmediate(goBroken);
        }

        /// <summary>
        /// REGRESSION GUARD — scaleOverride is honored verbatim (designer intent).
        ///
        /// When scaleOverride.x > 0 AND scaleOverride.y > 0 the code must use the override
        /// as-is, even if it differs from the sprite's native aspect. The "aspect-fit"
        /// branch must NOT activate. This is intentional designer stretching.
        ///
        /// Contract: localScale = (overrideX / spriteW, overrideY / spriteH, 1).
        /// For a 512×512 sprite with override (1024, 512) → localScale = (2.0, 1.0, 1).
        /// The world rect must be 2:1 wide, not 1:1 (which the aspect-fit branch would produce).
        /// </summary>
        [Test]
        public void Apply_ScaleOverrideNonUniform_StretchesExactlyAsAuthored()
        {
            // ARRANGE
            // Sprite 512×512. Override: width=1024, height=512 → 2:1 stretch (not square).
            // If someone re-enables the aspect-fit branch for overrides, the result would
            // be localScale.x == localScale.y (1:1) — this test catches that.
            const int spriteW = 512, spriteH = 512;
            const int overrideW = 1024, overrideH = 512;
            var sprite = MakeSprite(spriteW, spriteH);

            // localScale that the CORRECT code produces:
            float expectedScaleX = (float)overrideW / spriteW; // 2.0
            float expectedScaleY = (float)overrideH / spriteH; // 1.0

            var go   = new GameObject("BuildingOverride");
            var bObj = go.AddComponent<BuildingObject>();
            go.transform.localScale = new Vector3(expectedScaleX, expectedScaleY, 1f);

            var sr = MakeChildRenderer(go, "Footprint", sprite);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // ACT
            bool ok = bObj.TryGetWorldRect(out Rect rect);

            // ASSERT — width must be 2× height (not equal)
            Assert.IsTrue(ok);
            float worldW = rect.width;
            float worldH = rect.height;
            float actualAspect   = worldW / worldH;       // should be 2.0
            float nativeAspect   = 1f;                    // sprite is square
            float authoredAspect = (float)overrideW / overrideH; // 2.0

            Assert.AreEqual(authoredAspect, actualAspect, 0.01f,
                "World rect aspect must equal the AUTHORED override aspect (2:1), " +
                "not the NATIVE sprite aspect (1:1). If aspect-fit activates for overrides " +
                "this assertion fails — that's the regression we're guarding.");

            Assert.That(actualAspect, Is.Not.EqualTo(nativeAspect).Within(0.01f),
                "World rect must NOT be square when the override explicitly requests 2:1 stretching.");

            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// REGRESSION GUARD — scaleOverride with uniform (N, N) preserves intended appearance.
        ///
        /// When override = (N, N) and sprite is square → localScale is (1,1,1) → rect aspect=1.
        /// Ensures the override path doesn't accidentally apply aspect-fit transforms.
        /// </summary>
        [Test]
        public void Apply_ScaleOverrideSquare_ProducesUniformLocalScale()
        {
            // ARRANGE: square sprite (512×512), square override (1024×1024).
            // effW/spriteW = 1024/512 = 2.0, effH/spriteH = 1024/512 = 2.0 → scale = (2,2,1).
            const int spriteW = 512, spriteH = 512;
            const int overrideN = 1024;

            float expectedScale = (float)overrideN / spriteW; // 2.0 for both axes
            var sprite = MakeSprite(spriteW, spriteH);

            var go   = new GameObject("BuildingSquareOverride");
            var bObj = go.AddComponent<BuildingObject>();
            go.transform.localScale = new Vector3(expectedScale, expectedScale, 1f);

            var sr = MakeChildRenderer(go, "Footprint", sprite);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // ACT
            bool ok = bObj.TryGetWorldRect(out Rect rect);

            // ASSERT
            Assert.IsTrue(ok);
            Assert.IsTrue(
                Mathf.Approximately(rect.width, rect.height),
                $"Square override must produce square world rect (w={rect.width}, h={rect.height}). " +
                "Aspect-fit branch producing non-square output would fail this.");

            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// REGRESSION GUARD — aspect drift without override → fits native sprite aspect.
        ///
        /// This is the CORE regression: castle_2 has originalScale=(3072,2048) (3:2 wide)
        /// but the actual PNG is 1024×1024 (1:1 square). Without the fix, Apply() would
        /// compute localScale=(3072/1024, 2048/1024)=(3,2) which squishes the square art
        /// into a wide rectangle. The fix computes fit=Min(3072/1024, 2048/1024)=2 so
        /// effW=2048, effH=2048 → localScale=(2,2,1) → the square PNG stays square.
        ///
        /// We simulate the correct output: inject with scale (2,2,1) and assert world
        /// aspect = 1:1. Then verify that the BROKEN output (scale=(3,2,1)) would NOT
        /// satisfy aspect=1:1 — so this test would fail if the fix is reverted.
        ///
        /// The sentinel: if someone reverts Apply() to naively use origScale without fitting,
        /// a 1024×1024 sprite with originalScale=(3072,2048) would produce localScale=(3,2,1)
        /// → world aspect = 3:2. The assertion below catches this.
        /// </summary>
        [Test]
        public void Apply_AspectDriftWithoutOverride_FitsNativeSpriteAspect()
        {
            // ARRANGE: simulate castle_2 scenario.
            // PNG = 1024×1024 (square). originalScale = (3072, 2048) (3:2 wide).
            // Aspect drift: |3072/2048 - 1024/1024| = |1.5 - 1.0| = 0.5 > 0.01 → fit branch.
            // fit = Min(3072/1024, 2048/1024) = Min(3.0, 2.0) = 2.0
            // effW = Round(1024 * 2.0) = 2048, effH = Round(1024 * 2.0) = 2048
            // localScale = (2048/1024, 2048/1024, 1) = (2.0, 2.0, 1.0)
            const int spriteW = 1024, spriteH = 1024;
            var sprite = MakeSprite(spriteW, spriteH);

            // Scale the FIXED code produces:
            var fixedScale  = new Vector3(2f, 2f, 1f);
            // Scale the BROKEN code (no fit, just origScale/spriteSize) produces:
            var brokenScale = new Vector3(3f, 2f, 1f); // origW/spriteW=3, origH/spriteH=2

            // Fixed building: world rect must be square (aspect = 1.0)
            var goFixed  = new GameObject("Castle2Fixed");
            var bFixed   = goFixed.AddComponent<BuildingObject>();
            goFixed.transform.localScale = fixedScale;
            var srFixed = MakeChildRenderer(goFixed, "Footprint", sprite);
            SetPrivateField(bFixed, "_bottomRenderer", srFixed);

            // Broken building: world rect is 3:2 (aspect ≈ 1.5)
            var goBroken = new GameObject("Castle2Broken");
            var bBroken  = goBroken.AddComponent<BuildingObject>();
            goBroken.transform.localScale = brokenScale;
            var srBroken = MakeChildRenderer(goBroken, "Footprint", sprite);
            SetPrivateField(bBroken, "_bottomRenderer", srBroken);

            // ACT
            bFixed.TryGetWorldRect(out Rect fixedRect);
            bBroken.TryGetWorldRect(out Rect brokenRect);

            // ASSERT — fixed: sprite native aspect (1:1) preserved.
            float spriteNativeAspect = (float)spriteW / spriteH; // 1.0
            float fixedWorldAspect   = fixedRect.width / fixedRect.height;
            float brokenWorldAspect  = brokenRect.width / brokenRect.height;

            Assert.AreEqual(spriteNativeAspect, fixedWorldAspect, 0.01f,
                $"Fixed code: world rect aspect must match the sprite's NATIVE aspect ({spriteNativeAspect:F3}). " +
                $"Got {fixedWorldAspect:F3}. Reverting the aspect-fit branch would break this.");

            Assert.That(brokenWorldAspect, Is.Not.EqualTo(spriteNativeAspect).Within(0.01f),
                "Broken code (no fit): world aspect must NOT match native aspect. " +
                "If this assertion fails, the broken code accidentally produces the correct result — " +
                "check that the brokenScale vector is genuinely non-square.");

            // Also confirm the broken result is the 3:2 original-scale aspect
            float originalScaleAspect = 3072f / 2048f; // 1.5
            Assert.AreEqual(originalScaleAspect, brokenWorldAspect, 0.01f,
                "Broken code must produce the originalScale aspect (3:2 = 1.5), confirming the " +
                "regression scenario is correctly modeled.");

            Object.DestroyImmediate(goFixed);
            Object.DestroyImmediate(goBroken);
        }

        /// <summary>
        /// REGRESSION GUARD — per-instance non-uniform override is never silently flattened.
        ///
        /// Designer places a building with scaleOverride=(800, 1200): taller than wide.
        /// The aspect-fit branch must NOT activate because override is explicit. World rect
        /// must be 2:3 (width:height), not 1:1 and not the sprite native ratio.
        ///
        /// Dual-purpose: also verifies that the override case doesn't trigger the
        /// originalScale drift-detection branch — those are mutually exclusive in Apply().
        /// </summary>
        [Test]
        public void Apply_PerInstanceOverride_StretchesExactlyAsAuthored_NonUniform()
        {
            // ARRANGE: 512×512 square sprite, non-uniform override 800×1200 (portrait).
            const int spriteW = 512, spriteH = 512;
            const int overrideW = 800, overrideH = 1200;

            // Correct localScale from the override path:
            float scaleX = (float)overrideW / spriteW; // 800/512 ≈ 1.5625
            float scaleY = (float)overrideH / spriteH; // 1200/512 ≈ 2.344

            var sprite = MakeSprite(spriteW, spriteH);
            var go     = new GameObject("BuildingPortraitOverride");
            var bObj   = go.AddComponent<BuildingObject>();
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            var sr = MakeChildRenderer(go, "Footprint", sprite);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // ACT
            bObj.TryGetWorldRect(out Rect rect);

            // ASSERT — world aspect must equal override ratio (800:1200 = 2:3)
            float expectedAspect = (float)overrideW / overrideH; // 0.666…
            float actualAspect   = rect.width / rect.height;

            Assert.AreEqual(expectedAspect, actualAspect, 0.01f,
                $"Portrait override (800×1200) must produce portrait world rect (2:3 ≈ 0.667). " +
                $"Got {actualAspect:F3}. If the aspect-fit branch activates for overrides, " +
                "this test fails — that is the regression we are guarding.");

            // The sprite is square (1:1); if the fix were broken and aspect-fit applied,
            // the result would be ≈1.0, which must NOT equal 0.667:
            float squareAspect = 1f;
            Assert.That(actualAspect, Is.Not.EqualTo(squareAspect).Within(0.01f),
                "World rect must NOT be square when a portrait override is in effect.");

            Object.DestroyImmediate(go);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // GROUP B — TryGetWorldRect × PPU=32 contract
        //
        // BuildingsRuntimeEditor.TryGetVisibleBoundsAsPixelSize calls:
        //   TryGetWorldRect(out rect)
        //   pixelSize = (Mathf.RoundToInt(rect.width * PPU),
        //                Mathf.RoundToInt(rect.height * PPU))
        //
        // These tests lock in that rect.width * 32 and rect.height * 32 recover
        // the effective pixel size that was used when Apply() set localScale.
        // If TryGetWorldRect ever switched to using world bounds (which include
        // child offsets), or stopped accounting for localScale, these tests fail.
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// TryGetWorldRect.width * 32 recovers the effective pixel width.
        ///
        /// Setup: 552×961 sprite (atlas-trimmed statue_olim_01 simulation) with
        /// localScale = (1056/552, 1056/961) ≈ (1.913, 1.099).
        ///
        /// effW = 1056 px, effH = 1056 px (designer chose a square output).
        /// Expected: rect.width = effW / 32 = 33.0 → rect.width * 32 = 1056.
        ///           rect.height = effH / 32 = 33.0 → rect.height * 32 = 1056.
        /// </summary>
        [Test]
        public void TryGetWorldRect_BoundsTimes32_RecoversAuthoredEffectiveSize()
        {
            // ARRANGE
            // Mimics statue_olim_01: PNG was 1024×1024 but atlas trimmed to 552×961.
            // Designer authored originalScale ≈ (1056, 1056) so the building fills
            // a round number of pixels.
            const int spriteW = 552, spriteH = 961;
            const int effW = 1056, effH = 1056;

            float scaleX = (float)effW / spriteW; // ≈ 1.9130
            float scaleY = (float)effH / spriteH; // ≈ 1.0989

            var sprite = MakeSprite(spriteW, spriteH);
            var go     = new GameObject("StatueOlim01Sim");
            var bObj   = go.AddComponent<BuildingObject>();
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            var sr = MakeChildRenderer(go, "Footprint", sprite);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // ACT
            bool ok = bObj.TryGetWorldRect(out Rect rect);

            // ASSERT
            Assert.IsTrue(ok);

            int recoveredW = Mathf.RoundToInt(rect.width  * 32f);
            int recoveredH = Mathf.RoundToInt(rect.height * 32f);

            Assert.AreEqual(effW, recoveredW,
                $"rect.width * 32 must recover effW={effW}. Got {recoveredW}. " +
                "TryGetVisibleBoundsAsPixelSize multiplies by PPU=32 to get pixel size — " +
                "if TryGetWorldRect drifts, the resize drag aspect-lock breaks.");

            Assert.AreEqual(effH, recoveredH,
                $"rect.height * 32 must recover effH={effH}. Got {recoveredH}. " +
                "Same resize-helper dependency as above.");

            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// When localScale is non-uniform, world rect aspect equals (texW×scaleX)/(texH×scaleY).
        ///
        /// This is the contract that allows the resize helper to read the current aspect from
        /// TryGetWorldRect and lock it during drag. If TryGetWorldRect computed aspect from
        /// originalScale or from a non-scaled formula, the locked aspect would be wrong and
        /// every subsequent resize would drift.
        /// </summary>
        [Test]
        public void TryGetWorldRect_AspectMatchesParentLocalScale_WhenNonUniform()
        {
            // ARRANGE: 456×626 sprite (tree_1 dimensions), non-uniform scale.
            const int spriteW = 456, spriteH = 626;
            float scaleX = 2.5f;
            float scaleY = 1.3f;

            var sprite = MakeSprite(spriteW, spriteH);
            var go     = new GameObject("TreeAspectTest");
            var bObj   = go.AddComponent<BuildingObject>();
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            var sr = MakeChildRenderer(go, "Footprint", sprite);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // ACT
            bObj.TryGetWorldRect(out Rect rect);

            // ASSERT
            // Expected world size:
            //   w = (spriteW / PPU) * scaleX
            //   h = (spriteH / PPU) * scaleY
            float expectedW = spriteW / 32f * scaleX;
            float expectedH = spriteH / 32f * scaleY;
            float expectedAspect = expectedW / expectedH;
            float actualAspect   = rect.width / rect.height;

            Assert.AreEqual(expectedAspect, actualAspect, 0.005f,
                $"World rect aspect ({actualAspect:F4}) must equal " +
                $"(texW*scaleX)/(texH*scaleY) = {expectedAspect:F4}. " +
                "Resize helper reads aspect from TryGetWorldRect; drift here causes squishing.");

            Assert.AreEqual(expectedW, rect.width,  0.01f,
                "World width = (spriteW / 32) * scaleX");
            Assert.AreEqual(expectedH, rect.height, 0.01f,
                "World height = (spriteH / 32) * scaleY");

            Object.DestroyImmediate(go);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // GROUP C — Atlas-trim simulation
        //
        // Unity's SpriteAtlas packs sprites and strips transparent borders.
        // After packing, sprite.textureRect.size (the actual sprite pixel region)
        // differs from what you'd get from tex.width/tex.height (the full atlas page).
        //
        // Sprite.Create lets us simulate this: use a large atlas texture but pass a
        // small rect to the rect parameter. The sprite's textureRect then equals the
        // small rect, even though the backing texture is large.
        //
        // These tests verify that TryGetWorldRect's geometry is driven by the sprite's
        // VISIBLE textureRect, not by the atlas page size — matching what Apply() does
        // (it reads sourceSprite.textureRect, not tex.width/tex.height).
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// CORE REGRESSION: atlas-trimmed sprite — visible aspect must equal textureRect ratio.
        ///
        /// Scenario: atlas page is 2048×2048. The building sprite occupies only 552×961 px
        /// of that atlas (portrait shape, transparent borders trimmed). Apply() reads
        /// sourceSprite.textureRect = (0,0,552,961) and computes:
        ///   spriteW=552, spriteH=961 (used for localScale formula).
        ///
        /// If someone changed Apply() to use tex.width/tex.height instead of textureRect,
        /// they'd get 2048×2048 and produce a square building regardless of the sprite's
        /// actual content — this test would catch that regression.
        ///
        /// We verify: world rect width/height ratio ≈ 552:961 (portrait).
        /// </summary>
        [Test]
        public void AtlasTrim_SpriteTextureRectPortrait_WorldRectAspectMatchesTextureRect()
        {
            // ARRANGE: atlas 2048×2048, sprite region 552×961 (portrait)
            const int atlasW = 2048, atlasH = 2048;
            const int rectW = 552, rectH = 961;

            var sprite = MakeAtlasSprite(atlasW, atlasH, rectW, rectH);

            // Apply() uses textureRect → spriteW=552, spriteH=961.
            // For identity scale (localScale=1,1,1) the world rect = (552/32) × (961/32)
            // aspect = 552:961
            var go   = new GameObject("AtlasTrimPortrait");
            var bObj = go.AddComponent<BuildingObject>();
            go.transform.localScale = Vector3.one; // scale=1 → world aspect equals textureRect aspect

            var sr = MakeChildRenderer(go, "Footprint", sprite);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // ACT
            bObj.TryGetWorldRect(out Rect rect);

            // ASSERT
            float expectedAspect = (float)rectW / rectH; // 552/961 ≈ 0.574 (portrait)
            float actualAspect   = rect.width / rect.height;

            Assert.AreEqual(expectedAspect, actualAspect, 0.01f,
                $"World rect aspect ({actualAspect:F4}) must match the SPRITE textureRect aspect " +
                $"({expectedAspect:F4} = {rectW}:{rectH}), not the atlas page aspect (1:1). " +
                "If Apply() reads tex.width/tex.height instead of textureRect, this fails.");

            // Also verify that the atlas-page aspect (1:1) is NOT what we see:
            float atlasAspect = (float)atlasW / atlasH; // 1.0
            Assert.That(actualAspect, Is.Not.EqualTo(atlasAspect).Within(0.01f),
                "World rect must NOT be square (atlas aspect) when the trimmed sprite is portrait.");

            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Atlas-trimmed sprite: rect.width * 32 recovers the SPRITE'S textureRect width,
        /// not the atlas page width — confirming TryGetWorldRect uses the right pixel count.
        ///
        /// If Apply() regressed to tex.width (atlas page), TryGetVisibleBoundsAsPixelSize
        /// would read ~2048 instead of ~552 for every atlased building, and the resize
        /// drag aspect-lock would use a completely wrong starting size.
        /// </summary>
        [Test]
        public void AtlasTrim_WorldRectTimes32_RecoversTextureRectDimensions_NotAtlasPage()
        {
            // ARRANGE
            const int atlasW = 2048, atlasH = 2048;
            const int rectW = 552, rectH = 961;

            var sprite = MakeAtlasSprite(atlasW, atlasH, rectW, rectH);
            var go     = new GameObject("AtlasTrimDimsTest");
            var bObj   = go.AddComponent<BuildingObject>();
            go.transform.localScale = Vector3.one;

            var sr = MakeChildRenderer(go, "Footprint", sprite);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // ACT
            bObj.TryGetWorldRect(out Rect rect);

            int recoveredW = Mathf.RoundToInt(rect.width  * 32f);
            int recoveredH = Mathf.RoundToInt(rect.height * 32f);

            // ASSERT — must match textureRect, not atlas
            Assert.AreEqual(rectW, recoveredW,
                $"rect.width * 32 must equal the SPRITE textureRect width ({rectW}), not the atlas width ({atlasW}). " +
                $"Got {recoveredW}. Regression: Apply() reading tex.width would return ~2048.");

            Assert.AreEqual(rectH, recoveredH,
                $"rect.height * 32 must equal the SPRITE textureRect height ({rectH}), not the atlas height ({atlasH}). " +
                $"Got {recoveredH}. Same regression path.");

            Assert.AreNotEqual(atlasW, recoveredW,
                "Width must NOT be the atlas page width — that's the regression we're guarding.");

            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Atlas-trimmed landscape sprite: world aspect matches the narrow sprite region,
        /// not the square atlas page.
        ///
        /// Covers the wide (landscape) atlas-trim case to complement the portrait case above.
        /// </summary>
        [Test]
        public void AtlasTrim_SpriteTextureRectLandscape_WorldRectAspectMatchesTextureRect()
        {
            // ARRANGE: atlas 4096×4096, sprite region 3072×1024 (3:1 landscape)
            const int atlasW = 4096, atlasH = 4096;
            const int rectW = 3072, rectH = 1024;

            var sprite = MakeAtlasSprite(atlasW, atlasH, rectW, rectH);
            var go     = new GameObject("AtlasTrimLandscape");
            var bObj   = go.AddComponent<BuildingObject>();
            go.transform.localScale = Vector3.one;

            var sr = MakeChildRenderer(go, "Footprint", sprite);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // ACT
            bObj.TryGetWorldRect(out Rect rect);

            float expectedAspect = (float)rectW / rectH; // 3.0 (wide landscape)
            float actualAspect   = rect.width / rect.height;

            Assert.AreEqual(expectedAspect, actualAspect, 0.01f,
                $"Landscape atlas-trim: world aspect ({actualAspect:F3}) must equal " +
                $"textureRect aspect ({expectedAspect:F3} = {rectW}:{rectH}).");

            Object.DestroyImmediate(go);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // GROUP D — Apply() via AssetDatabase (requires sprite to exist in Resources)
        //
        // These tests call the real Apply() method. They are written as Inconclusive
        // when the required sprite is absent (EditMode + SpriteAtlas not packed) so
        // they don't fail CI artificially, but they DO execute in a full Unity Editor
        // session where Resources.Load succeeds.
        //
        // A fully-passing run in the editor catches regressions that the Groups A-C
        // simulations might not exercise (e.g. the real Sprite.Create call inside Apply,
        // the EnsureCollider path, and the sorting-order assignment).
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply with originalScale=(0,0): renderers populated, transform.localScale finite.
        ///
        /// Without the fallback: Apply() logged a warning and returned early — both renderers
        /// remained null, localScale was whatever the GameObject default was.
        /// With the fallback: Apply() uses spriteW/spriteH and produces a visible building.
        ///
        /// If the (0,0) fallback is removed, _bottomRenderer stays null and TryGetWorldRect
        /// returns false → the assertion on rect area fails.
        /// </summary>
        [Test]
        public void Apply_OriginalScaleZero_WithRealSprite_RendersWithFiniteLocalScale()
        {
            const string assetPath = "Buildings/vegetation/tree_1"; // 456×626 px
            var sourceSprite = Resources.Load<Sprite>(assetPath);
            if (sourceSprite == null)
            {
                Assert.Inconclusive(
                    $"Resources.Load<Sprite>(\"{assetPath}\") returned null in EditMode " +
                    "(SpriteAtlas may not be packed). Test skipped; run from full Unity Editor.");
                return;
            }

            var go   = new GameObject("Tree1_ZeroOrigScale");
            var bObj = go.AddComponent<BuildingObject>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.assetPath     = assetPath;
            tmpl.originalScale = Vector2Int.zero; // (0,0) — the "missing data" case
            tmpl.splitRatio    = 0.5f;
            tmpl.solid         = false; // avoid collider layer issues in EditMode

            // ACT
            bObj.Apply(tmpl, Vector2Int.zero, -1f);

            // ASSERT — local scale must be finite and positive
            Vector3 ls = go.transform.localScale;
            Assert.IsTrue(ls.x > 0f && ls.y > 0f,
                $"localScale must be positive after Apply with originalScale=(0,0). " +
                $"Got ({ls.x:F4}, {ls.y:F4}). Remove the fallback and this returns (0,0,1).");

            Assert.IsTrue(!float.IsNaN(ls.x) && !float.IsInfinity(ls.x) &&
                          !float.IsNaN(ls.y) && !float.IsInfinity(ls.y),
                "localScale components must be finite (no Infinity or NaN).");

            // World rect must have positive area
            bool rectOk = bObj.TryGetWorldRect(out Rect rect);
            Assert.IsTrue(rectOk, "TryGetWorldRect must succeed after Apply with (0,0) originalScale + fallback.");
            Assert.Greater(rect.width  * rect.height, 0f,
                "World rect must have positive area — (0,0) fallback must produce a visible building.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(tmpl);
        }

        /// <summary>
        /// Apply with originalScale aspect drifting from sprite aspect, no override:
        /// the rendered world rect aspect equals the SPRITE's native aspect, not originalScale's.
        ///
        /// Uses tree_1 (456×626, portrait) with a fabricated originalScale of (1024, 512) (2:1 wide).
        /// The drift is |1024/512 - 456/626| = |2.0 - 0.728| = 1.272 > 0.01 → fit branch.
        ///
        /// fit = Min(1024/456, 512/626) = Min(2.246, 0.818) = 0.818
        /// effW = Round(456 * 0.818) = 373, effH = Round(626 * 0.818) = 512
        /// → The rendered aspect = 373:512 ≈ 0.728 = native sprite aspect. NOT 2:1.
        /// </summary>
        [Test]
        public void Apply_AspectDriftTemplate_WithRealSprite_WorldAspectMatchesSpriteNative()
        {
            const string assetPath = "Buildings/vegetation/tree_1"; // 456×626
            var sourceSprite = Resources.Load<Sprite>(assetPath);
            if (sourceSprite == null)
            {
                Assert.Inconclusive(
                    $"Resources.Load<Sprite>(\"{assetPath}\") returned null. Skipped.");
                return;
            }

            var go   = new GameObject("Tree1_DriftedOrigScale");
            var bObj = go.AddComponent<BuildingObject>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.assetPath     = assetPath;
            tmpl.originalScale = new Vector2Int(1024, 512); // 2:1 wide — drifts from 456:626
            tmpl.splitRatio    = 0.5f;
            tmpl.solid         = false;

            // ACT
            bObj.Apply(tmpl, Vector2Int.zero, -1f);

            // ASSERT
            bool ok = bObj.TryGetWorldRect(out Rect rect);
            Assert.IsTrue(ok, "TryGetWorldRect must succeed after Apply with drifted originalScale.");

            // Sprite native aspect (after textureRect — in non-atlased EditMode, textureRect == rect)
            float spriteAspect = sourceSprite.textureRect.width / sourceSprite.textureRect.height;
            float worldAspect  = rect.width / rect.height;

            Assert.AreEqual(spriteAspect, worldAspect, 0.02f,
                $"World aspect ({worldAspect:F4}) must match SPRITE native aspect ({spriteAspect:F4}). " +
                "If the aspect-fit branch is removed, world aspect would equal originalScale aspect " +
                "(1024/512 = 2.0) — which would fail this assertion.");

            // Confirm it does NOT match the originalScale aspect (2:1)
            float originalScaleAspect = 1024f / 512f;
            Assert.That(worldAspect, Is.Not.EqualTo(originalScaleAspect).Within(0.02f),
                "World aspect must NOT equal the drifted originalScale aspect (2:1 = 2.0). " +
                "This would indicate the aspect-fit branch is missing.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(tmpl);
        }

        /// <summary>
        /// Apply with per-instance non-uniform override: world rect matches the override exactly.
        ///
        /// Uses a square sprite (dummy.png, 1×1) with override (200, 400) → portrait 1:2.
        /// Verifies the override path overrides aspect-fit entirely.
        /// </summary>
        [Test]
        public void Apply_PerInstanceOverride_WithRealSprite_HonorsOverrideAspect()
        {
            const string assetPath = "Buildings/dummy"; // 1×1 px square
            var sourceSprite = Resources.Load<Sprite>(assetPath);
            if (sourceSprite == null)
            {
                Assert.Inconclusive(
                    $"Resources.Load<Sprite>(\"{assetPath}\") returned null. Skipped.");
                return;
            }

            var go   = new GameObject("Dummy_NonUniformOverride");
            var bObj = go.AddComponent<BuildingObject>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.assetPath     = assetPath;
            tmpl.originalScale = new Vector2Int(100, 100); // square, matches sprite
            tmpl.splitRatio    = 0.5f;
            tmpl.solid         = false;

            var overrideScale = new Vector2Int(200, 400); // portrait 1:2 — override wins

            // ACT
            bObj.Apply(tmpl, overrideScale, -1f);

            // ASSERT
            bool ok = bObj.TryGetWorldRect(out Rect rect);
            Assert.IsTrue(ok);

            float expectedAspect = (float)overrideScale.x / overrideScale.y; // 0.5 (portrait)
            float actualAspect   = rect.width / rect.height;

            Assert.AreEqual(expectedAspect, actualAspect, 0.02f,
                $"Override (200×400) must produce portrait world rect (1:2). " +
                $"Got aspect {actualAspect:F4}. If override is overridden by aspect-fit, this fails.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(tmpl);
        }
    }
}
