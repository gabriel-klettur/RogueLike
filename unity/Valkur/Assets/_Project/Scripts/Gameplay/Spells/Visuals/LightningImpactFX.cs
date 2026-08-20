using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Where a bolt lands: a white flash, a cross-shaped glare, an expanding shock ring and
    /// sparks thrown in every direction.
    ///
    /// The strike previously reused the generic <c>SpawnImpact</c> puff, which is a soft
    /// coloured blob — it reads as something being extinguished rather than as several
    /// thousand volts arriving. An electrical discharge scatters, so its sparks are radial
    /// rather than directional, and the flash is white before it is any colour at all.
    /// </summary>
    internal sealed class LightningImpactFX : MonoBehaviour
    {
        private const float LIFETIME = 0.30f;
        private const int SPARK_COUNT = 16;

        private float _age;
        private float _scale;
        private Color _tint;
        private SpriteRenderer _flash;
        private SpriteRenderer _glare;
        private SpriteRenderer _ring;
        private Transform[] _sparkTransforms;
        private SpriteRenderer[] _sparkRenderers;
        private Vector2[] _velocities;
        private Component _light;

        public static void Spawn(Vector3 position, Color tint, float scale = 1f)
        {
            var go = new GameObject("LightningImpactFX");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            var fx = go.AddComponent<LightningImpactFX>();
            fx._tint = tint;
            fx._scale = Mathf.Max(0.3f, scale);
            fx.Build();
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();

            _flash = Create("Flash", ElementalSprites.HotCore, Color.white, 78);
            _glare = Create("Glare", ElementalSprites.SparkleStar, Color.white, 79);
            _ring = Create("ShockRing", ElementalSprites.Ring, _tint, 77);
            _flash.transform.localScale = Vector3.one * 0.9f * _scale;
            _glare.transform.localScale = Vector3.one * 1.5f * _scale;
            _ring.transform.localScale = Vector3.one * 0.2f * _scale;

            _sparkTransforms = new Transform[SPARK_COUNT];
            _sparkRenderers = new SpriteRenderer[SPARK_COUNT];
            _velocities = new Vector2[SPARK_COUNT];

            for (int i = 0; i < SPARK_COUNT; i++)
            {
                var spark = Create("Spark_" + i.ToString("00"), ElementalSprites.Sparkle,
                    Color.Lerp(_tint, Color.white, Random.Range(0.3f, 1f)), 78);
                float size = Random.Range(0.05f, 0.12f) * _scale;
                spark.transform.localScale = new Vector3(size * Random.Range(2f, 4f), size, 1f);

                // Even angular coverage with jitter: a clean ring of sparks looks stamped,
                // pure randomness leaves gaps that read as missing particles.
                float angle = (i + Random.Range(-0.35f, 0.35f)) * (360f / SPARK_COUNT);
                float speed = Random.Range(3.2f, 7.5f) * _scale;
                float radians = angle * Mathf.Deg2Rad;
                _velocities[i] = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * speed;
                spark.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                _sparkTransforms[i] = spark.transform;
                _sparkRenderers[i] = spark;
            }

            BuildLight();
        }

        private SpriteRenderer Create(string objectName, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;
            try
            {
                _light = gameObject.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 2));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _tint);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 3.2f * _scale);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.2f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.8f);
            }
            catch { _light = null; }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            float t = Mathf.Clamp01(_age / LIFETIME);
            float fade = 1f - t;
            float sharp = fade * fade;

            if (_flash != null)
            {
                _flash.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 0.2f, t) * _scale;
                _flash.color = new Color(1f, 1f, 1f, sharp);
            }
            if (_glare != null)
            {
                float stretch = Mathf.Lerp(1.5f, 2.6f, t) * _scale;
                _glare.transform.localScale = new Vector3(stretch, stretch * Mathf.Lerp(1f, 0.25f, t), 1f);
                _glare.color = new Color(1f, 1f, 1f, sharp);
            }
            if (_ring != null)
            {
                _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 2.4f, t) * _scale;
                _ring.color = new Color(_tint.r, _tint.g, _tint.b, fade * 0.7f);
            }

            for (int i = 0; i < _sparkTransforms.Length; i++)
            {
                _sparkTransforms[i].localPosition += (Vector3)(_velocities[i] * dt);
                _velocities[i] *= Mathf.Pow(0.04f, dt);
                Color c = _sparkRenderers[i].color;
                c.a = sharp;
                _sparkRenderers[i].color = c;
            }

            if (_light != null)
            {
                try
                {
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 4.2f * sharp);
                }
                catch { /* URP 2D lighting absent in this project configuration. */ }
            }

            if (_age >= LIFETIME) Destroy(gameObject);
        }
    }
}
