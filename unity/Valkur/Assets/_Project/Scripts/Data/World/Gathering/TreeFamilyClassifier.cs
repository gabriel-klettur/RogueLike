using System;

namespace Valkur.Data
{
    /// <summary>The kinds of tree the woodcutting skill tells apart.</summary>
    public enum TreeFamily
    {
        None = 0,
        Small,
        Common,
        Autumn,
        Tropical,
        Swamp,
        Winter,
        Volcanic,
        Ancient,
        Enchanted,
        Corrupted,
    }

    /// <summary>
    /// Which family a tree sprite belongs to, read off its asset path.
    ///
    /// <para><b>ONE RULE, TWO READERS.</b> The seeder uses it to wire each of the 553 tree
    /// templates to its family's profile, and <c>TreeFamilyWiringTests</c> uses it to check the
    /// shipped templates still agree. A second copy of the rule inside the test would compare the
    /// wiring against itself; a hand-kept list would go stale on the next art wave. The art is
    /// named by family already (<c>tree_volcanic_charred_3</c>), so the name is the source.</para>
    ///
    /// <para><b>ORDER IS PART OF THE RULE.</b> <c>tree_ancient_swamp_guardian</c> is ancient before
    /// it is swamp, and <c>tree_corrupted_enchanted</c> corrupted before enchanted: the stronger
    /// statement about the tree wins, because it is the one the player can see.</para>
    /// </summary>
    public static class TreeFamilyClassifier
    {
        public static TreeFamily Classify(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return TreeFamily.None;

            string name = assetPath.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            string file = (slash >= 0 ? name.Substring(slash + 1) : name).ToLowerInvariant();

            if (!file.StartsWith("tree_", StringComparison.Ordinal)) return TreeFamily.None;
            // Remains and props that merely START with the word are not trees to fell.
            if (file.Contains("stump")) return TreeFamily.None;

            if (Has(file, "ancient"))   return TreeFamily.Ancient;
            if (Has(file, "corrupted")) return TreeFamily.Corrupted;
            if (Has(file, "enchanted")) return TreeFamily.Enchanted;
            if (Has(file, "volcanic"))  return TreeFamily.Volcanic;
            if (Has(file, "winter") || Has(file, "snow")) return TreeFamily.Winter;
            if (Has(file, "swamp"))     return TreeFamily.Swamp;
            if (Has(file, "tropical"))  return TreeFamily.Tropical;
            if (Has(file, "autumn") || Has(file, "maple") || Has(file, "cherry_blossom_pink"))
                return TreeFamily.Autumn;
            if (Has(file, "bonsai") || Has(file, "small")) return TreeFamily.Small;
            return TreeFamily.Common;
        }

        /// <summary>The profile asset name a family is wired to.</summary>
        public static string ProfileName(TreeFamily family) =>
            family == TreeFamily.None ? null : "DP_tree_" + family.ToString().ToLowerInvariant();

        public static string DisplayName(TreeFamily family)
        {
            switch (family)
            {
                case TreeFamily.Small:     return "Árbol joven";
                case TreeFamily.Common:    return "Árbol común";
                case TreeFamily.Autumn:    return "Árbol otoñal";
                case TreeFamily.Tropical:  return "Árbol tropical";
                case TreeFamily.Swamp:     return "Árbol de pantano";
                case TreeFamily.Winter:    return "Árbol nevado";
                case TreeFamily.Volcanic:  return "Árbol volcánico";
                case TreeFamily.Ancient:   return "Árbol ancestral";
                case TreeFamily.Enchanted: return "Árbol encantado";
                case TreeFamily.Corrupted: return "Árbol corrupto";
                default:                   return "Árbol";
            }
        }

        private static bool Has(string file, string word) =>
            file.IndexOf(word, StringComparison.Ordinal) >= 0;
    }
}
