using NUnit.Framework;
using UnityEngine;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The generated icon atlas. The property that matters is TWO TONES in one texture: a white
    /// fill (tinted by vertex colour) inside a black outline (which no tint can lighten). The
    /// minimap this replaced drew flat squares with no outline, and a gold square over gold-lit
    /// ground was not there.
    /// </summary>
    public class MinimapIconAtlasTests
    {
        [Test]
        public void EveryIcon_HasACell()
        {
            Assert.That(MinimapIconAtlas.IconCount, Is.LessThanOrEqualTo(MinimapIconAtlas.Columns * MinimapIconAtlas.Rows));
            Assert.IsNotNull(MinimapIconAtlas.Texture);
        }

        [TestCase(MinimapIcon.Dot)]
        [TestCase(MinimapIcon.Arrow)]
        [TestCase(MinimapIcon.Skull)]
        [TestCase(MinimapIcon.Exclaim)]
        [TestCase(MinimapIcon.Door)]
        [TestCase(MinimapIcon.Hammer)]
        public void AnOutlinedIcon_IsWhiteInside_AndBlackAroundIt(MinimapIcon icon)
        {
            var tex = MinimapIconAtlas.Texture;
            var uv = MinimapIconAtlas.UvOf(icon);
            int x0 = Mathf.RoundToInt(uv.x * tex.width), y0 = Mathf.RoundToInt(uv.y * tex.height);
            int n = MinimapIconAtlas.CellPixels;
            var px = tex.GetPixels32(0);
            int white = 0, black = 0;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var c = px[(y0 + y) * tex.width + x0 + x];
                if (c.a < 250) continue;
                if (c.r > 240) white++;
                else if (c.r < 15) black++;
            }
            Assert.That(white, Is.GreaterThan(40), "a fill the tint can colour");
            Assert.That(black, Is.GreaterThan(40), "an outline the tint cannot lighten");
        }

        [TestCase(MinimapIcon.Glow)]
        [TestCase(MinimapIcon.Spark)]
        [TestCase(MinimapIcon.SoftRing)]
        public void AGlowIcon_HasNoDarkPixels(MinimapIcon icon)
        {
            var tex = MinimapIconAtlas.Texture;
            var uv = MinimapIconAtlas.UvOf(icon);
            int x0 = Mathf.RoundToInt(uv.x * tex.width), y0 = Mathf.RoundToInt(uv.y * tex.height);
            int n = MinimapIconAtlas.CellPixels;
            var px = tex.GetPixels32(0);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var c = px[(y0 + y) * tex.width + x0 + x];
                if (c.a > 8) Assert.That(c.r, Is.EqualTo(255), "an additive glow carrying dark rgb would dim instead of glow");
            }
        }

        [Test]
        public void CellEdges_AreTransparent_SoMipsDoNotBleed()
        {
            var tex = MinimapIconAtlas.Texture;
            var px = tex.GetPixels32(0);
            int n = MinimapIconAtlas.CellPixels;
            for (int i = 0; i < MinimapIconAtlas.IconCount; i++)
            {
                var icon = (MinimapIcon)i;
                if (icon == MinimapIcon.Bar) continue;     // a solid block by design
                var uv = MinimapIconAtlas.UvOf(icon);
                int x0 = Mathf.RoundToInt(uv.x * tex.width), y0 = Mathf.RoundToInt(uv.y * tex.height);
                for (int k = 0; k < n; k++)
                {
                    Assert.That(px[(y0 + k) * tex.width + x0].a, Is.LessThan(8), $"{icon} touches its left edge");
                    Assert.That(px[(y0 + k) * tex.width + x0 + n - 1].a, Is.LessThan(8), $"{icon} touches its right edge");
                }
            }
        }

        [Test]
        public void SpriteOf_ReturnsASpriteOverTheCell()
        {
            var s = MinimapIconAtlas.SpriteOf(MinimapIcon.MapFold);
            Assert.IsNotNull(s);
            Assert.AreEqual(MinimapIconAtlas.CellPixels, (int)s.rect.width);
            Assert.AreSame(s, MinimapIconAtlas.SpriteOf(MinimapIcon.MapFold), "cached");
        }
    }
}
