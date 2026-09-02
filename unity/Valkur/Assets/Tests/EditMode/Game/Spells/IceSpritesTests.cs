using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Pins the procedural ice maps against each other.
    ///
    /// <para>Four textures describe one crystal — its body, its rim light, its inner facet
    /// and its cracks — and they only look right while all four agree on the SAME outline.
    /// Nothing fails loudly when they drift: a rim that overshoots reads as a blue halo in
    /// the air beside the shard, and a crack that overshoots reads as a scratch on the
    /// screen. Both are the sort of thing that survives review and is noticed in play, which
    /// is why the agreement is asserted rather than trusted to the shared
    /// <c>ShardShape</c>.</para>
    /// </summary>
    public class IceSpritesTests
    {
        private const float InsideAlpha = 0.04f;

        [SetUp]
        public void SetUp() => IceSprites.EnsureAll();

        [Test]
        public void EveryVariantExists_AndIsBasePivoted()
        {
            for (int v = 0; v < IceSprites.VariantCount; v++)
            {
                var body = IceSprites.Body(v);
                Assert.IsNotNull(body, "variant " + v);
                Assert.AreEqual(0f, body.pivot.y, 0.01f,
                    "A shard grows UP out of the ground, so its pivot is its base — scaling Y " +
                    "from a centre pivot would sink half the crystal into the floor.");
                Assert.AreEqual(body.rect.width * 0.5f, body.pivot.x, 0.01f);
            }
        }

        [Test]
        public void ScaleShard_TranslatesWorldSizeThroughTheSpriteHeight()
        {
            var go = new GameObject("probe");
            try
            {
                IceSprites.ScaleShard(go.transform, widthWu: 3f, heightWu: 2f);
                Assert.AreEqual(3f, go.transform.localScale.x, 0.001f);
                Assert.AreEqual(2f / IceSprites.ShardUnitHeight, go.transform.localScale.y, 0.001f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Body_TapersFromBaseToTip()
        {
            for (int v = 0; v < IceSprites.VariantCount; v++)
            {
                var pixels = IceSprites.Body(v).texture.GetPixels();
                int width = IceSprites.Body(v).texture.width;
                int height = IceSprites.Body(v).texture.height;

                int baseRow = CountOpaque(pixels, width, height / 12);
                int tipRow = CountOpaque(pixels, width, height - height / 12);

                Assert.Greater(baseRow, tipRow,
                    "variant " + v + ": a crystal is thick where it meets the ground and " +
                    "pointed at the top. Equal widths draw a post.");
                Assert.Greater(baseRow, 2, "variant " + v + ": the base must be solid.");
            }
        }

        [Test]
        public void Body_IsDeepAtTheBase_AndPaleAtTheTip()
        {
            for (int v = 0; v < IceSprites.VariantCount; v++)
            {
                var texture = IceSprites.Body(v).texture;
                Color low = AverageOpaque(texture, texture.height / 8);
                Color high = AverageOpaque(texture, texture.height - texture.height / 4);

                Assert.Greater(high.r + high.g + high.b, low.r + low.g + low.b,
                    "variant " + v + ": the vertical gradient is what gives a flat top-down " +
                    "sprite its depth. A single SpriteRenderer.color cannot express it, which " +
                    "is why these textures carry colour rather than only alpha.");
            }
        }

        [Test]
        public void RimAndCrack_NeverDrawOutsideTheBody()
        {
            for (int v = 0; v < IceSprites.VariantCount; v++)
            {
                var body = IceSprites.Body(v).texture;
                AssertClipsToBody(body, IceSprites.Rim(v).texture, v, "rim");
                AssertClipsToBody(body, IceSprites.Facet(v).texture, v, "facet");
                AssertClipsToBody(body, IceSprites.Crack(v).texture, v, "crack");
            }
        }

        /// <summary>
        /// Every visible texel of an overlay must sit on the body, allowing ONE texel of
        /// slack at the outline.
        ///
        /// <para>The slack is not a fudge, it is the format. The maps are RGBA32, so alpha is
        /// quantised to 1/255: the body feathers its outermost texel towards zero for
        /// antialiasing and a coverage of a thousandth rounds to a stored zero, while the rim
        /// — whose entire job is that outline — holds the same texel near full. Demanding an
        /// exact overlap fails on the one texel the two maps are MEANT to treat differently.
        /// A rim that had actually drifted off the silhouette would miss by many texels.</para>
        /// </summary>
        private static void AssertClipsToBody(Texture2D body, Texture2D overlay, int variant, string what)
        {
            Assert.AreEqual(body.width, overlay.width, what + " map is a different size");
            Assert.AreEqual(body.height, overlay.height, what + " map is a different size");

            var bodyPixels = body.GetPixels();
            var overlayPixels = overlay.GetPixels();
            int width = body.width, height = body.height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (overlayPixels[i].a <= InsideAlpha) continue;
                    Assert.IsTrue(BodyWithinOneTexel(bodyPixels, width, height, x, y),
                        "variant " + variant + ": " + what + " draws at (" + x + "," + y +
                        ") where the crystal has no silhouette.");
                }
            }
        }

        private static bool BodyWithinOneTexel(Color[] body, int width, int height, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int sy = y + dy;
                if (sy < 0 || sy >= height) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = x + dx;
                    if (sx < 0 || sx >= width) continue;
                    if (body[sy * width + sx].a > 0f) return true;
                }
            }
            return false;
        }

        [Test]
        public void Rim_ClingsToTheEdge_NotTheCore()
        {
            var body = IceSprites.Body(0).texture;
            var rim = IceSprites.Rim(0).texture;
            int width = body.width;
            int row = body.height / 3;

            var bodyRow = body.GetPixels(0, row, width, 1);
            var rimRow = rim.GetPixels(0, row, width, 1);

            int first = -1, last = -1;
            for (int x = 0; x < width; x++)
                if (bodyRow[x].a > InsideAlpha) { if (first < 0) first = x; last = x; }
            Assert.Greater(last - first, 4, "the sampled row must actually cross the crystal");

            int centre = (first + last) / 2;
            float edgeAlpha = Mathf.Max(rimRow[first + 1].a, rimRow[last - 1].a);
            Assert.Greater(edgeAlpha, rimRow[centre].a,
                "The rim is a light catching the SILHOUETTE. Brightest in the middle would be " +
                "a glow, and a glow is what makes a hard surface read as soft.");
        }

        [Test]
        public void GroundAndDebris_AreDrawnAtAll()
        {
            Assert.IsNotNull(IceSprites.Rime);
            Assert.IsNotNull(IceSprites.Debris);
            Assert.Greater(CountOpaqueAll(IceSprites.Rime.texture), 0, "the frost patch is empty");
            Assert.Greater(CountOpaqueAll(IceSprites.Debris.texture), 0, "the debris chunk is empty");
        }

        private static int CountOpaque(Color[] pixels, int width, int row)
        {
            int count = 0;
            for (int x = 0; x < width; x++)
                if (pixels[row * width + x].a > InsideAlpha) count++;
            return count;
        }

        private static int CountOpaqueAll(Texture2D texture)
        {
            var pixels = texture.GetPixels();
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a > InsideAlpha) count++;
            return count;
        }

        private static Color AverageOpaque(Texture2D texture, int row)
        {
            var pixels = texture.GetPixels(0, row, texture.width, 1);
            Color sum = Color.clear;
            int count = 0;
            for (int x = 0; x < pixels.Length; x++)
            {
                if (pixels[x].a <= InsideAlpha) continue;
                sum += pixels[x];
                count++;
            }
            return count == 0 ? Color.black : sum / count;
        }
    }
}
