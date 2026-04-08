using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Base stats shared by all entities (players and NPCs).
    /// Maps to Python's stats dict in new_hostiles.json / new_players.json.
    /// </summary>
    [Serializable]
    public struct EntityStats
    {
        [Header("Vitals")]
        public int hp;
        public float speed;
        public float chasingSpeed;
        public int defense;
        public int power;

        [Header("Combat")]
        public int meleeRange;
        public int meleeDamage;
        public float meleeCooldown;
        public float aggroRange;
        public float damageDuration;
        public float damageStopProbability;
        public float attackWindupSeconds;

        [Header("Spawn")]
        public int spawnCount;
        public int spawnPadding;
        public int spawnMargin;
        public float deathDisappearTime;

        [Header("Collision")]
        public float feetWidthFactor;
        public float feetHeightFactor;

        [Header("Faction")]
        public string faction;

        [Header("NPC / Vendor")]
        public float chatRange;
    }
}
