using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Continuous smoke emitter anchored to a position for the spell's duration. Shares the
    /// <see cref="AreaFXRig"/> and the Smoke palette with <see cref="SmokeExecutor"/>; the
    /// only difference is that it runs long enough to be a screen, not a puff.
    ///
    /// Like the burst, this no longer spawns the spell's <c>vfxPreset</c> as a second
    /// particle system — the rig draws, the preset supplies the flipbook data.
    /// </summary>
    public class SmokeEmitterExecutor : ISpellExecutor
    {
        internal const float DEFAULT_DURATION = 3f;
        internal const float DEFAULT_RADIUS = 2f;

        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0f ? ctx.Spell.duration : DEFAULT_DURATION;
            float radius = ctx.Spell.radius > 0f ? ctx.Spell.radius : DEFAULT_RADIUS;
            Vector3 pos = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            var go = new GameObject("SmokeEmitter");
            go.transform.position = pos;
            var lt = go.AddComponent<SmokeLifetime>();
            lt.Init(duration, radius, AreaPalette.Smoke(SmokeExecutor.ResolveFlipbook(ctx.Spell)));
            SpellEffectRegistry.Track(go, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }
    }
}
