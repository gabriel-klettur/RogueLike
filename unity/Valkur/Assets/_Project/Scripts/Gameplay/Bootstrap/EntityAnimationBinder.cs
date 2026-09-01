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

        /// <summary>
        /// Re-applies <paramref name="config"/>'s sprites with <paramref name="loadoutKey"/>
        /// active — or with the base art when it is null or names no loadout.
        ///
        /// Deliberately the SAME code path as the initial bind rather than a swap that
        /// reaches in and replaces four sets: the fallback chain (walk falls back to idle,
        /// chase to walk, attack to cast, …) is what decides what a state without art shows,
        /// and a second implementation of it would answer differently the moment one of the
        /// six states a loadout does not override happened to be empty.
        /// </summary>
        public static bool ApplyLoadout(GameObject go, EntityAssetConfig config, string loadoutKey)
        {
            if (go == null || config == null)
                return false;
            return ApplyVisuals(go, config, config.FindLoadout(loadoutKey));
        }

        /// <summary>
        /// The loadout's override for <paramref name="state"/> if it has one, else the base
        /// art. Written as one call per state so the fallback chain below reads exactly as it
        /// did before loadouts existed.
        /// </summary>
        private static DirectionalAnimator.DirectionalSpriteSet BuildStateSet(
            Loadout loadout, string state,
            DirectionalSprites baseDirectional, List<Sprite> baseSheets,
            EntitySheetDirectionLayout layout, out bool usesFourDirectionalLayout)
        {
            LoadoutStateSheets over = loadout?.Find(state);
            if (over != null)
            {
                var overridden = BuildSet(over.directional, over.sheets, layout,
                                          out usesFourDirectionalLayout);
                // An override that resolved to nothing is authoring debris, not an
                // instruction to blank the state — fall through to the base art.
                if (HasFrames(overridden))
                    return overridden;
            }
            return BuildSet(baseDirectional, baseSheets, layout, out usesFourDirectionalLayout);
        }

        private static bool ApplyVisuals(GameObject go, EntityAssetConfig assetConfig,
                                         Loadout loadout = null)
        {
            var renderer = go.GetComponentInChildren<SpriteRenderer>();
            if (renderer == null)
                return false;

            var animator = go.GetComponent<DirectionalAnimator>();
            if (animator == null)
                animator = go.AddComponent<DirectionalAnimator>();

            EntitySheetDirectionLayout layout = assetConfig.directionLayout;

            var idleSet = BuildStateSet(loadout, "idle", assetConfig.idle, assetConfig.idleSheets,
                                        layout, out bool idleUsesFourDirectionalLayout);
            if (!HasFrames(idleSet))
                return false;

            var walkSet = BuildStateSet(loadout, "walk", assetConfig.walk, assetConfig.walkSheets,
                                        layout, out bool walkUsesFourDirectionalLayout);
            if (!HasFrames(walkSet))
                walkSet = idleSet;

            var chaseSet = BuildStateSet(loadout, "chase", assetConfig.chase, assetConfig.chaseSheets,
                                         layout, out _);
            if (!HasFrames(chaseSet))
                chaseSet = walkSet;

            var castSet = BuildStateSet(loadout, "cast", assetConfig.cast, assetConfig.castSheets,
                                        layout, out _);
            if (!HasFrames(castSet))
                castSet = walkSet;

            var attackSet = BuildStateSet(loadout, "attack", assetConfig.attack, assetConfig.attackSheets,
                                          layout, out _);
            if (!HasFrames(attackSet))
                attackSet = castSet;

            var damageSet = BuildStateSet(loadout, "damage", assetConfig.damage, assetConfig.damageSheets,
                                          layout, out _);
            if (!HasFrames(damageSet))
                damageSet = idleSet;

            var deathSet = BuildStateSet(loadout, "death", assetConfig.death, assetConfig.deathSheets,
                                         layout, out _);
            if (!HasFrames(deathSet))
                deathSet = idleSet;

            // Deliberately NOT falling back to idle here: DirectionalAnimator.GetSpriteSet
            // already does that for Recover, and doing it twice would make an entity that
            // genuinely has no recover art indistinguishable from one whose art failed to
            // resolve when reading the animator in the inspector.
            var recoverSet = BuildStateSet(loadout, "recover", assetConfig.recover,
                                           assetConfig.recoverSheets, layout, out _);

            bool preferCardinalDirectionSampling = idleUsesFourDirectionalLayout || walkUsesFourDirectionalLayout;
            animator.SetSpriteSets(idleSet, walkSet, chaseSet, castSet, attackSet, damageSet, deathSet, preferCardinalDirectionSampling);
            animator.SetRecoverSprites(recoverSet);
            var attackSets = BuildAttackVariants(assetConfig, layout, out var attackSpellKeys,
                                                out var attackPacing);
            animator.SetVariants(DirectionalAnimator.AnimState.Attack, attackSets,
                                 attackSpellKeys, attackPacing);
            var castSets = BuildCastVariants(assetConfig, layout, out var castSpellKeys,
                                             out var castPacing);
            animator.SetVariants(DirectionalAnimator.AnimState.Cast, castSets,
                                 castSpellKeys, castPacing);
            animator.SetAnimationSpeedMultiplier(assetConfig.scaleConfig.animationSpeedMultiplier);
            var initialFrame = animator.PeekFirstFrame(idleSet);
            if (initialFrame != null)
                renderer.sprite = initialFrame;

            ApplyEntityScale(go, assetConfig.scaleConfig, renderer);

            // Match Pygame's BLEND_RGB_MULT tint baked into sprites at load time.
            // A zero/unset Color (alpha == 0) means "no tint configured"; treat as white.
            Color tint = assetConfig.scaleConfig.tint;
            if (tint.a <= 0f && tint.r == 0f && tint.g == 0f && tint.b == 0f)
                tint = Color.white;

            ApplyHdrTint(renderer, tint);
            return true;
        }

        // SpriteRenderer.color flows through vertex color, which Unity packs as
        // Color32 (clamped to 0-1 per channel). That's fine for white/dimming tints
        // but it crushes HDR boosts: a yellow tint of (2.5, 2.1, 0) becomes (1, 1, 0)
        // before reaching the fragment, so a brown barbol×yellow ends up olive
        // instead of vibrant yellow. Route the saturated tint through the material's
        // HDR _Color uniform (via MaterialPropertyBlock so we don't allocate per-
        // entity materials) and keep SpriteRenderer.color as pure white so the
        // GrayscaleDeath dim/fade still has a clean lerp target.
        private static readonly int HdrColorPropertyId = Shader.PropertyToID("_Color");

        private static void ApplyHdrTint(SpriteRenderer renderer, Color tint)
        {
            renderer.color = Color.white;

            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor(HdrColorPropertyId, tint);
            renderer.SetPropertyBlock(mpb);
        }

        private static DirectionalAnimator.DirectionalSpriteSet BuildSet(
            DirectionalSprites directional,
            List<Sprite> sheetFrames,
            EntitySheetDirectionLayout layout,
            out bool usesFourDirectionalLayout)
        {
            usesFourDirectionalLayout = false;

            if (HasDirectionalSprites(directional))
                return DirectionalAnimator.CreateSetFromDirectional(directional);

            if (sheetFrames != null && sheetFrames.Count > 0)
            {
                bool preferFourDir = ResolvePreferFourDirectional(sheetFrames.Count, layout);
                usesFourDirectionalLayout = preferFourDir;
                return DirectionalAnimator.CreateSetFromLinearFrames(sheetFrames, preferFourDir);
            }

            return default;
        }

        /// <summary>
        /// Explicit layouts (<see cref="EntitySheetDirectionLayout.EightDirectional"/> /
        /// <see cref="EntitySheetDirectionLayout.FourDirectional_S_W_E_N"/>) win outright.
        /// <see cref="EntitySheetDirectionLayout.Auto"/> — the value every asset authored
        /// before this field existed resolves to — falls back to the historical frame-count
        /// heuristic, unchanged, so no shipped asset's rendering moves.
        /// </summary>
        private static bool ResolvePreferFourDirectional(int frameCount, EntitySheetDirectionLayout layout)
        {
            switch (layout)
            {
                case EntitySheetDirectionLayout.FourDirectional_S_W_E_N:
                    return true;
                case EntitySheetDirectionLayout.EightDirectional:
                    return false;
                default:
                    // Auto-detect layout: 8-direction strips have 40 frames (8 dirs × 5 frames),
                    // 4-direction strips have 16–20 frames (4 dirs × 4–5 frames).
                    // If dividing by 8 yields fewer than 3 frames per direction but dividing
                    // by 4 yields 3+, prefer the 4-directional mapping to avoid wrong directions.
                    return frameCount % 4 == 0
                        && frameCount / 8 < 3
                        && frameCount / 4 >= 3;
            }
        }

        /// <summary>
        /// The entity's alternative attack animations, in authored order, built through the
        /// same directional-or-sheet path as the seven base states.
        ///
        /// A variant with no frames is DROPPED rather than kept as an empty set: the
        /// selector picks an index at random, and an empty slot would render the idle pose
        /// mid-swing roughly one attack in N. Returns null when nothing survives, which is
        /// what every entity but the knight does today.
        /// </summary>
        private static List<DirectionalAnimator.DirectionalSpriteSet> BuildAttackVariants(
            EntityAssetConfig assetConfig, EntitySheetDirectionLayout layout,
            out List<IReadOnlyList<string>> spellKeys,
            out List<DirectionalAnimator.VariantPacing> pacing)
        {
            spellKeys = null;
            pacing = null;
            if (assetConfig.attackVariants == null || assetConfig.attackVariants.Count == 0)
                return null;

            var sets = new List<DirectionalAnimator.DirectionalSpriteSet>(assetConfig.attackVariants.Count);
            // Appended in lockstep with `sets`, for the reason BuildCastVariants gives: the
            // loop drops variants that resolved to no frames, so a table indexed by the
            // authored list would point one variant off from the first empty one onwards.
            var keys = new List<IReadOnlyList<string>>(assetConfig.attackVariants.Count);
            var paces = new List<DirectionalAnimator.VariantPacing>(assetConfig.attackVariants.Count);
            bool anyReserved = false;

            for (int i = 0; i < assetConfig.attackVariants.Count; i++)
            {
                AttackVariant variant = assetConfig.attackVariants[i];
                if (variant == null) continue;

                var set = BuildSet(variant.directional, variant.sheets, layout, out _);
                if (!HasFrames(set)) continue;

                sets.Add(set);
                keys.Add(variant.spellKeys);
                paces.Add(PacingOf(variant.animationSpeedMultiplier, variant.holdLastFrame));
                anyReserved |= variant.IsReservedForSpell;
            }

            if (sets.Count == 0) return null;
            if (anyReserved) spellKeys = keys;
            pacing = paces;
            return sets;
        }

        /// <summary>One variant's pacing, with a zero or negative multiplier read as the
        /// neutral 1x — an unset float on an asset authored before the field existed.</summary>
        private static DirectionalAnimator.VariantPacing PacingOf(float speed, bool hold)
            => new DirectionalAnimator.VariantPacing
            {
                SpeedMultiplier = speed > 0f ? speed : 1f,
                HoldLastFrame = hold,
            };

        /// <summary>
        /// Same, for the casting animations. Kept a separate method rather than a generic
        /// over the two variant types because <see cref="AttackVariant"/> and
        /// <see cref="CastVariant"/> are deliberately unrelated classes — see CastVariant's
        /// doc for why sharing a base would change how the shipped attack variants serialize.
        /// </summary>
        private static List<DirectionalAnimator.DirectionalSpriteSet> BuildCastVariants(
            EntityAssetConfig assetConfig, EntitySheetDirectionLayout layout,
            out List<IReadOnlyList<string>> spellKeys,
            out List<DirectionalAnimator.VariantPacing> pacing)
        {
            spellKeys = null;
            pacing = null;
            if (assetConfig.castVariants == null || assetConfig.castVariants.Count == 0)
                return null;

            var sets = new List<DirectionalAnimator.DirectionalSpriteSet>(assetConfig.castVariants.Count);
            // Appended in lockstep with `sets`, NOT indexed by the authored list: the loop
            // below drops any variant that resolved to no frames, so the two lists would
            // slide apart by one from the first empty slot onwards and every spell after it
            // would reserve its neighbour's animation.
            var keys = new List<IReadOnlyList<string>>(assetConfig.castVariants.Count);
            var paces = new List<DirectionalAnimator.VariantPacing>(assetConfig.castVariants.Count);
            bool anyReserved = false;

            for (int i = 0; i < assetConfig.castVariants.Count; i++)
            {
                CastVariant variant = assetConfig.castVariants[i];
                if (variant == null) continue;

                var set = BuildSet(variant.directional, variant.sheets, layout, out _);
                if (!HasFrames(set)) continue;

                sets.Add(set);
                keys.Add(variant.spellKeys);
                paces.Add(PacingOf(variant.animationSpeedMultiplier, variant.holdLastFrame));
                anyReserved |= variant.IsReservedForSpell;
            }

            if (sets.Count == 0) return null;
            if (anyReserved) spellKeys = keys;
            pacing = paces;
            return sets;
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
        ///
        /// PYTHON_TILE_PX is fixed at 32 (Python's tile size) regardless of the importing
        /// sprite's own PPU — NPC art imports at PPU 64, so this formula halves scaleIdle's
        /// effective size relative to a naive "PPU / PPU" read, which is WHY every barbol
        /// variant is authored at ~0.15 rather than ~0.3. That is not a bug to silently
        /// "fix": every shipped scaleIdle value was tuned against exactly this formula, so
        /// changing PYTHON_TILE_PX (or reading renderer.sprite.pixelsPerUnit for anything
        /// other than the multiply above) would resize every monster and vendor in the
        /// catalog at once. See CLAUDE.md dimension-10 audit notes before touching this.
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
