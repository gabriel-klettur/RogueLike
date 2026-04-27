using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spell wall with epic ice visuals: shimmering core, frost mist particles,
    /// blue Light2D, crack overlay flash on damage. Auto-destroys after duration
    /// or when Health depletes.
    /// </summary>
    public class WallController : MonoBehaviour
    {
        private float _remainingTime;
        private Health _health;
        private SpriteRenderer _sr;
        private AreaFXRig _rig;
        private float _hitFlash;
        private int _lastHp = -1;

        public void Initialize(float duration, Health health)
        {
            _remainingTime = duration;
            _health = health;
            _sr = GetComponent<SpriteRenderer>();

            BuildVisual();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_wall_ice_create");
            if (_health != null) _lastHp = _health.CurrentHp;
        }

        private void BuildVisual()
        {
            _rig = AreaFXRig.Attach(transform, AreaPalette.IceWall(), 1.0f);
            // Tint the underlying sprite (if any) to ice blue
            if (_sr != null) _sr.color = new Color(0.78f, 0.92f, 1f, 0.9f);
        }

        private void Update()
        {
            _remainingTime -= Time.deltaTime;

            if (_health != null)
            {
                if (_health.IsDead)
                {
                    OnDestroyed();
                    return;
                }
                if (_health.CurrentHp < _lastHp)
                {
                    _hitFlash = 1f;
                    _lastHp = _health.CurrentHp;
                }
            }

            if (_remainingTime <= 0f)
            {
                OnDestroyed();
                return;
            }

            Animate();
        }

        private void Animate()
        {
            float t = Time.time;
            _hitFlash = Mathf.Max(0f, _hitFlash - Time.deltaTime * 3f);
            float shimmer = 0.85f + 0.15f * Mathf.PerlinNoise(t * 5f, 0.17f);
            float fade = (_remainingTime < 1f) ? Mathf.Clamp01(_remainingTime) : 1f;
            _rig?.SetGlobalAlpha(fade * shimmer);
            _rig?.SetIntensity(_rig.Palette.lightIntensity * shimmer + 1.5f * _hitFlash);
        }

        private void OnDestroyed()
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_wall_ice_destroy");
            ElementalImpactFX.Spawn(transform.position, SpellElement.Ice);
            _rig?.Destroy();
            Destroy(gameObject);
        }
    }
}
