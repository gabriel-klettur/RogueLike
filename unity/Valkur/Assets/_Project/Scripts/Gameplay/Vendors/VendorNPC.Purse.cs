using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// The vendor's own money and their shelf refilling — the half of a shop that makes it a
    /// PARTICIPANT in the economy rather than a wall the player trades against.
    ///
    /// <para><b>Why it matters more than it looks.</b> Until this existed a vendor had
    /// infinite coin and finite stock that never came back, which is exactly backwards: the
    /// player could sell an unbounded quantity of anything forever (the only real gold faucet
    /// in the game was therefore uncapped) while the shop itself ran dry and stayed dry. A
    /// finite purse that drains and refills IS a supply-and-demand model — it needs no market
    /// simulation at all, because "the smith has no coin left today" is what a busy morning
    /// looks like from the player's side.</para>
    ///
    /// <para><b>Both features ship OFF for anything already authored, and that is forced
    /// rather than chosen.</b> A field absent from a shipped <c>.asset</c> deserialises to 0,
    /// so 0 has to mean "as before" for both: an unlimited purse and no restocking. Reading a
    /// missing key as an EMPTY purse would have made all five shipped vendors refuse to buy
    /// anything the instant the field was added, silently.</para>
    /// </summary>
    public partial class VendorNPC
    {
        /// <summary>
        /// Coins on hand. <c>int.MaxValue</c> stands for an unlimited purse so every caller
        /// can do plain arithmetic instead of branching — <see cref="HasLimitedPurse"/> is the
        /// question to ask when the DISTINCTION matters, which is only when reporting.
        /// </summary>
        private int _coins = int.MaxValue;

        /// <summary>Seconds of credit accumulated toward the next restock tick.</summary>
        private float _restockClock;

        /// <summary>What this vendor can still pay out. <see cref="int.MaxValue"/> when unlimited.</summary>
        public int Coins => _coins;

        /// <summary>Whether this vendor can actually run out of money.</summary>
        public bool HasLimitedPurse =>
            vendorConfig != null && vendorConfig.coinFloat > 0;

        /// <summary>Whether this vendor's shelf and purse recover over time.</summary>
        public bool Restocks =>
            vendorConfig != null && vendorConfig.restockSeconds > 0f;

        /// <summary>
        /// Sets the purse from the config. Called from <see cref="Configure"/>, because a
        /// spawned vendor gets its config AFTER <c>Awake</c> — the same reason that method
        /// re-applies the minimap marker.
        /// </summary>
        private void InitializePurse()
        {
            _coins = HasLimitedPurse ? vendorConfig.coinFloat : int.MaxValue;
            _restockClock = 0f;
        }

        /// <summary>
        /// The most units of <paramref name="item"/> this vendor can afford to buy from the
        /// player at <paramref name="unitPrice"/> each.
        ///
        /// <para>The mirror of the affordability cut <c>ChatTradeBroker.QuoteBuy</c> already
        /// makes on the PLAYER's purse, and the symmetry is the point: "you can have two"
        /// is a better answer than "no" in both directions, and it is the answer a real
        /// shopkeeper gives. A price of zero cannot happen (the pipeline floors at 1) but is
        /// treated as unaffordable rather than as infinite, because dividing by it is the one
        /// way this method could hand back a number that empties the shop.</para>
        /// </summary>
        public int AffordableUnits(int unitPrice)
        {
            if (!HasLimitedPurse) return int.MaxValue;
            if (unitPrice <= 0) return 0;
            return _coins / unitPrice;
        }

        /// <summary>
        /// Takes <paramref name="amount"/> out of the purse for a purchase FROM this vendor,
        /// returning false when they cannot cover it. Unlimited purses always succeed and
        /// never move.
        /// </summary>
        private bool TrySpendFromPurse(int amount)
        {
            if (!HasLimitedPurse) return true;
            if (amount <= 0) return true;
            if (_coins < amount) return false;
            _coins -= amount;
            return true;
        }

        /// <summary>
        /// Puts the player's payment into the purse. Saturating rather than wrapping: an
        /// unlimited purse is <c>int.MaxValue</c> and adding to it must not roll it negative,
        /// which would turn the richest vendor in the game into the poorest in one sale.
        /// </summary>
        private void CreditPurse(int amount)
        {
            if (!HasLimitedPurse || amount <= 0) return;
            _coins = (int)Mathf.Min((long)_coins + amount, int.MaxValue);
        }

        /// <summary>
        /// Recovers stock and coin toward the authored float. Proportional to how empty the
        /// vendor is, so <c>restockSeconds</c> reads as "how long from empty to full"
        /// regardless of how large the shop is — a fixed per-tick amount would refill a
        /// four-slot mage and a seventy-seven-slot lumberjack at wildly different rates from
        /// the same number.
        /// </summary>
        private void TickRestock()
        {
            if (!Restocks) return;

            _restockClock += Time.deltaTime;
            if (_restockClock < RestockTickSeconds) return;

            float fraction = _restockClock / vendorConfig.restockSeconds;
            _restockClock = 0f;

            RestockShelf(fraction);
            RestockPurse(fraction);
        }

        /// <summary>
        /// How often the restock arithmetic runs. Coarse on purpose — a shop refilling is not
        /// something anyone watches, and at one tick a second a seventy-seven-slot inventory
        /// costs one pass over a list per second per vendor instead of one per frame.
        /// </summary>
        private const float RestockTickSeconds = 1f;

        private void RestockShelf(float fraction)
        {
            if (vendorConfig.inventorySeed == null) return;

            foreach (var seed in vendorConfig.inventorySeed)
            {
                if (seed.item == null) continue;
                int target = Mathf.Max(1, seed.quantity);

                for (int i = 0; i < shopInventory.Count; i++)
                {
                    if (shopInventory[i].item != seed.item) continue;
                    var entry = shopInventory[i];
                    if (entry.stock >= target) break;

                    // Ceil, not round: a slot missing one unit with a tiny fraction would
                    // otherwise recover 0 forever and the shelf would never actually fill.
                    int step = Mathf.CeilToInt(target * fraction);
                    entry.stock = Mathf.Min(target, entry.stock + step);
                    shopInventory[i] = entry;
                    break;
                }
            }
        }

        private void RestockPurse(float fraction)
        {
            if (!HasLimitedPurse || _coins >= vendorConfig.coinFloat) return;
            int step = Mathf.CeilToInt(vendorConfig.coinFloat * fraction);
            _coins = Mathf.Min(vendorConfig.coinFloat, _coins + step);
        }
    }
}
