using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Controls cone breath: particles + damage ticks in a directional cone from the caster.
    /// Uses LineRenderer to visualize the cone edges + procedural particle emission.
    /// </summary>
    public class ConeBreathController : MonoBehaviour
    {
        private float _remaining;
        private float _arc;
        private float _length;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private Vector2 _direction;
        private Transform _caster;
        private LayerMask _targetLayers;
        private string _element;
        private LineRenderer _lr;
        private ParticleSystem _ps;
        private GameObject _lightGo;
        private Component _light;

        public void Initialize(float duration, float arc, float length, int damagePerTick,
            float tickPeriod, Vector2 direction, Transform caster, LayerMask targetLayers, string element)
        {
            _remaining = duration;
            _arc = arc;
            _length = length;
            _damagePerTick = damagePerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _direction = direction.normalized;
            _caster = caster;
            _targetLayers = targetLayers;
            _element = element;

            SetupVisual();

            // Audio cue at cast
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById(_element == "fire" ? "spell_flame_breath_loop" : "spell_frost_breath_loop");
        }

        private void SetupVisual()
        {
            ElementalSprites.EnsureAll();

            bool fire = _element == "fire";

            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.positionCount = 12;
            _lr.startWidth = 0.12f;
            _lr.endWidth = 0.30f;
            _lr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            _lr.sortingLayerName = SortingConfig.LAYER_VFX;
            _lr.sortingOrder = 4;
            _lr.useWorldSpace = true;

            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.hideFlags = HideFlags.HideAndDontSave;

            Color hotColor = fire
                ? new Color(1.00f, 0.85f, 0.30f, 0.85f)
                : new Color(0.85f, 0.98f, 1.00f, 0.80f);
            Color coolColor = fire
                ? new Color(0.95f, 0.30f, 0.05f, 0.10f)
                : new Color(0.30f, 0.65f, 1.00f, 0.10f);
            mat.color = hotColor;
            _lr.material = mat;
            _lr.startColor = hotColor;
            _lr.endColor = coolColor;

            BuildParticles(fire);
            TryAttachLight(fire);

            UpdateConeVisual();
        }

        private void BuildParticles(bool fire)
        {
            var psGo = new GameObject("BreathParticles");
            psGo.transform.SetParent(transform, false);
            psGo.transform.localPosition = Vector3.zero;
            _ps = psGo.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.duration = 999f;
            main.loop = true;
            main.startLifetime = _length / Mathf.Max(2f, _length * 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_length * 1.4f, _length * 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.20f);
            main.startColor = fire
                ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.40f, 1f), new Color(1f, 0.30f, 0.05f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.98f, 1f, 1f), new Color(0.40f, 0.75f, 1f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.gravityModifier = fire ? -0.2f : 0.1f;

            var emission = _ps.emission;
            emission.rateOverTime = 80f;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = _arc * 0.5f;
            shape.radius = 0.15f;
            shape.radiusThickness = 1f;
            // ConeShape emits along +Z by default; we'll rotate the GO to face direction in Update.

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            if (fire)
            {
                grad.SetKeys(
                    new[] {
                        new GradientColorKey(new Color(1f, 0.95f, 0.55f), 0f),
                        new GradientColorKey(new Color(1f, 0.55f, 0.10f), 0.4f),
                        new GradientColorKey(new Color(0.45f, 0.05f, 0.00f), 1f),
                    },
                    new[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.95f, 0.15f),
                        new GradientAlphaKey(0f, 1f),
                    });
            }
            else
            {
                grad.SetKeys(
                    new[] {
                        new GradientColorKey(new Color(0.95f, 1f, 1f), 0f),
                        new GradientColorKey(new Color(0.45f, 0.85f, 1f), 0.5f),
                        new GradientColorKey(new Color(0.20f, 0.45f, 0.85f), 1f),
                    },
                    new[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.85f, 0.15f),
                        new GradientAlphaKey(0f, 1f),
                    });
            }
            col.color = grad;

            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.6f));

            var psr = _ps.GetComponent<ParticleSystemRenderer>();
            psr.material = ElementalSprites.SharedUnlitMaterial;
            psr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            psr.sortingLayerName = SortingConfig.LAYER_VFX;
            psr.sortingOrder = 5;
        }

        private void TryAttachLight(bool fire)
        {
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType == null) return;
            _lightGo = new GameObject("BreathLight");
            _lightGo.transform.SetParent(transform, false);
            _lightGo.transform.localPosition = Vector3.zero;
            try
            {
                _light = _lightGo.AddComponent(l2dType);
                var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light,
                    fire ? new Color(1f, 0.55f, 0.15f, 1f) : new Color(0.45f, 0.75f, 1f, 1f));
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.8f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _length * 0.7f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.5f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; }
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f || _caster == null)
            {
                CleanupAndDestroy();
                return;
            }

            transform.position = _caster.position;
            UpdateConeVisual();

            // Orient cone-shape particle emitter along _direction
            if (_ps != null)
            {
                float deg = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
                // ConeShape emits along +Z; we rotate the GO so particles emit toward _direction in 2D.
                _ps.transform.rotation = Quaternion.Euler(deg - 90f, 90f, 0f);
            }

            // Animate light flicker
            if (_light != null)
            {
                try
                {
                    float flick = 1.6f + 0.4f * Mathf.PerlinNoise(Time.time * 18f, 0.31f);
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, flick);
                }
                catch { }
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                DamageTick();
                _tickTimer = _tickPeriod;

                // Spawn particle impact VFX along cone
                if (VFXManager.Instance != null)
                {
                    Vector3 midPoint = _caster.position + (Vector3)_direction * _length * 0.6f;
                    VFXManager.Instance.SpawnImpact(midPoint,
                        _element == "fire" ? new Color(1f, 0.5f, 0.15f) : new Color(0.3f, 0.8f, 1f),
                        0.15f, _length * 0.3f);
                }
            }
        }

        private void UpdateConeVisual()
        {
            if (_lr == null || _caster == null) return;

            float halfArc = _arc * 0.5f * Mathf.Deg2Rad;
            float baseAngle = Mathf.Atan2(_direction.y, _direction.x);
            Vector3 origin = _caster.position;

            // Draw a fan shape: origin → left edge → arc → right edge → origin
            int points = _lr.positionCount;
            _lr.SetPosition(0, origin);
            for (int i = 1; i < points - 1; i++)
            {
                float t = (float)(i - 1) / (points - 3);
                float angle = baseAngle - halfArc + t * _arc * Mathf.Deg2Rad;
                float growFactor = Mathf.Clamp01(1.75f - _remaining / 2f); // Cone grows
                float len = _length * Mathf.Clamp01(growFactor);
                _lr.SetPosition(i, origin + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * len);
            }
            _lr.SetPosition(points - 1, origin);

            // Fade alpha over time
            float alpha = Mathf.Clamp01(_remaining) * 0.6f;
            _lr.startColor = new Color(_lr.startColor.r, _lr.startColor.g, _lr.startColor.b, alpha);
        }

        private void DamageTick()
        {
            if (_caster == null) return;

            // Overlap in range and filter by cone angle
            var hits = Physics2D.OverlapCircleAll(_caster.position, _length, _targetLayers);
            float halfArc = _arc * 0.5f;
            float baseAngle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;

            foreach (var hit in hits)
            {
                if (hit.gameObject == _caster.gameObject) continue;
                var health = hit.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                Vector2 toTarget = ((Vector2)hit.transform.position - (Vector2)_caster.position).normalized;
                float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
                float angleDiff = Mathf.Abs(Mathf.DeltaAngle(baseAngle, targetAngle));

                if (angleDiff <= halfArc)
                {
                    health.TakeDamage(_damagePerTick);

                    // Apply burn if fire element
                    if (_element == "fire")
                    {
                        var statusMgr = hit.GetComponent<StatusEffectManager>();
                        if (statusMgr != null)
                            statusMgr.Apply(new BurnEffect(2f, 3));
                    }
                }
            }
        }

        private void CleanupAndDestroy()
        {
            if (_lr != null && _lr.material != null)
                Object.Destroy(_lr.material);
            if (_lightGo != null) Destroy(_lightGo);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_lr != null && _lr.material != null)
                Object.Destroy(_lr.material);
        }
    }
}
