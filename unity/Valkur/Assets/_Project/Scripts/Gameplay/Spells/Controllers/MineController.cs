using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Placed mine: arming phase (pulsing yellow rune) â†’ armed (steady red glow + warning ring) â†’
    /// proximity detonate (massive ElementalImpactFX with Fire palette + camera shake + light flash).
    /// </summary>
    public class MineController : MonoBehaviour
    {
        // â”€â”€ Tuning â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private const float ArmingPulseHz = 4f;
        private const float ArmedRingSpinSpeed = 90f;
        private const float CoreScale = 0.35f;
        private const float GlowScale = 0.85f;
        private const float RingScale = 1.20f;
        private const float HaloScale = 1.55f;

        // â”€â”€ State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private float _armingTimer;
        private float _triggerRadius;
        private float _explosionRadius;
        private int _explosionDamage;
        private float _ttl;
        private LayerMask _targetLayers;
        private string _impactPreset;
        private bool _armed;

        // â”€â”€ Visual rig â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private SpriteRenderer _core, _glow, _ring, _halo;
        private GameObject _lightGo;
        private Component _light;

        // â”€â”€ Palette â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static readonly Color ArmingColor   = new Color(1.00f, 0.85f, 0.20f, 1f);
        private static readonly Color ArmedCore     = new Color(1.00f, 0.30f, 0.10f, 1f);
        private static readonly Color ArmedGlow     = new Color(1.00f, 0.15f, 0.05f, 0.70f);
        private static readonly Color ArmedHalo     = new Color(0.65f, 0.05f, 0.00f, 0.30f);
        private static readonly Color ArmedRing     = new Color(1.00f, 0.40f, 0.15f, 0.85f);

        public void Initialize(float armingTime, float triggerRadius, float explosionRadius,
            int explosionDamage, float ttl, LayerMask targetLayers, string impactPreset)
        {
            _armingTimer = armingTime;
            _triggerRadius = triggerRadius;
            _explosionRadius = explosionRadius;
            _explosionDamage = explosionDamage;
            _ttl = ttl;
            _targetLayers = targetLayers;
            _impactPreset = impactPreset;

            BuildVisual();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_mine_arm");
        }

        private void Update()
        {
            _ttl -= Time.deltaTime;
            if (_ttl <= 0f)
            {
                Cleanup();
                Destroy(gameObject);
                return;
            }

            if (!_armed)
            {
                _armingTimer -= Time.deltaTime;
                AnimateArming();
                if (_armingTimer <= 0f)
                {
                    _armed = true;
                    SwitchToArmedColors();
                    var audio = ServiceLocator.Get<IAudioService>();
                    if (audio != null) audio.PlaySfxById("spell_mine_armed");
                }
                return;
            }

            AnimateArmed();

            // Proximity check
            var hits = Physics2D.OverlapCircleAll(transform.position, _triggerRadius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                {
                    Detonate();
                    return;
                }
            }
        }

        // â”€â”€ Build â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void BuildVisual()
        {
            ElementalSprites.EnsureAll();

            _halo = MakeChild("Halo", ElementalSprites.Halo, ArmedHalo, HaloScale,
                              SortingConfig.LAYER_FLOOR_DECALS, 50);
            _ring = MakeChild("Ring", ElementalSprites.Ring, ArmingColor, RingScale,
                              SortingConfig.LAYER_FLOOR_DECALS, 51);
            _glow = MakeChild("Glow", ElementalSprites.Glow, ArmingColor, GlowScale,
                              SortingConfig.LAYER_FLOOR_DECALS, 52);
            _core = MakeChild("Core", ElementalSprites.HotCore, ArmingColor, CoreScale,
                              SortingConfig.LAYER_FLOOR_DECALS, 53);

            TryAttachLight();

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        private SpriteRenderer MakeChild(string name, Sprite sprite, Color color, float scale, string layer, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerID = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            sr.material = ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private void TryAttachLight()
        {
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType == null) return;
            _lightGo = new GameObject("MineLight");
            _lightGo.transform.SetParent(transform, false);
            _lightGo.transform.localPosition = Vector3.zero;
            try
            {
                _light = _lightGo.AddComponent(l2dType);
                var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, ArmingColor);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 0.8f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _triggerRadius * 1.6f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _triggerRadius * 0.3f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; }
        }

        private void SwitchToArmedColors()
        {
            if (_core != null) _core.color = ArmedCore;
            if (_glow != null) _glow.color = ArmedGlow;
            if (_ring != null) _ring.color = ArmedRing;
            if (_halo != null) _halo.color = ArmedHalo;
            if (_light != null)
            {
                try
                {
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, ArmedCore);
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.4f);
                }
                catch { }
            }
        }

        // â”€â”€ Animate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void AnimateArming()
        {
            float t = Time.time;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * ArmingPulseHz);
            float scale = 0.85f + 0.25f * pulse;
            if (_core != null)
            {
                _core.transform.localScale = Vector3.one * CoreScale * scale;
                var c = ArmingColor; c.a = 0.5f + 0.5f * pulse;
                _core.color = c;
            }
            if (_glow != null)
                _glow.transform.localScale = Vector3.one * GlowScale * (0.95f + 0.1f * pulse);
            if (_ring != null)
                _ring.transform.localRotation = Quaternion.Euler(0f, 0f, t * 60f);
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 0.4f + 0.8f * pulse); }
                catch { }
            }
        }

        private void AnimateArmed()
        {
            float t = Time.time;
            float fastPulse = 0.7f + 0.3f * Mathf.Sin(t * Mathf.PI * 2f * 5.5f);
            if (_core != null)
                _core.transform.localScale = Vector3.one * CoreScale * (0.9f + 0.25f * fastPulse);
            if (_glow != null)
                _glow.transform.localScale = Vector3.one * GlowScale * (1f + 0.15f * fastPulse);
            if (_ring != null)
                _ring.transform.localRotation = Quaternion.Euler(0f, 0f, t * ArmedRingSpinSpeed);
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.2f + 0.6f * fastPulse); }
                catch { }
            }
        }

        // â”€â”€ Detonate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void Detonate()
        {

            var hits = Physics2D.OverlapCircleAll(transform.position, _explosionRadius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                    health.TakeDamage(_explosionDamage);
            }

            // Epic explosion FX
            ElementalImpactFX.Spawn(transform.position, SpellElement.Fire);

            // Big secondary shockwave scaled to explosion radius
            CameraShake.Trigger(0.45f, 0.35f);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_mine_explode");

            if (VFXManager.Instance != null)
            {
                if (!string.IsNullOrEmpty(_impactPreset))
                    VFXManager.Instance.SpawnParticlePreset(_impactPreset, transform.position);
                VFXManager.Instance.SpawnAreaIndicator(transform.position,
                    new Color(1f, 0.4f, 0.1f, 0.7f), _explosionRadius, 0.5f);
            }

            Cleanup();
            Destroy(gameObject);
        }

        private void Cleanup()
        {
            if (_lightGo != null) Destroy(_lightGo);
            _lightGo = null;
            _light = null;
        }
    }
}
