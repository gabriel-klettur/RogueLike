using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Persistent ground puddle (lava/poison/water) with epic procedural visuals via
    /// <see cref="AreaFXRig"/>: rotating rune, halo, bubbling particles, dynamic Light2D.
    /// Ticks damage on enemies within radius and optionally applies burn (lava/fire).
    /// </summary>
    public class PuddleController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private LayerMask _targetLayers;
        private string _element;

        private AreaFXRig _rig;
        private float _pulse;

        public void Initialize(float duration, float radius, int damagePerTick, float tickPeriod,
            LayerMask targetLayers, string element)
        {
            _remaining = duration;
            _radius = radius;
            _damagePerTick = damagePerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _targetLayers = targetLayers;
            _element = element;

            BuildVisual();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById(element == "lava" ? "spell_puddle_lava_cast" : "spell_puddle_cast");
        }

        private void BuildVisual()
        {
            var palette = (_element == "lava" || _element == "fire")
                ? AreaPalette.LavaPuddle()
                : AreaPalette.LavaPuddle();   // future: PoisonPuddle/WaterPuddle palettes
            _rig = AreaFXRig.Attach(transform, palette, _radius);
            transform.localScale = Vector3.one * Mathf.Max(0.5f, _radius);

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

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                DamageTick();
                _tickTimer = _tickPeriod;
                _pulse = 1f;
            }

            Animate();
        }

        private void Animate()
        {
            float t = Time.time;
            _pulse = Mathf.Max(0f, _pulse - Time.deltaTime * 2.5f);
            float pulseScale = 1f + 0.20f * _pulse;
            float baseFlick = 0.85f + 0.15f * Mathf.PerlinNoise(t * 4f, 0.31f);
            float fade = (_remaining < 1f) ? Mathf.Clamp01(_remaining) : 1f;

            if (_rig != null)
            {
                if (_rig.Rune != null)
                    _rig.Rune.transform.localRotation = Quaternion.Euler(0f, 0f, t * _rig.Palette.runeSpinSpeed);
                if (_rig.Core != null)
                    _rig.Core.transform.localScale = Vector3.one * _rig.Palette.coreScale * baseFlick * pulseScale;
                if (_rig.Glow != null)
                    _rig.Glow.transform.localScale = Vector3.one * _rig.Palette.glowScale * pulseScale;
                _rig.SetGlobalAlpha(fade * baseFlick);
                _rig.SetIntensity(_rig.Palette.lightIntensity * (0.85f + 0.15f * baseFlick) + 1.0f * _pulse);
            }
        }

        private void DamageTick()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                if (_damagePerTick > 0)
                    health.TakeDamage(_damagePerTick);

                if (_element == "lava" || _element == "fire")
                {
                    var statusMgr = hit.GetComponent<StatusEffectManager>();
                    if (statusMgr != null)
                        statusMgr.Apply(new BurnEffect(3f, 5));
                }
            }
        }
    }
}
