using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Per-dungeon-level pool of enemy templates that can spawn in this room.
    /// Replaces Udemy's <c>SpawnableObjectsByLevel&lt;EnemyDetailsSO&gt;</c> with a
    /// non-generic shape that references the existing Valkur
    /// <c>SpawnerTemplateCatalog</c> by string id.
    /// </summary>
    [System.Serializable]
    public class SpawnableEnemyByLevel
    {
        [Tooltip("Dungeon level this entry applies to.")]
        public DungeonLevelSO dungeonLevel;

        [Tooltip("Weighted enemy template choices.")]
        public List<SpawnableEnemyRatio> spawnableEnemyRatioList = new List<SpawnableEnemyRatio>();
    }

    /// <summary>
    /// One weighted entry in a <see cref="SpawnableEnemyByLevel"/> pool.
    /// </summary>
    [System.Serializable]
    public class SpawnableEnemyRatio
    {
        [Tooltip("SpawnerTemplateCatalog id of the enemy template to spawn.")]
        public string enemyTemplateId;

        [Tooltip("Weight relative to other entries in this level. 0 = never.")]
        [Min(0)] public int ratio = 1;
    }
}
