using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Applies data-driven character sprites from definitions into DirectionalAnimator.
    /// Keeps rendering concerns isolated from gameplay stat setup.
    /// </summary>
    public static class EntityAnimationBinder
    {
        public static bool ApplyPlayerVisuals(GameObject go, PlayerDefinition def)
        {
            if (go == null || def == null || def.assetConfig == null)
                return false;

            return ApplyVisuals(go, def.assetConfig);
        }

        public static bool ApplyMonsterVisuals(GameObject go, MonsterDefinition def)
        {
            if (go == null || def == null || def.assetConfig == null)
                return false;

            return ApplyVisuals(go, def.assetConfig);
        }

        private static bool ApplyVisuals(GameObject go, EntityAssetConfig assetConfig)
        {
            var renderer = go.GetComponentInChildren<SpriteRenderer>();
            if (renderer == null)
                return false;

            var animator = go.GetComponent<DirectionalAnimator>();
            if (animator == null)
                animator = go.AddComponent<DirectionalAnimator>();

            var idleSet = BuildSet(assetConfig.idle, assetConfig.idleSheets, out bool idleUsesFourDirectionalLayout);
            if (!HasFrames(idleSet))
                return false;

            var walkSet = BuildSet(assetConfig.walk, assetConfig.walkSheets, out bool walkUsesFourDirectionalLayout);
            if (!HasFrames(walkSet))
                walkSet = idleSet;

            var chaseSet = BuildSet(assetConfig.chase, assetConfig.chaseSheets, out _);
            if (!HasFrames(chaseSet))
                chaseSet = walkSet;

            var castSet = BuildSet(assetConfig.cast, assetConfig.castSheets, out _);
            if (!HasFrames(castSet))
                castSet = walkSet;

            var attackSet = BuildSet(assetConfig.attack, assetConfig.attackSheets, out _);
            if (!HasFrames(attackSet))
                attackSet = castSet;

            var damageSet = BuildSet(assetConfig.damage, assetConfig.damageSheets, out _);
            if (!HasFrames(damageSet))
                damageSet = idleSet;

            var deathSet = BuildSet(assetConfig.death, assetConfig.deathSheets, out _);
            if (!HasFrames(deathSet))
                deathSet = idleSet;

            bool preferCardinalDirectionSampling = idleUsesFourDirectionalLayout || walkUsesFourDirectionalLayout;
            animator.SetSpriteSets(idleSet, walkSet, chaseSet, castSet, attackSet, damageSet, deathSet, preferCardinalDirectionSampling);
            var initialFrame = animator.PeekFirstFrame(idleSet);
            if (initialFrame != null)
                renderer.sprite = initialFrame;

            ApplyEntityScale(go, assetConfig.scaleConfig, renderer);

            // Match Pygame's BLEND_RGB_MULT tint baked into sprites at load time.
            // A zero/unset Color (alpha == 0) means "no tint configured"; treat as white.
            Color tint = assetConfig.scaleConfig.tint;
            if (tint.a <= 0f && tint.r == 0f && tint.g == 0f && tint.b == 0f)
                tint = Color.white;
            renderer.color = new Color(tint.r, tint.g, tint.b, 1f);

            return true;
        }

        private static DirectionalAnimator.DirectionalSpriteSet BuildSet(DirectionalSprites directional, List<Sprite> sheetFrames, out bool usesFourDirectionalLayout)
        {
            usesFourDirectionalLayout = false;

            if (HasDirectionalSprites(directional))
                return DirectionalAnimator.CreateSetFromDirectional(directional);

            if (sheetFrames != null && sheetFrames.Count > 0)
            {
                // Auto-detect layout: 8-direction strips have 40 frames (8 dirs × 5 frames),
                // 4-direction strips have 16–20 frames (4 dirs × 4–5 frames).
                // If dividing by 8 yields fewer than 3 frames per direction but dividing
                // by 4 yields 3+, prefer the 4-directional mapping to avoid wrong directions.
                bool preferFourDir = sheetFrames.Count % 4 == 0
                                     && sheetFrames.Count / 8 < 3
                                     && sheetFrames.Count / 4 >= 3;
                usesFourDirectionalLayout = preferFourDir;
                return DirectionalAnimator.CreateSetFromLinearFrames(sheetFrames, preferFourDir);
            }

            return default;
        }

        private static bool HasDirectionalSprites(DirectionalSprites d)
        {
            return d.south != null || d.southEast != null || d.east != null || d.northEast != null ||
                   d.north != null || d.northWest != null || d.west != null || d.southWest != null;
        }

        /// <summary>
        /// Applies Python-parity visual scale to the entity.
        /// Python pre-scales sprites: rendered_px = raw_px * scale_idle.
        /// Unity equivalent: localScale = scaleIdle * sprite.pixelsPerUnit / PYTHON_TILE_PX.
        /// Skipped when scaleIdle is zero (e.g. players use default scale).
        /// </summary>
        private static void ApplyEntityScale(GameObject go, AnimationScaleConfig scaleConfig, SpriteRenderer renderer)
        {
            float scaleIdle = scaleConfig.scaleIdle;
            if (scaleIdle <= 0f || renderer.sprite == null)
                return;

            const float PYTHON_TILE_PX = 32f;
            float ppu = renderer.sprite.pixelsPerUnit;
            float scale = scaleIdle * ppu / PYTHON_TILE_PX;
            go.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private static bool HasFrames(DirectionalAnimator.DirectionalSpriteSet set)
        {
            return (set.south != null && set.south.Length > 0) ||
                   (set.southEast != null && set.southEast.Length > 0) ||
                   (set.east != null && set.east.Length > 0) ||
                   (set.northEast != null && set.northEast.Length > 0) ||
                   (set.north != null && set.north.Length > 0) ||
                   (set.northWest != null && set.northWest.Length > 0) ||
                   (set.west != null && set.west.Length > 0) ||
                   (set.southWest != null && set.southWest.Length > 0);
        }
    }
}
