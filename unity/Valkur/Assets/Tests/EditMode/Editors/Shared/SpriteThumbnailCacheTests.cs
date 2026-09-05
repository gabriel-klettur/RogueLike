using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Editors;

namespace Valkur.Tests.EditMode.Editors.Shared
{
    /// <summary>
    /// Picker icons are point-sampled production sprites: a 1024 px castle in a 64 px slot
    /// keeps one texel in sixteen and reads as speckle. <see cref="SpriteThumbnailCache"/>
    /// answers with a box-filtered, bilinear, mip-mapped copy — GPU-built, cropped to the
    /// sprite's own rect on its atlas page, and cached per source.
    /// </summary>
    [TestFixture]
    public class SpriteThumbnailCacheTests
    {
        private readonly List<Object> _cleanup = new List<Object>();

        [SetUp]    public void SetUp()    => SpriteThumbnailCache.Clear();

        [TearDown]
        public void TearDown()
        {
            SpriteThumbnailCache.Clear();
            foreach (var o in _cleanup) if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();
        }

        private Texture2D MakeTexture(int w, int h, System.Func<int, int, Color> paint)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = paint(x, y);
            tex.SetPixels(px);
            tex.Apply(false, false);
            _cleanup.Add(tex);
            return tex;
        }

        private Sprite MakeSprite(Texture2D tex, Rect rect)
        {
            var s = Sprite.Create(tex, rect, new Vector2(0.5f, 0f), 32f);
            _cleanup.Add(s);
            return s;
        }

        /// <summary>Reads a (possibly non-readable) texture back through a render texture.</summary>
        private static Color[] ReadBack(Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                readable.Apply();
                var px = readable.GetPixels();
                Object.DestroyImmediate(readable);
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        [Test]
        public void SmallSprite_IsReturnedAsItIs()
        {
            var tex = MakeTexture(64, 64, (x, y) => Color.white);
            var src = MakeSprite(tex, new Rect(0, 0, 64, 64));

            var thumb = SpriteThumbnailCache.Get(src, 128);

            Assert.AreSame(src, thumb, "At or under the target size the point-filtered art is the correct look.");
            Assert.AreEqual(0, SpriteThumbnailCache.Count, "Nothing to cache for a sprite that was not copied.");
        }

        [Test]
        public void LargeSprite_BecomesABilinearMippedThumbnail()
        {
            var tex = MakeTexture(512, 512, (x, y) => ((x / 8 + y / 8) % 2 == 0) ? Color.black : Color.white);
            var src = MakeSprite(tex, new Rect(0, 0, 512, 512));

            var thumb = SpriteThumbnailCache.Get(src, 128);

            Assert.AreNotSame(src, thumb);
            Assert.AreEqual(128, thumb.texture.width);
            Assert.AreEqual(128, thumb.texture.height);
            Assert.AreEqual(FilterMode.Bilinear, thumb.texture.filterMode, "Point sampling is what made the icons speckle.");
            Assert.Greater(thumb.texture.mipmapCount, 1, "Mips are what keep a further downscale in the Image clean.");
            Assert.AreEqual(1, SpriteThumbnailCache.Count);
        }

        [Test]
        public void Thumbnail_PreservesAspect_OnTheLongestSide()
        {
            var tex = MakeTexture(400, 100, (x, y) => Color.gray);
            var src = MakeSprite(tex, new Rect(0, 0, 400, 100));

            var thumb = SpriteThumbnailCache.Get(src, 128);

            Assert.AreEqual(128, thumb.texture.width);
            Assert.AreEqual(32,  thumb.texture.height);
        }

        [Test]
        public void Thumbnail_IsAnAverage_NotASample()
        {
            // A fine checker averages to mid-grey. Point sampling would return black or
            // white per texel — which is exactly the speckle the picker showed.
            var tex = MakeTexture(256, 256, (x, y) => ((x + y) % 2 == 0) ? Color.black : Color.white);
            var src = MakeSprite(tex, new Rect(0, 0, 256, 256));

            var thumb = SpriteThumbnailCache.Get(src, 64);
            var px = ReadBack(thumb.texture);

            float mean = 0f;
            for (int i = 0; i < px.Length; i++) mean += px[i].r;
            mean /= px.Length;
            Assert.That(mean, Is.InRange(0.3f, 0.7f), "A box-filtered fine checker is grey.");

            int extremes = 0;
            for (int i = 0; i < px.Length; i++) if (px[i].r < 0.05f || px[i].r > 0.95f) extremes++;
            Assert.Less(extremes, px.Length / 10, "Most texels must be blended, not one of the two source colours.");
        }

        [Test]
        public void Thumbnail_CropsTheSpriteRect_NotThePage()
        {
            // Left half red, right half blue; the sprite is the RIGHT half only.
            var tex = MakeTexture(256, 256, (x, y) => x < 128 ? Color.red : Color.blue);
            var src = MakeSprite(tex, new Rect(128, 0, 128, 256));

            var thumb = SpriteThumbnailCache.Get(src, 64);
            var px = ReadBack(thumb.texture);

            Assert.AreEqual(32, thumb.texture.width, "Half as wide as tall, like the sprite rect.");
            Assert.AreEqual(64, thumb.texture.height);
            float red = 0f, blue = 0f;
            for (int i = 0; i < px.Length; i++) { red += px[i].r; blue += px[i].b; }
            Assert.Greater(blue / px.Length, 0.8f, "The sprite's own pixels are blue.");
            Assert.Less(red / px.Length, 0.2f, "Rect(0,0,…) on the page would have handed back the red half.");
        }

        [Test]
        public void SameSource_ReturnsTheCachedThumbnail()
        {
            var tex = MakeTexture(512, 512, (x, y) => Color.green);
            var src = MakeSprite(tex, new Rect(0, 0, 512, 512));

            var a = SpriteThumbnailCache.Get(src, 128);
            var b = SpriteThumbnailCache.Get(src, 128);

            Assert.AreSame(a, b);
            Assert.IsTrue(SpriteThumbnailCache.Has(src));
            Assert.AreEqual(1, SpriteThumbnailCache.Count);
        }

        [Test]
        public void Clear_DestroysTheTextures_AndForgetsThem()
        {
            var tex = MakeTexture(512, 512, (x, y) => Color.green);
            var src = MakeSprite(tex, new Rect(0, 0, 512, 512));
            var thumb = SpriteThumbnailCache.Get(src, 128);
            var owned = thumb.texture;

            SpriteThumbnailCache.Clear();

            Assert.AreEqual(0, SpriteThumbnailCache.Count);
            Assert.IsFalse(SpriteThumbnailCache.Has(src));
            Assert.IsTrue(owned == null, "The thumbnail texture is HideAndDontSave; nothing else will ever release it.");
        }

        [Test]
        public void Null_IsNull()
        {
            Assert.IsNull(SpriteThumbnailCache.Get(null));
        }
    }
}
