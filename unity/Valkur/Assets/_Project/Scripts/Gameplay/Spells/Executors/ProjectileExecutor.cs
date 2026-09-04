using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns a projectile in the cast direction with speed, damage, and lifetime from the spell definition.
    /// Uses VFXManager pool when available for frequently spawned projectiles.
    /// The look is entirely the spell's own preset stack — see ParticleProjectileVisual.
    /// </summary>
    public class ProjectileExecutor : ISpellExecutor
    {
        private const string POOL_PREFIX = "proj_";

        public void Execute(SpellContext ctx)
        {
            if (ctx.ProjectilePrefab == null)
            {
                // Silence here is the worst outcome: the cast is accepted, the mana is
                // spent, and nothing appears. Every other bail in this pipeline says so.
                Debug.LogWarning($"[ProjectileExecutor] '{ctx.Spell.spellKey}' has no projectile " +
                                 "prefab, so the cast produced nothing.");
                return;
            }

            var spell = ctx.Spell;

            // Charge resolves to a neutral 1 for every spell that is not chargeable, which
            // is all but one of them. See ChargeMath for why this must not be inlined.
            float damageMul = ChargeMath.DamageMultiplier(spell, ctx.ChargeFraction);
            float scaleMul  = ChargeMath.ScaleMultiplier(spell, ctx.ChargeFraction);

            // This is the canonical start point for every spell emitted by a caster.
            // Keeping Fireball on the shared helper makes its proven-good launch point the
            // source of truth for beams, breaths, boomerangs, lightning and slashes too.
            Vector3 spawnPos = ResolveCastStart(ctx.Caster, ctx.Direction, spell);

            int shots = Mathf.Max(1, spell.projectileCount);
            for (int i = 0; i < shots; i++)
                SpawnOne(ctx, spawnPos, FanDirection(ctx.Direction, i, shots, spell.spreadDegrees),
                         damageMul, scaleMul);

            // The muzzle beat belongs to the CAST, not to each shot: firing it per projectile
            // would stack five flashes on one point for a volley and make a five-shot spell
            // five times brighter than a one-shot spell at the same authored values, which is
            // the additive-layer-count trap in a different costume.
            var vfxService = ServiceLocator.Get<IVFXService>();
            if (vfxService != null)
            {
                Color flashColor = spell.particleColor != Color.white
                    ? spell.particleColor
                    : new Color(1f, 0.8f, 0.3f, 0.8f);
                vfxService.SpawnImpact(spawnPos, flashColor, 0.15f * scaleMul, 0.5f);
            }

            // Launch stack, played at the caster. Unlike the trail this is not parented to
            // anything: the muzzle effect belongs to the moment and the place, not to the
            // projectile that is already leaving.
            var castPresets = spell.CollectCastPresets();
            if (castPresets.Count > 0 && VFXManager.Instance != null)
            {
                for (int i = 0; i < castPresets.Count; i++)
                    VFXManager.Instance.SpawnParticlePreset(castPresets[i], spawnPos, -1f);
            }
        }

        /// <summary>
        /// Heading for shot <paramref name="index"/> of <paramref name="shots"/>, spread
        /// evenly across <paramref name="spreadDegrees"/> and centred on the aim.
        ///
        /// <para>A single shot is returned untouched rather than "centred on a zero-width
        /// fan", so a spell that never authored the volley fields flies exactly where it
        /// always did — including through the floating-point path, which a lerp over a
        /// degenerate range would not guarantee.</para>
        /// </summary>
        public static Vector2 FanDirection(Vector2 aim, int index, int shots, float spreadDegrees)
        {
            if (shots <= 1 || spreadDegrees <= 0f) return aim;

            float t = index / (float)(shots - 1);          // 0 .. 1 across the fan
            float angle = Mathf.Lerp(-spreadDegrees * 0.5f, spreadDegrees * 0.5f, t);
            return (Vector2)(Quaternion.Euler(0f, 0f, angle) * aim);
        }

        private void SpawnOne(SpellContext ctx, Vector3 spawnPos, Vector2 direction,
                              float damageMul, float scaleMul)
        {
            var spell = ctx.Spell;
            string poolKey = POOL_PREFIX + spell.spellKey;

            // Try pool-based spawn, fall back to Instantiate
            GameObject go = null;
            var vfxMgr = VFXManager.Instance;
            if (vfxMgr != null)
            {
                // Prewarm by the volley size: a five-shot spell that reserved four would
                // Instantiate one projectile on every single cast, forever.
                vfxMgr.RegisterPrefab(poolKey, ctx.ProjectilePrefab, Mathf.Max(4, spell.projectileCount * 2));
                go = vfxMgr.Spawn(poolKey, spawnPos, Quaternion.identity);
            }
            if (go == null)
                go = Object.Instantiate(ctx.ProjectilePrefab, spawnPos, Quaternion.identity);
            go.SetActive(true);

            // Ball projectiles render exclusively through the spell's vfxPreset particle
            // trail. AttachVisual installs ParticleProjectileVisual on the pooled projectile
            // and strips any legacy SpriteRenderer-based rig left over from earlier prefab
            // configurations.
            AttachVisual(go, spell);

            var proj = go.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.SetPoolKey(poolKey);
                // Bind the caster BEFORE Initialize/FixedUpdate so the very first
                // sweep can already filter caster-owned colliders. Without this,
                // a caster with a child collider on the target layer (perception
                // trigger, hurtbox) would self-damage on spawn frame ("fireball
                // blew up in my face").
                proj.SetCaster(ctx.Caster);
                proj.Initialize(
                    direction,
                    spell.speed,
                    SpellPower.Scale(spell.damage, ctx.Caster) * damageMul,
                    spell.lifetime > 0 ? spell.lifetime : 3f,
                    spell.range > 0 ? spell.range : 20f,
                    ctx.TargetLayers
                );
                // Assigned unconditionally: the projectile is pooled, so skipping the call
                // for a spell with no impact preset would leave the PREVIOUS spell's
                // explosion attached to it. CollectImpactPresets also folds in
                // impactPresetLayers, which a spell may use without a primary.
                proj.SetImpactPresets(spell.CollectImpactPresets());
                // Damage typing + status rolls, consulted by Health on impact — see
                // ResolveElement's doc for why the SO field wins over the legacy key switch.
                proj.SetElement(ResolveElement(spell));
                proj.SetStatusApplications(spell.statusApplications);
                // Both set unconditionally, for the same pool-reuse reason as the impact
                // presets above: a shot that inherited the last spell's pierce budget would
                // fly straight through this caster's target.
                proj.SetPiercing(spell.pierceCount, spell.pierceDamageFalloff);
                proj.SetHoming(spell.homingStrength, spell.homingRange);
                AttachMechanicFeedback(proj, spell);
                // Acceleration: reuses the SpellDefinition.distance field (unused for projectiles).
                if (spell.distance > 0f)
                    proj.SetAcceleration(spell.distance);
                // AOE explosion on impact. A chargeable spell only splashes once it is
                // actually charged — that difference is the payoff the ramp promises, and
                // hiding it behind an identical impact would make the hold pointless.
                bool splashes = spell.explosionRadius > 0f
                             && (!spell.IsChargeable || ChargeMath.IsFullyCharged(spell, ctx.ChargeFraction));
                if (splashes)
                    proj.SetExplosion(spell.explosionRadius * scaleMul, SpellPower.Scale(
                        spell.explosionDamage > 0f ? spell.explosionDamage : spell.damage,
                        ctx.Caster) * damageMul);
                else
                    proj.SetExplosion(0f, 0f);
            }

            if (spell.sprite != null)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sprite = spell.sprite;
                    // Apply visual scale to the sprite child only — never to the root GO.
                    // Python's spell.scale is a sprite pixel-scale factor (e.g. 0.05 for
                    // fireball), not a world-unit scale. Applying it to the root GO would
                    // shrink physics colliders and make procedural visuals invisible.
                    float drawn = spell.scale > 0 ? spell.scale * scaleMul : scaleMul;
                    if (!Mathf.Approximately(drawn, 1f))
                        sr.transform.localScale = Vector3.one * drawn;
                }
            }

            // Apply particle color tint to sprite (only meaningful for legacy
            // sprite-driven projectiles; ParticleProjectileVisual hides the
            // root SpriteRenderer so this is a no-op for ball projectiles).
            if (spell.particleColor != Color.white)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sr.color = spell.particleColor;
            }
        }

        /// <summary>
        /// Give the two new projectile mechanics a visible beat each.
        ///
        /// <para>A pierce and a lock-on are both EVENTS, and an event the player cannot see
        /// is a mechanic they cannot learn: without this a piercing lance four bodies deep
        /// looks exactly like the one that just left the hand, and its damage falloff is a
        /// number that exists only in the asset. Subscriptions are cleared by
        /// <c>Projectile.ResetState</c> on pool return, so a recycled shot never drives a
        /// rig belonging to a cast that finished several seconds ago.</para>
        /// </summary>
        private static void AttachMechanicFeedback(Projectile proj, SpellDefinition spell)
        {
            if (spell.pierceCount > 0)
            {
                Color pierceColor = spell.particleColor != Color.white
                    ? spell.particleColor
                    : new Color(0.85f, 0.92f, 1f, 0.9f);
                int budget = spell.pierceCount;
                proj.OnPierced += (contact, remaining) =>
                {
                    var vfx = ServiceLocator.Get<IVFXService>();
                    if (vfx == null) return;
                    // Each pierce is dimmer than the last, so the falloff is legible on
                    // screen rather than only in the tooltip.
                    float fade = Mathf.Clamp01((remaining + 1f) / (budget + 1f));
                    vfx.SpawnImpact(contact, pierceColor, 0.18f, 0.35f + 0.45f * fade);
                };
            }

            if (spell.homingStrength > 0f && spell.homingRange > 0f)
            {
                Color lockColor = spell.particleColor != Color.white
                    ? spell.particleColor
                    : new Color(1f, 0.95f, 0.45f, 0.9f);
                proj.OnHomingAcquired += target =>
                {
                    if (target == null) return;
                    var vfx = ServiceLocator.Get<IVFXService>();
                    vfx?.SpawnImpact(target.position, lockColor, 0.22f, 0.5f);
                };
            }
        }

        /// <summary>
        /// Resolves the world-space "center" of a caster for projectile spawning.
        /// Priority: SpriteRenderer.bounds.center (visual center) â†’ any Collider2D
        /// bounds.center â†’ transform.position. A guaranteed minimum upward lift of
        /// <see cref="MIN_LIFT_ABOVE_PIVOT"/> world units is applied so the spawn
        /// never sits at the feet of a 2D character (handles sprites with
        /// center pivot, null sprite frames, and centered colliders).
        /// Public + static so it can be unit-tested independently from spell execution.
        /// </summary>
        public const float MIN_LIFT_ABOVE_PIVOT = 0.5f;

        public static Vector3 ResolveCasterCenter(Transform caster)
        {
            if (caster == null) return Vector3.zero;

            Vector3 fallback = caster.position + new Vector3(0f, MIN_LIFT_ABOVE_PIVOT, 0f);
            Vector3 result = fallback;

            // 1. Prefer the visual sprite bounds. Use root component first so we
            //    don't accidentally pick up a child shadow / aura SpriteRenderer.
            var sr = caster.GetComponent<SpriteRenderer>();
            if (sr == null) sr = caster.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                result = sr.bounds.center;
            }
            else
            {
                // 2. Fall back to any Collider2D bounds.
                var col2d = caster.GetComponent<Collider2D>();
                if (col2d == null) col2d = caster.GetComponentInChildren<Collider2D>();
                if (col2d != null) result = col2d.bounds.center;
            }

            // 3. Enforce a minimum lift above the transform pivot. This handles
            //    center-pivot sprites and centered colliders (offset 0,0) where
            //    bounds.center coincides with the character's feet.
            float minY = caster.position.y + MIN_LIFT_ABOVE_PIVOT;
            if (result.y < minY) result.y = minY;
            return result;
        }

        /// <summary>
        /// Fraction of the caster's half-height, above its centre, where a cast leaves the
        /// body. <see cref="ResolveCasterCenter"/> returns the geometric middle of the
        /// sprite — the waist on a humanoid — so a projectile launched from there reads as
        /// coming out of the character's stomach. Hands sit roughly here.
        /// </summary>
        private const float CAST_HEIGHT_FRACTION = 0.45f;

        /// <summary>
        /// Forward clearance used by Fireball and every other caster-emitted spell. It keeps
        /// the effect outside the caster collider without visually detaching it from the hand.
        /// </summary>
        public const float CAST_FORWARD_OFFSET = 0.5f;

        /// <summary>
        /// Where a projectile leaves the caster: the body centre lifted to hand height.
        ///
        /// Deliberately separate from <see cref="ResolveCasterCenter"/>, which stays the
        /// body centre because melee arcs, knockback directions and AOE origins all want
        /// the middle of the character rather than its hands.
        /// </summary>
        public static Vector3 ResolveCastOrigin(Transform caster)
            => ResolveCastOrigin(caster, SpellCastAnchor.Hands);

        /// <summary>
        /// Where a spell leaves the caster, for a chosen body anchor. The anchor is
        /// applied as a fraction of the caster's half-height above its visual centre,
        /// so one setting reads correctly on every sprite size instead of baking in
        /// pixel offsets that only suit one character.
        /// </summary>
        public static Vector3 ResolveCastOrigin(Transform caster, SpellCastAnchor anchor)
        {
            if (caster == null) return Vector3.zero;

            Vector3 center = ResolveCasterCenter(caster);
            return center + new Vector3(0f, ResolveCasterHalfHeight(caster) * AnchorFraction(anchor), 0f);
        }

        /// <summary>
        /// Height of each anchor, as a signed fraction of the caster's half-height
        /// measured from its visual centre: -1 is the bottom of the sprite, +1 the top.
        /// </summary>
        private static float AnchorFraction(SpellCastAnchor anchor)
        {
            switch (anchor)
            {
                case SpellCastAnchor.Feet:   return -1f;
                case SpellCastAnchor.Center: return 0f;
                case SpellCastAnchor.Head:   return 1f;
                default:                     return CAST_HEIGHT_FRACTION;   // Hands
            }
        }

        /// <summary>
        /// Forward clearance a spell asks for, or the system default when it asks for
        /// nothing. Only an exact 0 means "default" — that is the value every asset
        /// authored before the field existed reads. Every other value is literal,
        /// negatives included, so a spell can be born behind its anchor.
        /// </summary>
        public static float ResolveCastForwardOffset(SpellDefinition spell)
            => spell != null && !Mathf.Approximately(spell.castForwardOffset, 0f)
                ? spell.castForwardOffset
                : CAST_FORWARD_OFFSET;

        /// <summary>
        /// Exact world-space point where Fireball starts: hand height plus a small clearance
        /// in the normalized cast direction. All spells that visibly leave the caster must
        /// use this method so their first frame shares the same origin.
        /// </summary>
        public static Vector3 ResolveCastStart(Transform caster, Vector2 direction)
            => ResolveCastStart(caster, direction, SpellCastAnchor.Hands, CAST_FORWARD_OFFSET);

        /// <summary>
        /// World-space point a spell is born at, honouring its own anchor and forward
        /// clearance. Every spell that places something relative to its caster resolves
        /// through here, so the two knobs mean the same thing everywhere.
        /// </summary>
        public static Vector3 ResolveCastStart(Transform caster, Vector2 direction, SpellDefinition spell)
            => ResolveCastStart(caster, direction,
                                spell != null ? spell.castAnchor : SpellCastAnchor.Hands,
                                ResolveCastForwardOffset(spell));

        public static Vector3 ResolveCastStart(Transform caster, Vector2 direction,
                                               SpellCastAnchor anchor, float forwardOffset)
        {
            if (caster == null) return Vector3.zero;

            return ResolveCastOrigin(caster, anchor)
                + (Vector3)(direction.normalized * forwardOffset);
        }

        /// <summary>
        /// Half the caster's visual height, from the sprite if there is one, else its
        /// collider, else the minimum lift. Used to express cast height as a proportion of
        /// the character rather than as a magic number that only suits one sprite size.
        /// </summary>
        private static float ResolveCasterHalfHeight(Transform caster)
        {
            var sr = caster.GetComponent<SpriteRenderer>();
            if (sr == null) sr = caster.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null && sr.bounds.extents.y > 0.01f)
                return sr.bounds.extents.y;

            var col2d = caster.GetComponent<Collider2D>();
            if (col2d == null) col2d = caster.GetComponentInChildren<Collider2D>();
            if (col2d != null && col2d.bounds.extents.y > 0.01f)
                return col2d.bounds.extents.y;

            return MIN_LIFT_ABOVE_PIVOT;
        }

        /// <summary>
        /// Install <see cref="ParticleProjectileVisual"/> on a freshly spawned
        /// (or pool-recycled) ball projectile and re-arm it with the current
        /// spell's <c>vfxPreset</c>. The visual is particles-only — the trail
        /// is the preset's particle system parented to the projectile, the
        /// impact is whatever <c>Projectile.OnExpire</c> spawns from
        /// <c>impactPreset</c>.
        ///
        /// Strips the legacy SpriteRenderer-based rig (<see cref="ElementalProjectileVisual"/>,
        /// which <c>BoomerangExecutor</c> still attaches) plus any leftover child
        /// scaffolding (Halo / Glow / Core / HotCore / Ghost*/ FireballLight /
        /// Accent) from earlier prefab configurations or pool reuses.
        /// Idempotent across pool re-spawns.
        /// </summary>
        private static void AttachVisual(GameObject go, SpellDefinition spell)
        {
            StripLegacyVisualRigs(go);

            var pv = go.GetComponent<ParticleProjectileVisual>();
            if (pv == null) pv = go.AddComponent<ParticleProjectileVisual>();
            pv.SetSpell(spell);
        }

        private static void StripLegacyVisualRigs(GameObject go)
        {
            var elemental = go.GetComponent<ElementalProjectileVisual>();
            if (elemental != null) Object.Destroy(elemental);

            // Tear down the SpriteRenderer scaffolding the legacy rig builds as
            // direct children. We can't blindly destroy every child because
            // ParticleProjectileVisual will parent its own trail GO under us;
            // gate by name so only legacy layers are removed.
            var t = go.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var ch = t.GetChild(i);
                if (ch == null) continue;
                if (IsLegacyVisualChild(ch.name))
                    Object.Destroy(ch.gameObject);
            }
        }

        private static bool IsLegacyVisualChild(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // "Aura" is the container ElementalProjectileVisual builds today; the loose names
            // below are the same layers as it built them BEFORE that container existed, and a
            // pooled object can still be carrying either shape.
            return name == "Aura"
                || name == "Halo"
                || name == "Glow"
                || name == "Core"
                || name == "HotCore"
                || name == "Accent"
                || name == "FireballLight"
                || name.StartsWith("Ghost", System.StringComparison.Ordinal);
        }

        // Public + static so tests / UI / tooling can resolve a spell's element
        // independently of the executor (the executor itself no longer needs
        // it: ParticleProjectileVisual is element-agnostic and just plays
        // whatever vfxPreset the SpellDefinition declares).
        public static SpellElement? ResolveElement(SpellDefinition spell)
        {
            if (spell == null) return null;

            // Prefer the SO's `element` field â€” data-driven, designer-editable.
            if (!string.IsNullOrWhiteSpace(spell.element))
            {
                if (System.Enum.TryParse<SpellElement>(spell.element, ignoreCase: true, out var parsed))
                    return parsed;
            }

            // Legacy fallback: per-key switch for spells whose data hasn't yet
            // been migrated to the SO `element` field. New spell keys should
            // populate `element` instead of growing this table.
            return MapSpellKeyToElement(spell.spellKey);
        }

        private static SpellElement? MapSpellKeyToElement(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey)) return null;
            switch (spellKey)
            {
                case "darkball":  return SpellElement.Dark;
                case "iceball":   return SpellElement.Ice;
                case "lightball": return SpellElement.Light;
                case "arcane_flame": return SpellElement.Arcane;
                case "firework_launch": return SpellElement.Fire;
                // boomerang/chain_lightning use their own controllers, not ProjectileExecutor
                default: return null;
            }
        }
    }
}
