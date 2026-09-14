using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Skills;

// Valkur.Gameplay.Inventory is a NAMESPACE and Inventory is a class inside it, so the bare name
// is ambiguous from a sibling namespace. VendorShopUI spells it Inventory.Inventory; an alias
// says the same thing once instead of at every signature.
using Bag = Valkur.Gameplay.Inventory.Inventory;

namespace Valkur.Gameplay.Crafting
{
    /// <summary>Why a recipe cannot be crafted right now.</summary>
    public enum CraftBlockReason
    {
        /// <summary>It can. Nothing is in the way.</summary>
        None = 0,

        /// <summary>
        /// The recipe asset is broken — no output, no profession, or an ingredient line whose
        /// reference went null. A DATA defect rather than a player-facing state, which is why
        /// the panel drops the row entirely instead of greying it out: a row saying "you cannot
        /// make this" invites the player to go looking for something that does not exist.
        /// </summary>
        Malformed = 1,

        /// <summary>The player is short of at least one ingredient.</summary>
        MissingIngredients = 2,

        /// <summary>Everything is in the bag, but this needs a station and there is none in range.</summary>
        NeedsStation = 3,

        /// <summary>
        /// Everything is in the bag and the result has nowhere to go. Rare but reachable: a
        /// full bag plus a recipe that takes one unit off a partial stack frees no slot.
        /// </summary>
        NoRoomForOutput = 4,

        /// <summary>The player's skill in this trade is below the recipe's requirement.</summary>
        SkillTooLow = 5,
    }

    /// <summary>One ingredient the player is short of, and by how much.</summary>
    public readonly struct CraftShortfall
    {
        public readonly ItemDefinition Item;
        public readonly int Required;
        public readonly int Held;

        public CraftShortfall(ItemDefinition item, int required, int held)
        {
            Item = item;
            Required = required;
            Held = held;
        }

        public int Missing => Mathf.Max(0, Required - Held);
    }

    /// <summary>
    /// Whether one recipe can be crafted, and if not, exactly what is in the way.
    ///
    /// <para>A struct with a REASON rather than a bool, for the same argument
    /// <c>InteractionPromptInfo</c> makes: "you cannot make this" is not useful, and a panel
    /// that only knows a bool has to either say nothing or guess. The shortfall list is what
    /// lets a row read "faltan 2 remolachas" — the single thing that turns the panel from a
    /// menu into a shopping list.</para>
    /// </summary>
    public readonly struct CraftAvailability
    {
        public readonly CraftBlockReason Reason;

        /// <summary>
        /// Every ingredient the player is short of — never just the first. Reporting one at a
        /// time makes the player re-open the panel after each trip to find out what else they
        /// need, which is a worse answer arrived at more slowly.
        /// </summary>
        public readonly IReadOnlyList<CraftShortfall> Shortfalls;

        /// <summary>The skill percent the recipe wanted, when <see cref="Reason"/> is SkillTooLow.</summary>
        public readonly int RequiredSkill;

        public CraftAvailability(CraftBlockReason reason,
            IReadOnlyList<CraftShortfall> shortfalls = null, int requiredSkill = 0)
        {
            Reason = reason;
            Shortfalls = shortfalls ?? System.Array.Empty<CraftShortfall>();
            RequiredSkill = requiredSkill;
        }

        public bool CanCraft => Reason == CraftBlockReason.None;

        public static readonly CraftAvailability Ok = new CraftAvailability(CraftBlockReason.None);
    }

    /// <summary>
    /// The crafting rules for every trade, as pure functions over an <c>Inventory</c>.
    ///
    /// <para>DELIBERATELY NOT A MonoBehaviour AND NOT A SINGLETON. Every input it needs is
    /// passed in — the bag, the recipe, the player's trades, whether a station is in range — so
    /// the whole system is exercisable from an EditMode test with a bare GameObject and no
    /// scene, no ServiceLocator and no station in the world. The panel, the station and the
    /// Skills editor are all callers; none of them owns the rules.</para>
    ///
    /// <para>ONE SERVICE FOR EVERY PROFESSION. What differs between forging and cooking is the
    /// data — which profession, which station, which ingredients — and none of that is a
    /// branch. A per-trade service would be four copies of the atomicity below, and the first
    /// bug fixed in one of them would live on in the other three.</para>
    ///
    /// <para>THE ATOMICITY IS THE POINT. A craft that takes the ingredients and then fails to
    /// place the result has destroyed the player's materials, and it is the failure they will
    /// never forgive. It cannot be avoided by checking for room FIRST, either: the removal is
    /// what frees the slots, so a bag with no room before the craft usually has room after it,
    /// and a pre-check would refuse most legitimate crafts on a fullish bag. So the order is
    /// remove, place, and ROLL BACK on the remainder — see <see cref="TryCraft"/>.</para>
    /// </summary>
    public static class CraftingService
    {
        /// <summary>
        /// Whether <paramref name="recipe"/> can be crafted right now, and what is missing.
        ///
        /// <para>The order the reasons are tested in is a design decision, not an accident.
        /// SKILL comes first because it is the only refusal the player cannot fix by walking
        /// somewhere or picking something up, so telling them anything else first is telling
        /// them to do work that will not help. Ingredients come before the station, because
        /// "you are short two beets" is actionable wherever they stand while "find a forge"
        /// sends them to a forge they still cannot use. Room comes last because it is the only
        /// one that can change without the player doing anything about this recipe at all.</para>
        ///
        /// <para><paramref name="skills"/> may be null — a bare test rig, or the panel opened for a
        /// frame before the player resolves. A null one is read as every skill at 0 %, which
        /// still crafts every recipe authored at 0 %: a missing component cannot lock the whole
        /// system, only the recipes that genuinely ask for training.</para>
        /// </summary>
        public static CraftAvailability Evaluate(Bag inventory, RecipeDefinition recipe,
            PlayerSkills skills, bool stationInRange)
        {
            if (recipe == null || !recipe.IsWellFormed)
                return new CraftAvailability(CraftBlockReason.Malformed);
            if (inventory == null)
                return new CraftAvailability(CraftBlockReason.MissingIngredients);

            if (SkillTenthsFor(skills, recipe) < RequiredTenths(recipe))
                return new CraftAvailability(CraftBlockReason.SkillTooLow, null, recipe.requiredSkill);

            List<CraftShortfall> missing = null;
            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                var line = recipe.ingredients[i];
                int held = inventory.GetItemCount(line.item);
                if (held >= line.quantity) continue;
                missing ??= new List<CraftShortfall>();
                missing.Add(new CraftShortfall(line.item, line.quantity, held));
            }
            if (missing != null)
                return new CraftAvailability(CraftBlockReason.MissingIngredients, missing);

