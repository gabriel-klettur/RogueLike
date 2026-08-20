using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Authored player slash whose rendering and damage share one moving sector.
    /// The leading edge crosses targets from -arc/2 to +arc/2; each target receives
    /// damage, impact feedback and hit reporting on the exact frame it is crossed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RegularSlashAttack : MonoBehaviour
    {
        public const string SpellKey = "slash_regular";
        public const float SweepDuration = 0.14f;
        public const float MinimumTotalDuration = 0.30f;

        private const int RibbonSegments = 40;
        private const int AirMoteCount = 9;
        private const float TrailWindow = 0.72f;

        private readonly HashSet<Health> _damaged = new HashSet<Health>();
        private readonly List<SlashRibbon> _ribbons = new List<SlashRibbon>(4);

        private SpellContext _context;
        private Vector2 _direction;
        private float _radius;
        private float _arc;
        private float _halfArc;
        private float _age;
        private float _totalDuration;
        private float _previousHeadAngle;
        private int _hitCount;
        private Color _baseColor;
        private Material _material;
        private SpriteRenderer _leadingGlint;
        private SpriteRenderer _originRing;
        private Transform[] _moteTransforms;
        private SpriteRenderer[] _moteRenderers;
        private Component _light;
        private GameObject _lightGo;

        public static bool Matches(SpellDefinition spell)
            => spell != null && string.Equals(spell.spellKey, SpellKey,
                System.StringComparison.OrdinalIgnoreCase);

        public static void Spawn(SpellContext context, Vector2 origin, float radius,
                                 float arcDegrees, Color color)
        {
            var go = new GameObject("RegularSlashAttack");
            go.transform.position = origin;

            Vector2 direction = context.Direction.sqrMagnitude > 0.0001f
                ? context.Direction.normalized
                : Vector2.right;
            go.transform.rotation = Quaternion.Euler(
                0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

            var attack = go.AddComponent<RegularSlashAttack>();
            attack.Initialize(context, direction, Mathf.Max(0.25f, radius),
                              Mathf.Clamp(arcDegrees, 20f, 170f), color);
        }

        /// <summary>Pure sector predicate shared by gameplay and regression tests.</summary>
        public static bool IsInsideSector(Vector2 origin, Vector2 forward, Vector2 point,
                                          float radius, float arcDegrees)
        {
            Vector2 delta = point - origin;
            if (delta.sqrMagnitude > radius * radius) return false;
            if (delta.sqrMagnitude <= 0.0001f) return true;
            if (forward.sqrMagnitude <= 0.0001f) forward = Vector2.right;
            return Mathf.Abs(Vector2.SignedAngle(forward.normalized, delta.normalized))
                   <= arcDegrees * 0.5f + 0.001f;
        }

        private void Initialize(SpellContext context, Vector2 direction, float radius,
                                float arcDegrees, Color color)
        {
            _context = context;
            _direction = direction;
            _radius = radius;
            _arc = arcDegrees;
            _halfArc = arcDegrees * 0.5f;
            _previousHeadAngle = -_halfArc - 0.01f;
            _totalDuration = Mathf.Max(MinimumTotalDuration,
                context.Spell != null ? context.Spell.lifetime : 0f);
            _baseColor = color;

            BuildVisuals();

            // A miss deliberately gets only the airy swing. Contact adds the sharper
            // hit transient later, making successful and unsuccessful attacks readable.
            ServiceLocator.Get<IAudioService>()?.PlaySfxById("spell_slash_swing");
        }

        private void BuildVisuals()
        {
            ElementalSprites.EnsureAll();
            _material = new Material(ElementalSprites.SharedUnlitMaterial)
            {
                name = "RegularSlashRuntimeMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (_material.HasProperty("_MainTex"))
                _material.mainTexture = Texture2D.whiteTexture;

            Color atmosphere = Color.Lerp(_baseColor, new Color(0.28f, 0.62f, 1f, 1f), 0.35f);
            Color blade = Color.Lerp(_baseColor, Color.white, 0.34f);
            Color edge = Color.Lerp(_baseColor, Color.white, 0.82f);

            _ribbons.Add(new SlashRibbon(transform, "AirWake", _material, RibbonSegments,
                _arc, _radius * 0.42f, _radius * 1.08f,
                WithAlpha(atmosphere, 0.15f), TrailWindow * 1.12f, 60));
            _ribbons.Add(new SlashRibbon(transform, "CrescentBody", _material, RibbonSegments,
                _arc, _radius * 0.56f, _radius,
                WithAlpha(blade, 0.70f), TrailWindow, 61));
            _ribbons.Add(new SlashRibbon(transform, "CuttingEdge", _material, RibbonSegments,
                _arc, _radius * 0.80f, _radius * 1.015f,
                WithAlpha(edge, 0.98f), TrailWindow * 0.76f, 62));
            _ribbons.Add(new SlashRibbon(transform, "InnerReflection", _material, RibbonSegments,
                _arc, _radius * 0.59f, _radius * 0.70f,
                WithAlpha(Color.white, 0.48f), TrailWindow * 0.46f, 63));

            _leadingGlint = CreateSprite("LeadingGlint", ElementalSprites.SparkleStar,
                Color.white, 66);
            _leadingGlint.transform.localScale = Vector3.one * 0.42f;

            _originRing = CreateSprite("OriginRing", ElementalSprites.Ring,
                WithAlpha(_baseColor, 0.52f), 59);
            _originRing.transform.localScale = Vector3.one * 0.18f;

            BuildAirMotes();
            BuildLight();
        }

        private void BuildAirMotes()
        {
            _moteTransforms = new Transform[AirMoteCount];
            _moteRenderers = new SpriteRenderer[AirMoteCount];
            for (int i = 0; i < AirMoteCount; i++)
            {
                var sr = CreateSprite($"AirMote_{i:00}", ElementalSprites.Sparkle,
                    WithAlpha(Color.Lerp(_baseColor, Color.white, 0.65f), 0f), 64);
                float scale = Mathf.Lerp(0.055f, 0.14f, (i % 4) / 3f);
                sr.transform.localScale = new Vector3(scale * 2.2f, scale, 1f);
                _moteTransforms[i] = sr.transform;
                _moteRenderers[i] = sr;
            }
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightGo = new GameObject("RegularSlashLight");
            _lightGo.transform.SetParent(transform, false);
            try
            {
                _light = _lightGo.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 2));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _baseColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.08f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.12f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.82f);
            }
            catch
            {
                _light = null;
            }
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, Color color, int order)
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

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;

            float sweep01 = Mathf.Clamp01(_age / SweepDuration);
            float easedHead = SmoothSwing(sweep01);
            float headAngle = Mathf.Lerp(-_halfArc, _halfArc, easedHead);
            float linger = _age <= SweepDuration
                ? 1f
                : 1f - Mathf.Clamp01((_age - SweepDuration) /
                    Mathf.Max(0.01f, _totalDuration - SweepDuration));

            for (int i = 0; i < _ribbons.Count; i++)
                _ribbons[i].Update(easedHead, linger);

            UpdateLeadingGlint(headAngle, sweep01, linger);
            UpdateOriginRing(sweep01, linger);
            UpdateAirMotes(easedHead, linger);
            UpdateLight(sweep01, linger);

            SweepTargets(_previousHeadAngle, headAngle);
            _previousHeadAngle = headAngle;

            if (_age >= _totalDuration)
                Destroy(gameObject);
        }

        private void UpdateLeadingGlint(float headAngle, float sweep01, float linger)
        {
            if (_leadingGlint == null) return;
            float radians = headAngle * Mathf.Deg2Rad;
            _leadingGlint.transform.localPosition = new Vector3(
                Mathf.Cos(radians) * _radius * 0.93f,
                Mathf.Sin(radians) * _radius * 0.93f,
                0f);
            _leadingGlint.transform.localRotation = Quaternion.Euler(0f, 0f, headAngle);
            float pulse = 0.38f + Mathf.Sin(sweep01 * Mathf.PI) * 0.18f;
            _leadingGlint.transform.localScale = new Vector3(pulse * 1.5f, pulse, 1f);
            _leadingGlint.color = WithAlpha(Color.white, linger * 0.95f);
        }

        private void UpdateOriginRing(float sweep01, float linger)
        {
            if (_originRing == null) return;
            float birth = Mathf.Clamp01(sweep01 * 3.2f);
            float scale = Mathf.Lerp(0.14f, 0.72f, birth);
            _originRing.transform.localScale = Vector3.one * scale;
            _originRing.color = WithAlpha(_baseColor,
                (1f - birth) * 0.62f * linger);
        }

        private void UpdateAirMotes(float head01, float linger)
        {
            if (_moteTransforms == null) return;
            for (int i = 0; i < _moteTransforms.Length; i++)
            {
                float lag = 0.07f + i * 0.048f;
                float progress = head01 - lag;
                float visibility = Mathf.Clamp01(progress * 16f) *
                                   Mathf.Clamp01((1f - progress) * 10f + 1f) * linger;
                float angle = Mathf.Lerp(-_halfArc, _halfArc, Mathf.Clamp01(progress));
                float radial = _radius * Mathf.Lerp(0.62f, 1.04f,
                    Mathf.Repeat(i * 0.37f, 1f));
                float radians = angle * Mathf.Deg2Rad;
                _moteTransforms[i].localPosition = new Vector3(
                    Mathf.Cos(radians) * radial,
                    Mathf.Sin(radians) * radial,
                    0f);
                _moteTransforms[i].localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
                Color c = _moteRenderers[i].color;
                c.a = visibility * 0.72f;
                _moteRenderers[i].color = c;
            }
        }

        private void UpdateLight(float sweep01, float linger)
        {
            if (_light == null) return;
            float intensity = (0.55f + Mathf.Sin(sweep01 * Mathf.PI) * 1.25f) * linger;
            try
            {
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, intensity);
            }
            catch { }
        }

        private void SweepTargets(float previousAngle, float currentAngle)
        {
            if (_context.Caster == null || _context.Spell == null) return;
            if (_context.TargetLayers.value == 0) return;

            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, _radius,
                                                           _context.TargetLayers);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null) continue;

                Health health = hit.GetComponentInParent<Health>();
                if (health == null || health.IsDead || _damaged.Contains(health)) continue;
                if (health.transform == _context.Caster ||
                    health.transform.IsChildOf(_context.Caster) ||
                    _context.Caster.IsChildOf(health.transform)) continue;

                // The overlap query returns whatever collider of theirs sits on the target
                // layer, including the large off-centre trigger an NPC uses to notice the
                // player. Measured against that, the contact landed between the two
                // characters rather than on the one that was struck.
                Collider2D body = EntityColliderConfigurator.GetBodyCollider(health.gameObject);
                Vector2 targetPoint = ResolveBodyPoint(health, body);
                if (!IsInsideSector(transform.position, _direction, targetPoint, _radius, _arc))
                    continue;

                Vector2 toTarget = targetPoint - (Vector2)transform.position;
                float signedAngle = toTarget.sqrMagnitude <= 0.0001f
                    ? 0f
                    : Vector2.SignedAngle(_direction, toTarget.normalized);
                if (signedAngle < previousAngle - 0.01f || signedAngle > currentAngle + 0.01f)
                    continue;

                int before = health.CurrentHp;
                int damage = Mathf.Max(1, Mathf.RoundToInt(_context.Spell.damage));
                health.TakeDamage(damage, _context.Caster.gameObject);
                if (health.CurrentHp == before) continue;

                _damaged.Add(health);
                GameEvents.FireHitDealt(_context.Caster.gameObject, health.gameObject, damage);

                CombatFeedback feedback = health.GetComponent<CombatFeedback>();
                if (feedback != null) feedback.ApplyKnockback(transform.position);

                Vector2 impactPoint = body != null
                    ? body.ClosestPoint(transform.position)
                    : targetPoint;
                if ((impactPoint - (Vector2)transform.position).sqrMagnitude < 0.01f)
                    impactPoint = targetPoint;
                RegularSlashImpactFX.Spawn(impactPoint, _direction, _baseColor);

                _hitCount++;
                if (_hitCount == 1)
                {
                    // The camera and the hit-stop are now driven from the director's own
                    // OnHitDealt handler, which applies the audience filter this call site
                    // never had: an NPC swinging slash_regular used to freeze the session.
                    ServiceLocator.Get<IAudioService>()?.PlaySfxById("spell_slash_hit");
                }
            }
        }

        /// <summary>Centre of a victim's body, preferring the bootstrap-built body box.</summary>
        private static Vector2 ResolveBodyPoint(Health health, Collider2D body)
        {
            if (body != null) return body.bounds.center;

            var sr = health.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr.bounds.center;

            return health.transform.position;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _ribbons.Count; i++)
                _ribbons[i]?.Dispose();
            _ribbons.Clear();

            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
            }
        }

        private static float SmoothSwing(float t)
            => t * t * (3f - 2f * t);

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>Allocation-free annular mesh with a moving alpha head and tail.</summary>
        private sealed class SlashRibbon
        {
            private readonly Mesh _mesh;
            private readonly Vector3[] _vertices;
            private readonly Color[] _colors;
            private readonly Color _color;
            private readonly float _trailWindow;
            private readonly int _segments;
            private readonly float _halfArc;
            private readonly float _innerRadius;
            private readonly float _outerRadius;

            public SlashRibbon(Transform parent, string name, Material material, int segments,
                               float arcDegrees, float innerRadius, float outerRadius,
                               Color color, float trailWindow, int sortingOrder)
            {
                _segments = segments;
                _color = color;
                _trailWindow = Mathf.Max(0.08f, trailWindow);
                _halfArc = arcDegrees * 0.5f;
                _innerRadius = innerRadius;
                _outerRadius = outerRadius;

                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingLayerName = SortingConfig.LAYER_VFX;
                renderer.sortingOrder = sortingOrder;

                int pointCount = segments + 1;
                _vertices = new Vector3[pointCount * 2];
                var uvs = new Vector2[_vertices.Length];
                _colors = new Color[_vertices.Length];
                var triangles = new int[segments * 6];

                for (int i = 0; i < pointCount; i++)
                {
                    float p = i / (float)segments;
                    float angle = Mathf.Lerp(-_halfArc, _halfArc, p) * Mathf.Deg2Rad;
                    Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    int v = i * 2;
                    _vertices[v] = radial * innerRadius;
                    _vertices[v + 1] = radial * outerRadius;
                    uvs[v] = new Vector2(p, 0f);
                    uvs[v + 1] = new Vector2(p, 1f);
                    _colors[v] = _colors[v + 1] = new Color(color.r, color.g, color.b, 0f);
                }

                for (int i = 0; i < segments; i++)
                {
                    int v = i * 2;
                    int t = i * 6;
                    triangles[t] = v;
                    triangles[t + 1] = v + 1;
                    triangles[t + 2] = v + 3;
                    triangles[t + 3] = v;
                    triangles[t + 4] = v + 3;
                    triangles[t + 5] = v + 2;
                }

                _mesh = new Mesh { name = $"{name}Mesh", hideFlags = HideFlags.HideAndDontSave };
                _mesh.MarkDynamic();
                _mesh.vertices = _vertices;
                _mesh.uv = uvs;
                _mesh.colors = _colors;
                _mesh.triangles = triangles;
                _mesh.RecalculateBounds();
                filter.sharedMesh = _mesh;
            }

            public void Update(float head01, float linger)
            {
                float step = 1f / _segments;
                for (int i = 0; i <= _segments; i++)
                {
                    float p = i / (float)_segments;
                    float behind = head01 - p;
                    float alpha = 0f;
                    float widthTaper = 0f;
                    if (behind >= -step && behind <= _trailWindow)
                    {
                        float tail = Mathf.Clamp01(1f - Mathf.Max(0f, behind) / _trailWindow);
                        float tip = Mathf.Clamp01((behind + step) / step);
                        alpha = Mathf.Pow(tail, 1.35f) * tip * linger * _color.a;

                        // Collapse radial thickness at both ends of the moving trail.
                        // This turns the strip into a pointed crescent instead of a
                        // radar-fan wedge while retaining the same gameplay sector.
                        float alongTrail = Mathf.Clamp01(Mathf.Max(0f, behind) / _trailWindow);
                        widthTaper = Mathf.Pow(Mathf.Sin(alongTrail * Mathf.PI), 0.38f);
                    }

                    float centerRadius = (_innerRadius + _outerRadius) * 0.5f;
                    float halfWidth = (_outerRadius - _innerRadius) * 0.5f * widthTaper;
                    float angle = Mathf.Lerp(-_halfArc, _halfArc, p) * Mathf.Deg2Rad;
                    Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Color c = new Color(_color.r, _color.g, _color.b, alpha);
                    int v = i * 2;
                    _vertices[v] = radial * (centerRadius - halfWidth);
                    _vertices[v + 1] = radial * (centerRadius + halfWidth);
                    _colors[v] = c;
                    _colors[v + 1] = c;
                }
                _mesh.vertices = _vertices;
                _mesh.colors = _colors;
            }

            public void Dispose()
            {
                if (_mesh == null) return;
                if (Application.isPlaying) Object.Destroy(_mesh);
                else Object.DestroyImmediate(_mesh);
            }
        }
    }

    /// <summary>Localized contact flash, ring and directional shards.</summary>
    internal sealed class RegularSlashImpactFX : MonoBehaviour
    {
        private const float Lifetime = 0.24f;
        private const int ShardCount = 12;

        private float _age;
        private Color _color;
        private SpriteRenderer _core;
        private SpriteRenderer _star;
        private SpriteRenderer _ring;
        private Transform[] _shardTransforms;
        private SpriteRenderer[] _shardRenderers;
        private Vector2[] _velocities;
        private float[] _spin;
        private Component _light;

        public static void Spawn(Vector2 position, Vector2 direction, Color color)
        {
            var go = new GameObject("RegularSlashImpactFX");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            var fx = go.AddComponent<RegularSlashImpactFX>();
            fx._color = color;
            fx.Build();
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();
            _core = Create("CoreFlash", ElementalSprites.HotCore, Color.white, 74);
            _star = Create("ContactStar", ElementalSprites.SparkleStar, Color.white, 76);
            _ring = Create("ShockRing", ElementalSprites.Ring, WithAlpha(_color, 0.9f), 73);
            _core.transform.localScale = Vector3.one * 0.72f;
            _star.transform.localScale = new Vector3(1.15f, 0.52f, 1f);
            _ring.transform.localScale = Vector3.one * 0.18f;

            _shardTransforms = new Transform[ShardCount];
            _shardRenderers = new SpriteRenderer[ShardCount];
            _velocities = new Vector2[ShardCount];
            _spin = new float[ShardCount];
            for (int i = 0; i < ShardCount; i++)
            {
                var shard = Create($"Shard_{i:00}", ElementalSprites.Sparkle,
                    Color.Lerp(_color, Color.white, Random.Range(0.35f, 0.9f)), 75);
                float size = Random.Range(0.055f, 0.13f);
                shard.transform.localScale = new Vector3(size * Random.Range(2.2f, 4.2f), size, 1f);
                float lateral = Random.Range(-2.7f, 2.7f);
                float forward = Random.Range(1.2f, 4.2f);
                _velocities[i] = new Vector2(forward, lateral);
                _spin[i] = Random.Range(-420f, 420f);
                _shardTransforms[i] = shard.transform;
                _shardRenderers[i] = shard;
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
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _color);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 1.5f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.08f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.78f);
            }
            catch { _light = null; }
        }

        private void Update()
        {
            // The impact is allowed to bloom during the global micro-freeze; the
            // attacker and world stop while the contact point remains readable.
            float dt = Time.unscaledDeltaTime;
            _age += dt;
            float t = Mathf.Clamp01(_age / Lifetime);
            float fade = 1f - t;

            if (_core != null)
            {
                _core.transform.localScale = Vector3.one * Mathf.Lerp(0.72f, 0.16f, t);
                _core.color = WithAlpha(Color.white, fade * fade);
            }
            if (_star != null)
            {
                _star.transform.localScale = new Vector3(
                    Mathf.Lerp(1.15f, 1.75f, t), Mathf.Lerp(0.52f, 0.08f, t), 1f);
                _star.color = WithAlpha(Color.white, fade * fade);
            }
            if (_ring != null)
            {
                _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.18f, 1.55f, t);
                _ring.color = WithAlpha(_color, fade * 0.78f);
            }

            for (int i = 0; i < _shardTransforms.Length; i++)
            {
                _shardTransforms[i].localPosition += (Vector3)(_velocities[i] * dt);
                _shardTransforms[i].Rotate(0f, 0f, _spin[i] * dt);
                _velocities[i] *= Mathf.Pow(0.035f, dt);
                Color c = _shardRenderers[i].color;
                c.a = fade * fade;
                _shardRenderers[i].color = c;
            }

            if (_light != null)
            {
                try
                {
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(
                        _light, 2.4f * fade * fade);
                }
                catch { }
            }

            if (_age >= Lifetime) Destroy(gameObject);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }

    /// <summary>
    /// One global, pause-safe hit-stop driver. It restores time only when it still
    /// owns the current scale, so an external pause can never be accidentally undone.
    /// </summary>
    internal sealed class RegularSlashHitStop : MonoBehaviour
    {
        private const float FrozenScale = 0.06f;
        private static RegularSlashHitStop _instance;

        private float _restoreScale = 1f;
        private float _restoreAt;
        private bool _active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
        }

        public static void Trigger(float realSeconds)
        {
            if (!Application.isPlaying || realSeconds <= 0f || Time.timeScale <= 0.001f)
                return;

            if (_instance == null)
            {
                var go = new GameObject("RegularSlashHitStop");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<RegularSlashHitStop>();
            }
            _instance.Begin(realSeconds);
        }

        private void Begin(float seconds)
        {
            if (!_active)
                _restoreScale = Time.timeScale;
            _restoreAt = Mathf.Max(_restoreAt, Time.realtimeSinceStartup + seconds);
            _active = true;
            Time.timeScale = FrozenScale;
        }

        private void Update()
        {
            if (!_active || Time.realtimeSinceStartup < _restoreAt) return;
            if (Mathf.Approximately(Time.timeScale, FrozenScale))
                Time.timeScale = _restoreScale;
            _active = false;
        }

        private void OnDestroy()
        {
            if (_active && Mathf.Approximately(Time.timeScale, FrozenScale))
                Time.timeScale = _restoreScale;
            if (_instance == this) _instance = null;
        }
    }
}
