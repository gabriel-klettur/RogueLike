using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Purpose-built Ice Lance flight rig. Unlike a ball projectile, its identity depends on
    /// a rigid directional silhouette: a faceted spear remains readable while the softer
    /// glow, contrail and chips communicate speed around it.
    /// </summary>
    public sealed class IceLanceProjectileVisual : MonoBehaviour, IProjectileVisual
    {
        private static readonly Color Deep       = new Color(0.10f, 0.34f, 0.76f, 0.96f);
        private static readonly Color Ice        = new Color(0.38f, 0.82f, 1.00f, 1.00f);
        private static readonly Color Pale       = new Color(0.82f, 0.98f, 1.00f, 1.00f);
        private static readonly Color WhiteHot   = new Color(0.96f, 1.00f, 1.00f, 1.00f);

        private const float BladeLength = 1.58f;
        private const float BladeWidth = 0.34f;
        private const float RearX = -0.70f;

        private Transform _rig;
        private SpriteRenderer _body;
        private SpriteRenderer _rim;
        private SpriteRenderer _facet;
        private SpriteRenderer _beam;
        private SpriteRenderer _tip;
        private SpriteRenderer _halo;
        private TrailRenderer _outerTrail;
        private TrailRenderer _innerTrail;
        private Component _light;
        private float _seed;
        private float _chipClock;
        private float _power = 1f;
        private bool _built;
        private bool _impacted;
        private Vector3 _lastPosition;
        private Vector2 _travelDirection = Vector2.right;

        public Vector2 TravelDirection => _travelDirection;

        public void Configure(SpellDefinition spell)
        {
            EnsureBuilt();
            _power = 1f;
            ApplyPower();
        }

        /// <summary>Called by the piercing mechanic after one body has been crossed.</summary>
        public void OnPierced(Vector3 contact, int remaining, int total)
        {
            float fraction = total > 0 ? Mathf.Clamp01(remaining / (float)total) : 0f;
            _power = Mathf.Lerp(0.68f, 1f, fraction);
            ApplyPower();
            IceLanceImpactFX.SpawnPierce(contact, _travelDirection, _power);
        }

        public void OnImpact(Vector3 worldPos)
        {
            if (_impacted) return;
            _impacted = true;
            IceLanceImpactFX.SpawnFinal(worldPos, _travelDirection, _power);
        }

        private void Awake()
        {
            EnsureBuilt();
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null) rootSr.enabled = false;
        }

        private void OnEnable()
        {
            EnsureBuilt();
            _impacted = false;
            _power = 1f;
            _seed = Random.Range(0f, 100f);
            _chipClock = 0f;
            _lastPosition = transform.position;
            if (_outerTrail != null) { _outerTrail.Clear(); _outerTrail.emitting = true; }
            if (_innerTrail != null) { _innerTrail.Clear(); _innerTrail.emitting = true; }
            ApplyPower();
        }

        private void OnDisable()
        {
            if (_outerTrail != null) { _outerTrail.emitting = false; _outerTrail.Clear(); }
            if (_innerTrail != null) { _innerTrail.emitting = false; _innerTrail.Clear(); }
        }

        private void Update()
        {
            if (!_built || _impacted) return;

            Vector3 delta = transform.position - _lastPosition;
            if (delta.sqrMagnitude > 0.000001f)
                _travelDirection = ((Vector2)delta).normalized;
            if (_rig != null)
                _rig.rotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(_travelDirection.y, _travelDirection.x) * Mathf.Rad2Deg);

            float t = Time.time + _seed;
            float shimmer = 0.72f + 0.28f * Mathf.Sin(t * 28f);
            float pulse = 0.92f + 0.08f * Mathf.Sin(t * 17f + 0.8f);

            if (_facet != null)
                _facet.color = new Color(Pale.r, Pale.g, Pale.b, (0.46f + 0.34f * shimmer) * _power);
            if (_rim != null)
                _rim.color = new Color(WhiteHot.r, WhiteHot.g, WhiteHot.b, (0.72f + 0.20f * shimmer) * _power);
            if (_tip != null)
            {
                _tip.transform.localRotation = Quaternion.Euler(0f, 0f, t * 95f);
                _tip.transform.localScale = Vector3.one * (0.19f + 0.035f * shimmer) * _power;
            }
            if (_halo != null)
                _halo.transform.localScale = new Vector3(1.65f * pulse, 0.50f * pulse, 1f);
            if (_beam != null)
                _beam.transform.localScale = new Vector3(1.18f * pulse, 0.095f, 1f);

            if (_light != null)
            {
                try
                {
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(
                        _light, (1.45f + 0.25f * shimmer) * _power);
                }
                catch { }
            }

            _chipClock -= Time.deltaTime;
            if (delta.sqrMagnitude > 0.0001f && _chipClock <= 0f)
            {
                _chipClock = 0.032f;
                SpawnTrailChip();
            }

            _lastPosition = transform.position;
        }

        private void EnsureBuilt()
        {
            if (_built && _rig != null) return;
            IceSprites.EnsureAll();
            ElementalSprites.EnsureAll();
            BuildRig();
            _built = true;
        }

        private void BuildRig()
        {
            var rigGo = new GameObject("IceLanceRig");
            rigGo.transform.SetParent(transform, false);
            _rig = rigGo.transform;

            _halo = MakeSprite(_rig, "GlacialHalo", ElementalSprites.Glow,
                new Color(0.18f, 0.67f, 1f, 0.30f), 0, true);
            _halo.transform.localPosition = new Vector3(-0.02f, 0f, 0f);
            _halo.transform.localScale = new Vector3(1.65f, 0.50f, 1f);

            _beam = MakeSprite(_rig, "WhiteCore", ElementalSprites.Core,
                new Color(0.80f, 0.98f, 1f, 0.72f), 4, true);
            _beam.transform.localPosition = new Vector3(-0.02f, 0.015f, 0f);
            _beam.transform.localScale = new Vector3(1.18f, 0.095f, 1f);

            var shaft = MakeShardAnchor(_rig, "MainCrystal", new Vector3(RearX, 0f, 0f), -90f);
            _body = MakeShardLayer(shaft, "Body", IceSprites.Body(2), Color.white, 2, false,
                                   BladeWidth, BladeLength);
            _facet = MakeShardLayer(shaft, "Facet", IceSprites.Facet(2), Pale, 5, true,
                                    BladeWidth, BladeLength);
            _rim = MakeShardLayer(shaft, "Rim", IceSprites.Rim(2), WhiteHot, 6, true,
                                  BladeWidth, BladeLength);

            // Two shorter rear fins turn a single icicle into a deliberately forged spear.
            BuildFin(new Vector3(-0.58f, 0.11f, 0f), -73f, 0.18f, 0.62f, 1);
            BuildFin(new Vector3(-0.58f, -0.11f, 0f), -107f, 0.18f, 0.62f, 1);

            _tip = MakeSprite(_rig, "TipGlint", ElementalSprites.SparkleStar,
                WhiteHot, 8, true);
            _tip.transform.localPosition = new Vector3(RearX + BladeLength - 0.02f, 0f, 0f);
            _tip.transform.localScale = Vector3.one * 0.20f;

            _outerTrail = BuildTrail("OuterTrail", new Vector3(RearX + 0.05f, 0f, 0f),
                0.16f, 0.31f,
                new Color(0.18f, 0.64f, 1f, 0.68f), new Color(0.04f, 0.18f, 0.52f, 0f), 0);
            _innerTrail = BuildTrail("InnerTrail", new Vector3(RearX + 0.08f, 0f, 0f),
                0.095f, 0.095f,
                new Color(0.90f, 1f, 1f, 0.92f), new Color(0.20f, 0.64f, 1f, 0f), 3);

            BuildLight();
        }

        private void BuildFin(Vector3 localPos, float angle, float width, float length, int order)
        {
            var anchor = MakeShardAnchor(_rig, "RearFin", localPos, angle);
            MakeShardLayer(anchor, "Body", IceSprites.Body(4), new Color(0.72f, 0.93f, 1f, 0.92f),
                order, false, width, length);
            MakeShardLayer(anchor, "Rim", IceSprites.Rim(4), Pale,
                order + 1, true, width, length);
        }

        private static Transform MakeShardAnchor(Transform parent, string name, Vector3 pos, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            return go.transform;
        }

        private static SpriteRenderer MakeShardLayer(Transform parent, string name, Sprite sprite,
            Color color, int order, bool additive, float width, float height)
        {
            var sr = MakeSprite(parent, name, sprite, color, order, additive);
            IceSprites.ScaleShard(sr.transform, width, height);
            return sr;
        }

        private static SpriteRenderer MakeSprite(Transform parent, string name, Sprite sprite,
            Color color, int order, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
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

        private TrailRenderer BuildTrail(string name, Vector3 localPos, float time, float width,
            Color start, Color end, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_rig, false);
            go.transform.localPosition = localPos;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = time;
            trail.minVertexDistance = 0.035f;
            trail.widthMultiplier = width;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.16f, 0.72f), new Keyframe(1f, 1f));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(end, 0f),
                    new GradientColorKey(start, 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(start.a, 1f),
                },
            };
            trail.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 1;
            trail.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            trail.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            trail.sortingOrder = order;
            return trail;
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;
            var go = new GameObject("IceLanceLight");
            go.transform.SetParent(_rig, false);
            go.transform.localPosition = new Vector3(0.04f, 0f, 0f);
            try
            {
                _light = go.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, new Color(0.42f, 0.86f, 1f));
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.55f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 2.15f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.12f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.82f);
            }
            catch
            {
                _light = null;
                Destroy(go);
            }
        }

        private void SpawnTrailChip()
        {
            if (_rig == null) return;
            Vector2 side = new Vector2(-_travelDirection.y, _travelDirection.x);
            Vector3 origin = transform.position
                           - (Vector3)_travelDirection * 0.48f
                           + (Vector3)side * Random.Range(-0.13f, 0.13f);
            Vector2 velocity = -_travelDirection * Random.Range(0.30f, 1.20f)
                             + side * Random.Range(-0.75f, 0.75f);
            IceLanceShardParticle.Spawn(origin, velocity, Random.Range(0.045f, 0.095f),
                Random.Range(0.18f, 0.30f), Ice, additive: true);
        }

        private void ApplyPower()
        {
            if (_body != null)
                _body.color = Color.Lerp(Deep, Color.white, 0.72f * _power);
            if (_outerTrail != null) _outerTrail.widthMultiplier = 0.31f * _power;
            if (_innerTrail != null) _innerTrail.widthMultiplier = 0.095f * _power;
        }
    }

    /// <summary>One tiny angular fragment shared by flight, cast and impact.</summary>
    internal sealed class IceLanceShardParticle : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Vector2 _velocity;
        private float _life;
        private float _age;
        private float _spin;
        private float _size;
        private Color _color;

        public static IceLanceShardParticle Spawn(Vector3 position, Vector2 velocity,
            float size, float lifetime, Color color, bool additive)
        {
            var go = new GameObject("IceLanceShard");
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = IceSprites.Debris;
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            sr.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            sr.sortingOrder = 7;
            sr.color = color;
            var particle = go.AddComponent<IceLanceShardParticle>();
            particle._renderer = sr;
            particle._velocity = velocity;
            particle._life = Mathf.Max(0.05f, lifetime);
            particle._size = size;
            particle._color = color;
            particle._spin = Random.Range(-420f, 420f);
            go.transform.localScale = Vector3.one * size;
            return particle;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float u = Mathf.Clamp01(_age / _life);
            transform.position += (Vector3)(_velocity * Time.deltaTime);
            _velocity *= Mathf.Exp(-3.5f * Time.deltaTime);
            _velocity += Vector2.down * (0.65f * Time.deltaTime);
            transform.Rotate(0f, 0f, _spin * Time.deltaTime);
            transform.localScale = Vector3.one * (_size * Mathf.Lerp(1f, 0.18f, u));
            if (_renderer != null)
                _renderer.color = new Color(_color.r, _color.g, _color.b,
                    _color.a * (1f - u) * (1f - u));
            if (_age >= _life) Destroy(gameObject);
        }
    }
}
