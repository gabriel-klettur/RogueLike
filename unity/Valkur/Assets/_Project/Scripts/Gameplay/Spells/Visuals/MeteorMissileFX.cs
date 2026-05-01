using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Single falling meteor: bright fiery streak descending from above the impact
    /// point with trailing embers, finished with a Fire <see cref="ElementalImpactFX"/>
    /// burst + scorch-mark decal at <c>worldImpact</c>.
    /// </summary>
    public class MeteorMissileFX : MonoBehaviour
    {
        private const float FallHeight = 9f;       // world units above target
        private const float FallDuration = 0.55f;
        private const float TrailEmberInterval = 0.012f;

        private Vector3 _start, _target;
        private float _age;
        private float _emberTimer;

        private SpriteRenderer _core, _glow, _halo;
        private GameObject _lightGo;
        private Component _light;

        private System.Action<Vector3> _onImpact;

        public static void Spawn(Vector3 worldImpact, System.Action<Vector3> onImpact)
        {
            var go = new GameObject("MeteorMissile");
            go.transform.position = worldImpact + Vector3.up * FallHeight;
            var fx = go.AddComponent<MeteorMissileFX>();
            fx._target = worldImpact;
            fx._start  = worldImpact + new Vector3(Random.Range(-2f, 2f), FallHeight, 0f);
            fx.transform.position = fx._start;
            fx._onImpact = onImpact;
            fx.Build();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_meteor_fall");
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();
            _halo = MakeChild("Halo", ElementalSprites.Halo, new Color(0.85f, 0.20f, 0.05f, 0.40f), 1.55f, 70);
            _glow = MakeChild("Glow", ElementalSprites.Glow, new Color(1.00f, 0.45f, 0.10f, 0.85f), 0.95f, 71);
            _core = MakeChild("Core", ElementalSprites.HotCore, new Color(1.00f, 0.95f, 0.55f, 1f), 0.45f, 72);

            // Light
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _lightGo = new GameObject("Light");
                _lightGo.transform.SetParent(transform, false);
                try
                {
                    _light = _lightGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, new Color(1f, 0.55f, 0.20f, 1f));
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.6f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 3.5f);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.4f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                }
                catch { _light = null; }
            }
        }

        private SpriteRenderer MakeChild(string name, Sprite sprite, Color color, float scale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            sr.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            sr.sortingOrder = order;
            sr.material = ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / FallDuration);
            // Ease-in for accelerating fall
            float eased = t * t;
            transform.position = Vector3.Lerp(_start, _target, eased);

            // Slight stretch in fall direction
            Vector3 dir = (_target - _start).normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            // Emit trail embers
            _emberTimer -= Time.deltaTime;
            if (_emberTimer <= 0f)
            {
                _emberTimer = TrailEmberInterval;
                SpawnEmber();
            }

            if (t >= 1f)
            {
                Impact();
            }
        }

        private void SpawnEmber()
        {
            var go = new GameObject("Ember");
            go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ElementalSprites.Sparkle;
            sr.color = new Color(1f, Random.Range(0.4f, 0.8f), 0.1f, 0.95f);
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            sr.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            sr.sortingOrder = 60;
            sr.material = ElementalSprites.SharedUnlitMaterial;

            var ember = go.AddComponent<TrailEmber>();
            ember.Init(Random.Range(0.18f, 0.32f), Random.Range(0.10f, 0.20f));
        }

        private void Impact()
        {
            transform.position = _target;
            ElementalImpactFX.Spawn(_target, SpellElement.Fire);
            _onImpact?.Invoke(_target);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_meteor_impact");

            CameraShake.Trigger(0.40f, 0.30f);
            if (_lightGo != null) Destroy(_lightGo);
            Destroy(gameObject);
        }
    }

    /// <summary>Lightweight ember that fades + shrinks then self-destructs.</summary>
    internal class TrailEmber : MonoBehaviour
    {
        private float _life, _age, _scale;
        private SpriteRenderer _sr;

        public void Init(float life, float scale)
        {
            _life = Mathf.Max(0.05f, life);
            _scale = scale;
            transform.localScale = Vector3.one * scale;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / _life;
            if (t >= 1f) { Destroy(gameObject); return; }
            transform.localScale = Vector3.one * _scale * (1f - t * 0.5f);
            if (_sr != null)
            {
                var c = _sr.color;
                c.a = (1f - t);
                _sr.color = c;
            }
        }
    }
}
