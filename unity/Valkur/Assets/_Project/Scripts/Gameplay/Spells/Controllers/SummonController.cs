using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Summoned unit with epic spawn-burst VFX (rune ground + halo flash + Light2D pop)
    /// and an ambient Light2D aura that follows it. Auto-destroys on Health death or
    /// duration expiry.
    /// </summary>
    public class SummonController : MonoBehaviour
    {
        private float _remaining;
        private float _duration;
        private Transform _owner;
        private Health _health;
        private SpriteRenderer _sr;

        private SpriteRenderer _aura;
        private GameObject _lightGo;
        private Component _light;

        public void Initialize(float duration, Transform owner)
        {
            _remaining = duration;
            _duration = duration;
            _owner = owner;
            _health = GetComponent<Health>();
            _sr = GetComponentInChildren<SpriteRenderer>();

            BuildVisual();
            SpawnSummonBurst();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_summon_create");
        }

        private void BuildVisual()
        {
            ElementalSprites.EnsureAll();
            // Aura follows summon, behind sprite
            var auraGo = new GameObject("SummonAura");
            auraGo.transform.SetParent(transform, false);
            auraGo.transform.localScale = Vector3.one * 0.9f;
            _aura = auraGo.AddComponent<SpriteRenderer>();
            _aura.sprite = ElementalSprites.Halo;
            _aura.color = new Color(0.85f, 0.55f, 1f, 0.45f);
            _aura.sortingLayerID = SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_FLOOR_DECALS);
            _aura.sortingLayerName = Valkur.Core.SortingConfig.LAYER_FLOOR_DECALS;
            _aura.sortingOrder = 80;
            _aura.sharedMaterial = ElementalSprites.SharedUnlitMaterial;

            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _lightGo = new GameObject("SummonLight");
                _lightGo.transform.SetParent(transform, false);
                try
                {
                    _light = _lightGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 3));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, new Color(0.85f, 0.55f, 1f, 1f));
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.0f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 1.6f);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.3f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                }
                catch { }
            }
        }

        private void SpawnSummonBurst()
        {
            ElementalImpactFX.Spawn(transform.position, SpellElement.Arcane);
            Feel.CameraFeel.Cue(Data.Feel.CameraFeelCue.ImpactMedium, Vector2.up);
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;

            if (_health != null && _health.IsDead)
            {
                if (_lightGo != null) Destroy(_lightGo);
                ElementalImpactFX.Spawn(transform.position, SpellElement.Arcane);
                Destroy(gameObject);
                return;
            }

            if (_remaining <= 0f)
            {
                if (_lightGo != null) Destroy(_lightGo);
                ElementalImpactFX.Spawn(transform.position, SpellElement.Arcane);
                Destroy(gameObject);
                return;
            }

            // Basic follow
            if (_owner != null)
            {
                float dist = Vector2.Distance(transform.position, _owner.position);
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    if (dist > 4f)
                    {
                        Vector2 dir = ((Vector2)_owner.position - (Vector2)transform.position).normalized;
                        rb.velocity = dir * 3f;
                    }
                    else
                    {
                        rb.velocity = Vector2.zero;
                    }
                }
            }

            // Aura pulse + fade
            float t = Time.time;
            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 4f);
            float fade = (_remaining < 2f) ? Mathf.Clamp01(_remaining * 0.5f) : 1f;
            if (_aura != null)
            {
                _aura.transform.localRotation = Quaternion.Euler(0f, 0f, t * 30f);
                var c = _aura.color; c.a = 0.45f * fade * pulse; _aura.color = c;
            }
            if (_sr != null && _remaining < 2f)
            {
                var c = _sr.color; c.a = fade; _sr.color = c;
            }
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.0f * fade * pulse); }
                catch { }
            }
        }
    }
}
