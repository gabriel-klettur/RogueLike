using NUnit.Framework;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// EditMode coverage for <see cref="ChatRelationship"/>, the half that was missing for
    /// the life of the project: <c>PersonaPromptBuilder</c> has always read
    /// <c>friendshipScore</c> and nothing in production ever wrote it, so every character
    /// met the player at 0 forever.
    ///
    /// <para>What this fixture protects is the shape of the rule rather than its numbers:
    /// gains are capped per conversation and losses are not, the score is clamped at both
    /// ends, and trade talk on its own is worth nothing. Each of those is a way the layer
    /// stops measuring regard and starts measuring persistence.</para>
    /// </summary>
    [TestFixture]
    public class ChatRelationshipTests
    {
        private NPCMemory _memory;
        private int _goodwill;

        [SetUp]
        public void SetUp()
        {
            _memory = new NPCMemory { npcKey = "test-npc" };
            _goodwill = 0;
        }

        // ── Deltas ──────────────────────────────────────────────────────────

        [Test]
        public void DeltaFor_Insult_IsNegativeAndTheLargestSingleMove()
        {
            int insult = ChatRelationship.DeltaFor(DialogueIntent.Insult);

            Assert.Less(insult, 0, "An insult must cost regard.");
            Assert.Less(insult, -ChatRelationship.DeltaFor(DialogueIntent.Greeting),
                "One insult must outweigh one pleasantry, or an apology tour is just typing " +
                "'hola' a few more times.");
        }

        [Test]
        public void DeltaFor_TradeTalk_IsZeroBecauseEveryoneHaggles()
        {
            Assert.AreEqual(0, ChatRelationship.DeltaFor(DialogueIntent.Trade),
                "Scoring trade talk would make the number measure how much shopping the " +
                "player does. The goodwill of a deal is paid on a COMPLETED trade instead.");
        }

        [Test]
        public void DeltaFor_FarewellAndUnknown_AreNeutral()
        {
            Assert.AreEqual(0, ChatRelationship.DeltaFor(DialogueIntent.Farewell));
            Assert.AreEqual(0, ChatRelationship.DeltaFor(DialogueIntent.Unknown),
                "An unclassified line must not move anything — most messages land here, and " +
                "scoring them would make the number drift on volume alone.");
        }

        // ── The conversation cap ────────────────────────────────────────────

        [Test]
        public void ApplyIntent_RepeatedGreetings_StopAtTheConversationCap()
        {
            for (int i = 0; i < 50; i++)
                ChatRelationship.ApplyIntent(_memory, DialogueIntent.Greeting, ref _goodwill);

            Assert.AreEqual(ChatRelationship.GAIN_CAP_PER_CONVERSATION, _memory.friendshipScore,
                "Fifty greetings in one conversation must buy exactly one conversation's " +
                "worth of regard; without the cap the score measures persistence.");
        }

        [Test]
        public void ApplyIntent_PastTheCap_ReportsZeroSoNothingIsPersisted()
        {
            while (_goodwill < ChatRelationship.GAIN_CAP_PER_CONVERSATION)
                ChatRelationship.ApplyIntent(_memory, DialogueIntent.Greeting, ref _goodwill);

            int applied = ChatRelationship.ApplyIntent(_memory, DialogueIntent.Joke, ref _goodwill);

            Assert.AreEqual(0, applied,
                "A refused gain must report 0 — the caller writes the file on a non-zero " +
                "return, so reporting the intended delta would write on every message.");
        }

        [Test]
        public void ApplyIntent_ResetTally_StartsANewConversationsBudget()
        {
            for (int i = 0; i < 20; i++)
                ChatRelationship.ApplyIntent(_memory, DialogueIntent.Greeting, ref _goodwill);

            _goodwill = 0; // what ChatSystem.OpenChat does
            int applied = ChatRelationship.ApplyIntent(_memory, DialogueIntent.Greeting, ref _goodwill);

            Assert.Greater(applied, 0,
                "The cap is per CONVERSATION. A friendship that could never grow past the " +
                "first chat would be a ceiling, not a pace.");
        }

        [Test]
        public void ApplyIntent_Insults_AreNotCappedByTheConversationBudget()
        {
            for (int i = 0; i < 5; i++)
                ChatRelationship.ApplyIntent(_memory, DialogueIntent.Insult, ref _goodwill);

            Assert.AreEqual(5 * ChatRelationship.DeltaFor(DialogueIntent.Insult), _memory.friendshipScore,
                "The cap exists to stop grinding, and an insult is not a grind — it is a " +
                "thing the player chose to type five times.");
        }

        // ── Clamping ────────────────────────────────────────────────────────

        [Test]
        public void Apply_Clamps_AtBothEnds()
        {
            _memory.friendshipScore = ChatRelationship.MAX_SCORE;
            ChatRelationship.Apply(_memory, 50, ref _goodwill);
            Assert.AreEqual(ChatRelationship.MAX_SCORE, _memory.friendshipScore);

            _goodwill = 0;
            _memory.friendshipScore = ChatRelationship.MIN_SCORE;
            ChatRelationship.Apply(_memory, -50, ref _goodwill);
            Assert.AreEqual(ChatRelationship.MIN_SCORE, _memory.friendshipScore);
        }

        [Test]
        public void Apply_AtTheCeiling_ChargesNothingToTheConversationBudget()
        {
            _memory.friendshipScore = ChatRelationship.MAX_SCORE;

            ChatRelationship.Apply(_memory, 5, ref _goodwill);

            Assert.AreEqual(0, _goodwill,
                "A gain the clamp swallowed was never granted, so it must not spend budget — " +
                "otherwise a maxed-out friend silently blocks the trade goodwill too.");
        }

        [Test]
        public void Apply_NullMemory_IsASilentNoOp()
        {
            Assert.AreEqual(0, ChatRelationship.Apply(null, 5, ref _goodwill),
                "A conversation with no memory record is legal (a hand-placed NPC before " +
                "the store has one) and must not throw from a chat message.");
        }

        // ── Trade ───────────────────────────────────────────────────────────

        [Test]
        public void ApplyTrade_AddsGoodwill_AndSharesTheConversationBudget()
        {
            int first = ChatRelationship.ApplyTrade(_memory, ref _goodwill);
            Assert.AreEqual(ChatRelationship.TRADE_GAIN, first);

            for (int i = 0; i < 20; i++)
                ChatRelationship.ApplyTrade(_memory, ref _goodwill);

            Assert.AreEqual(ChatRelationship.GAIN_CAP_PER_CONVERSATION, _memory.friendshipScore,
                "Trades draw on the same budget as talk. A separate allowance would make " +
                "confirming twenty one-coin deals the fastest way to be adored.");
        }
    }
}
