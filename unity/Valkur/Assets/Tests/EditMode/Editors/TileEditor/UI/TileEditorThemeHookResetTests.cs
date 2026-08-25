using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    /// <summary>
    /// Regression test for the "TileEditorTheme's SubsystemRegistration hook only
    /// cleared OnChanged, not the 8 mutable color/size fields" domain-reload blind
    /// spot: the F8 UX panel edits <see cref="TileEditorTheme.PanelBg"/>,
    /// <see cref="TileEditorTheme.HeaderBg"/>, <see cref="TileEditorTheme.Border"/>,
    /// <see cref="TileEditorTheme.Separator"/>, <see cref="TileEditorTheme.HeaderTitle"/>,
    /// <see cref="TileEditorTheme.SectionText"/>, <see cref="TileEditorTheme.MenuBarBg"/>
    /// and <see cref="TileEditorTheme.OutlinePx"/> live, and — because Domain Reload is
    /// OFF — a tuned value used to survive Stop/Play and bleed into every editor's
    /// floating panels for the rest of the process (<c>PanelChrome.ColorSource</c> is
    /// wired to this theme in the static constructor).
    ///
    /// Unlike <c>TileEditorThemeTests.ResetToDefaults_RestoresEveryField</c> (which
    /// calls <see cref="TileEditorTheme.ResetToDefaults"/> directly and does not cover
    /// <see cref="TileEditorTheme.SectionText"/>), this drives the actual
    /// <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c> hook via
    /// reflection — the same entry point Unity calls on every Play — and checks all 8
    /// fields, so a future edit that adds a 9th field or detaches the hook from
    /// <see cref="TileEditorTheme.ResetToDefaults"/> fails here specifically.
    /// </summary>
    [TestFixture]
    public class TileEditorThemeHookResetTests
    {
        [SetUp]
        public void SetUp() => TileEditorTheme.ResetToDefaults();

        [TearDown]
        public void TearDown() => TileEditorTheme.ResetToDefaults();

        /// <summary>Runs every SubsystemRegistration hook on <paramref name="type"/>, as Unity would on Play.</summary>
        private static void SimulatePlayModeEnter(Type type)
        {
            const BindingFlags flags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            var hooks = type.GetMethods(flags)
                .Where(m => m.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false)
                             .Cast<RuntimeInitializeOnLoadMethodAttribute>()
                             .Any(a => a.loadType == RuntimeInitializeLoadType.SubsystemRegistration))
                .ToList();

            Assert.IsNotEmpty(hooks,
                $"{type.Name} has no SubsystemRegistration hook. Domain Reload is OFF, so its statics " +
                "carry straight into the next Play session.");

            foreach (var h in hooks) h.Invoke(null, null);
        }

        [Test]
        public void SubsystemRegistrationHook_RestoresAllEightThemeFieldsToDefaults_EvenAfterMutation()
        {
            // Capture the real defaults up front (SetUp already reset to them).
            var defPanel   = TileEditorTheme.PanelBg;
            var defHeader  = TileEditorTheme.HeaderBg;
            var defBorder  = TileEditorTheme.Border;
            var defSep     = TileEditorTheme.Separator;
            var defTitle   = TileEditorTheme.HeaderTitle;
            var defSection = TileEditorTheme.SectionText;
            var defMenuBar = TileEditorTheme.MenuBarBg;
            var defOutline = TileEditorTheme.OutlinePx;

            // Mutate every one of the 8 fields, as the F8 UX panel's sliders would.
            TileEditorTheme.PanelBg     = new Color(1f, 0f, 0f, 0.9f);
            TileEditorTheme.HeaderBg    = new Color(0f, 1f, 0f, 0.9f);
            TileEditorTheme.Border      = new Color(0f, 0f, 1f, 0.9f);
            TileEditorTheme.Separator   = new Color(1f, 1f, 0f, 0.9f);
            TileEditorTheme.HeaderTitle = new Color(1f, 0f, 1f, 0.9f);
            TileEditorTheme.SectionText = new Color(0f, 1f, 1f, 0.9f);
            TileEditorTheme.MenuBarBg   = new Color(0.5f, 0.5f, 0.5f, 0.9f);
            TileEditorTheme.OutlinePx   = 9f;

            // Sanity: the mutation actually took, and differs from the captured default,
            // for every single field -- otherwise a coincidental default value would let
            // this test pass without ever exercising the reset.
            Assert.AreNotEqual(defPanel,   TileEditorTheme.PanelBg);
            Assert.AreNotEqual(defHeader,  TileEditorTheme.HeaderBg);
            Assert.AreNotEqual(defBorder,  TileEditorTheme.Border);
            Assert.AreNotEqual(defSep,     TileEditorTheme.Separator);
            Assert.AreNotEqual(defTitle,   TileEditorTheme.HeaderTitle);
            Assert.AreNotEqual(defSection, TileEditorTheme.SectionText);
            Assert.AreNotEqual(defMenuBar, TileEditorTheme.MenuBarBg);
            Assert.AreNotEqual(defOutline, TileEditorTheme.OutlinePx);

            // Act: enter Play the way Unity actually does -- via the hook, not by
            // calling ResetToDefaults() directly.
            SimulatePlayModeEnter(typeof(TileEditorTheme));

            // Assert: every one of the 8 fields is back to its default.
            Assert.AreEqual(defPanel,   TileEditorTheme.PanelBg,     "PanelBg was not restored by the hook.");
            Assert.AreEqual(defHeader,  TileEditorTheme.HeaderBg,    "HeaderBg was not restored by the hook.");
            Assert.AreEqual(defBorder,  TileEditorTheme.Border,      "Border was not restored by the hook.");
            Assert.AreEqual(defSep,     TileEditorTheme.Separator,   "Separator was not restored by the hook.");
            Assert.AreEqual(defTitle,   TileEditorTheme.HeaderTitle, "HeaderTitle was not restored by the hook.");
            Assert.AreEqual(defSection, TileEditorTheme.SectionText, "SectionText was not restored by the hook.");
            Assert.AreEqual(defMenuBar, TileEditorTheme.MenuBarBg,   "MenuBarBg was not restored by the hook.");
            Assert.AreEqual(defOutline, TileEditorTheme.OutlinePx, 0.0001f, "OutlinePx was not restored by the hook.");
        }

        [Test]
        public void SubsystemRegistrationHook_AlsoClearsOnChangedSubscribers()
        {
            // The hook's original behaviour (dropping stale listeners from the previous
            // Play session) must survive alongside the new field-reset call.
            int count = 0;
            Action handler = () => count++;
            TileEditorTheme.OnChanged += handler;

            SimulatePlayModeEnter(typeof(TileEditorTheme));

            TileEditorTheme.ApplyToAll();
            Assert.AreEqual(0, count,
                "The pre-existing subscriber should have been dropped by the hook, same as before this fix.");
        }
    }
}
