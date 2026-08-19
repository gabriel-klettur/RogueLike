using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// One rung of the combo ladder. A tier owns everything the badge needs to
    /// repaint itself when the streak crosses <see cref="MinCount"/>: the word
    /// shown next to the number, the accent colour shared by the number, the
    /// edge and the drain bar, the halo tint/opacity, and how hard the number
    /// punches on a hit that lands inside the tier.
    ///
    /// Tiers are plain serializable data so the ladder can grow without a code
    /// change — add an entry, and the pip row, colours and titles follow.
    /// </summary>
    [Serializable]
    public sealed class ComboTier
    {
        [SerializeField, Tooltip("Lowest combo count that activates this tier.")]
        private int minCount = 1;

        [SerializeField, Tooltip("Word shown beside the number (e.g. SAVAGE).")]
        private string title = "COMBO";

        [SerializeField, Tooltip("Number, edge and drain-bar colour for this tier.")]
        private Color color = UnityEngine.Color.white;

        [SerializeField, Tooltip("Tint of the soft halo behind the number.")]
        private Color glowColor = UnityEngine.Color.white;

        [SerializeField, Range(0f, 1f), Tooltip("Halo opacity while the combo idles.")]
        private float glowStrength = 0.3f;

        [SerializeField, Range(1f, 2f), Tooltip("Scale the number snaps to when a hit lands.")]
        private float punchScale = 1.2f;

        public int    MinCount     => minCount;
        public string Title        => title;
        public Color  Color        => color;
        public Color  GlowColor    => glowColor;
        public float  GlowStrength => glowStrength;
        public float  PunchScale   => punchScale;

        /// <summary>Parameterless ctor kept for Unity serialization.</summary>
        public ComboTier() { }

        public ComboTier(int minCount, string title, Color color, Color glowColor,
                         float glowStrength, float punchScale)
        {
            this.minCount     = minCount;
            this.title        = title;
            this.color        = color;
            this.glowColor    = glowColor;
            this.glowStrength = glowStrength;
            this.punchScale   = punchScale;
        }
    }

    public sealed partial class ComboHUD
    {
        [Header("Ladder")]
        [SerializeField, Tooltip("Combo ladder, ascending by MinCount. Add a tier and the " +
                                 "pip row, colours and titles follow automatically.")]
        private ComboTier[] tiers = BuildDefaultTiers();

        // Last-resort tier used when the ladder is empty or misconfigured, so the
        // badge degrades to something readable instead of throwing.
        private static readonly ComboTier FallbackTier = new ComboTier(
            1, "COMBO",
            new Color(1f, 0.85f, 0.54f, 1f), new Color(1f, 0.72f, 0.30f, 1f),
            0.25f, 1.18f);

        /// <summary>Number of rungs currently configured (one pip each).</summary>
        public int TierCount => tiers != null ? tiers.Length : 0;

        /// <summary>The tier the badge is painted with right now.</summary>
        public ComboTier CurrentTier => _tier ?? FallbackTier;

        /// <summary>
        /// Replace the ladder at runtime (data-driven difficulty, a modded
        /// ruleset, a test). The list is copied and sorted ascending, so callers
        /// keep ownership of theirs and order does not matter.
        /// </summary>
        public void SetTiers(IReadOnlyList<ComboTier> newTiers)
        {
            if (newTiers == null || newTiers.Count == 0)
            {
                tiers = BuildDefaultTiers();
            }
            else
            {
                var copy = new ComboTier[newTiers.Count];
                for (int i = 0; i < newTiers.Count; i++)
                    copy[i] = newTiers[i] ?? FallbackTier;
                Array.Sort(copy, CompareByMinCount);
                tiers = copy;
            }

            RebuildPips();
            ApplyTier(ResolveTier(_displayedCount), force: true);
        }

        private static int CompareByMinCount(ComboTier a, ComboTier b) =>
            a.MinCount.CompareTo(b.MinCount);

        /// <summary>Highest tier whose MinCount the streak has reached.</summary>
        private ComboTier ResolveTier(int count)
        {
            if (tiers == null || tiers.Length == 0) return FallbackTier;

            for (int i = tiers.Length - 1; i >= 0; i--)
            {
                var tier = tiers[i];
                if (tier != null && count >= tier.MinCount) return tier;
            }
            return tiers[0] ?? FallbackTier;
        }

        /// <summary>Index of the highest reached tier, or -1 below the first rung.</summary>
        private int ResolveTierIndex(int count)
        {
            if (tiers == null) return -1;
            for (int i = tiers.Length - 1; i >= 0; i--)
            {
                var tier = tiers[i];
                if (tier != null && count >= tier.MinCount) return i;
            }
            return -1;
        }

        // Warm gold → amber → hot orange → red → violet. Each rung punches a
        // little harder and glows a little brighter than the one below it.
        private static ComboTier[] BuildDefaultTiers() => new[]
        {
            new ComboTier( 2, "COMBO",
                new Color(1.00f, 0.85f, 0.54f, 1f), new Color(1.00f, 0.72f, 0.30f, 1f), 0.22f, 1.16f),
            new ComboTier( 5, "GREAT",
                new Color(1.00f, 0.76f, 0.29f, 1f), new Color(1.00f, 0.60f, 0.18f, 1f), 0.32f, 1.22f),
            new ComboTier(10, "SAVAGE",
                new Color(1.00f, 0.54f, 0.24f, 1f), new Color(1.00f, 0.37f, 0.10f, 1f), 0.42f, 1.28f),
            new ComboTier(18, "BRUTAL",
                new Color(1.00f, 0.35f, 0.32f, 1f), new Color(1.00f, 0.09f, 0.27f, 1f), 0.54f, 1.35f),
            new ComboTier(30, "GODLIKE",
                new Color(0.89f, 0.42f, 1.00f, 1f), new Color(0.69f, 0.30f, 1.00f, 1f), 0.70f, 1.44f),
        };
    }
}
