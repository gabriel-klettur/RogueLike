using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Two related impact beats: a narrow pass-through fracture for each pierced body and
    /// a larger directional shatter when the lance finally stops. Keeping them distinct is
    /// what lets the player read the remaining pierce budget without UI text.
    /// </summary>
    internal sealed class IceLanceImpactFX : MonoBehaviour
    {
        private static readonly Color Deep = new Color(0.08f, 0.30f, 0.72f, 1f);
        private static readonly Color Cyan = new Color(0.34f, 0.82f, 1f, 1f);
        private static readonly Color Pale = new Color(0.88f, 0.99f, 1f, 1f);

        private bool _final;
        private float _power;
        private float _duration;
        private float _age;
        private Vector2 _direction;
        private SpriteRenderer _flash;
        private SpriteRenderer _ring;
        private SpriteRenderer _cross;
        private Component _light;

        public static void SpawnPierce(Vector3 position, Vector2 direction, float power)
            => Spawn(position, direction, Mathf.Clamp01(power), final: false);

        public static void SpawnFinal(Vector3 position, Vector2 direction, float power)
            => Spawn(position, direction, Mathf.Clamp01(power), final: true);

        private static void Spawn(Vector3 position, Vector2 direction, float power, bool final)
        {
            var go = new GameObject(final ? "IceLanceFinalShatter" : "IceLancePierceFracture");
            go.transform.position = position;
            var fx = go.AddComponent<IceLanceImpactFX>();
            fx._direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            fx._power = Mathf.Max(0.55f, power);
            fx._final = final;
            fx._duration = final ? 0.46f : 0.24f;
            fx.Build();
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();
            IceSprites.EnsureAll();

            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            _flash = Make("Flash", ElementalSprites.Glow, Pale, 16, true);
            _flash.transform.localScale = Vector3.one * (_final ? 0.38f : 0.22f);

            _ring = Make("RefractionRing", ElementalSprites.Ring,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.86f), 14, true);
            _ring.transform.localScale = Vector3.one * 0.18f;

            _cross = Make("FractureCross", ElementalSprites.SparkleStar,
                new Color(Pale.r, Pale.g, Pale.b, 0.94f), 17, true);
            _cross.transform.localScale = new Vector3(_final ? 0.68f : 0.42f,
                                                       _final ? 0.42f : 0.28f, 1f);

            int count = _final ? 20 : 7;
            Vector2 side = new Vector2(-_direction.y, _direction.x);
            for (int i = 0; i < count; i++)
            {
                float forward = _final ? Random.Range(-0.35f, 1f) : Random.Range(0.15f, 0.75f);
                float lateral = Random.Range(-1f, 1f) * (_final ? 1.7f : 1.1f);
                Vector2 velocity = _direction * forward * Random.Range(2.4f, 5.2f)
                                 + side * lateral * Random.Range(1.1f, 2.8f);
                Color color = Random.value < 0.32f ? Pale : (Random.value < 0.72f ? Cyan : Deep);
                IceLanceShardParticle.Spawn(transform.position, velocity,
                    Random.Range(_final ? 0.07f : 0.045f, _final ? 0.17f : 0.10f) * _power,
                    Random.Range(_final ? 0.34f : 0.18f, _final ? 0.72f : 0.36f),
                    color, additive: Random.value < 0.62f);
            }

            BuildLight();
        }

        private SpriteRenderer Make(string name, Sprite sprite, Color color, int order, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            sr.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            sr.sortingOrder = order;
            return sr;
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;
            var go = new GameObject("ShatterLight");
            go.transform.SetParent(transform, false);
            try
            {
                _light = go.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, Cyan);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(
                    _light, (_final ? 2.8f : 1.65f) * _power);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(
                    _light, (_final ? 2.4f : 1.25f) * _power);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.08f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.86f);
            }
            catch
            {
                _light = null;
                Destroy(go);
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float u = Mathf.Clamp01(_age / _duration);
            float ease = 1f - Mathf.Pow(1f - u, 3f);

            if (_flash != null)
            {
                float from = _final ? 0.34f : 0.20f;
                float to = _final ? 1.55f : 0.75f;
                _flash.transform.localScale = Vector3.one * Mathf.Lerp(from, to, ease) * _power;
                _flash.color = new Color(Pale.r, Pale.g, Pale.b, 0.92f * (1f - u) * (1f - u));
            }
            if (_ring != null)
            {
                float to = _final ? 2.15f : 1.05f;
                _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.16f, to, ease) * _power;
                _ring.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.82f * (1f - u));
            }
            if (_cross != null)
            {
                _cross.transform.localRotation = Quaternion.Euler(0f, 0f, ease * (_final ? 48f : 24f));
                var scale = _cross.transform.localScale;
                _cross.transform.localScale = scale * (1f + Time.deltaTime * (_final ? 3.2f : 1.8f));
                _cross.color = new Color(Pale.r, Pale.g, Pale.b, 0.92f * (1f - ease));
            }
            if (_light != null)
            {
                try
                {
                    float peak = (_final ? 2.8f : 1.65f) * _power;
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(
                        _light, Mathf.Lerp(peak, 0f, ease));
                }
                catch { }
            }

            if (_age >= _duration) Destroy(gameObject);
        }
    }
}
