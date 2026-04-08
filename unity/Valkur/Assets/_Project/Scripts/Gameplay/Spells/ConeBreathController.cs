using UnityEngine;
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
        }

        private void SetupVisual()
        {
            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.positionCount = 12;
            _lr.startWidth = 0.1f;
            _lr.endWidth = 0.1f;
            _lr.sortingLayerName = "VFX";
            _lr.sortingOrder = 4;
            _lr.useWorldSpace = true;

            // Use default sprite material
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.hideFlags = HideFlags.HideAndDontSave;

            Color coneColor = _element == "fire"
                ? new Color(1f, 0.4f, 0.05f, 0.6f)
                : new Color(0.4f, 0.9f, 1f, 0.6f);
            mat.color = coneColor;
            _lr.material = mat;
            _lr.startColor = coneColor;
            _lr.endColor = new Color(coneColor.r, coneColor.g, coneColor.b, 0.1f);

            UpdateConeVisual();
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
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_lr != null && _lr.material != null)
                Object.Destroy(_lr.material);
        }
    }
}
