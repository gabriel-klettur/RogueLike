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
        Projectile,
        Slash,
        Area,
        Dash,
        Teleport,
        Beam,
        Smoke,
        Wall,
        Trap,
        Shield,
        Boomerang,
        Meteor,
        Lightning,
        ChainLightning,
        Aura,
        ArcaneFlame,
        FireworkLaunch,
        SmokeEmitter,
        SphereMagicShield,
        Puddle,
        Mine,
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

        [Header("Projectile")]
        public float speed;
        public float damage;
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
