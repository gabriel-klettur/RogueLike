using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Smoke burst at caster: short-lived volumetric cloud (gray particles + halo)
    /// via shared <see cref="AreaFXRig"/>. Visual/utility only — no damage.
    /// Mirrors Python's SmokeResolver.
    /// </summary>
    public class SmokeExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            Vector3 pos = ctx.Caster.position;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 1.2f;
            float radius = 1.5f;

            var go = new GameObject("SmokeBurst");
            go.transform.position = pos;
            var lt = go.AddComponent<SmokeLifetime>();
            lt.Init(duration, radius, AreaPalette.Smoke());

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_smoke_burst");

            // Keep legacy preset spawn for any tuned vfxPreset
            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, pos);

            Debug.Log($"[SpellDebug] Smoke cast at {pos}, duration={duration:F2}s");
        }
    }

    /// <summary>Self-destroying smoke cloud holder; fades out then disposes the rig.</summary>
    internal class SmokeLifetime : MonoBehaviour
    {
        private float _life, _age;
        private float _radius;
        private AreaPalette _palette;
        private AreaFXRig _rig;

        public void Init(float life, float radius, AreaPalette palette)
        {
            _life = life;
            _radius = radius;
            _palette = palette;
            _rig = AreaFXRig.Attach(transform, palette, radius);
            transform.localScale = Vector3.one * Mathf.Max(0.5f, radius);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / _life;
            if (t >= 1f) { _rig?.Destroy(); Destroy(gameObject); return; }
            _rig?.SetGlobalAlpha(1f - t);
        }
    }
}
