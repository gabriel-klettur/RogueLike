using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Instant crystalline release at the caster's hand. Ice Lance has no mechanical
    /// wind-up, so this short-lived snap supplies anticipation without delaying the cast.
    /// </summary>
    internal sealed class IceLanceCastFX : MonoBehaviour
    {
        private const float Duration = 0.24f;

        private static readonly Color Cyan = new Color(0.36f, 0.84f, 1f, 1f);
        private static readonly Color Pale = new Color(0.86f, 0.99f, 1f, 1f);

        private SpriteRenderer _flash;
        private SpriteRenderer _ring;
        private SpriteRenderer _line;
        private Component _light;
        private Vector2 _direction;
        private float _age;

        public static void Spawn(Vector3 position, Vector2 direction)
        {
            var go = new GameObject("IceLanceCastFX");
            go.transform.position = position;
            var fx = go.AddComponent<IceLanceCastFX>();
            fx._direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            fx.Build();
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();
            IceSprites.EnsureAll();

            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            _flash = Make("Flash", ElementalSprites.Glow,
                new Color(0.55f, 0.92f, 1f, 0.82f), 13, true);
            _flash.transform.localScale = new Vector3(0.34f, 0.34f, 1f);

            _ring = Make("FreezeRing", ElementalSprites.Ring,
                new Color(0.55f, 0.91f, 1f, 0.82f), 11, true);
            _ring.transform.localScale = Vector3.one * 0.18f;

            _line = Make("DirectionalFlash", ElementalSprites.Core,
                new Color(0.92f, 1f, 1f, 0.95f), 14, true);
            _line.transform.localPosition = new Vector3(0.34f, 0f, 0f);
            _line.transform.localScale = new Vector3(0.62f, 0.075f, 1f);

            Vector2 side = new Vector2(-_direction.y, _direction.x);
            for (int i = 0; i < 9; i++)
            {
                float along = Random.Range(-0.28f, 0.18f);
                float lateral = Random.Range(-0.34f, 0.34f);
                Vector3 pos = transform.position + (Vector3)(_direction * along + side * lateral);
                Vector2 vel = _direction * Random.Range(0.4f, 1.5f)
                            - side * Mathf.Sign(lateral) * Random.Range(0.15f, 0.75f);
                IceLanceShardParticle.Spawn(pos, vel, Random.Range(0.055f, 0.12f),
                    Random.Range(0.18f, 0.34f), Random.value > 0.45f ? Pale : Cyan, true);
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
            var go = new GameObject("CastLight");
            go.transform.SetParent(transform, false);
            try
            {
                _light = go.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, Cyan);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.25f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 1.75f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.08f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.88f);
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
            float u = Mathf.Clamp01(_age / Duration);
            float ease = 1f - Mathf.Pow(1f - u, 3f);

            if (_flash != null)
            {
                _flash.transform.localScale = Vector3.one * Mathf.Lerp(0.28f, 1.15f, ease);
                var c = _flash.color;
                _flash.color = new Color(c.r, c.g, c.b, 0.84f * (1f - u) * (1f - u));
            }
            if (_ring != null)
            {
                _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.16f, 1.25f, ease);
                _ring.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.78f * (1f - u));
            }
            if (_line != null)
            {
                _line.transform.localPosition = new Vector3(Mathf.Lerp(0.18f, 0.70f, ease), 0f, 0f);
                _line.transform.localScale = new Vector3(Mathf.Lerp(0.32f, 0.95f, ease), 0.075f * (1f - u), 1f);
                _line.color = new Color(Pale.r, Pale.g, Pale.b, 0.94f * (1f - u));
            }
            if (_light != null)
            {
                try
                {
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(
                        _light, Mathf.Lerp(2.25f, 0f, ease));
                }
                catch { }
            }

            if (_age >= Duration) Destroy(gameObject);
        }
    }
}
