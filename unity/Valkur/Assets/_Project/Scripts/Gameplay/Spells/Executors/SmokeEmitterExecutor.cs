using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A cloud that stays. Two of them today and they are not the same thing:
    /// <c>smoke_emitter</c> is a SCREEN — visual only, dropped at the caster, no damage — and
    /// <c>spore_cloud</c> is a HAZARD that poisons and slows whatever stands in it.
    ///
    /// <para><b>THE DISCRIMINATOR IS THE MECHANIC, NOT THE SPELL KEY.</b> A cloud that authors
    /// <c>damagePerTick</c> or a status application is a hazard; anything else is a screen. The
    /// same argument <see cref="GroundFieldProfile"/> makes: a key-matched branch is a second
    /// opinion about the spell that is free to disagree with the data silently.</para>
    ///
    /// <para><b>THE HAZARD USED TO DO NOTHING AT ALL.</b> There was no <c>Physics2D</c> call
    /// anywhere in this file, so <c>spore_cloud</c>'s authored <c>damagePerTick: 4</c>,
    /// <c>Poison</c> 4 s and <c>Slow</c> 1.2 s reached zero code — a control spell that could
    /// not control anything, with 20 mana and a 12 s cooldown on it. It runs through
    /// <see cref="PuddleController"/> now, which already owns the damage sweep, the
    /// status-on-its-own-clock rule and the compressed close, rather than growing a third copy
    /// of all three.</para>
    ///
    /// <para><b>THE HAZARD IS AIMED AND THE SCREEN IS NOT.</b> <c>spore_cloud</c> authors
    /// <c>spawnAtMouse: 1</c> and <c>range: 8</c>, and both were inert here — the cloud always
    /// appeared on top of the caster. Ground-PLACED spells resolve through
    /// <see cref="SpellTargeting"/>, the single owner of where an aimed spell lands; the screen
    /// keeps <c>ResolveCastStart</c>, because a smoke screen you drop to break line of sight is
    /// dropped where you are standing.</para>
    /// </summary>
    public class SmokeEmitterExecutor : ISpellExecutor
    {
        internal const float DEFAULT_DURATION = 3f;
        internal const float DEFAULT_RADIUS = 2f;

        /// <summary>Cast range used when an aimed cloud authors none.</summary>
        private const float DEFAULT_AIMED_RANGE = 6f;
        /// <summary>How far in front of the caster a non-aimed cloud sits by default.</summary>
        private const float DEFAULT_PLACED_DISTANCE = 2f;
        /// <summary>Tick period used when a hazard cloud authors none.</summary>
        private const float DEFAULT_TICK_PERIOD = 0.5f;

        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0f ? ctx.Spell.duration : DEFAULT_DURATION;
            float radius = ctx.Spell.radius > 0f ? ctx.Spell.radius : DEFAULT_RADIUS;

            if (IsHazard(ctx.Spell)) { ExecuteHazard(ctx, duration, radius); return; }

            Vector3 pos = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            var go = new GameObject("SmokeEmitter");
            go.transform.position = pos;
            var lt = go.AddComponent<SmokeLifetime>();
            lt.Init(duration, radius, AreaPalette.Smoke(SmokeExecutor.ResolveFlipbook(ctx.Spell)));
            SpellEffectRegistry.Track(go, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }

        /// <summary>
        /// A cloud that can hurt or afflict somebody. Read off the mechanics the definition
        /// already declares — see the class doc for why this is not a spell-key test.
        /// </summary>
        private static bool IsHazard(Valkur.Data.SpellDefinition spell)
        {
            if (spell == null) return false;
            if (spell.damagePerTick > 0f) return true;
            return spell.statusApplications != null && spell.statusApplications.Length > 0;
        }

        private static void ExecuteHazard(SpellContext ctx, float duration, float radius)
        {
            float tickPeriod = ctx.Spell.tickPeriod > 0f ? ctx.Spell.tickPeriod : DEFAULT_TICK_PERIOD;
            float damagePerTick = SpellPower.Scale(ctx.Spell.damagePerTick, ctx.Caster);

            Vector2 spawnPos = SpellTargeting.ResolveGroundTarget(
                ctx, DEFAULT_AIMED_RANGE, DEFAULT_PLACED_DISTANCE);

            var go = new GameObject("SpellSporeCloud");
            go.transform.position = (Vector3)spawnPos;

            var profile = GroundFieldProfile.Resolve(ctx.Spell);
            var visual = SporeCloudFX.Attach(go.transform, radius, profile.Palette);

            var controller = go.AddComponent<PuddleController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(damagePerTick), tickPeriod,
                ctx.TargetLayers, ctx.Spell.element,
                ctx.Caster != null ? ctx.Caster.gameObject : null,
                ProjectileExecutor.ResolveElement(ctx.Spell), ctx.Spell.statusApplications,
                visual);

            // Free-standing world object: nothing else can end it. The registry enforces
            // maxInstances (two, for this spell) and clears it on a zone change.
            SpellEffectRegistry.Track(go, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }
    }
}
