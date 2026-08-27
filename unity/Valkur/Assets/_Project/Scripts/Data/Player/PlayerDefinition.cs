using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining a player class.
    /// Maps to Python's new_players.json -> players.classes[className].
    /// One asset per player class (dwarf, barbarian, elven, mague, valkyrie).
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlayer", menuName = "Valkur/Data/Player Definition")]
    public class PlayerDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string playerKey;
        public string displayName;

        [Header("Attributes")]
        public int maxStrength;
        public int maxIntelligence;
        public int maxDexterity;
        public int initialStrength;
        public int initialIntelligence;
        public int initialDexterity;

        [Header("Combat")]
        public float basicSpeed;
        public int basicAttack;
        public int basicArmor;
        public float basicDeathTimerDuration;
        public float damageStopProbability;
        public float manaRegenPerSecond;
        public int dashCharges;

        [Header("Interaction")]
        public float dragDropRange;

        [Header("Assets")]
        public EntityAssetConfig assetConfig;

        private void OnEnable()
        {
            SanitizeAssetConfig();
        }

        private void OnValidate()
        {
            SanitizeAssetConfig();
        }

        private void SanitizeAssetConfig()
        {
            if (assetConfig == null)
                return;

            assetConfig.idleSheets = SanitizeSheet(assetConfig.idleSheets);
            assetConfig.walkSheets = SanitizeSheet(assetConfig.walkSheets);
            assetConfig.chaseSheets = SanitizeSheet(assetConfig.chaseSheets);
            assetConfig.castSheets = SanitizeSheet(assetConfig.castSheets);
            assetConfig.attackSheets = SanitizeSheet(assetConfig.attackSheets);
            assetConfig.damageSheets = SanitizeSheet(assetConfig.damageSheets);
            assetConfig.deathSheets = SanitizeSheet(assetConfig.deathSheets);
            assetConfig.recoverSheets = SanitizeSheet(assetConfig.recoverSheets);

            // Variants are picked by index at runtime, so a null hole inside one is a
            // blank frame mid-swing rather than a merely wasted slot.
            if (assetConfig.attackVariants != null)
            {
                for (int i = 0; i < assetConfig.attackVariants.Count; i++)
                {
                    var variant = assetConfig.attackVariants[i];
                    if (variant != null) variant.sheets = SanitizeSheet(variant.sheets);
                }
            }

            if (assetConfig.castVariants != null)
            {
                for (int i = 0; i < assetConfig.castVariants.Count; i++)
                {
                    var variant = assetConfig.castVariants[i];
                    if (variant != null) variant.sheets = SanitizeSheet(variant.sheets);
                }
            }
        }

        private static List<Sprite> SanitizeSheet(List<Sprite> source)
        {
            if (source == null || source.Count == 0)
                return source ?? new List<Sprite>();

            var clean = new List<Sprite>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                    clean.Add(source[i]);
            }

            if (clean.Count == 0)
                return new List<Sprite>();

            return clean;
        }
    }
}
