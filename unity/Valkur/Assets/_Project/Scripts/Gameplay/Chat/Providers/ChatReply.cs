using Valkur.Data;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>Which way goods and coins move in a proposed trade.</summary>
    public enum TradeIntent
    {
        /// <summary>Nothing was proposed. The overwhelmingly common case.</summary>
        None = 0,

        /// <summary>The player pays, the vendor hands over goods.</summary>
        Buy,

        /// <summary>The player hands over goods, the vendor pays.</summary>
        Sell,
    }

    /// <summary>
    /// A trade the character has offered to make, as a machine-readable claim rather than a
    /// sentence.
    ///
    /// <para>WHY THIS EXISTS AS DATA. Before it, the NPC could only ROLE-PLAY commerce, and
    /// did: recovered from a shipped conversation, Gatita said "aquí tienes, dos manzanas
    /// brillantes" for apples she does not stock, in a sale that moved no item and no coin.
    /// A model asked to sell things in prose will always narrate a transaction rather than
    /// perform one, because prose is all it has.</para>
    ///
    /// <para>A proposal is NOT a transaction. Nothing here is trusted: the item id is looked
    /// up in the live shop, the price is read from the same <c>GetBuyPrice</c> the counter
    /// charges, the quantity is clamped to stock and to the purse, and the player has to
    /// confirm before a single coin moves. The model chooses what to offer; the game decides
    /// what is true.</para>
    /// </summary>
    public readonly struct TradeProposal
    {
        public TradeIntent Intent { get; }

        /// <summary>Item id as it appears in the shop. Validated, never trusted.</summary>
        public string ItemId { get; }

        /// <summary>How many units. Validated against stock, purse and inventory.</summary>
        public int Quantity { get; }

        public bool IsSomething => Intent != TradeIntent.None && !string.IsNullOrWhiteSpace(ItemId);

        public TradeProposal(TradeIntent intent, string itemId, int quantity)
        {
            Intent = intent;
            ItemId = itemId;
            Quantity = quantity;
        }

        public static readonly TradeProposal None = new TradeProposal(TradeIntent.None, null, 0);
    }

    /// <summary>
    /// What a provider gives back: what the character said, and optionally what it offered
    /// to do.
    ///
    /// <para>A struct rather than a bare string for the same reason <see cref="ChatRequest"/>
    /// is one on the way in. A reply is no longer only text, and the next thing it grows —
    /// a quest offered, a mood shift, an emote — should not be another signature change
    /// rippling through every implementor and every test fake. The emote arrived: see
    /// <see cref="Expression"/>.</para>
    /// </summary>
    public readonly struct ChatReply
    {
        /// <summary>What the character says out loud. May be empty when only an action came back.</summary>
        public string Text { get; }

        /// <summary>What the character offered to do, if anything.</summary>
        public TradeProposal Proposal { get; }

        /// <summary>
        /// The face the character is making as it says this.
        ///
        /// <para>Always a real value — <see cref="FacialExpression.Neutral"/> is the default
        /// and is a face every character has, so a provider that says nothing about the
        /// expression still produces a showable portrait rather than a hole.</para>
        ///
        /// <para>It belongs to the REPLY rather than to the character because it is a
        /// property of this particular utterance. A persistent mood is a different thing and
        /// does not live here.</para>
        /// </summary>
        public FacialExpression Expression { get; }

        public ChatReply(string text, TradeProposal proposal = default,
                         FacialExpression expression = FacialExpression.Neutral)
        {
            Text = text;
            Proposal = proposal;
            Expression = expression;
        }

        /// <summary>A plain spoken reply with no action attached.</summary>
        public static ChatReply Spoken(string text) =>
            new ChatReply(text, TradeProposal.None, FacialExpression.Neutral);

        /// <summary>A plain spoken reply delivered with a particular face.</summary>
        public static ChatReply Spoken(string text, FacialExpression expression) =>
            new ChatReply(text, TradeProposal.None, expression);

        /// <summary>This reply with a different face and everything else unchanged.</summary>
        public ChatReply WithExpression(FacialExpression expression) =>
            new ChatReply(Text, Proposal, expression);
    }
}
