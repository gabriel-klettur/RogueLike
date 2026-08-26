using System;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Editorial grouping for the Buildings Editor (F10) template picker.
    ///
    /// The catalog holds 969 templates in one flat grid, and no author is browsing more than
    /// one family of them at a time: 137 tree variants, 106 pieces of ground flora, and — since
    /// the second prop wave landed 463 more — whole themed sets of 33 to 69 (graveyard, arcane,
    /// military, bandit, forge, water, quest, monuments). Scrolling past all of them to reach a
    /// temple is the problem these tabs solve, which is why a themed sheet earns its own tab
    /// instead of being poured into Props.
    ///
    /// Classification keys off the template's <c>assetPath</c> — the Resources folder the
    /// sprite lives in — rather than a field on the ScriptableObject, so importing a new
    /// sprite into an existing folder files it correctly with no data edit. Rules are ordered
    /// and the first match wins, which is what lets a handful of specific files override
    /// their folder (the three assets dumped in <c>Buildings/others/</c> are a portal, a
    /// fountain and a guillotine — no single tab is right for all three).
    ///
    /// A path that matches nothing lands in <see cref="Category.Structures"/> rather than
    /// vanishing from the picker. <see cref="BuildingCategoryTests"/> asserts that no
    /// shipped template relies on that fallback.
    /// </summary>
    public static class BuildingCategory
    {
        public enum Category
        {
            /// <summary>Trees, the single largest family in the catalog.</summary>
            Trees,
            /// <summary>Ground cover: bushes, flowers, grass, rocks, mushrooms, forest litter.</summary>
            Flora,
            /// <summary>Anything a character enters or a town is built from: houses, shops, temples, castles, arenas.</summary>
            Structures,
            /// <summary>Street and yard furniture: benches, barrels, carts, wells, fountains, fences.</summary>
            Props,
            /// <summary>Market stalls, counters, produce crates, sacks, baskets, merchant tools.</summary>
            Market,
            /// <summary>Lamp posts, lanterns, braziers, torches, wall sconces.</summary>
            Lights,
            /// <summary>Signposts, shop signs, heraldic banners, notice boards.</summary>
            Signs,
            /// <summary>Portals, totems, crystals, runes, summoning circles — the ritual set dressing.</summary>
            Arcane,
            /// <summary>Civic monuments: statues, obelisks, memorial columns, fountains, sundials.</summary>
            Monuments,
            /// <summary>Camp and fortification: banners, palisades, tents, training grounds, supply.</summary>
            Military,
            /// <summary>Headstones, crypts, iron fencing, mourning statues, funeral flowers.</summary>
            Graveyard,
            /// <summary>Hideout dressing: wreckage, campfires, cages, loot stashes, gang graffiti.</summary>
            Bandit,
            /// <summary>Smithy fittings: forges, anvils, workbenches, racks, ore and fuel.</summary>
            Forge,
            /// <summary>Wells, pumps, fountains, troughs, pipes, aqueducts, drains, puddles.</summary>
            Water,
            /// <summary>Quest and guild fixtures: notice boards, bounty posts, chests, lecterns.</summary>
            Quest,
        }

        /// <summary>Tab order in the picker. Explicit so it never depends on enum order.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable tab order built once from enum literals. " +
            "Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        public static readonly Category[] TabOrder =
        {
            Category.Trees, Category.Flora, Category.Structures, Category.Props,
            Category.Market, Category.Lights, Category.Signs, Category.Arcane,
            Category.Monuments, Category.Military, Category.Graveyard, Category.Bandit,
            Category.Forge, Category.Water, Category.Quest,
        };

        /// <summary>
        /// Short label. The Buildings panel gives its content 368 px and the tab block splits
        /// that three to a row, so a label has ~121 px at font size 10 — comfortable for
        /// "Structures", the longest name here, and not much more.
        /// </summary>
        public static string Label(Category c)
        {
            switch (c)
            {
                case Category.Trees:      return "Trees";
                case Category.Flora:      return "Flora";
                case Category.Structures: return "Structures";
                case Category.Props:      return "Props";
                case Category.Market:     return "Market";
                case Category.Lights:     return "Lights";
                case Category.Signs:      return "Signs";
                case Category.Arcane:     return "Arcane";
                case Category.Monuments:  return "Monuments";
                case Category.Military:   return "Military";
                case Category.Graveyard:  return "Graveyard";
                case Category.Bandit:     return "Bandit";
                case Category.Forge:      return "Forge";
                case Category.Water:      return "Water";
                case Category.Quest:      return "Quest";
                default:                  return c.ToString();
            }
        }

        // Ordered rules over the full Resources path; the first prefix that matches decides.
        // File-level rules must precede the folder rule they override.
        [Valkur.Core.SelfHealingStatic("Immutable prefix table built once from string literals. " +
            "Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly (string Prefix, Category Cat)[] Rules =
        {
            // ── Prop sheets imported in 2026-08 ───────────────────────────────────
            ("buildings/lights/",  Category.Lights),
            ("buildings/signs/",   Category.Signs),
            ("buildings/market/",  Category.Market),
            ("buildings/props/",   Category.Props),
            // nature/ is ground cover except for the seven tree sprites in it, which belong
            // with the other 137 trees rather than with the grass tufts.
            ("buildings/nature/tree_", Category.Trees),
            ("buildings/nature/",      Category.Flora),

            // ── Legacy folders ────────────────────────────────────────────────────
            ("buildings/vegetation/",        Category.Trees),
            ("buildings/gardens/",           Category.Flora),
            ("buildings/forest_decoration/", Category.Flora),

            // ── Themed sheets imported in 2026-08 (second wave, 463 sprites) ──────
            // Each of these folders is one authored sheet, and each is big enough to be
            // the only thing an author is browsing at the time — the same argument the
            // first six tabs were added on. domestic/ is the exception: it is household
            // clutter, which is what Props already means.
            ("buildings/military/",   Category.Military),
            ("buildings/graveyard/",  Category.Graveyard),
            ("buildings/arcane/",     Category.Arcane),
            ("buildings/blacksmith/", Category.Forge),
            ("buildings/domestic/",   Category.Props),
            ("buildings/bandit/",     Category.Bandit),
            ("buildings/water/",      Category.Water),
            ("buildings/quest/",      Category.Quest),

            ("buildings/portals/", Category.Arcane),
            ("buildings/totems/",  Category.Arcane),
            // Monuments, not Arcane: a crowned king on a plinth and a summoning circle
            // are never wanted in the same breath. This moves the three legacy statues
            // out of Arcane along with the 32 new ones.
            ("buildings/statues/", Category.Monuments),

            // The three assets dumped straight into others/ have nothing in common.
            ("buildings/others/portal", Category.Arcane),
            ("buildings/others/fuente", Category.Props),
            ("buildings/others/",       Category.Props),

            ("buildings/houses/",      Category.Structures),
            ("buildings/castles/",     Category.Structures),
            ("buildings/temples/",     Category.Structures),
            ("buildings/shops/",       Category.Structures),
            ("buildings/combat/",      Category.Structures),
            ("buildings/backgrounds/", Category.Structures),

            // Two sprites sit loose at the root of Buildings/ rather than in a folder.
            ("buildings/dummy", Category.Structures),
            ("buildings/mine",  Category.Structures),
        };

        /// <summary>
        /// Category for a template's Resources path. Never throws; a null, empty or
        /// unmatched path is <see cref="Category.Structures"/>.
        /// </summary>
        public static Category Of(string assetPath)
            => TryMatch(assetPath, out Category c) ? c : Category.Structures;

        /// <summary>
        /// Same classification as <see cref="Of"/>, but reports whether a rule actually
        /// matched. Exists so the test suite can prove no shipped template is riding the
        /// silent fallback — the failure mode of any prefix table is that a renamed folder
        /// quietly drains into the default bucket.
        /// </summary>
        public static bool TryMatch(string assetPath, out Category category)
        {
            category = Category.Structures;
            if (string.IsNullOrEmpty(assetPath)) return false;

            string path = assetPath.ToLowerInvariant();
            for (int i = 0; i < Rules.Length; i++)
            {
                if (path.StartsWith(Rules[i].Prefix, StringComparison.Ordinal))
                {
                    category = Rules[i].Cat;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Convenience overload; a null template is <see cref="Category.Structures"/>.</summary>
        public static Category Of(Valkur.Data.BuildingTemplateData template)
            => Of(template != null ? template.assetPath : null);
    }
}
