using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining a spawner instance.
    /// Maps to Python's spawners_instances.json entries.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSpawner", menuName = "Valkur/Data/Spawner Definition")]
    public class SpawnerDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string templateId;
        public string zone;
        public Vector2Int tile;

        [Header("Life")]
        public bool damageable;
        public int maxHp;
        public bool flashOnHit;
        public Color flashColor = Color.white;
        public float flashDurationSeconds = 0.08f;
        public string hpResetOnEnter;
        public string hpScope;
        public bool visibleInGame;
    }
}
