using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Smoke burst at the caster: a short-lived volumetric cloud drawn by the shared
    /// <see cref="AreaFXRig"/>. Visual/utility only — no damage, no status effect.
    ///
    /// The rig is the single visual owner. This used to also spawn the spell's
    /// <c>vfxPreset</c> through <see cref="VFXManager"/>, so every cast ran two unrelated
    /// particle systems whose sizes differed by 4x and whose emission rates never lined up.
    /// The preset is still the home of the authored data — the flipbook frames are read
    /// from it — but it no longer draws a second cloud of its own.
    /// </summary>
    public class SmokeExecutor : ISpellExecutor
    {
        internal const float DEFAULT_DURATION = 1.2f;
        internal const float DEFAULT_RADIUS = 1.5f;

        public void Execute(SpellContext ctx)
        {
            Vector3 pos = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);
            float duration = ctx.Spell.duration > 0f ? ctx.Spell.duration : DEFAULT_DURATION;
            float radius = ctx.Spell.radius > 0f ? ctx.Spell.radius : DEFAULT_RADIUS;

            var go = new GameObject("SmokeBurst");
            go.transform.position = pos;
            var lt = go.AddComponent<SmokeLifetime>();
            lt.Init(duration, radius, AreaPalette.Smoke(ResolveFlipbook(ctx.Spell)));
            SpellEffectRegistry.Track(go, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }

        /// <summary>
        /// Pull the animation frames out of the spell's particle preset. Keeps the 64-frame
        /// sequence as Inspector-authored data on the preset asset instead of a hardcoded
        /// Resources path, and returns null harmlessly whenever the catalog, the preset or
        /// the frames are absent — the rig then falls back to its procedural smoke texture.
        /// </summary>
        internal static Sprite[] ResolveFlipbook(SpellDefinition spell)
        {
            if (spell == null || string.IsNullOrEmpty(spell.vfxPreset)) return null;
            if (VFXManager.Instance == null) return null;

            ParticlePresetDefinition preset = VFXManager.Instance.GetParticlePreset(spell.vfxPreset);
            var frames = preset?.vfx?.flipbookFrames;
            return (frames != null && frames.Length > 0) ? frames : null;
        }
    }

    /// <summary>
    /// Self-destroying smoke cloud. Runs in two stages: it lives for its authored duration
    /// while expanding and flickering, then stops emitting and waits out the particles still
    /// in the air before disposing itself.
    /// </summary>
    internal class SmokeLifetime : MonoBehaviour
    {
        /// <summary>How much the sprite layers grow across the cloud's life.</summary>
        private const float SPRITE_GROWTH = 0.45f;
        /// <summary>Amplitude of the Perlin breathing applied to the sprite layers.</summary>
        private const float FLICKER_AMPLITUDE = 0.12f;
        private const float FLICKER_SPEED = 1.7f;
        /// <summary>Fraction of the life spent fading in, so the cloud does not pop into being.</summary>
        private const float FADE_IN_FRACTION = 0.12f;
        /// <summary>Exponent on the fade-out — smoke thins slowly, then goes all at once.</summary>
        private const float FADE_EXPONENT = 1.4f;

        private float _life, _age;
        private float _baseScale;
        private float _noiseSeed;
        private AreaFXRig _rig;
        private bool _dissipating;
        private float _dissipateUntil;

        public void Init(float life, float radius, AreaPalette palette)
        {
            _life = Mathf.Max(0.05f, life);
            _baseScale = Mathf.Max(0.5f, radius);
            _noiseSeed = Random.value * 100f;
            _rig = AreaFXRig.Attach(transform, palette, radius);
            transform.localScale = Vector3.one * _baseScale;
        }

        private void Update()
        {
            if (_dissipating)
            {
                if (Time.time >= _dissipateUntil) Dispose();
                return;
            }

            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);

            // Grow with a smoothstep so the cloud eases out of its expansion instead of
            // stopping dead, and keep expanding right through the fade — smoke that stops
            // growing while it fades reads as a light being switched off.
            float eased = t * t * (3f - 2f * t);
            transform.localScale = Vector3.one * (_baseScale * (1f + SPRITE_GROWTH * eased));

            // Asymmetric alpha: quick to arrive, slow to leave.
            float alpha = t < FADE_IN_FRACTION
                ? t / FADE_IN_FRACTION
                : Mathf.Pow(1f - Mathf.InverseLerp(FADE_IN_FRACTION, 1f, t), FADE_EXPONENT);

            // Perlin breathing keeps the mass from reading as one flat decal.
            float flicker = 1f + (Mathf.PerlinNoise(_noiseSeed, Time.time * FLICKER_SPEED) - 0.5f)
                                 * 2f * FLICKER_AMPLITUDE;

            _rig?.SetGlobalAlpha(Mathf.Clamp01(alpha * flicker));
            _rig?.SetIntensity(Mathf.Max(0f, alpha * flicker));

            if (t >= 1f) BeginDissipate();
        }

        /// <summary>
        /// Cut emission and let the airborne particles finish. Without this the cloud is
        /// destroyed with its particles still alive and disappears on a single frame.
        /// </summary>
        private void BeginDissipate()
        {
            _dissipating = true;
            float tail = _rig != null ? _rig.StopEmitting() : 0f;
            _dissipateUntil = Time.time + Mathf.Max(0.05f, tail);
            _rig?.SetGlobalAlpha(0f);
            _rig?.SetIntensity(0f);
        }

        private void Dispose()
        {
            _rig?.Destroy();
            _rig = null;
            Destroy(gameObject);
        }
    }
}
