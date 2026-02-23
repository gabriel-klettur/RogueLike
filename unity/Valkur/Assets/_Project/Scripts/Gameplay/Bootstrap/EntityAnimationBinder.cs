using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Applies data-driven character sprites from PlayerDefinition into DirectionalAnimator.
    /// Keeps rendering concerns isolated from gameplay stat setup.
    /// </summary>
    public static class EntityAnimationBinder
    {
        public static bool ApplyPlayerVisuals(GameObject go, PlayerDefinition def)
        {
            if (go == null || def == null || def.assetConfig == null)
                return false;

            var renderer = go.GetComponentInChildren<SpriteRenderer>();
            if (renderer == null)
                return false;

            var animator = go.GetComponent<DirectionalAnimator>();
            if (animator == null)
                return false;

            var idleSet = BuildSet(def.assetConfig.idle, def.assetConfig.idleSheets, out bool idleUsesFourDirectionalLayout);
            if (!HasFrames(idleSet))
                return false;

            var walkSet = BuildSet(def.assetConfig.walk, def.assetConfig.walkSheets, out bool walkUsesFourDirectionalLayout);
            if (!HasFrames(walkSet))
                walkSet = idleSet;

            var chaseSet = BuildSet(def.assetConfig.chase, def.assetConfig.chaseSheets, out _);
            if (!HasFrames(chaseSet))
                chaseSet = walkSet;

            var castSet = BuildSet(def.assetConfig.cast, def.assetConfig.castSheets, out _);
            if (!HasFrames(castSet))
                castSet = walkSet;

            var attackSet = BuildSet(def.assetConfig.attack, def.assetConfig.attackSheets, out _);
            if (!HasFrames(attackSet))
                attackSet = castSet;

            var damageSet = BuildSet(def.assetConfig.damage, def.assetConfig.damageSheets, out _);
            if (!HasFrames(damageSet))
                damageSet = idleSet;

            var deathSet = BuildSet(def.assetConfig.death, def.assetConfig.deathSheets, out _);
            if (!HasFrames(deathSet))
                deathSet = idleSet;

            bool preferCardinalDirectionSampling = idleUsesFourDirectionalLayout || walkUsesFourDirectionalLayout;
            animator.SetSpriteSets(idleSet, walkSet, chaseSet, castSet, attackSet, damageSet, deathSet, preferCardinalDirectionSampling);
            var initialFrame = animator.PeekFirstFrame(idleSet);
            if (initialFrame != null)
                renderer.sprite = initialFrame;

            return true;
        }

        private static DirectionalAnimator.DirectionalSpriteSet BuildSet(DirectionalSprites directional, List<Sprite> sheetFrames, out bool usesFourDirectionalLayout)
        {
            usesFourDirectionalLayout = false;

            if (HasDirectionalSprites(directional))
                return DirectionalAnimator.CreateSetFromDirectional(directional);

            if (sheetFrames != null && sheetFrames.Count > 0)
            {
                usesFourDirectionalLayout = true;
                return DirectionalAnimator.CreateSetFromLinearFrames(sheetFrames, assumeFourDirectionalLayout: true);
            }

            return default;
        }

        private static bool HasDirectionalSprites(DirectionalSprites d)
        {
            return d.south != null || d.southEast != null || d.east != null || d.northEast != null ||
                   d.north != null || d.northWest != null || d.west != null || d.southWest != null;
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
