using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Chat.Providers;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.NPC;

namespace Valkur.Gameplay.Chat
{
    public partial class ChatSystem
    {
        /// <summary>
        /// The trade the character has offered and the player has not yet answered.
        /// Invalid when there is nothing pending.
        /// </summary>
        public TradeQuote PendingTrade => _pendingTrade;
        private TradeQuote _pendingTrade;

        /// <summary>
        /// The vendor the pending offer belongs to.
        ///
        /// Held so an offer cannot outlive the conversation that produced it: walking to
        /// another shop and confirming there would otherwise buy that vendor's version of
        /// the same item, at their price, with the first one's wording still on screen.
        /// </summary>
        private VendorNPC _pendingVendor;

        /// <summary>
        /// A trade is on the table, or has just left it. The panel builds and clears its
        /// confirmation row on this; the argument is false when there is nothing pending.
        /// </summary>
        public event Action<bool> OnTradeOfferChanged;

        /// <summary>
        /// Checks a proposal against the live world and, if it holds up, puts it on the
        /// table. Returns what the character should SAY about it.
        ///
        /// <para>The model's words are kept when the offer stands — it is better at being
        /// Gatita than any template — and replaced when it does not, because a refusal has
        /// to state the real reason. A model told only "propose a trade" happily writes
        /// "¡marchando!" for an item that sold out two purchases ago.</para>
        /// </summary>
        private string OfferTrade(TradeProposal proposal, string spokenByModel)
        {
            ClearPendingTrade(notify: false);

            var vendor = ActiveVendor;
            var player = EntityRegistry.PlayerTransform;
            var inventory = player != null ? player.GetComponent<Inventory.Inventory>() : null;
            var wallet = player != null ? player.GetComponent<CurrencyWallet>() : null;

            var quote = ChatTradeBroker.Quote(proposal, vendor, inventory, wallet);

            if (!quote.IsValid)
            {
                OnTradeOfferChanged?.Invoke(false);
                return string.IsNullOrWhiteSpace(quote.Refusal) ? spokenByModel : quote.Refusal;
            }

            _pendingTrade = quote;
            _pendingVendor = vendor;
            OnTradeOfferChanged?.Invoke(true);

            // The model asked for more than the world can give. Saying so is the whole
            // difference between a shop and a vending machine, and its own sentence would
            // otherwise still be promising the original number.
            if (quote.Quantity < Mathf.Max(1, proposal.Quantity))
            {
                return $"Puedo darte {quote.Quantity} de {quote.Item.displayName}, no más. " +
                       $"Serían {quote.TotalPrice} monedas.";
            }

            // A model that calls the tool sometimes says nothing at all — the action WAS its
            // answer. Measured live: asked for two borsch, gpt-5-mini returned a correct tool
            // call and empty content, and the player saw the provider's "..." placeholder
            // over a confirmation row. The offer needs a sentence, so the game writes one.
            if (string.IsNullOrWhiteSpace(spokenByModel) || spokenByModel.Trim() == "...")
                return DescribeOffer(quote);

            return spokenByModel;
        }

        /// <summary>
        /// Executes the pending offer. Returns the units that actually went through, which
        /// can be fewer than offered and is never assumed to be all of them.
        ///
        /// <para>Re-quoted against the world before executing, not taken on trust from when
        /// the offer was made: a conversation is slow, and between the offer and the tap the
        /// player may have bought the last one from the counter, filled their bag, or spent
        /// the coins. The offer is a promise to ASK again, not a reservation.</para>
        /// </summary>
        public int ConfirmPendingTrade()
        {
            if (!_pendingTrade.IsValid) return 0;

            var player = EntityRegistry.PlayerTransform;
            var inventory = player != null ? player.GetComponent<Inventory.Inventory>() : null;
            var wallet = player != null ? player.GetComponent<CurrencyWallet>() : null;

            var vendor = _pendingVendor;
            var offer = _pendingTrade;
            ClearPendingTrade(notify: true);

            if (vendor == null || vendor != ActiveVendor)
            {
                Say("Ese trato era con otra persona.");
                return 0;
            }

            var fresh = ChatTradeBroker.Quote(
                new TradeProposal(offer.Intent, offer.Item.itemId, offer.Quantity),
                vendor, inventory, wallet);

            if (!fresh.IsValid)
            {
                Say(string.IsNullOrWhiteSpace(fresh.Refusal) ? "Ya no puede ser." : fresh.Refusal);
                return 0;
            }

            int done = ChatTradeBroker.Execute(fresh, vendor, inventory, wallet);
            Say(DescribeOutcome(fresh, done));
            return done;
        }

        /// <summary>Takes the pending offer off the table without executing it.</summary>
        public void CancelPendingTrade()
        {
            if (!_pendingTrade.IsValid) return;
            ClearPendingTrade(notify: true);
            Say("Como quieras, tesoro.");
        }

        private void ClearPendingTrade(bool notify)
        {
            bool had = _pendingTrade.IsValid;
            _pendingTrade = default;
            _pendingVendor = null;
            if (notify && had) OnTradeOfferChanged?.Invoke(false);
        }

        /// <summary>
        /// The offer, as a sentence, for when the model returned an action and no words.
        ///
        /// Deliberately plain. It stands in for a line the character would normally write
        /// herself, so it says the thing that must be said — what, how many, how much — and
        /// leaves the charm to the confirmation row beneath it rather than inventing a voice
        /// that is not quite hers.
        /// </summary>
        private static string DescribeOffer(TradeQuote quote)
        {
            string what = quote.Quantity > 1
                ? $"{quote.Quantity}x {quote.Item.displayName}"
                : quote.Item.displayName;

            return quote.Intent == TradeIntent.Buy
                ? $"{what} son {quote.TotalPrice} monedas. ¿Te lo preparo?"
                : $"Te doy {quote.TotalPrice} monedas por {what}. ¿Trato?";
        }

        /// <summary>
        /// What the character says once the coins have moved — or have not.
        ///
        /// Written by the game rather than the model on purpose: this sentence is the
        /// player's receipt, and a receipt has to be accurate before it is charming.
        /// </summary>
        private static string DescribeOutcome(TradeQuote quote, int done)
        {
            if (done <= 0) return "No ha podido ser.";

            string what = done > 1
                ? $"{done}x {quote.Item.displayName}"
                : quote.Item.displayName;

            // Priced from what actually happened, not from what was offered: a run cut short
            // by an inventory filling up must not quote the whole basket's total.
            int perUnit = quote.Quantity > 0 ? quote.TotalPrice / quote.Quantity : quote.TotalPrice;
            int paid = perUnit * done;

            return quote.Intent == TradeIntent.Buy
                ? $"Hecho: {what} por {paid} monedas. ¡Que aproveche!"
                : $"Trato hecho: me quedo {what} y te doy {paid} monedas.";
        }

        /// <summary>
        /// Puts a line in the conversation as the NPC, without asking the provider for it.
        /// Used for outcomes the GAME decides — a receipt, a refusal — which must be exact.
        /// </summary>
        private void Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            string npcName = _activePersona != null && !string.IsNullOrEmpty(_activePersona.displayName)
                ? _activePersona.displayName
                : "NPC";

            AddMessage(npcName, text);
            ShowTargetBubble(text, NPC_BUBBLE_TTL_MS);
        }
    }
}
