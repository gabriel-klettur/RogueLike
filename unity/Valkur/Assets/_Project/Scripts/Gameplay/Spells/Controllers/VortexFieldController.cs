using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Vortex field that pulls or pushes nearby rigidbodies. Epic visual: spinning rune,
    /// inrushing/outrushing particles, dynamic Light2D pulse, optional caster-follow.
    /// </summary>
    public class VortexFieldController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private float _force;
        private bool _isPull;
        private Transform _followTarget;
        private LayerMask _targetLayers;

        private AreaFXRig _rig;

        public void Initialize(float duration, float radius, float force, bool isPull,
            Transform followTarget, LayerMask targetLayers)
        {
            _remaining = duration;
            _radius = radius;
            _force = force;
            _isPull = isPull;
            _followTarget = followTarget;
            _targetLayers = targetLayers;

            BuildVisual();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById(isPull ? "spell_vortex_pull" : "spell_vortex_push");
        }

        private void BuildVisual()
        {
            _rig = AreaFXRig.Attach(transform, _isPull ? AreaPalette.VortexPull() : AreaPalette.VortexPush(), _radius);
            transform.localScale = Vector3.one * Mathf.Max(0.5f, _radius);

            // Tweak particle emission shape: pull = inrushing radial, push = outrushing
            if (_rig.Particles != null)
            {
                var velOverLife = _rig.Particles.velocityOverLifetime;
                velOverLife.enabled = true;
                velOverLife.space = ParticleSystemSimulationSpace.Local;
                velOverLife.radial = new ParticleSystem.MinMaxCurve(_isPull ? -3f : 3f);
            }

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                _rig?.Destroy();
                Destroy(gameObject);
                return;
            }

            if (_followTarget != null)
                transform.position = _followTarget.position;

            ApplyForce();
            Animate();
        }

        private void Animate()
        {
            float t = Time.time;
            float fade = (_remaining < 1f) ? Mathf.Clamp01(_remaining) : 1f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 6f);
            if (_rig != null)
            {
                if (_rig.Rune != null)
                    _rig.Rune.transform.localRotation = Quaternion.Euler(0f, 0f, t * _rig.Palette.runeSpinSpeed);
                if (_rig.Core != null)
                    _rig.Core.transform.localScale = Vector3.one * _rig.Palette.coreScale * pulse;
                _rig.SetGlobalAlpha(fade);
                _rig.SetIntensity(_rig.Palette.lightIntensity * pulse);
            }
        }

        private void ApplyForce()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            foreach (var hit in hits)
            {
                var rb = hit.GetComponent<Rigidbody2D>();
                if (rb == null) continue;

                var health = hit.GetComponentInParent<Health>();
                if (health != null && health.IsDead) continue;

                Vector2 dir = ((Vector2)transform.position - rb.position).normalized;
                if (!_isPull) dir = -dir;

                float dist = Vector2.Distance(transform.position, rb.position);
                float falloff = 1f - Mathf.Clamp01(dist / _radius);
                rb.AddForce(dir * _force * falloff * Time.deltaTime, ForceMode2D.Impulse);
            }
        }
    }
}
