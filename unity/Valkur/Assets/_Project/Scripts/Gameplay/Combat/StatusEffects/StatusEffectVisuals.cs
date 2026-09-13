using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// What a status effect LOOKS like on the body: a small emitter per kind, on for as long
    /// as the effect is, off the moment it ends.
    ///
    /// Before this the eight effects were eight tints of <see cref="SpriteTintStack"/> — and the
    /// stack multiplies the burn together with the hit flash, the death fade and the transporter
    /// effect, so "it is burning" and "I just hit it" arrived on the same channel. A burning
    /// monster now has embers rising off it; a poisoned one drips; a frozen one sparkles.
    ///
    /// One pooled emitter per kind per entity, built on first use, sized from the body, sorted
    /// one order OVER the body on its own layer: the embers are in front of the creature they
    /// come off, and still under whoever stands in front of it. Each budget is a dozen
    /// particles — on the shipped additive material six of these over a 40-px body would be a
    /// white blob, so the alpha is low and the counts are small on purpose.
    ///
    /// Driven by <see cref="StatusEffectManager"/>'s apply/remove events, which fire on the
    /// REPLACE a refresh does as well, so a refreshed burn restarts nothing visible: the
    /// remove and the apply land in the same frame and the emitter simply keeps emitting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatusEffectVisuals : MonoBehaviour
    {
        private readonly Dictionary<StatusEffectKind, ParticleSystem> _emitters = new Dictionary<StatusEffectKind, ParticleSystem>();
        private StatusEffectManager _manager;
        private SpriteRenderer _body;
        private bool _subscribed;

        private void Awake()
        {
            _manager = GetComponent<StatusEffectManager>();
            _body    = SpriteTintStack.ResolveBodyRenderer(gameObject);
            Subscribe();
        }

        private void OnEnable()  => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed) return;
            if (_manager == null) _manager = GetComponent<StatusEffectManager>();
            if (_manager == null) return;
            _manager.OnEffectApplied += OnApplied;
            _manager.OnEffectRemoved += OnRemoved;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _manager == null) return;
            _manager.OnEffectApplied -= OnApplied;
            _manager.OnEffectRemoved -= OnRemoved;
            _subscribed = false;
        }

        private void OnApplied(StatusEffect effect) { if (effect != null) Show(effect.Kind); }
        private void OnRemoved(StatusEffect effect) { if (effect != null) Hide(effect.Kind); }

        /// <summary>The emitter for <paramref name="kind"/>, if one has been built. Test seam.</summary>
        public ParticleSystem EmitterFor(StatusEffectKind kind)
            => _emitters.TryGetValue(kind, out var ps) ? ps : null;

        /// <summary>True while <paramref name="kind"/> is drawing. Test seam.</summary>
        public bool IsShowing(StatusEffectKind kind)
            => _emitters.TryGetValue(kind, out var ps) && ps != null && ps.emission.enabled;

        /// <summary>Start drawing <paramref name="kind"/>. Public so a test can drive it without an effect.</summary>
        public void Show(StatusEffectKind kind)
        {
            var ps = GetOrBuild(kind);
            if (ps == null) return;
            var emission = ps.emission;
            emission.enabled = true;
            if (!ps.isPlaying) ps.Play();
        }

        /// <summary>Stop drawing <paramref name="kind"/>. The particles already out finish their lives.</summary>
        public void Hide(StatusEffectKind kind)
        {
            if (!_emitters.TryGetValue(kind, out var ps) || ps == null) return;
            var emission = ps.emission;
            emission.enabled = false;
        }

        private ParticleSystem GetOrBuild(StatusEffectKind kind)
        {
            if (_emitters.TryGetValue(kind, out var existing) && existing != null) return existing;

            if (_body == null) _body = SpriteTintStack.ResolveBodyRenderer(gameObject);
            var go = new GameObject("Status_" + kind);
            // Parented here, inside an event handler that runs from Update, never from
            // OnEnable: reparenting during activation is silently refused.
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Configure(ps, kind, BodySize());

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            if (_body != null)
            {
                r.sortingLayerID = _body.sortingLayerID;
                r.sortingOrder   = _body.sortingOrder + 1;
            }
            bool additive = IsAdditive(kind);
            r.sharedMaterial = ParticleMaterialCache.Get(WeatherTextures.Dot(16, 0.6f), additive);

            ps.Play();
            _emitters[kind] = ps;
            return ps;
        }

        private void LateUpdate()
        {
            // The body's order moves with its Y; the emitters follow it.
            if (_body == null || _emitters.Count == 0) return;
            foreach (var kv in _emitters)
            {
                if (kv.Value == null) continue;
                var r = kv.Value.GetComponent<ParticleSystemRenderer>();
                int order = _body.sortingOrder + 1;
                if (r.sortingOrder != order) r.sortingOrder = order;
            }
        }

        private Vector2 BodySize()
        {
            if (_body != null && _body.sprite != null)
            {
                var s = _body.sprite.bounds.size;
                return new Vector2(Mathf.Max(0.4f, s.x), Mathf.Max(0.6f, s.y));
            }
            return new Vector2(1f, 1.6f);
        }

        /// <summary>Additive for the kinds that are LIGHT (fire, frost, lightning-ish); alpha for the matter (poison, mist).</summary>
        public static bool IsAdditive(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Poison:
                case StatusEffectKind.Slow:
                case StatusEffectKind.Root:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>The colour each kind is drawn in. Pure.</summary>
        public static Color ColourFor(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Burn:       return new Color(1f, 0.50f, 0.15f, 0.85f);
                case StatusEffectKind.Poison:     return new Color(0.35f, 0.95f, 0.35f, 0.70f);
                case StatusEffectKind.Freeze:     return new Color(0.65f, 0.92f, 1f, 0.85f);
                case StatusEffectKind.Slow:       return new Color(0.50f, 0.62f, 1f, 0.28f);
                case StatusEffectKind.Stun:       return new Color(1f, 0.92f, 0.35f, 0.9f);
                case StatusEffectKind.Root:       return new Color(0.42f, 0.70f, 0.30f, 0.75f);
                case StatusEffectKind.Vulnerable: return new Color(0.80f, 0.40f, 0.85f, 0.75f);
                default:                          return new Color(0.45f, 0.15f, 0.60f, 0.8f);   // Marked, and whatever is appended later
            }
        }

        private static void Configure(ParticleSystem ps, StatusEffectKind kind, Vector2 body)
        {
            var main = ps.main;
            main.loop            = true;
            main.duration        = 1f;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor      = ColourFor(kind);
            main.maxParticles    = 24;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale     = new Vector3(body.x * 0.7f, body.y * 0.8f, 0.1f);
            shape.position  = new Vector3(0f, body.y * 0.5f, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.World;

            switch (kind)
            {
                case StatusEffectKind.Burn:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                    main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                    emission.rateOverTime = 11f;
                    SetVelocity(vel, -0.15f, 0.15f, 0.7f, 1.3f);
                    break;
                case StatusEffectKind.Poison:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                    main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.10f);
                    main.gravityModifier = 0.9f;
                    emission.rateOverTime = 5f;
                    SetVelocity(vel, -0.05f, 0.05f, -0.1f, 0.05f);
                    break;
                case StatusEffectKind.Freeze:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                    main.startSize     = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                    emission.rateOverTime = 7f;
                    SetVelocity(vel, -0.05f, 0.05f, 0.02f, 0.12f);
                    break;
                case StatusEffectKind.Stun:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                    main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.09f);
                    emission.rateOverTime = 6f;
                    shape.scale    = new Vector3(body.x * 0.9f, 0.15f, 0.1f);
                    shape.position = new Vector3(0f, body.y * 1.05f, 0f);   // the stars go round the head
                    SetVelocity(vel, -0.3f, 0.3f, 0.05f, 0.2f);
                    break;
                case StatusEffectKind.Slow:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
                    main.startSize     = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
                    emission.rateOverTime = 3f;
                    SetVelocity(vel, -0.05f, 0.05f, 0.1f, 0.25f);
                    break;
                case StatusEffectKind.Root:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
                    main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
                    emission.rateOverTime = 5f;
                    shape.scale    = new Vector3(body.x, 0.12f, 0.1f);
                    shape.position = new Vector3(0f, 0.05f, 0f);            // the vines are at the feet
                    SetVelocity(vel, -0.1f, 0.1f, 0.2f, 0.5f);
                    break;
                default:   // Vulnerable, Marked
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
                    main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
                    emission.rateOverTime = 6f;
                    SetVelocity(vel, -0.12f, 0.12f, 0.15f, 0.45f);
                    break;
            }

            // Every particle fades out over its life; a hard-ending mote is a dead pixel.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private static void SetVelocity(ParticleSystem.VelocityOverLifetimeModule vel, float xMin, float xMax, float yMin, float yMax)
        {
            // Every axis in the same curve mode, or Unity rejects the module once per frame.
            vel.x = new ParticleSystem.MinMaxCurve(xMin, xMax);
            vel.y = new ParticleSystem.MinMaxCurve(yMin, yMax);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }
    }
}
