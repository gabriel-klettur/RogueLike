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
    /// Editorial catalog grouping for the Spells Editor. This is deliberately
    /// independent from runtime ownership/permission rules: a spell can appear
    /// in more than one audience tab without changing who can cast it.
    /// </summary>
    [Flags]
    public enum SpellAudience
    {
        None   = 0,
        Player = 1 << 0,
        NPC    = 1 << 1,
        Boss   = 1 << 2,
    }

    /// <summary>
    /// Point on the caster's body a spell is born from. Resolved as a fraction of
    /// the caster's half-height above its visual centre, so it scales by itself
    /// from a rat to a boss instead of baking in one sprite's pixel offsets.
    ///
    /// <c>Hands</c> is deliberately the zero value: every SpellDefinition asset
    /// authored before this field existed deserializes to 0, and Hands is the
    /// origin all of them were tuned against.
    /// </summary>
    public enum SpellCastAnchor
    {
        [Tooltip("Hand height. The historical origin for every spell.")]
        Hands = 0,
        [Tooltip("Ground level, at the caster's feet.")]
        Feet,
        [Tooltip("Visual centre of the sprite — the waist on a humanoid.")]
        Center,
        [Tooltip("Top of the sprite.")]
        Head,
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
        [Tooltip("Editorial groups shown in the Spells Editor. Does not restrict runtime casting.")]
        public SpellAudience audience;

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
        [Tooltip("Never expire on a timer. Only meaningful for spells whose lifetime is a " +
                 "cleanup timer rather than an animation clock, and which already own a real " +
                 "termination condition: the mine detonates, the wall runs out of HP, the " +
                 "summon dies. Do NOT set it on effects whose visuals are a function of " +
                 "age/duration — they normalise time against their lifetime and freeze. " +
                 "An infinite effect is tracked by SpellEffectRegistry, which honours " +
                 "maxInstances and clears it on a zone change; without that it would outlive " +
                 "the caster, the run, and every zone transition.")]
        public bool infinite;

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

        [Header("Cast Origin")]
        [Tooltip("Where on the caster's body this spell is born. Scales with the caster's " +
                 "size, so the same setting reads correctly on a rat and on a boss.")]
        public SpellCastAnchor castAnchor = SpellCastAnchor.Hands;

        [Tooltip("Offset from the anchor along the cast direction, in world units. Positive " +
                 "pushes the effect in front of the caster, negative behind it. Exactly 0 " +
                 "means 'use the system default' (0.5) — the value every asset authored " +
                 "before this field existed reads.")]
        public float castForwardOffset;

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
        [Tooltip("Sprite shown on the in-world projectile / area / mine / boomerang / summon / wall. " +
                 "Leave null to let the procedural visual (FireballVisual, ElementalProjectileVisual, …) drive the look.")]
        public Sprite sprite;
        public float scale = 1f;
        public float speedMultiplier = 1f;
        public float offset;
        [Tooltip("Square HUD icon shown in the spell bar, drag-preview and skill-tree. " +
                 "Independent of the in-world sprite above. Auto-assigned by Valkur > Spells > Assign Icons.")]
        public Sprite iconSprite;

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

        [Tooltip("Extra trail presets layered on top of vfxPreset, in draw order. " +
                 "A convincing effect is a stack — core, wake, sparks, smoke — because one " +
                 "ParticleSystem is one material and one behaviour.")]
        public List<string> vfxPresetLayers;

        [Tooltip("Extra impact presets layered on top of impactPreset, in draw order.")]
        public List<string> impactPresetLayers;

        [Tooltip("Preset played at the caster the moment the spell fires. The launch is the " +
                 "only beat the player is guaranteed to be looking at, so it carries most of " +
                 "the spell's sense of power.")]
        public string castPreset;

        [Tooltip("Extra cast presets layered on top of castPreset, in draw order.")]
        public List<string> castPresetLayers;

        /// <summary>
        /// Every trail preset this spell wants, primary first. Never returns null.
        /// </summary>
        public List<string> CollectVfxPresets() => CollectPresets(vfxPreset, vfxPresetLayers);

        /// <summary>
        /// Every impact preset this spell wants, primary first. Never returns null.
        /// </summary>
        public List<string> CollectImpactPresets() => CollectPresets(impactPreset, impactPresetLayers);

        /// <summary>
        /// Every launch preset this spell wants, primary first. Never returns null.
        /// </summary>
        public List<string> CollectCastPresets() => CollectPresets(castPreset, castPresetLayers);

        /// <summary>
        /// Merges the single legacy field with the layer list, dropping blanks and
        /// duplicates. Keeping the single field authoritative means the other spells keep
        /// working untouched and a layered spell stays readable in the Inspector: the
        /// primary is the one you would name if you could only name one.
        /// </summary>
        private static List<string> CollectPresets(string primary, List<string> extra)
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(primary)) result.Add(primary);

            if (extra != null)
            {
                for (int i = 0; i < extra.Count; i++)
                {
                    string id = extra[i];
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (result.Contains(id)) continue;
                    result.Add(id);
                }
            }
            return result;
        }
    }
}
