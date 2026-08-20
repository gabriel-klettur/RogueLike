using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Contact flash for a <see cref="SlashAttack"/>: a hot core, a directional star, an
    /// expanding shock ring and a spray of shards. The spread and the mass come from the
    /// slash style, so a thrust punches a tight cone of sparks forward while a whirl throws
    /// a broad, slow curtain of them.
    ///
    /// It runs on unscaled time on purpose. The hit-stop freezes the attacker and the world
    /// while the contact point keeps blooming, which is what makes the freeze read as
    /// impact rather than as a hitch.
    /// </summary>
    internal sealed class SlashImpactBurst : MonoBehaviour
    {
        private const float LIFETIME = 0.26f;

        private float _age;
        private SlashProfile _profile;
        private SpriteRenderer _core;
        private SpriteRenderer _star;
        private SpriteRenderer _ring;
        private Transform[] _shardTransforms;
        private SpriteRenderer[] _shardRenderers;
        private Vector2[] _velocities;
        private float[] _spin;
        private float _ringPeak;
        private Component _light;

        public static void Spawn(Vector2 position, Vector2 direction, SlashProfile profile)
        {
            var go = new GameObject("SlashImpactBurst");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            var burst = go.AddComponent<SlashImpactBurst>();
            burst._profile = profile;
            burst.Build();
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();

            ResolveStyleBudget(out int shardCount, out float lateralSpread,
                               out float forwardSpeed, out float ringPeak);
            _ringPeak = ringPeak;

            _core = Create("CoreFlash", ElementalSprites.HotCore, _profile.Rim, 74);
            _star = Create("ContactStar", ElementalSprites.SparkleStar, _profile.Rim, 76);
            _ring = Create("ShockRing", ElementalSprites.Ring,
                           SlashProfile.WithAlpha(_profile.Edge, 0.9f), 73);
            _core.transform.localScale = Vector3.one * 0.72f;
            _star.transform.localScale = new Vector3(1.15f, 0.52f, 1f);
            _ring.transform.localScale = Vector3.one * 0.18f;

            _shardTransforms = new Transform[shardCount];
            _shardRenderers = new SpriteRenderer[shardCount];
            _velocities = new Vector2[shardCount];
            _spin = new float[shardCount];

            for (int i = 0; i < shardCount; i++)
            {
                var shard = Create("Shard_" + i.ToString("00"), ElementalSprites.Sparkle,
                    Color.Lerp(_profile.Edge, _profile.Rim, Random.Range(0.25f, 1f)), 75);
                float size = Random.Range(0.055f, 0.14f);
                shard.transform.localScale = new Vector3(size * Random.Range(2.2f, 4.2f), size, 1f);
                _velocities[i] = new Vector2(
                    Random.Range(forwardSpeed * 0.3f, forwardSpeed),
                    Random.Range(-lateralSpread, lateralSpread));
                _spin[i] = Random.Range(-420f, 420f);
                _shardTransforms[i] = shard.transform;
                _shardRenderers[i] = shard;
            }

            BuildLight();
        }

        /// <summary>
        /// How much debris the contact throws and how far it carries. A thrust concentrates
        /// everything forward; a whirl spends the same energy sideways.
        /// </summary>
        private void ResolveStyleBudget(out int shardCount, out float lateralSpread,
                                        out float forwardSpeed, out float ringPeak)
        {
            switch (_profile.Style)
            {
                case SlashStyle.Thrust:
                    shardCount = 10; lateralSpread = 1.1f; forwardSpeed = 6.4f; ringPeak = 1.15f;
                    break;
                case SlashStyle.Cleave:
                    shardCount = 16; lateralSpread = 3.4f; forwardSpeed = 4.2f; ringPeak = 1.95f;
                    break;
                case SlashStyle.Whirl:
                    shardCount = 20; lateralSpread = 4.4f; forwardSpeed = 3.4f; ringPeak = 2.35f;
                    break;
                default:
                    shardCount = 12; lateralSpread = 2.7f; forwardSpeed = 4.2f; ringPeak = 1.55f;
                    break;
            }
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
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _profile.LightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 1.5f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.08f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.78f);
            }
            catch { _light = null; }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _age += dt;
            float t = Mathf.Clamp01(_age / LIFETIME);
            float fade = 1f - t;
            float sharpFade = fade * fade;

            if (_core != null)
            {
                _core.transform.localScale = Vector3.one * Mathf.Lerp(0.72f, 0.16f, t);
                _core.color = SlashProfile.WithAlpha(_profile.Rim, sharpFade);
            }
            if (_star != null)
            {
                _star.transform.localScale = new Vector3(
                    Mathf.Lerp(1.15f, 1.75f, t), Mathf.Lerp(0.52f, 0.08f, t), 1f);
                _star.color = SlashProfile.WithAlpha(_profile.Rim, sharpFade);
            }
            if (_ring != null)
            {
                _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.18f, _ringPeak, t);
                _ring.color = SlashProfile.WithAlpha(_profile.Edge, fade * 0.78f);
            }

            for (int i = 0; i < _shardTransforms.Length; i++)
            {
                _shardTransforms[i].localPosition += (Vector3)(_velocities[i] * dt);
                _shardTransforms[i].Rotate(0f, 0f, _spin[i] * dt);
                _velocities[i] *= Mathf.Pow(0.035f, dt);
                _shardRenderers[i].color =
                    SlashProfile.WithAlpha(_shardRenderers[i].color, sharpFade);
            }

            if (_light != null)
            {
                try
                {
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(
                        _light, 2.4f * sharpFade);
                }
                catch { /* URP 2D lighting absent in this project configuration. */ }
            }

            if (_age >= LIFETIME) Destroy(gameObject);
        }
    }
}
