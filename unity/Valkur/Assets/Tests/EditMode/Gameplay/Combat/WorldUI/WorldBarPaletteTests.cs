using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Combat.Death;
namespace Valkur.Tests.EditMode.Gameplay.Combat.WorldUI
{
    /// <summary>
    /// The colour arithmetic behind a bar: one authored colour becomes four tones, and the tones
    /// have to go the way a pixel artist shades — light toward yellow, shade toward blue — or the
    /// bar is a sticker.
    /// </summary>
    public class WorldBarPaletteTests
    {
        private static readonly Color[] Bases =
        {
            new Color(0.31f, 0.84f, 0.40f, 1f),   // player green
            new Color(0.90f, 0.24f, 0.22f, 1f),   // hostile red - hue 0, the wrap-around case
            new Color(0.30f, 0.52f, 1.00f, 1f),   // mana blue
            new Color(0.94f, 0.27f, 0.20f, 0.5f), // low red, half faded
            new Color(0.98f, 0.76f, 0.28f, 1f),   // the dash gold
        };

        private const float WARM = 1f / 6f, COOL = 2f / 3f;

        private static float HueDistance(float a, float b)
        {
            float d = Mathf.Abs(Mathf.Repeat(a - b, 1f));
            return Mathf.Min(d, 1f - d);
        }

        private static float Hue(Color c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h;
        }

        [Test]
        public void TheRamp_GoesLightToDark_TopToBottom_AndTheEdgeIsTheBrightest()
        {
            foreach (var b in Bases)
            {
                var r = WorldBarPalette.Ramp(b);
                float hi = WorldBarPalette.Luminance(r.Highlight);
                float body = WorldBarPalette.Luminance(r.Base);
                float lo = WorldBarPalette.Luminance(r.Shadow);
                float edge = WorldBarPalette.Luminance(r.Edge);
                Assert.Greater(hi, body, $"highlight must be lighter than the body for {b}");
                Assert.Greater(body, lo, $"shadow must be darker than the body for {b}");
                Assert.GreaterOrEqual(edge, hi - 1e-4f,
                    $"the leading edge is the brightest tone, or the fill has no front, for {b}");
            }
        }

        [Test]
        public void TheRamp_ShiftsHue_WarmUpAndCoolDown()
        {
            foreach (var b in Bases)
            {
                Color.RGBToHSV(b, out float h, out float s, out _);
                if (s < 0.05f) continue;
                var r = WorldBarPalette.Ramp(b);
                float baseToWarm = HueDistance(h, WARM), baseToCool = HueDistance(h, COOL);
                Assert.LessOrEqual(HueDistance(Hue(r.Highlight), WARM), baseToWarm + 1e-4f,
                    $"the highlight leans toward yellow for {b}");
                Assert.LessOrEqual(HueDistance(Hue(r.Shadow), COOL), baseToCool + 1e-4f,
                    $"the shadow leans toward blue for {b} - through magenta for a red, never the long way round through green");
            }
        }

        [Test]
        public void TheRamp_KeepsTheAuthoredAlpha_OnEveryTone()
        {
            var r = WorldBarPalette.Ramp(new Color(0.9f, 0.2f, 0.2f, 0.37f));
            Assert.AreEqual(0.37f, r.Highlight.a, 1e-5f);
            Assert.AreEqual(0.37f, r.Base.a, 1e-5f);
            Assert.AreEqual(0.37f, r.Shadow.a, 1e-5f);
            Assert.AreEqual(0.37f, r.Edge.a, 1e-5f,
                "alpha on a bar is the fade, never a shading decision");
        }

        [Test]
        public void Toward_TakesTheShorterArc()
        {
            // Red (0) toward blue (2/3) must go DOWN through magenta: the result sits just below 1.
            float h = WorldBarPalette.Toward(0f, COOL, 0.14f);
            Assert.Greater(h, 0.9f, "red toward blue goes through magenta, not through green");
            // Red toward yellow goes up.
            Assert.Greater(WorldBarPalette.Toward(0f, WARM, 0.18f), 0f);
            Assert.Less(WorldBarPalette.Toward(0f, WARM, 0.18f), WARM);
        }
    }
}
