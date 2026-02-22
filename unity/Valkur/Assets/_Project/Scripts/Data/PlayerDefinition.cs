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
    }
}
