using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Skills
{
    /// <summary>
    /// Carries a save written while trades still had LEVELS (1..20) onto the 0-100 % skills that
    /// replaced them.
    ///
    /// <para><b>A FIXED TABLE, NOT A CATALOG LOOKUP.</b> The legacy format is frozen: no new save
    /// will ever write <c>professionKeys</c> again, so the mapping it needs is the mapping that was
    /// true when those saves were written. Resolving it through the live catalog would let a later
    /// rename of a profession silently orphan an old save's progress.</para>
    ///
    /// <para><b>ONLY EVER RAISES.</b> A skill the character has already practised under the new
    /// model keeps whichever is higher. A migration that could lower a number the player earned is
    /// a migration nobody can run twice safely, and loading a save twice is normal.</para>
    /// </summary>
    public static class LegacyProfessionMigration
    {
        /// <summary>The level cap every shipped trade had. Level 20 maps to 100.0 %.</summary>
        public const int LegacyMaxLevel = 20;

        /// <summary>Legacy profession key -> skill key. Lumberjack became woodcutting.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of literal keys, built once at type " +
                                       "initialisation and never written again.")]
        public static readonly IReadOnlyDictionary<string, string> SkillForProfession =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "cooking", "cooking" },
                { "blacksmith", "blacksmith" },
                { "mining", "mining" },
                { "crafting", "crafting" },
                { "lumberjack", "woodcutting" },
            };

        /// <summary>Tenths a legacy level is worth: level 1 is 0 %, the cap is 100 %.</summary>
        public static int TenthsForLevel(int level)
        {
            int clamped = Mathf.Clamp(level, 1, LegacyMaxLevel);
            return Mathf.RoundToInt((clamped - 1) * SkillDefinition.MaxTenths / (float)(LegacyMaxLevel - 1));
        }

        /// <summary>
        /// Apply the legacy lists in <paramref name="data"/> to <paramref name="skills"/>.
        /// Returns how many skills moved. Silent: no toasts for progress the player already had.
        /// </summary>
        public static int Apply(ProgressionSaveData data, PlayerSkills skills)
        {
            if (data?.professionKeys == null || data.professionLevels == null || skills == null) return 0;

            int moved = 0;
            int count = Mathf.Min(data.professionKeys.Count, data.professionLevels.Count);
            for (int i = 0; i < count; i++)
            {
                string key = data.professionKeys[i];
                if (string.IsNullOrEmpty(key) || !SkillForProfession.TryGetValue(key, out var skillKey)) continue;

                int tenths = TenthsForLevel(data.professionLevels[i]);
                if (tenths <= skills.GetTenths(skillKey)) continue;

                skills.SetTenthsSilently(skillKey, tenths);
                moved++;
            }
            return moved;
        }
    }
}
