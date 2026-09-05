using UnityEngine;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Moves <c>NPCMemory.friendshipScore</c> from what the player says and does.
    ///
    /// <para>The score was a dead branch: <c>PersonaPromptBuilder</c> has always read it and
    /// nothing in production ever wrote it, so every character in the game has been meeting
    /// the player at exactly 0 forever. The signal was already there —
    /// <see cref="DialogueIntentClassifier"/> classifies an insult, a flirt, a confidence and
    /// a warning on every message for the offline provider's benefit — so this is the
    /// missing half rather than a new mechanic.</para>
    ///
    /// <para>GAINS ARE CAPPED PER CONVERSATION and losses are not, and that asymmetry is the
    /// whole design. Without the cap, "hola" typed forty times walks a stranger to adored,
    /// which makes the number measure persistence rather than regard. An insult is not a
    /// grind — it is a thing the player chose to type — so it lands in full, and the way back
    /// is several conversations of behaving differently.</para>
    ///
    /// <para>Pure and static: no state of its own, so nothing to reset across a Play-mode
    /// boundary. The per-conversation tally lives in <c>ChatSystem</c>, which is what knows
    /// when a conversation starts.</para>
    /// </summary>
    public static class ChatRelationship
    {
        public const int MIN_SCORE = -100;
        public const int MAX_SCORE = 100;

        /// <summary>
        /// How much regard one conversation can buy. Small enough that a friendship takes
        /// several visits and large enough that a good one is visible in the next prompt.
        /// </summary>
        public const int GAIN_CAP_PER_CONVERSATION = 6;

        /// <summary>What a completed trade is worth. Business, not affection.</summary>
        public const int TRADE_GAIN = 2;

        /// <summary>
        /// What one player line is worth before the conversation cap is applied.
        ///
        /// <para>Trade talk is worth nothing on purpose: everyone haggles with a vendor, so
        /// scoring it would mean the score measures how much shopping the player does. The
        /// trade GAIN is paid on a completed deal instead, which is a thing that happened
        /// rather than a thing that was mentioned.</para>
        /// </summary>
        public static int DeltaFor(DialogueIntent intent)
        {
            switch (intent)
            {
                case DialogueIntent.Insult: return -8;
                case DialogueIntent.Distress: return 3;
                case DialogueIntent.Flirt: return 2;
                case DialogueIntent.Joke: return 2;
                case DialogueIntent.Greeting: return 1;
                case DialogueIntent.SmallTalk: return 1;
                case DialogueIntent.Danger: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// Applies one player line. Returns the change actually made, which is 0 whenever
        /// the conversation has already spent its gain budget.
        /// </summary>
        public static int ApplyIntent(
            NPCMemory memory, DialogueIntent intent, ref int gainedThisConversation) =>
            Apply(memory, DeltaFor(intent), ref gainedThisConversation);

        /// <summary>Applies the goodwill of a deal that actually completed.</summary>
        public static int ApplyTrade(NPCMemory memory, ref int gainedThisConversation) =>
            Apply(memory, TRADE_GAIN, ref gainedThisConversation);

        /// <summary>
        /// The single write. Clamps the score, charges gains against the conversation
        /// budget, and reports what it really moved so the caller knows whether to persist.
        /// </summary>
        public static int Apply(NPCMemory memory, int delta, ref int gainedThisConversation)
        {
            if (memory == null || delta == 0) return 0;

            if (delta > 0)
            {
                int budget = GAIN_CAP_PER_CONVERSATION - gainedThisConversation;
                if (budget <= 0) return 0;
                delta = Mathf.Min(delta, budget);
            }

            int before = memory.friendshipScore;
            memory.friendshipScore = Mathf.Clamp(before + delta, MIN_SCORE, MAX_SCORE);

            int applied = memory.friendshipScore - before;
            if (applied > 0) gainedThisConversation += applied;
            return applied;
        }
    }
}
