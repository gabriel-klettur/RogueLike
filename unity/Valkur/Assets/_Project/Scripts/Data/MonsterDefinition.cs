using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining a monster type.
    /// Maps to Python's new_hostiles.json -> hostiles.classes[className].
    /// One asset per monster class (barbol, barbol_elite, dragon, etc.).
    /// </summary>
    [CreateAssetMenu(fileName = "NewMonster", menuName = "Valkur/Data/Monster Definition")]
    public class MonsterDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string monsterKey;
        public string displayName;

        [Header("Stats")]
        public EntityStats stats;

        [Header("AI")]
        public string fsmSet;
        public string patrolType;
        public bool useAttackTelegraph;

        [Header("Phase Boss")]
        public string nextPhase;
        public int phaseIndex;

        [Header("Auto Cast")]
        public bool autoCast;
        public string[] autoCastList;

        [Header("Assets")]
        public EntityAssetConfig assetConfig;
    }
}
