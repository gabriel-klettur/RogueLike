using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.Tests.EditMode.UI.MainMenu.Kit
{
    /// <summary>
    /// The menus' legibility, measured rather than eyeballed.
    ///
    /// <para><b>Why this fixture exists.</b> The 2026-09-12 audit measured the shipped menu's
    /// SELECTED row at <b>4.15 : 1</b> against its own pill, and the row that was NOT selected at
    /// <b>9.61 : 1</b> — so choosing a row made it harder to read, and the one the player was
    /// looking at was the worst-drawn thing on the panel. It shipped because 78 EditMode tests
    /// could see the structure and none of them could see a colour.</para>
    ///
    /// <para><b>It is PURE.</b> No scene, no canvas, no Play Mode — uGUI performs no layout in
    /// EditMode, so a test that builds a panel and reads a rect measures the default 100 px. What
    /// CAN be checked without any of that is the arithmetic: composite the colours the code will
    /// use and compute the WCAG ratio, exactly as <c>WorldBarContrastTests</c> does for the bars
    /// over an entity's head.</para>
    /// </summary>
    public class MenuContrastTests
    {
        private const float AaLargeText = 3.0f;
        private const float AaBodyText = 4.5f;

        private static MenuStyle Style => MenuStyle.Active;

        [Test]
        public void TheSelectedRow_IsAtLeastAsLegibleAsAnUnselectedOne()
        {
            var panel = Opaque(Style.Panel);
            float unselected = MenuUIKit.Contrast(Style.TextPrimary, panel);

            // The pill is drawn at full alpha over the panel: it is the row's background.
            var pill = MenuUIKit.Composite(Style.Gold, panel);
            float selected = MenuUIKit.Contrast(Style.textOnSelection, pill);

            Assert.GreaterOrEqual(selected, AaBodyText,
                $"selected row measured {selected:F2}:1, under the AA body threshold");
            Assert.GreaterOrEqual(selected, unselected * 0.5f,
                $"selecting a row must not halve its legibility (selected {selected:F2}:1, " +
                $"unselected {unselected:F2}:1)");
        }

        [Test]
        public void AnUnselectedRow_ClearsAaBodyText()
        {
            float ratio = MenuUIKit.Contrast(Style.TextPrimary, Opaque(Style.Panel));
            Assert.GreaterOrEqual(ratio, AaBodyText, $"measured {ratio:F2}:1");
        }

        [Test]
        public void TheValueColumn_ClearsAaBodyText_OnBothRowStates()
        {
            var panel = Opaque(Style.Panel);
            float unselected = MenuUIKit.Contrast(Style.Gold, panel);
            Assert.GreaterOrEqual(unselected, AaBodyText,
                $"gold on the panel measured {unselected:F2}:1");

            var pill = MenuUIKit.Composite(Style.Gold, panel);
            float selected = MenuUIKit.Contrast(Style.textOnSelection, pill);
            Assert.GreaterOrEqual(selected, AaBodyText,
                $"the value on a selected row measured {selected:F2}:1");
        }

        [Test]
        public void ReadableOn_PicksTheDarkInkOverGold_AndTheLightInkOverThePanel()
        {
            // This is the one-line rule the whole 4.15:1 defect came down to: on a filled gold
            // pill the text goes dark, not gold.
            Assert.AreEqual(Style.textOnSelection, MenuUIKit.ReadableOn(Style.Gold, Style));
            Assert.AreEqual(Style.TextPrimary, MenuUIKit.ReadableOn(Opaque(Style.Panel), Style));
        }

        [Test]
        public void TheHintLine_IsLegibleOverTheBottomScrim()
        {
            // The hints sit on the cubic black ramp along the foot of the screen, which reaches
            // about 0.85 alpha where they are. Anything brighter behind them is the ramp's job.
            var scrimmed = MenuUIKit.Composite(new Color(0f, 0f, 0f, 0.85f), Color.white);
            float ratio = MenuUIKit.Contrast(Style.TextMuted, scrimmed);
            Assert.GreaterOrEqual(ratio, AaLargeText,
                $"the hint line measured {ratio:F2}:1 over the worst case the ramp has to cover");
        }

        [Test]
        public void TheDangerButton_CarriesReadableInk()
        {
            var bg = new Color(Style.Danger.r * 0.55f, Style.Danger.g * 0.28f, Style.Danger.b * 0.28f, 1f);
            float ratio = MenuUIKit.Contrast(MenuUIKit.ReadableOn(bg, Style), bg);
            Assert.GreaterOrEqual(ratio, AaLargeText, $"Delete measured {ratio:F2}:1");
        }

        [Test]
        public void TheTitlePalette_RunsHotCoreToWarmRim()
        {
            // Colour is a property of the pen: near-white at the middle of a stroke, ember at its
            // edge. A ramp that went the other way would read as a hollow outline.
            float core = MenuUIKit.Luminance(Style.titleCore);
            float mid = MenuUIKit.Luminance(Style.titleMid);
            float edge = MenuUIKit.Luminance(Style.titleEdge);
            Assert.Greater(core, mid, "the core must be the brightest tone");
            Assert.Greater(mid, edge, "the rim must be the darkest tone");
        }

        [Test]
        public void Contrast_IsSymmetricAndBounded()
        {
            Assert.AreEqual(1f, MenuUIKit.Contrast(Color.red, Color.red), 0.0001f);
            Assert.AreEqual(21f, MenuUIKit.Contrast(Color.white, Color.black), 0.05f);
            Assert.AreEqual(MenuUIKit.Contrast(Color.white, Color.black),
                            MenuUIKit.Contrast(Color.black, Color.white), 0.0001f);
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);
    }
}
