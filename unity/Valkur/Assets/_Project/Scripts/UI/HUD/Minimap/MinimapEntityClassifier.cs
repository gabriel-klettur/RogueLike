using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.NPC;

namespace Valkur.UI.HUD
{
    /// <summary>What an entity is, as far as the map is concerned.</summary>
    public enum MinimapEntityKind { Hidden, Player, Enemy, Elite, Boss, Ally, Neutral, Vendor }

    /// <summary>
    /// Decides how an entity is drawn on the map from what the GAME knows about it, not from
    /// the dot type it was registered with.
    ///
    /// <para><b>Why not trust the dot type.</b> <c>EntitySetup.ConfigureMonster</c> registered
    /// every NPC as a <c>Monster</c> for the life of the project, so the six vendors of the
    /// town were six red enemy dots under their own gold markers. The dot type is a hint from
    /// an assembly that cannot see this one; the facts — <see cref="EntityFaction"/>,
    /// <see cref="AlliedUnit"/>, the bar rig's rank, <see cref="Health"/> — are readable here
    /// directly, because <c>Valkur.UI</c> references <c>Valkur.Gameplay</c>. A charmed
    /// monster therefore turns into an ally glyph the moment <c>AlliedUnit</c> says so, with
    /// nothing to re-register.</para>
    /// </summary>
    public static class MinimapEntityClassifier
    {
        /// <summary>Classify an entity. Never throws; a null object is Hidden.</summary>
        public static MinimapEntityKind Classify(GameObject go, MinimapDotType hint)
        {
            if (go == null || !go.activeInHierarchy) return MinimapEntityKind.Hidden;
            if (hint == MinimapDotType.Player || go.CompareTag("Player")) return MinimapEntityKind.Player;

            var health = go.GetComponent<Health>();
            if (health != null && health.IsDead) return MinimapEntityKind.Hidden;

            if (hint == MinimapDotType.Ally || AlliedUnit.IsAllied(go)) return MinimapEntityKind.Ally;
            if (go.GetComponent<VendorNPC>() != null) return MinimapEntityKind.Vendor;

            var side = EntityFaction.SideOf(go);
            if (side == FactionSide.PlayerSide) return MinimapEntityKind.Ally;
            if (side == FactionSide.Neutral || hint == MinimapDotType.NPC) return MinimapEntityKind.Neutral;

            var rig = go.GetComponent<WorldBarRig>();
            if (rig != null)
            {
                if (rig.Rank == WorldBarRank.Boss) return MinimapEntityKind.Boss;
                if (rig.Rank == WorldBarRank.Elite) return MinimapEntityKind.Elite;
            }
            return MinimapEntityKind.Enemy;
        }

        /// <summary>
        /// What the hover tooltip calls an entity: a vendor by their persona's name, anything
        /// else by its object name (spawned entities are named after their display name).
        /// </summary>
        public static string DisplayName(GameObject go, MinimapEntityKind kind)
        {
            if (go == null) return string.Empty;
            string name = go.name.Replace("(Clone)", string.Empty).Trim();
            if (kind == MinimapEntityKind.Vendor)
            {
                var v = go.GetComponent<VendorNPC>();
                var persona = v != null && v.VendorConfig != null ? v.VendorConfig.persona : null;
                if (persona != null && !string.IsNullOrWhiteSpace(persona.displayName)) name = persona.displayName;
                return name + " · comerciante";
            }
            switch (kind)
            {
                case MinimapEntityKind.Boss:  return name + " · jefe";
                case MinimapEntityKind.Elite: return name + " · élite";
                case MinimapEntityKind.Ally:  return name + " · aliado";
                default:                      return name;
            }
        }

        /// <summary>
        /// The trade icon for a vendor, from what their shop is seeded with. Read off the stock
        /// rather than off the persona's name, because the stock is what the player is walking
        /// over to buy — and because the old caption ("SM", "VA", "RO") was the initials of a
        /// NAME, which says nothing to anyone who has not met the character yet.
        /// </summary>
        public static MinimapIcon VendorIcon(VendorNPC vendor)
        {
            if (vendor == null) return MinimapIcon.Coin;
            var cfg = vendor.VendorConfig;
            if (cfg == null || cfg.inventorySeed == null || cfg.inventorySeed.Count == 0) return MinimapIcon.Coin;

            s_counts.Clear();
            string best = null;
            int bestN = 0;
            for (int i = 0; i < cfg.inventorySeed.Count; i++)
            {
                var item = cfg.inventorySeed[i].item;
                if (item == null || string.IsNullOrEmpty(item.itemType)) continue;
                string t = item.itemType.ToLowerInvariant();
                s_counts.TryGetValue(t, out int n);
                n++;
                s_counts[t] = n;
                if (n > bestN) { bestN = n; best = t; }
            }
            return IconForTrade(best);
        }

        /// <summary>Icon for an item-type trade key.</summary>
        public static MinimapIcon IconForTrade(string trade)
        {
            switch (trade)
            {
                case "blacksmith": return MinimapIcon.Hammer;
                case "lumberjack": return MinimapIcon.Axe;
                case "food":       return MinimapIcon.Bowl;
                case "mineral":    return MinimapIcon.Pickaxe;
                case "alchemy":    return MinimapIcon.Flask;
                case "magic":      return MinimapIcon.Star;
                default:           return MinimapIcon.Coin;
            }
        }

        private static Dictionary<string, int> s_counts = new Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetClassifierStatics()
        {
            s_counts = new Dictionary<string, int>();
        }
    }
}
