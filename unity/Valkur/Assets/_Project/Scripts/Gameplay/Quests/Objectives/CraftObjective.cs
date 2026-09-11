using System;
using Valkur.Core;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Cook / forge N of recipe X", or N of anything when <see cref="RecipeId"/>
    /// is empty.
    ///
    /// <para>Event-driven and NOT polled, unlike collecting, because the thing being
    /// counted is an ACT rather than a state: a player who crafts three stews and
    /// eats them has still crafted three, and a bag count would read zero. It is
    /// also why this cannot be expressed as a Collect objective on the result item,
    /// which is the tempting simplification.</para>
    ///
    /// <para><c>GameEvents.OnItemCrafted</c> fires after the result is in the bag
    /// and the profession xp is paid, so a craft that rolled back for want of room
    /// never counts.</para>
    /// </summary>
    public sealed class CraftObjective : ObjectiveBase
    {
        public string RecipeId { get; }

        public CraftObjective(string id, string description, int target, string recipeId)
            : base(id, description, target)
        {
            RecipeId = recipeId ?? string.Empty;
        }

        protected override void OnBegin() => GameEvents.OnItemCrafted += HandleCrafted;
        protected override void OnEnd()   => GameEvents.OnItemCrafted -= HandleCrafted;

        private void HandleCrafted(string recipeId, string resultItemId, int quantity)
        {
            if (IsComplete) return;
            if (!string.IsNullOrEmpty(RecipeId) &&
                !string.Equals(recipeId, RecipeId, StringComparison.OrdinalIgnoreCase)) return;

            // One craft is one tick, whatever the batch produced. The objective is
            // "make the dish", and a recipe that yields two portions would otherwise
            // count double for the same act.
            Increment();
        }
    }
}
