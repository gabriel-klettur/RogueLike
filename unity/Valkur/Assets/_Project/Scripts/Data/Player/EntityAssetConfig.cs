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

        [Header("Scale & Tint")]
        public AnimationScaleConfig scaleConfig;
    }
}