            if (recipe.requiresStation && !stationInRange)
                return new CraftAvailability(CraftBlockReason.NeedsStation);

            return CraftAvailability.Ok;
        }

        /// <summary>The player's skill in the recipe's trade, in tenths. 0 with no component.</summary>
        public static int SkillTenthsFor(PlayerSkills skills, RecipeDefinition recipe)
        {
            var skill = recipe != null && recipe.profession != null ? recipe.profession.skill : null;
            return skills != null && skill != null ? skills.GetTenths(skill.skillKey) : 0;
        }

        /// <summary>The recipe's requirement in tenths, clamped to the skill's range.</summary>
        public static int RequiredTenths(RecipeDefinition recipe) =>
            recipe == null ? 0 : Mathf.Clamp(recipe.requiredSkill, 0, 100) * 10;

        /// <summary>
        /// Craft one batch and roll the trade's skill gains. Returns true only if the result
        /// reached the bag.
        ///
        /// <para>Re-evaluates rather than trusting a caller's earlier answer: the panel
        /// refreshes on <c>OnInventoryChanged</c>, but a click and the frame it is handled in
        /// are not the same moment, and the player may have walked out of a station's range in
        /// between.</para>
        ///
        /// <para>WHY THE ROLLBACK IS WRITTEN OUT RATHER THAN AVOIDED. Placing the output can
        /// leave a remainder even after the removals — take one unit off a fifty-stack in a bag
        /// whose every other slot is occupied and nothing was freed. When that happens the
        /// partial output is pulled back out and every ingredient returned. That restore is
        /// guaranteed to fit because the bag is being put back to a state it held one statement
        /// ago, with the same items in the same total quantities.</para>
        ///
        /// <para>THE SKILL IS ROLLED LAST, after the result is known to be in the bag. Paying a
        /// trade for a craft that rolled back would let a player with a full inventory farm
        /// skill off a button that produces nothing.</para>
        /// </summary>
        public static bool TryCraft(Bag inventory, RecipeDefinition recipe,
            PlayerSkills skills, bool stationInRange)
        {
            var availability = Evaluate(inventory, recipe, skills, stationInRange);
            if (!availability.CanCraft) return false;

            // Record what actually left the bag rather than what the recipe asked for, so a
            // rollback puts back exactly what was taken even if the two ever disagree.
            var taken = new int[recipe.ingredients.Length];
            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                var line = recipe.ingredients[i];
                taken[i] = inventory.RemoveItem(line.item, line.quantity);
            }

            int leftover = inventory.AddItem(recipe.output, recipe.outputQuantity);
            if (leftover > 0)
            {
                inventory.RemoveItem(recipe.output, recipe.outputQuantity - leftover);
                for (int i = 0; i < recipe.ingredients.Length; i++)
                    if (taken[i] > 0) inventory.AddItem(recipe.ingredients[i].item, taken[i]);
                return false;
            }

            // The recipe's requirement IS its difficulty: cooking far below your skill teaches
            // only the definition's floor, which is what sends a cook to harder dishes.
            if (skills != null)
                for (int roll = 0; roll < recipe.skillGainRolls; roll++)
                    skills.TryGain(recipe.profession.skill, recipe.requiredSkill, wrongTool: false);

            // Announced only on the path that actually produced something: the rollback
            // above returns before here, so a craft refused for want of room never fires.
            // A listener therefore never has to ask whether the result really landed —
            // which is the whole reason this sits after the AddItem and not before it.
            Valkur.Core.GameEvents.FireItemCrafted(
                recipe.recipeId,
                recipe.output != null ? recipe.output.itemId : string.Empty,
                recipe.outputQuantity);

            return true;
        }

        /// <summary>
        /// How many consecutive batches the bag could supply, ignoring where the results would
        /// go. Used by the panel and by the tests.
        ///
        /// <para>Bounded by <paramref name="cap"/> because the honest answer for a cheap recipe
        /// against a deep bag is large enough to be useless as a button label, and because the
        /// output room — which this deliberately does not model — makes anything past the first
        /// few batches a guess anyway.</para>
        /// </summary>
        public static int MaxBatches(Bag inventory, RecipeDefinition recipe, int cap = 99)
        {
            if (inventory == null || recipe == null || !recipe.IsWellFormed) return 0;

            int best = cap;
            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                var line = recipe.ingredients[i];
                int possible = inventory.GetItemCount(line.item) / line.quantity;
                if (possible < best) best = possible;
                if (best == 0) break;
            }
            return Mathf.Max(0, best);
        }
    }
}
