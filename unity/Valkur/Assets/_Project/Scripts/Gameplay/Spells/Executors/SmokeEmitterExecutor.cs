using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Continuous smoke emitter attached to a position for the spell duration.
    /// Mirrors Python's SmokeEmitterResolver. Uses shared <see cref="AreaFXRig"/>
    /// with the Smoke palette.
    /// </summary>
    public class SmokeEmitterExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 3f;
            float radius = 2f;
            Vector3 pos = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            var go = new GameObject("SmokeEmitter");
            go.transform.position = pos;
            var lt = go.AddComponent<SmokeLifetime>();
            lt.Init(duration, radius, AreaPalette.Smoke());

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_smoke_emitter");

            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, pos, duration);

        }
    }
}
