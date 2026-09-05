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
                return ChatLanguage.OfferPartial(
                    quote.Quantity, quote.Item.displayName, quote.TotalPrice);
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
                Say(ChatLanguage.OfferBelongedToSomeoneElse);
                return 0;
            }

            var fresh = ChatTradeBroker.Quote(
                new TradeProposal(offer.Intent, offer.Item.itemId, offer.Quantity),
                vendor, inventory, wallet);

            if (!fresh.IsValid)
            {
                Say(string.IsNullOrWhiteSpace(fresh.Refusal) ? ChatLanguage.OfferNoLongerPossible : fresh.Refusal);
                return 0;
            }

            int done = ChatTradeBroker.Execute(fresh, vendor, inventory, wallet);
            RememberTrade(fresh, done);
            Say(DescribeOutcome(fresh, done));
            return done;
        }

        /// <summary>Takes the pending offer off the table without executing it.</summary>
        public void CancelPendingTrade()
        {
            if (!_pendingTrade.IsValid) return;
            ClearPendingTrade(notify: true);
            Say(ChatLanguage.OfferDeclined);
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
        /// <summary>
        /// Writes a completed deal into the durable record and pays the goodwill it earns.
        ///
        /// <para>Only a deal that MOVED something counts. An offer that was made, or one
        /// that failed at the counter, is not a thing the character would remember about
        /// this traveller — and paying regard for an attempt would make the score
        /// farmable by confirming trades that cannot complete.</para>
        /// </summary>
        private void RememberTrade(TradeQuote quote, int done)
        {
            if (done <= 0 || _activeMemory == null || quote.Item == null) return;

            // Into the day's page as an EVENT, beside the receipt the character speaks.
            // The two are not the same information: the receipt is a line of dialogue in her
            // voice, and this is what actually moved — which is the half a player rereading
            // the week wants, and the half that would be lost if the vendor's wording ever
            // changed.
            RecordEventToJournal(DescribeLedgerLine(quote, done));

            bool changed = ChatMemoryDigest.RecordTrade(
                _activeMemory, quote.Item.itemId, quote.Item.displayName, done,
                playerBought: quote.Intent == TradeIntent.Buy);

            changed |= ChatRelationship.ApplyTrade(_activeMemory, ref _goodwillThisConversation) != 0;

            if (changed) NPCMemoryStore.Save(_activeMemory);
        }

        /// <summary>
        /// The deal as a ledger line for the journal: what moved and what it cost, with no
        /// voice on it.
        ///
        /// <para>Priced from what ACTUALLY happened for the same reason
        /// <see cref="DescribeOutcome"/> is — a run cut short by a full inventory must not
        /// record the whole basket's total, and a page that disagrees with the coins the
        /// player has is worse than no page.</para>
        /// </summary>
        private static string DescribeLedgerLine(TradeQuote quote, int done)
        {
            int perUnit = quote.Quantity > 0 ? quote.TotalPrice / quote.Quantity : quote.TotalPrice;
            int paid = perUnit * done;

            return quote.Intent == TradeIntent.Buy
                ? ChatLanguage.LedgerBought(done, quote.Item.displayName, paid)
                : ChatLanguage.LedgerSold(done, quote.Item.displayName, paid);
        }

        private static string DescribeOffer(TradeQuote quote)
        {
            string what = quote.Quantity > 1
                ? $"{quote.Quantity}x {quote.Item.displayName}"
                : quote.Item.displayName;

            return quote.Intent == TradeIntent.Buy
                ? ChatLanguage.OfferBuy(what, quote.TotalPrice)
                : ChatLanguage.OfferSell(what, quote.TotalPrice);
        }

        /// <summary>
        /// What the character says once the coins have moved — or have not.
        ///
        /// Written by the game rather than the model on purpose: this sentence is the
        /// player's receipt, and a receipt has to be accurate before it is charming.
        /// </summary>
        private static string DescribeOutcome(TradeQuote quote, int done)
        {
            if (done <= 0) return ChatLanguage.TradeFailed;

            string what = done > 1
                ? $"{done}x {quote.Item.displayName}"
                : quote.Item.displayName;

            // Priced from what actually happened, not from what was offered: a run cut short
            // by an inventory filling up must not quote the whole basket's total.
            int perUnit = quote.Quantity > 0 ? quote.TotalPrice / quote.Quantity : quote.TotalPrice;
            int paid = perUnit * done;

            return quote.Intent == TradeIntent.Buy
                ? ChatLanguage.TradeDoneBuy(what, paid)
                : ChatLanguage.TradeDoneSell(what, paid);
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
