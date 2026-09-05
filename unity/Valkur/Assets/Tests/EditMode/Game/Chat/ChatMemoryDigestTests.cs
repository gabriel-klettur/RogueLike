using System.Linq;
using NUnit.Framework;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// EditMode coverage for <see cref="ChatMemoryDigest"/> — the second tier of memory,
    /// which is what a character still knows once the twelve-message verbatim window has
    /// rolled past.
    ///
    /// <para>Two properties matter more than the individual patterns. A note must be
    /// something the player REALLY said, because a wrong one cannot be corrected and is
    /// repeated back forever; and the list must stay bounded and deduplicated, because every
    /// entry is billed on every later message once it reaches the prompt.</para>
    /// </summary>
    [TestFixture]
    public class ChatMemoryDigestTests
    {
        private NPCMemory _memory;

        [SetUp]
        public void SetUp() => _memory = new NPCMemory { npcKey = "test-npc" };

        private string ValueOf(string key) =>
            _memory.digest.FirstOrDefault(n => n.key == key).value;

        // ── Extraction ──────────────────────────────────────────────────────

        [TestCase("Me llamo Bruno", ChatMemoryDigest.KEY_NAME, "Bruno")]
        [TestCase("mi nombre es Elena de Ardal", ChatMemoryDigest.KEY_NAME, "Elena de Ardal")]
        [TestCase("Vengo de las montañas del norte", ChatMemoryDigest.KEY_ORIGIN, "las montañas del norte")]
        [TestCase("Busco a mi hermana", ChatMemoryDigest.KEY_QUEST, "a mi hermana")]
        [TestCase("Me gusta el pan recién hecho", ChatMemoryDigest.KEY_LIKES, "el pan recién hecho")]
        [TestCase("Odio a los lobos", ChatMemoryDigest.KEY_HATES, "a los lobos")]
        [TestCase("My name is Bruno", ChatMemoryDigest.KEY_NAME, "Bruno")]
        [TestCase("i am looking for my sister", ChatMemoryDigest.KEY_QUEST, "my sister")]
        public void TryExtract_Discloses_KeyAndValue(string line, string expectedKey, string expectedValue)
        {
            Assert.IsTrue(ChatMemoryDigest.TryExtract(line, out string key, out string value),
                $"'{line}' is an explicit self-disclosure and must be captured.");
            Assert.AreEqual(expectedKey, key);
            Assert.AreEqual(expectedValue, value);
        }

        [Test]
        public void TryExtract_KeepsAccentsAndCapitalsFromThePlayersOwnText()
        {
            ChatMemoryDigest.TryExtract("me llamo Álvaro", out _, out string value);

            Assert.AreEqual("Álvaro", value,
                "Matching is accent-insensitive; the CAPTURE is not. A folded value would " +
                "have the character calling the player 'alvaro' from then on.");
        }

        [Test]
        public void TryExtract_StopsAtTheSentenceBreak()
        {
            ChatMemoryDigest.TryExtract("Me llamo Bruno. ¿Y tú cómo te llamas?", out _, out string value);

            Assert.AreEqual("Bruno", value,
                "A name is what precedes the full stop; swallowing the next sentence would " +
                "record the player's question as part of their name.");
        }

        [TestCase("¿cuánto vale el pan?")]
        [TestCase("hola, buenas tardes")]
        [TestCase("no soy nadie importante")]
        [TestCase("")]
        public void TryExtract_OrdinaryLines_DiscloseNothing(string line)
        {
            Assert.IsFalse(ChatMemoryDigest.TryExtract(line, out _, out _),
                "The patterns are deliberately narrow. A loose one ('soy …') would record " +
                "'nadie importante' as the traveller's name and repeat it forever.");
        }

        [Test]
        public void TryExtract_MarkerWithNothingAfterIt_CapturesNothing()
        {
            Assert.IsFalse(ChatMemoryDigest.TryExtract("me llamo", out _, out _),
                "An empty value is not a fact, and an empty name note would render as " +
                "'Se llama .' in the prompt.");
        }

        // ── Recording ───────────────────────────────────────────────────────

        [Test]
        public void RecordPlayerLine_Disclosure_WinsOverIntent()
        {
            ChatMemoryDigest.RecordPlayerLine(_memory, "me llamo Bruno", DialogueIntent.Greeting);

            Assert.AreEqual(1, _memory.digest.Count, "One line writes at most one note.");
            Assert.AreEqual(ChatMemoryDigest.KEY_NAME, _memory.digest[0].key);
        }

        [TestCase(DialogueIntent.Insult, ChatMemoryDigest.KEY_INSULTED)]
        [TestCase(DialogueIntent.Distress, ChatMemoryDigest.KEY_CONFIDED)]
        [TestCase(DialogueIntent.Flirt, ChatMemoryDigest.KEY_FLIRTED)]
        [TestCase(DialogueIntent.Danger, ChatMemoryDigest.KEY_WARNED)]
        public void RecordPlayerLine_NotableIntent_LeavesAnEvent(DialogueIntent intent, string expectedKey)
        {
            Assert.IsTrue(ChatMemoryDigest.RecordPlayerLine(_memory, "algo que no revela nada", intent));
            Assert.AreEqual(expectedKey, _memory.digest[0].key);
        }

        [TestCase(DialogueIntent.Trade)]
        [TestCase(DialogueIntent.SmallTalk)]
        [TestCase(DialogueIntent.Greeting)]
        [TestCase(DialogueIntent.Unknown)]
        public void RecordPlayerLine_OrdinaryIntent_WritesNothing(DialogueIntent intent)
        {
            Assert.IsFalse(ChatMemoryDigest.RecordPlayerLine(_memory, "una frase cualquiera", intent),
                "Everyone asks a vendor about prices, so 'asked about prices' is not a fact " +
                "about anyone — and a note per message would evict the real ones.");
            Assert.IsEmpty(_memory.digest);
        }

        [Test]
        public void RecordPlayerLine_SameFactTwice_KeepsOneSlotAndReportsNoChange()
        {
            Assert.IsTrue(ChatMemoryDigest.RecordPlayerLine(_memory, "me llamo Bruno", DialogueIntent.Unknown));
            Assert.IsFalse(ChatMemoryDigest.RecordPlayerLine(_memory, "me llamo Bruno", DialogueIntent.Unknown),
                "Re-stating a fact must report no change, or every repetition costs a file write.");
            Assert.AreEqual(1, _memory.digest.Count);
        }

        [Test]
        public void RecordPlayerLine_CorrectedFact_ReplacesTheOldValue()
        {
            ChatMemoryDigest.RecordPlayerLine(_memory, "me llamo Bruno", DialogueIntent.Unknown);
            ChatMemoryDigest.RecordPlayerLine(_memory, "me llamo Bruna", DialogueIntent.Unknown);

            Assert.AreEqual(1, _memory.digest.Count, "A key is one fact, not one note per telling.");
            Assert.AreEqual("Bruna", ValueOf(ChatMemoryDigest.KEY_NAME));
        }

        // ── The cap ─────────────────────────────────────────────────────────

        [Test]
        public void Write_PastTheCap_DropsTheOldestNote()
        {
            for (int i = 0; i < NPCMemory.DIGEST_CAP + 3; i++)
                ChatMemoryDigest.Write(_memory, "k" + i, "v" + i);

            Assert.AreEqual(NPCMemory.DIGEST_CAP, _memory.digest.Count);
            Assert.IsFalse(_memory.digest.Any(n => n.key == "k0"),
                "The oldest note is the one to lose; dropping the newest would make the " +
                "digest freeze on whatever the player said first.");
            Assert.AreEqual("k" + (NPCMemory.DIGEST_CAP + 2), _memory.digest.Last().key);
        }

        [Test]
        public void Write_RestatedNote_MovesToTheEndSoItSurvivesTheCap()
        {
            ChatMemoryDigest.Write(_memory, "keeper", "first");
            for (int i = 0; i < NPCMemory.DIGEST_CAP - 1; i++)
                ChatMemoryDigest.Write(_memory, "filler" + i, "v");

            ChatMemoryDigest.Write(_memory, "keeper", "second");
            ChatMemoryDigest.Write(_memory, "one-more", "v");

            Assert.AreEqual("second", ValueOf("keeper"),
                "Recency ordering is what makes the cap survivable — a fact the player keeps " +
                "mentioning must not fall off because they mentioned it early.");
        }

        // ── Trades ──────────────────────────────────────────────────────────

        [Test]
        public void RecordTrade_SameItemTwice_KeepsOneSlot()
        {
            ChatMemoryDigest.RecordTrade(_memory, "bread_01", "Pan", 1, playerBought: true);
            ChatMemoryDigest.RecordTrade(_memory, "bread_01", "Pan", 3, playerBought: true);

            Assert.AreEqual(1, _memory.digest.Count,
                "Buying bread every morning must not spend thirty slots.");
            Assert.AreEqual("3x Pan", ValueOf(ChatMemoryDigest.KEY_BOUGHT_PREFIX + "bread_01"));
        }

        [Test]
        public void RecordTrade_BuyAndSell_AreDifferentFacts()
        {
            ChatMemoryDigest.RecordTrade(_memory, "axe_01", "Hacha", 1, playerBought: true);
            ChatMemoryDigest.RecordTrade(_memory, "axe_01", "Hacha", 1, playerBought: false);

            Assert.AreEqual(2, _memory.digest.Count,
                "Selling someone an axe and buying one back are not the same thing to remember.");
        }

        [Test]
        public void RecordTrade_NothingMoved_WritesNothing()
        {
            Assert.IsFalse(ChatMemoryDigest.RecordTrade(_memory, "bread_01", "Pan", 0, true));
            Assert.IsEmpty(_memory.digest);
        }

        // ── Rendering ───────────────────────────────────────────────────────

        [Test]
        public void Render_FollowsTheConversationLanguage()
        {
            ChatMemoryDigest.RecordPlayerLine(_memory, "me llamo Bruno", DialogueIntent.Unknown);
            MemoryNote note = _memory.digest[0];

            StringAssert.Contains("Se llama", ChatMemoryDigest.Render(note, ChatLanguage.SPANISH));
            StringAssert.Contains("name is", ChatMemoryDigest.Render(note, ChatLanguage.ENGLISH));
            StringAssert.Contains("Bruno", ChatMemoryDigest.Render(note, ChatLanguage.ENGLISH),
                "The captured value is the player's own words and is never translated.");
        }

        [Test]
        public void Render_UnknownKey_FallsBackToTheRawValueRatherThanVanishing()
        {
            var note = new MemoryNote { key = "written-by-a-later-version", value = "algo" };

            Assert.AreEqual("algo", ChatMemoryDigest.Render(note, ChatLanguage.SPANISH),
                "A note this version does not understand must still be visible, or an old " +
                "save silently loses memories with nothing logged.");
        }
    }
}
