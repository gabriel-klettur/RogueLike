using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Spell type matching Python's spell type field.
    /// </summary>
    public enum SpellType
    {
        Projectile,      // 0
        Slash,            // 1
        Area,             // 2
        Dash,             // 3
        Teleport,         // 4
        Beam,             // 5
        Smoke,            // 6
        Wall,             // 7
        Trap,             // 8
        Shield,           // 9
        Boomerang,        // 10
        Meteor,           // 11
        Lightning,        // 12
        ChainLightning,   // 13
        Aura,             // 14
        ArcaneFlame,      // 15
        FireworkLaunch,   // 16
        SmokeEmitter,     // 17
        SphereMagicShield,// 18
        Puddle,           // 19
        Mine,             // 20
        VortexField,      // 21
        ConeBreath,       // 22
        Summon,           // 23
        Totem,            // 24
    }

    /// <summary>
    /// ScriptableObject defining a spell/ability.
    /// Maps to Python's SpellConfig dataclass and spells.json entries.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSpell", menuName = "Valkur/Data/Spell Definition")]
    public class SpellDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string spellKey;
        public string displayName;
        public SpellType type;

        [Header("Casting")]
        public float manaCost;
        public int maxInstances;
        public bool allowOverlap = true;
        public bool allowMovement;
        public bool interruptible;
        public bool automatic;
        public float automaticCastPunish = 1f;
        public bool lockCastDirection = true;

        [Header("Timings")]
        public float prepareDuration;
        public float channelDuration;
        public float cooldownDuration;

        [Header("Projectile / Beam")]
        public float speed;
        public float damage;
        [Tooltip("Maximum travel distance in world units. Used by projectiles (max flight before despawn), beams (max ray length / hit search) and any ranged spell that needs a hard cap. <= 0 means 'use the system default'.")]
        public float range;
        public float lifetime;

        [Header("Area / Slash")]
        public float radius;
        public float hitRadius;
        public float arcRangeDegrees;
        public float hitArcDegrees;
        public float length;

        [Header("Dash")]
        public float distance;
        public float knockback;
        public float collisionDamage;

        [Header("DoT / Aura")]
        public float duration;
        public float damagePerTick;
        public float tickPeriod;
        public string element;
        [Tooltip("Heal per tick for aura/totem spells")]
        public float healPerTick;

        [Header("Vortex / Force")]
        [Tooltip("Force magnitude for vortex spells")]
        public float force;
        [Tooltip("pull or push")]
        public string forceMode;
        [Tooltip("Whether to follow the caster")]
        public bool followCaster;

        [Header("Spawn Position")]
        [Tooltip("Spawn effect at mouse position instead of caster")]
        public bool spawnAtMouse;

        [Header("Meteor")]
        [Tooltip("Number of meteor strikes")]
        public int meteorCount;
        [Tooltip("Interval between meteor strikes")]
        public float meteorInterval;
        [Tooltip("Area radius for meteor scatter")]
        public float meteorAreaRadius;
        [Tooltip("Each meteor's impact damage radius")]
        public float meteorImpactRadius;

        [Header("Mine")]
        [Tooltip("Arming time before mine becomes active")]
        public float armingTime;
        [Tooltip("Trigger proximity radius")]
        public float triggerRadius;
        [Tooltip("Explosion radius on detonation")]
        public float explosionRadius;
        [Tooltip("Explosion damage on detonation")]
        public float explosionDamage;
        [Tooltip("Time-to-live before auto-despawn")]
        public float ttl;

        [Header("Wall")]
        [Tooltip("Wall width in world units")]
        public float wallWidth;
        [Tooltip("Wall height in world units")]
        public float wallHeight;
        [Tooltip("Wall hit points")]
        public float wallHP;
        [Tooltip("Whether the wall blocks projectiles")]
        public bool blockProjectiles;
        [Tooltip("Whether the wall blocks unit movement")]
        public bool blockUnits;

        [Header("Summon")]
        [Tooltip("Monster template key to summon")]
        public string summonTemplate;
        [Tooltip("Number of units to summon")]
        public int summonCount = 1;
        [Tooltip("Duration before summoned unit expires")]
        public float summonDuration;

        [Header("Totem")]
        [Tooltip("Totem kind: heal, damage, etc.")]
        public string totemKind;

        [Header("Cone Breath")]
        [Tooltip("Arc angle for cone breath")]
        public float coneArc;
        [Tooltip("Cone length")]
        public float coneLength;

        [Header("Visual")]
        public Sprite sprite;
        public float scale = 1f;
        public float speedMultiplier = 1f;
        public float offset;

        [Header("Telegraph")]
        public Color telegraphColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        public float telegraphAlpha = 80f;

        [Header("Particles")]
        public int particleCount;
        public float particleDispersion;
        public float particleLifespan;
        public float particleSpeed;
        public Color particleColor = Color.white;
        public List<Color> particleColors;
        public List<float> sizeRange;
        public int emitRate;

        [Header("VFX Preset")]
        public string vfxPreset;
        public string impactPreset;
    }
}
