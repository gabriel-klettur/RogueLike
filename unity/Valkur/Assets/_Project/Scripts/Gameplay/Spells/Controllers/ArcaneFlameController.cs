using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Persistent arcane flame zone with epic procedural visuals: pulsing purple
    /// core + glow + halo, rising arcane sparkles, ground rune circle, dynamic
    /// Light2D. Damage ticks every <c>tickPeriod</c> seconds.
    /// </summary>
    public class ArcaneFlameController : MonoBehaviour
    {
        private const float CoreScale = 0.55f;
        private const float GlowScale = 1.10f;
        private const float HaloScale = 1.85f;
        private const float RuneScale = 1.55f;
        private const float RuneSpinSpeed = 35f;
        private const float SparkleEmitRate = 22f;

        private float _remaining;
        private float _radius;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private LayerMask _targetLayers;

        private SpriteRenderer _core, _glow, _halo, _rune;
        private ParticleSystem _sparkles;
        private GameObject _lightGo;
        private Component _light;
        private float _pulsePhase;

        private static readonly Color CoreColor = new Color(0.85f, 0.50f, 1.00f, 1f);
        private static readonly Color GlowColor = new Color(0.55f, 0.20f, 0.95f, 0.65f);
        private static readonly Color HaloColor = new Color(0.35f, 0.10f, 0.85f, 0.30f);
        private static readonly Color RuneColor = new Color(0.95f, 0.65f, 1.00f, 0.85f);
        private static readonly Color LightCol  = new Color(0.75f, 0.35f, 1.00f, 1f);

        public void Initialize(float duration, float radius, int damagePerTick, float tickPeriod, LayerMask targetLayers)
        {
            _remaining = duration;
            _radius = radius;
            _damagePerTick = damagePerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _targetLayers = targetLayers;

            BuildVisual();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_arcane_flame_cast");
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Cleanup();
                Destroy(gameObject);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                DamageTick();
                _tickTimer = _tickPeriod;
                _pulsePhase = 1f;
                var audio = ServiceLocator.Get<IAudioService>();
                if (audio != null) audio.PlaySfxById("spell_arcane_flame_tick", 0.6f);
            }

            AnimateVisuals();
        }

        private void DamageTick()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponentInParent<Health>();
                if (health != null && !health.IsDead)
                    health.TakeDamage(_damagePerTick);
            }
        }

        private void BuildVisual()
        {
            ElementalSprites.EnsureAll();

            _rune = MakeChild("Rune", ElementalSprites.Ring, RuneColor, RuneScale,
                              SortingConfig.LAYER_FLOOR_DECALS, 50);
            _halo = MakeChild("Halo", ElementalSprites.Halo, HaloColor, HaloScale,
                              SortingConfig.LAYER_FLOOR_DECALS, 51);
            _glow = MakeChild("Glow", ElementalSprites.Glow, GlowColor, GlowScale,
                              SortingConfig.LAYER_FLOOR_DECALS, 52);
            _core = MakeChild("Core", ElementalSprites.Core, CoreColor, CoreScale,
                              SortingConfig.LAYER_FLOOR_DECALS, 53);

            transform.localScale = Vector3.one * Mathf.Max(0.5f, _radius);

            BuildSparkles();
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
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private void BuildSparkles()
        {
            var go = new GameObject("Sparkles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            _sparkles = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately, and Unity refuses to accept
            // main.duration on a system that is already playing — it asserts and keeps
            // the old value. Stop first, configure, then Play.
            _sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _sparkles.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = 1.0f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
            main.startColor = new ParticleSystem.MinMaxGradient(CoreColor, RuneColor);
            main.gravityModifier = -0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            var emission = _sparkles.emission;
            emission.rateOverTime = SparkleEmitRate;

            var shape = _sparkles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f;

            var col = _sparkles.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(RuneColor, 0f),
                    new GradientColorKey(CoreColor, 0.5f),
                    new GradientColorKey(GlowColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.9f, 0.2f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var size = _sparkles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 0f));

            var psr = _sparkles.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            psr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_FLOOR_DECALS);
            psr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            psr.sortingOrder = 60;
            psr.sortingFudge = 0.5f;

            _sparkles.Play();
        }

        private void TryAttachLight()
        {
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType == null) return;
            _lightGo = new GameObject("FlameLight");
            _lightGo.transform.SetParent(transform, false);
            _lightGo.transform.localPosition = Vector3.zero;
            try
            {
                _light = _lightGo.AddComponent(l2dType);
                var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, LightCol);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.6f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, Mathf.Max(1f, _radius * 1.4f));
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _radius * 0.4f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; }
        }

        private void AnimateVisuals()
        {
            float t = Time.time;
            float baseFlick = 0.85f + 0.15f * Mathf.PerlinNoise(t * 5f, 0.31f);
            float slow = 1f + 0.05f * Mathf.Sin(t * 1.6f);
            _pulsePhase = Mathf.Max(0f, _pulsePhase - Time.deltaTime * 2.5f);
            float pulse = 1f + 0.4f * _pulsePhase;

            if (_core != null)
            {
                _core.transform.localScale = Vector3.one * CoreScale * baseFlick * pulse;
                var c = CoreColor; c.a = baseFlick;
                _core.color = c;
            }
            if (_glow != null)
            {
                _glow.transform.localScale = Vector3.one * GlowScale * slow * pulse;
                var c = GlowColor; c.a = GlowColor.a * baseFlick;
                _glow.color = c;
            }
            if (_halo != null)
                _halo.transform.localScale = Vector3.one * HaloScale * slow;
            if (_rune != null)
                _rune.transform.localRotation = Quaternion.Euler(0f, 0f, t * RuneSpinSpeed);

            if (_light != null)
            {
                try
                {
                    float intensity = 1.6f * (0.85f + 0.15f * baseFlick) + 1.0f * _pulsePhase;
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, intensity);
                }
                catch { }
            }
        }

        private void Cleanup()
        {
            if (_lightGo != null) Destroy(_lightGo);
            _lightGo = null;
            _light = null;
        }
    }
}
