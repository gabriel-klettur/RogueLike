using System;
using Valkur.Core;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Have N of item X in the bag." Polled rather than event-driven, and that is
    /// the whole design: <c>GameEvents.OnItemPickedUp</c> carries the item's
    /// DISPLAY NAME (see <c>WorldPickup</c>) rather than its id, and it does not
    /// fire at all when the item arrives by crafting, by trade, or as another
    /// quest's reward. Counting what is actually in the bag cannot miss a source,
    /// and it is the only version that can go DOWN again when the player sells the
    /// ore they were collecting.
    ///
    /// <para>The counter is therefore a MEASUREMENT, not a tally — which also means
    /// this objective needs no save handling of its own: a reloaded game re-reads
    /// the bag on its first poll and lands on the truth.</para>
    ///
    /// <para><see cref="ConsumeOnComplete"/> is what separates "collect" from
    /// "deliver". <c>QuestManager</c> reads it when the quest closes; without it
    /// the player hands nothing over and keeps the ore as well as the fee.</para>
    /// </summary>
    public sealed class CollectItemObjective : ObjectiveBase
    {
        public string ItemId          { get; }
        public bool   ConsumeOnComplete { get; }

        public override bool IsPollable => true;

        public CollectItemObjective(string id, string description, int target,
                                    string itemId, bool consumeOnComplete)
            : base(id, description, target)
        {
            ItemId            = itemId ?? string.Empty;
            ConsumeOnComplete = consumeOnComplete;
        }

        public override void Poll() => SetCurrent(CountInPlayerBag(ItemId));

        /// <summary>
        /// How many of <paramref name="itemId"/> the player is carrying. Matches by
        /// id and not by <c>ItemDefinition</c> reference so an objective can be
        /// authored against an item the quest layer never has to resolve; equipment
        /// slots are deliberately excluded, because a sword being WORN is not a
        /// sword being DELIVERED.
        ///
        /// <para>Answers 0 when there is no player yet — the boot sequence restores
        /// quests before the world has finished coming up, and a poll that threw
        /// there would take the whole restore with it.</para>
        /// </summary>
        public static int CountInPlayerBag(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return 0;
            var inv = player.GetComponent<Inventory.Inventory>();
            if (inv == null) return 0;

            int count = 0;
            var slots = inv.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.IsEmpty || slot.Item == null) continue;
                if (string.Equals(slot.Item.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                    count += slot.Quantity;
            }
            return count;
        }
    }
}
