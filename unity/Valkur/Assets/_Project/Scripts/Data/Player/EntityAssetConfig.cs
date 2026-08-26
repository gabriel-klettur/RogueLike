using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Directional sprite references for an animation state.
    /// Maps to Python's directional dict: {s, se, e, ne, n, nw, w, sw}.
    /// </summary>
    [Serializable]
    public struct DirectionalSprites
    {
        public Sprite south;
        public Sprite southEast;
        public Sprite east;
        public Sprite northEast;
        public Sprite north;
        public Sprite northWest;
        public Sprite west;
        public Sprite southWest;
    }

    /// <summary>
    /// Scale values per animation state.
    /// Maps to Python's sprites_data_set: {scale_idle, scale_walk, ...}.
    /// </summary>
    [Serializable]
    public struct AnimationScaleConfig
    {
        public float scaleIdle;
        public float scaleWalk;
        public float scaleChase;
        public float scaleCast;
        public float scaleAttack;
        public float scaleDamage;
        public float scaleDeath;
        // HDR enabled so designers can push channel values above 1.0 to overcome
        // the multiplicative nature of SpriteRenderer.color. Multiplying a brown
        // sprite by (1, 0.84, 0) flattens to "dark yellow-brown"; multiplying by
        // (2.5, 2.1, 0) clips back to (1, ~0.63, 0) which reads as vibrant yellow.
        [ColorUsage(true, true)]
        public Color tint;
    }

    /// <summary>
    /// One alternative attack animation, beyond the single <c>attack</c> slot.
    ///
    /// A LIST, not three more slots. The seven animation states are enumerated
    /// positionally in four independent places — this class's own fields,
    /// <c>DirectionalAnimator</c>'s seven serialized sets plus its seven accessors and
    /// its seven-argument <c>SetSpriteSets</c>, the <c>GetSpriteSet</c> switch, and
    /// <c>EntityAnimationBinder</c>'s build-and-fallback chain. Adding an eighth state
    /// pays that tax four times over and again for the ninth; a list pays it once.
    ///
    /// It also keeps <c>AnimState</c> untouched, which matters more than it looks:
    /// <c>PlayerController.Movement</c> gates locomotion on an Idle/Walk/Chase whitelist
    /// and reverts on a Cast/Attack whitelist. A new enum value missing from the second
    /// list is entered and never left. A variant INDEX under the existing Attack state
    /// inherits both whitelists by construction.
    /// </summary>
    [Serializable]
    public class AttackVariant
    {
        [Tooltip("Identifier used in logs and by any future range/cooldown selection rule.")]
        public string key;

        [Tooltip("Directional sprites for this variant. Takes precedence over sheets, " +
                 "exactly as the seven base slots do.")]
        public DirectionalSprites directional;

        [Tooltip("Linear frame list for this variant: eight contiguous per-direction " +
                 "buckets in the order S, SE, E, NE, N, NW, W, SW.")]
        public List<Sprite> sheets;
    }

    /// <summary>
    /// Complete asset configuration for an entity.
    /// Maps to Python's "assets" block in new_hostiles/new_players.
    /// </summary>
    [Serializable]
    public class EntityAssetConfig
    {
        [Header("Directional Sprites (no-sets mode)")]
        public DirectionalSprites idle;
        public DirectionalSprites walk;
        public DirectionalSprites chase;
        public DirectionalSprites cast;
        public DirectionalSprites attack;
        public DirectionalSprites damage;
        public DirectionalSprites death;

        [Header("Sprite Sheet Mode (sets)")]
        public List<Sprite> idleSheets;
        public List<Sprite> walkSheets;
        public List<Sprite> chaseSheets;
        public List<Sprite> castSheets;
        public List<Sprite> attackSheets;
        public List<Sprite> damageSheets;
        public List<Sprite> deathSheets;

        [Header("Attack Variants")]
        // Empty for every entity that has one attack, which is all of them but the knight.
        // When it is non-empty it REPLACES the single attack set for selection purposes:
        // index 0 is what a picker falls back to, so put the entity's default swing first.
        // `attack`/`attackSheets` stay authoritative for callers that know nothing about
        // variants (the Spells Editor preview reads AttackSprites directly).
        public List<AttackVariant> attackVariants = new List<AttackVariant>();

        [Header("Scale & Tint")]
        public AnimationScaleConfig scaleConfig;
    }
}
