using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Healing totem with epic green visuals: rotating ground rune, halo, rising
    /// sparkle particles, dynamic Light2D pulse on heal tick.
    /// Mirrors Python's TotemComponent (kind=heal).
    /// </summary>
    public class TotemController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private int _healPerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private Transform _owner;

        private AreaFXRig _rig;
        private float _pulse;

        public void Initialize(float duration, float radius, int healPerTick, float tickPeriod, Transform owner)
        {
            _remaining = duration;
            _radius = radius;
            _healPerTick = healPerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _owner = owner;

            BuildVisual();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_totem_create");
        }

        private void BuildVisual()
        {
            _rig = AreaFXRig.Attach(transform, AreaPalette.HealingTotem(), _radius);
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
                Debug.Log($"[SpellDebug] Totem expired at {transform.position}");
                Destroy(gameObject);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                HealTick();
                _tickTimer = _tickPeriod;
                _pulse = 1f;
            }

            Animate();
        }

        private void Animate()
        {
            float t = Time.time;
            _pulse = Mathf.Max(0f, _pulse - Time.deltaTime * 2f);
            float baseFlick = 0.85f + 0.15f * Mathf.Sin(t * 3f);
            float fade = (_remaining < 2f) ? Mathf.Clamp01(_remaining * 0.5f) : 1f;

            if (_rig != null)
            {
                if (_rig.Rune != null)
                    _rig.Rune.transform.localRotation = Quaternion.Euler(0f, 0f, t * _rig.Palette.runeSpinSpeed);
                if (_rig.Core != null)
                    _rig.Core.transform.localScale = Vector3.one * _rig.Palette.coreScale * (1f + 0.30f * _pulse);
                if (_rig.Glow != null)
                    _rig.Glow.transform.localScale = Vector3.one * _rig.Palette.glowScale * (1f + 0.20f * _pulse);
                _rig.SetGlobalAlpha(fade * baseFlick);
                _rig.SetIntensity(_rig.Palette.lightIntensity * baseFlick + 1.2f * _pulse);
            }
        }

        private void HealTick()
        {
            if (_owner == null) return;

            float dist = Vector2.Distance(transform.position, _owner.position);
            if (dist <= _radius)
            {
                var health = _owner.GetComponent<Health>();
                if (health != null && !health.IsDead)
                {
                    health.Heal(_healPerTick);
                    var audio = ServiceLocator.Get<IAudioService>();
                    if (audio != null) audio.PlaySfxById("spell_totem_heal_tick");
                    Debug.Log($"[SpellDebug] Totem healed {_owner.name} for {_healPerTick} HP (dist={dist:F1})");
                }
            }
        }
    }
}
