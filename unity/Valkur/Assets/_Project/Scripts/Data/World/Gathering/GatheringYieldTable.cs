using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// What a gathering skill uncovers as it grows: tiers of goods, each unlocked at a skill,
    /// ramped in rather than switched on, optionally faded out once the worker has outgrown it,
    /// and optionally tied to the KIND of node (only a volcanic tree gives ember wood).
    ///
    /// <para><b>TIERS COMPETE, THEY DO NOT STACK.</b> One yield is one item: the table resolves a
    /// weight per tier against the worker and the node, draws a tier, then draws an item inside
    /// it. The alternative — rolling every tier independently — pays a master in every tier at
    /// once and turns skill into quantity rather than quality.</para>
    ///
    /// <para><b>A RAMP, NOT A THRESHOLD.</b> A tier that appeared at full weight the moment the
    /// worker crossed 35.0 % would make 34.9 and 35.0 two different games. It enters at zero and
    /// reaches its weight <see cref="Tier.rampSkill"/> points later, so the first birch log is a
    /// surprise and the hundredth an expectation.</para>
    ///
    /// <para><b>TAGS ARE THE NODE'S HALF.</b> A tier with a <see cref="Tier.requiredTag"/> is
    /// only reachable from a node whose profile carries that tag. That is the difference between
    /// "a better woodcutter finds better wood" and "a better woodcutter finds ember wood in an
    /// oak", which would teach the player that where they chop does not matter.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "GYT_NewTable", menuName = "Valkur/World/Gathering Yield Table")]
    public class GatheringYieldTable : ScriptableObject
    {
        [Serializable]
        public class Tier
        {
            [Tooltip("Stable key, e.g. 'birch'. Shown by nothing; named by tests and saves.")]
            public string key = "tier";

            [Tooltip("What the player reads when the tier unlocks: 'Madera de abedul'.")]
            public string displayName = "Madera";

            [Tooltip("Skill percent at which this tier starts to appear.")]
            [Range(0f, 100f)] public float minSkill;

            [Tooltip("Points after minSkill until the tier reaches its full weight.")]
            [Min(0f)] public float rampSkill = 10f;

            [Tooltip("Skill percent past which the tier fades toward fadedWeightFactor. 0 = never. " +
                     "A master does not come home with twigs.")]
            [Range(0f, 100f)] public float fadeOutSkill;

            [Range(0f, 1f)] public float fadedWeightFactor = 0.2f;

            [Tooltip("Relative weight at full ramp.")]
            [Min(0f)] public float weight = 100f;

            [Tooltip("Only nodes whose profile lists this tag can yield the tier. Empty = any node.")]
            public string requiredTag = "";

            [Tooltip("Where to look, for a tag-gated tier: 'árboles volcánicos'. Shown by the skills panel.")]
            public string whereHint = "";

            [Tooltip("The items of this tier. One is chosen uniformly per yield.")]
            public ItemDefinition[] items = Array.Empty<ItemDefinition>();
        }

        public List<Tier> tiers = new List<Tier>();

        /// <summary>
        /// Weight of one tier for a worker at <paramref name="skillPercent"/> against a node
        /// carrying <paramref name="nodeTags"/>. Zero means unreachable here and now.
        /// </summary>
        public static float WeightOf(Tier tier, float skillPercent, IReadOnlyList<string> nodeTags)
        {
            if (tier == null || tier.items == null || tier.items.Length == 0) return 0f;
            if (!HasTag(nodeTags, tier.requiredTag)) return 0f;
            if (skillPercent < tier.minSkill) return 0f;

            float ramp = tier.rampSkill <= 0f
                ? 1f
                : Mathf.Clamp01((skillPercent - tier.minSkill) / tier.rampSkill);

            float fade = 1f;
            if (tier.fadeOutSkill > 0f && skillPercent > tier.fadeOutSkill)
            {
                float t = Mathf.Clamp01((skillPercent - tier.fadeOutSkill) / 20f);
                fade = Mathf.Lerp(1f, tier.fadedWeightFactor, t);
            }

            // A tier that has JUST unlocked still has to be possible: a ramp of exactly zero at
            // minSkill would make the unlock toast announce something that cannot drop.
            return Mathf.Max(tier.weight * ramp * fade, tier.weight * 0.02f);
        }

        public static bool HasTag(IReadOnlyList<string> nodeTags, string required)
        {
            if (string.IsNullOrEmpty(required)) return true;
            if (nodeTags == null) return false;
            for (int i = 0; i < nodeTags.Count; i++)
                if (string.Equals(nodeTags[i], required, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>Draw one item. Null only when nothing in the table is reachable.</summary>
        public ItemDefinition Roll(System.Random rng, float skillPercent,
            IReadOnlyList<string> nodeTags, out Tier chosen)
        {
            chosen = null;
            if (rng == null || tiers == null || tiers.Count == 0) return null;

            float total = 0f;
            for (int i = 0; i < tiers.Count; i++) total += WeightOf(tiers[i], skillPercent, nodeTags);
            if (total <= 0f) return null;

            double roll = rng.NextDouble() * total;
            double cursor = 0d;
            for (int i = 0; i < tiers.Count; i++)
            {
                float w = WeightOf(tiers[i], skillPercent, nodeTags);
                if (w <= 0f) continue;
                cursor += w;
                if (roll < cursor || i == tiers.Count - 1)
                {
                    chosen = tiers[i];
                    return chosen.items[rng.Next(chosen.items.Length)];
                }
            }

            // Floating-point tail: the last reachable tier.
            for (int i = tiers.Count - 1; i >= 0; i--)
            {
                if (WeightOf(tiers[i], skillPercent, nodeTags) <= 0f) continue;
                chosen = tiers[i];
                return chosen.items[rng.Next(chosen.items.Length)];
            }
            return null;
        }

        /// <summary>
        /// Share of yields each tier represents at this skill on this node, 0..1, in table order.
        /// What the skills panel draws as "what you would find here".
        /// </summary>
        public void Shares(float skillPercent, IReadOnlyList<string> nodeTags, List<float> into)
        {
            into.Clear();
            float total = 0f;
            for (int i = 0; i < tiers.Count; i++)
            {
                float w = WeightOf(tiers[i], skillPercent, nodeTags);
                into.Add(w);
                total += w;
            }
            for (int i = 0; i < into.Count; i++) into[i] = total > 0f ? into[i] / total : 0f;
        }

        /// <summary>The tier a gain from <paramref name="beforePercent"/> to <paramref name="afterPercent"/> unlocked, or null.</summary>
        public Tier TierUnlockedBetween(float beforePercent, float afterPercent)
        {
            for (int i = 0; i < tiers.Count; i++)
            {
                var t = tiers[i];
                if (t != null && t.minSkill > 0f && beforePercent < t.minSkill && afterPercent >= t.minSkill)
                    return t;
            }
            return null;
        }
    }
}
