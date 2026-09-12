using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Valkur.Core;
using Valkur.Core.UI;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// The pre-game menus' string table.
    ///
    /// <para><b>What it deliberately does NOT assert.</b> That the Spanish differs from the
    /// English. "Audio", "Normal", "No" and several others are the same word in both, and a rule
    /// that demanded a difference would fail a correct translation. What is worth pinning is that
    /// both languages ANSWER, and that no label is also a key.</para>
    ///
    /// <para><b>The language lives in PlayerPrefs, which is MACHINE state.</b> It survives the
    /// run, the Editor and the reboot, so every test here forces it and restores it in a
    /// <c>finally</c> — otherwise this fixture is red only on the machine of whoever last chose
    /// English, for a reason the test's name does not mention. Same shape as the
    /// <c>Debug.unityLogger.logEnabled</c> trap CLAUDE.md records.</para>
    /// </summary>
    public class MenuTextTests
    {
        private string _saved;

        [SetUp]
        public void SetUp() => _saved = GameLanguage.Current;

        [TearDown]
        public void TearDown() => GameLanguage.Set(_saved);

        /// <summary>
        /// Runs <paramref name="body"/> in <paramref name="language"/> and puts the machine's own
        /// preference back in a <c>finally</c>.
        ///
        /// <para><b>A <c>TearDown</c> is not enough, and this fixture proved it the hard way.</b>
        /// The language lives in PlayerPrefs — machine state that survives the run, the Editor
        /// and the reboot — and a run that is CUT SHORT never reaches TearDown. One aborted run
        /// of this fixture left the whole machine in Spanish, which two other sessions then saw
        /// as their windows silently changing language. The restore has to be inside the test,
        /// on the path that runs even when an assertion throws. Same rule CLAUDE.md records for
        /// a probe that touches <c>Debug.unityLogger.logEnabled</c>.</para>
        /// </summary>
        private void InLanguage(string language, System.Action body)
        {
            string before = GameLanguage.Current;
            try
            {
                GameLanguage.Set(language);
                body();
            }
            finally
            {
                GameLanguage.Set(before);
            }
        }

        private static IEnumerable<PropertyInfo> StringProperties()
        {
            foreach (var p in typeof(MenuText).GetProperties(BindingFlags.Public | BindingFlags.Static))
                if (p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                    yield return p;
        }

        [Test]
        public void EveryString_AnswersInBothLanguages()
        {
            foreach (var language in new[] { GameLanguage.SPANISH, GameLanguage.ENGLISH })
                InLanguage(language, () =>
                {
                    foreach (var p in StringProperties())
                    {
                        var value = (string)p.GetValue(null);
                        Assert.IsFalse(string.IsNullOrWhiteSpace(value),
                            $"MenuText.{p.Name} is empty in '{language}'");
                    }
                });
        }

        [Test]
        public void TheTable_IsNotEmpty()
        {
            int count = 0;
            foreach (var _ in StringProperties()) count++;
            // A guard against a vacuous pass: a reflection sweep over an empty set is green.
            Assert.Greater(count, 40, "the table should cover the ten pre-game screens");
        }

        [Test]
        public void SwitchingLanguage_ChangesWhatTheMenuSays()
        {
            string es = null, en = null;
            InLanguage(GameLanguage.SPANISH, () => es = MenuText.NewGame);
            InLanguage(GameLanguage.ENGLISH, () => en = MenuText.NewGame);
            Assert.AreNotEqual(es, en, "'New game' is one of the labels that really does differ");
        }

        [Test]
        public void TheGameTitle_IsTheGamesName_AndDoesNotChangeWithLanguage()
        {
            Assert.AreEqual("VALKUR", MenuText.GameTitle);
            InLanguage(GameLanguage.ENGLISH,
                () => Assert.AreEqual("VALKUR", MenuText.GameTitle, "a name is not translated"));
        }

        /// <summary>
        /// The title is drawn from a stroke table, so every character of it must be spellable.
        /// A letter the table cannot draw is SKIPPED, which would silently shorten the name.
        /// </summary>
        [Test]
        public void EveryCharacterOfTheTitle_CanBeDrawn()
        {
            foreach (char c in MenuText.GameTitle)
                Assert.IsTrue(TitleGlyphStrokes.Has(c),
                    $"the title contains '{c}', which TitleGlyphStrokes cannot draw");
        }

        [Test]
        public void Timestamps_AreReadable_NotIso()
        {
            InLanguage(GameLanguage.SPANISH, TimestampsAreReadable);
        }

        private void TimestampsAreReadable()
        {
            var now = System.DateTime.Now;
            string today = MenuText.FormatTimestamp(now);
            Assert.IsFalse(today.Contains("T"), "an ISO stamp leaked into a player-facing date");
            StringAssert.Contains("hoy", today);

            string yesterday = MenuText.FormatTimestamp(now.AddDays(-1));
            StringAssert.Contains("ayer", yesterday);

            string old = MenuText.FormatTimestamp(now.AddDays(-30));
            Assert.IsFalse(old.Contains("T"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(old));
        }

        [Test]
        public void Playtime_IsHoursAndMinutes_NeverRawSeconds()
        {
            Assert.IsFalse(MenuText.FormatPlaytime(System.TimeSpan.FromSeconds(20)).Contains("20"));
            StringAssert.Contains("min", MenuText.FormatPlaytime(System.TimeSpan.FromMinutes(42)));
            StringAssert.Contains("h", MenuText.FormatPlaytime(System.TimeSpan.FromHours(3.5)));
        }

        [Test]
        public void GameLanguage_RefusesAnUnknownCode_AndFallsBackToSpanish()
        {
            string before = GameLanguage.Current;
            try
            {
                GameLanguage.Set("fr");
                Assert.AreEqual(GameLanguage.SPANISH, GameLanguage.Current,
                    "a preference file holding 'fr' would otherwise leave the game with no strings");
            }
            finally { GameLanguage.Set(before); }
        }

        [Test]
        public void ChatLanguage_ForwardsToGameLanguage()
        {
            // ChatLanguage kept its whole API when the decision moved to Core; ChatSystem's
            // per-NPC sync and ChatUI's chrome subscription both still read it.
            string before = GameLanguage.Current;
            try
            {
                GameLanguage.Set(GameLanguage.ENGLISH);
                Assert.AreEqual(GameLanguage.ENGLISH, Valkur.Gameplay.Chat.ChatLanguage.Current);
                Assert.IsTrue(Valkur.Gameplay.Chat.ChatLanguage.IsEnglish);

                Valkur.Gameplay.Chat.ChatLanguage.Set(GameLanguage.SPANISH);
                Assert.AreEqual(GameLanguage.SPANISH, GameLanguage.Current,
                    "setting it through the chat must move the one global preference");
            }
            finally { GameLanguage.Set(before); }
        }

        [Test]
        public void EveryPlayableClass_HasASentence()
        {
            foreach (var preset in Valkur.Data.PlayerClassCatalog.AllPresets)
            {
                var key = preset.PlayerKey;
                Assert.IsTrue(MenuClassCopy.Has(key),
                    $"the class selector would show '{key}' with no description");
                foreach (var language in new[] { GameLanguage.SPANISH, GameLanguage.ENGLISH })
                    InLanguage(language, () =>
                        Assert.IsFalse(string.IsNullOrWhiteSpace(MenuClassCopy.Describe(key)),
                            $"'{key}' has no line in '{language}'"));
            }
        }

        [Test]
        public void AnUnknownClass_DescribesAsNothing_NeverAsAnotherClass()
        {
            Assert.AreEqual(string.Empty, MenuClassCopy.Describe("necromancer"));
            Assert.AreEqual(string.Empty, MenuClassCopy.Describe(null));
        }
    }
}
