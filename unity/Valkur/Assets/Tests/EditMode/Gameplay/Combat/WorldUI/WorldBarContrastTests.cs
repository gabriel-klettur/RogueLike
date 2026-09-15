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
    /// Every fill has to stand at least 3:1 against the plate it sits in, measured OVER the
    /// ground the plate is translucent to — which is what the style's own tooltip promises.
    ///
    /// <para>Measured before the plate existed: the hostile red stood at 1.09:1 against
    /// cobblestone, i.e. readable by hue alone and invisible to a colour-blind player.</para>
    /// </summary>
    public class WorldBarContrastTests
    {
        // Grounds the bars are drawn over in the shipped world. The pale ones are the hard case:
        // a translucent plate is lightest over them.
        private static readonly (string name, Color colour)[] Grounds =
        {
            ("cobblestone", new Color(0.55f, 0.53f, 0.50f)),
            ("pale sand",   new Color(0.80f, 0.74f, 0.60f)),
            ("foliage",     new Color(0.16f, 0.30f, 0.12f)),
            ("dark stone",  new Color(0.22f, 0.22f, 0.24f)),
        };

        private static IEnumerable<(string, Color)> Fills(WorldBarStyle s)
        {
            yield return ("healthPlayer", s.healthPlayer);
            yield return ("healthAlly", s.healthAlly);
            yield return ("healthHostile", s.healthHostile);
            yield return ("healthLow", s.healthLow);
            yield return ("healthLowPlayer", s.healthLowPlayer);
            yield return ("mana", s.mana);
            yield return ("dashReady", s.dashReady);
            yield return ("energy", s.EnergyFillColour);
        }

        [Test]
        public void EveryFill_Stands3To1_AgainstThePlate_OverEveryGround()
        {
            var style = WorldBarStyle.Active;
            Assert.IsTrue(style.drawPlate, "the plate is what the contrast is measured against");
            foreach (var (fname, fill) in Fills(style))
                foreach (var (gname, ground) in Grounds)
                {
                    var behind = WorldBarPalette.Over(style.plate, ground);
                    float c = WorldBarPalette.Contrast(fill, behind);
                    Assert.GreaterOrEqual(c, 3f,
                        $"{fname} over the plate on {gname} measures {c:F2}:1; the readout has to be " +
                        "legible by luminance, not only by hue");
                }
        }

        [Test]
        public void ThePlate_IsWhatMakesTheHostileRedReadable()
        {
            var style = WorldBarStyle.Active;
            var cobble = Grounds[0].colour;
            float bare = WorldBarPalette.Contrast(style.healthHostile, cobble);
            float plated = WorldBarPalette.Contrast(style.healthHostile, WorldBarPalette.Over(style.plate, cobble));
            Assert.Less(bare, 2f, "on bare cobblestone the red has no luminance contrast - that is the defect");
            Assert.Greater(plated, bare * 2f, "the plate must at least double it, or it is not earning its pixels");
        }

        [Test]
        public void TheOutline_IsNearBlack_ForEveryRank()
        {
            // The outline separates the bar from the ground and cannot also carry rank; rank
            // lives in the caps and the halo.
            var style = WorldBarStyle.Active;
            Assert.Less(WorldBarPalette.Luminance(style.outline), 0.02f);
            foreach (WorldBarRank rank in System.Enum.GetValues(typeof(WorldBarRank)))
            {
                Assert.Greater(WorldBarPalette.Contrast(style.CapFor(rank), style.outline), 4f,
                    $"the {rank} caps must read against the outline they sit in");
            }
        }

        [Test]
        public void ThePlayersLowColour_IsNotInTheBrassFamily()
        {
            // Amber was tried first and read as gold beside the brass caps and the gold dash stone.
            var style = WorldBarStyle.Active;
            Color.RGBToHSV(style.healthLowPlayer, out float low, out float lowS, out _);
            Color.RGBToHSV(style.capPlayer, out float cap, out _, out _);
            Color.RGBToHSV(style.dashReady, out float gold, out _, out _);
            Assert.Greater(lowS, 0.5f, "a warning is saturated");
            Assert.Greater(HueDistance(low, cap), 0.06f, "the low colour must not share the caps' hue");
            Assert.Greater(HueDistance(low, gold), 0.06f, "nor the dash stone's");
        }

        [Test]
        public void LowFor_WarnsThePlayerAndTheAlly_AndLeavesAHostileAlone()
        {
            var style = WorldBarStyle.Active;
            Assert.AreEqual(style.healthLowPlayer, style.LowFor(WorldBarRank.Player));
            Assert.AreEqual(style.healthLow, style.LowFor(WorldBarRank.Ally));
            foreach (var rank in new[] { WorldBarRank.Normal, WorldBarRank.Elite, WorldBarRank.Boss })
                Assert.AreEqual(style.HealthFor(rank), style.LowFor(rank),
                    $"a {rank} near death is not a warning the player needs; its fill stays its own colour");
        }

        private static float HueDistance(float a, float b)
        {
            float d = Mathf.Abs(Mathf.Repeat(a - b, 1f));
            return Mathf.Min(d, 1f - d);
        }
    }
}
