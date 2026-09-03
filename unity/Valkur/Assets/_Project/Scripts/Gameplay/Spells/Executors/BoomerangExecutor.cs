using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns a boomerang projectile that travels out then returns to the caster.
    /// Builds on the shared ball prefab (<c>ProjectilePrefabFactory</c>) — rigidbody, trigger
    /// collider and Projectile layer — but the flight itself belongs to
    /// <see cref="BoomerangProjectile"/>.
    /// </summary>
    public class BoomerangExecutor : ISpellExecutor
    {
        /// <summary>
        /// Fallback swatch for a boomerang whose <c>particleColor</c> was never authored.
        /// The palette's own core, so the impact tint, the gathered cast flourish and the blade
        /// agree by construction instead of by three separate hard-coded colours.
        /// </summary>
        private static Color DefaultTint => ElementPalette.For(SpellElement.Boomerang).core;

        public void Execute(SpellContext ctx)
        {
            if (ctx.ProjectilePrefab == null)
            {
                Debug.LogWarning("[BoomerangExecutor] ProjectilePrefab is null; cannot spawn boomerang.");
                return;
            }

            Vector3 spawnPos = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);
            var go = Object.Instantiate(ctx.ProjectilePrefab, spawnPos, Quaternion.identity);
            go.SetActive(true);
            StripBallProjectileRig(go);

            var boom = go.GetComponent<BoomerangProjectile>();
            if (boom == null)
                boom = go.AddComponent<BoomerangProjectile>();

            float speed      = ctx.Spell.speed > 0 ? ctx.Spell.speed : 8f;
            float range      = ctx.Spell.range > 0 ? ctx.Spell.range : 6f;
            float hitRadius  = ctx.Spell.hitRadius > 0 ? ctx.Spell.hitRadius : 0.25f;
            float returnSpd  = speed; // same speed back unless overridden
            bool passes      = false; // conservative default

            boom.Initialize(ctx.Caster, ctx.Direction, speed, returnSpd,
                            ctx.Spell.damage, range, hitRadius, passes,
                            ctx.TargetLayers, ResolveTint(ctx.Spell));
            // Damage typing for Health.resistances, independent of the hardcoded
            // SpellElement.Boomerang palette below (which only drives the visual).
            boom.SetElement(ProjectileExecutor.ResolveElement(ctx.Spell));
            boom.SetStatusApplications(ctx.Spell.statusApplications);
            boom.SetImpactPreset(ctx.Spell.impactPreset);

            // Procedural epic visual: spinning blade inside a red saber bloom.
            var visual = go.GetComponent<IProjectileVisual>();
            if (visual == null)
            {
                var v = go.AddComponent<ElementalProjectileVisual>();
                v.SetElement(SpellElement.Boomerang);

                // An authored sprite is the blade itself, so the rig must stop hiding the root
                // renderer — SpellFieldRelevance offers the field for this spell type, and
                // before this the control did nothing at all.
                if (ctx.Spell.sprite != null)
                {
                    var sr = go.GetComponentInChildren<SpriteRenderer>();
                    if (sr != null) sr.sprite = ctx.Spell.sprite;
                    v.KeepRootSprite();
                }
            }

            var audio = Valkur.Core.ServiceLocator.Get<Valkur.Core.IAudioService>();
            if (audio != null) audio.PlaySFXAtPosition(BoomerangAudio.Throw(), spawnPos);

            if (!string.IsNullOrEmpty(ctx.Spell.vfxPreset) && VFXManager.Instance != null)
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, spawnPos);
        }

        /// <summary>
        /// The colour this boomerang IS. Opaque white is the project-wide "nobody authored
        /// this" sentinel (<c>KiPalette.IsUnauthored</c> uses the same one); the previous test
        /// here was against <c>Color.clear</c>, which no shipped spell carries, so the branch
        /// was unreachable and every boomerang was thrown white while its blade drew its own
        /// palette colour and its cast flourish gathered arcane violet — three colours for one
        /// spell.
        ///
        /// <para>Public and static so <c>SpellCastFlourishFX.ResolveSwatch</c> can ask the same
        /// question rather than re-reading the raw field and answering differently.</para>
        /// </summary>
        public static Color ResolveTint(SpellDefinition spell)
        {
            if (spell == null) return DefaultTint;
            return KiPalette.IsUnauthored(spell.particleColor) ? DefaultTint : spell.particleColor;
        }

        /// <summary>
        /// Take the ball-projectile rig off the clone.
        ///
        /// <para>The shared prefab carries a <see cref="Projectile"/>, and the boomerang never
        /// initialises it — so it rode along with its SERIALIZED DEFAULTS. Two of them were
        /// fatal and both were silent: <c>range = 20</c> made <c>Projectile.Update</c> call
        /// <c>Expire()</c> — deactivate and destroy — as soon as the blade was 20 units out, so
        /// a throw authored to turn at 26.25 was destroyed in mid-air 0.24 s in and the return
        /// leg never ran once in the spell's life; and <c>lifetime = 3</c> was a second timer
        /// waiting behind it. Its <c>FixedUpdate</c> also wrote <c>velocity = zero * speed</c>
        /// every step, which only failed to stop the blade because the boomerang component
        /// happened to be added later and therefore wrote last.</para>
        ///
        /// <para><c>enabled = false</c> as well as <c>Destroy</c>: destruction is deferred to
        /// the end of the frame, so a component that is only destroyed still runs its Update
        /// and FixedUpdate for the remainder of this one.</para>
        /// </summary>
        private static void StripBallProjectileRig(GameObject go)
        {
            var ball = go.GetComponent<Projectile>();
            if (ball == null) return;
            ball.enabled = false;
            if (Application.isPlaying) Object.Destroy(ball);
            else                       Object.DestroyImmediate(ball);
        }
    }
}
