using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Dash with epic motion-blur ghost trail and speed-line VFX.
    /// Spawns 6 ghost frames between origin and destination, plus a Light2D streak
    /// and screen shake. Mirrors Python's DashResolver damage/knockback rules.
    /// </summary>
    public class DashExecutor : ISpellExecutor
    {
        // Visible duration of the moving trail emitter. The caster itself is
        // teleported instantly via Rigidbody2D.MovePosition, but the trail
        // emitter physically lerps from start to end across this window so the
        // configured ParticlePreset (which uses World simulation space for
        // "dash" kind, see ParticleEmitter.ParticleSystem.cs) leaves dust
        // along the full path instead of pooling at the origin.
        // Falls back to ctx.Spell.duration when set, clamped to a sane range.
        private const float DefaultTrailMoveSeconds = 0.18f;
        private const float MinTrailMoveSeconds     = 0.08f;
        private const float MaxTrailMoveSeconds     = 0.6f;

        public void Execute(SpellContext ctx)
        {
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;
            Vector2 startPos = ctx.Caster.position;
            Vector2 endPos = startPos + ctx.Direction * dist;
            float moveDuration = ctx.Spell.duration > 0f
                ? Mathf.Clamp(ctx.Spell.duration, MinTrailMoveSeconds, MaxTrailMoveSeconds)
                : DefaultTrailMoveSeconds;

            // Caster motion. In real gameplay the caster has a Rigidbody2D and
            // the dash is an instant teleport (1-frame physics step) — the
            // ghost trail + particle wake sell the motion. The Spells Editor
            // preview spawns a synthetic caster WITHOUT a Rigidbody2D, so the
            // teleport branch is skipped; without a fallback the preview
            // character would stay stationary while only the FX traverse the
            // path. The else-branch attaches a smooth Transform-tween so the
            // preview clearly shows the dash motion.
            var rb = ctx.Caster.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.MovePosition(rb.position + ctx.Direction * dist);
            }
            else
            {
                var casterMover = ctx.Caster.gameObject.AddComponent<DashCasterMover>();
                casterMover.Init(startPos, endPos, moveDuration);
            }

            // VFX: ghost trail
            var casterSr = ctx.Caster.GetComponentInChildren<SpriteRenderer>();
            DashTrailFX.Spawn(startPos, endPos, ctx.Direction, casterSr);

            // Ground trail particles — spawn ONE emitter at startPos and lerp
            // it to endPos across the dash window. Because "dash" presets use
            // World simulation space, every particle is dropped where it was
            // emitted, producing a continuous wake along the actual path
            // instead of static puffs at fixed sample points.
            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
            {
                var trailGo = VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, startPos);
                if (trailGo != null)
                {
                    var mover = trailGo.AddComponent<DashTrailMover>();
                    mover.Init(startPos, endPos, moveDuration);
                }
            }

            CameraShake.Trigger(0.12f, 0.15f);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_dash_whoosh");

            // Collision damage + knockback
            if (ctx.Spell.collisionDamage > 0)
            {
                var hits = Physics2D.OverlapCircleAll(ctx.Caster.position, 1f, ctx.TargetLayers);
                foreach (var hit in hits)
                {
                    if (hit.gameObject == ctx.Caster.gameObject) continue;
                    var health = hit.GetComponent<Health>();
                    if (health != null && !health.IsDead)
                    {
                        health.TakeDamage(Mathf.RoundToInt(ctx.Spell.collisionDamage));
                        if (ctx.Spell.knockback > 0)
                        {
                            var hitRb = hit.GetComponent<Rigidbody2D>();
                            if (hitRb != null)
                            {
                                Vector2 knockDir = ((Vector2)hit.transform.position - (Vector2)ctx.Caster.position).normalized;
                                hitRb.AddForce(knockDir * ctx.Spell.knockback, ForceMode2D.Impulse);
                            }
                        }
                    }
                }
            }

        }
    }

    /// <summary>Spawns a chain of fading ghost sprites + speed-line Light2D streak.</summary>
    internal class DashTrailFX : MonoBehaviour
    {
        private const int GhostCount = 6;
        private const float Life = 0.35f;
        private float _age;
        private SpriteRenderer[] _ghosts;
        private GameObject _lightGo;
        private Component _light;

        public static void Spawn(Vector2 from, Vector2 to, Vector2 dir, SpriteRenderer source)
        {
            var go = new GameObject("DashTrailFX");
            go.transform.position = from;
            var fx = go.AddComponent<DashTrailFX>();
            fx.Build(from, to, dir, source);
        }

        private void Build(Vector2 from, Vector2 to, Vector2 dir, SpriteRenderer source)
        {
            _ghosts = new SpriteRenderer[GhostCount];
            ElementalSprites.EnsureAll();
            Sprite sprite = source != null && source.sprite != null ? source.sprite : ElementalSprites.Glow;
            float baseAlpha = 0.55f;

            for (int i = 0; i < GhostCount; i++)
            {
                float t = (i + 1f) / (GhostCount + 1f);
                var ghostGo = new GameObject($"Ghost_{i}");
                ghostGo.transform.SetParent(transform, false);
                ghostGo.transform.position = Vector2.Lerp(from, to, t);
                var sr = ghostGo.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = new Color(0.55f, 0.75f, 1f, baseAlpha * (1f - t));
                sr.sortingLayerID = SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_VFX);
                sr.sortingLayerName = Valkur.Core.SortingConfig.LAYER_VFX;
                sr.sortingOrder = 40;
                if (source != null) ghostGo.transform.localScale = source.transform.lossyScale;
                _ghosts[i] = sr;
            }

            // Light2D streak at midpoint
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _lightGo = new GameObject("DashLight");
                _lightGo.transform.SetParent(transform, false);
                _lightGo.transform.position = Vector2.Lerp(from, to, 0.5f);
                try
                {
                    _light = _lightGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, new Color(0.55f, 0.75f, 1f, 1f));
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.0f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, Mathf.Max(1.5f, Vector2.Distance(from, to) * 0.6f));
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.3f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.9f);
                }
                catch { }
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / Life;
            if (t >= 1f) { if (_lightGo != null) Destroy(_lightGo); Destroy(gameObject); return; }
            float fade = 1f - t;
            if (_ghosts != null)
            {
                foreach (var sr in _ghosts)
                {
                    if (sr == null) continue;
                    var c = sr.color; c.a *= 1f - Time.deltaTime * 3.5f; sr.color = c;
                }
            }
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.0f * fade); }
                catch { }
            }
        }
    }

    /// <summary>
    /// Lerps the host transform from <c>from</c> to <c>to</c> across <c>duration</c>
    /// seconds, then calls <see cref="ParticleEmitter.StopEmitting"/> so the
    /// trail tapers off naturally instead of pooling new particles at the
    /// destination. The host GO is destroyed by VFXManager after the preset's
    /// own lifespan expires — this component just drives motion + cutoff.
    ///
    /// Emission-rate override: the stock <c>dash_trail_emitter</c> preset emits
    /// at 10/s, which over a ~0.18 s dash drops only 1-2 particles. To get a
    /// continuous wake along the actual path the rate is bumped on Init so
    /// roughly one particle drops every ~0.012 s (≈ 14 along the full dash),
    /// then naturally falls off via StopEmitting once the move completes.
    /// </summary>
    internal class DashTrailMover : MonoBehaviour
    {
        // Particles per second emitted while traversing start→end. Tuned so a
        // 0.18 s dash drops ~14 particles, dense enough to read as a continuous
        // ground trail without overwhelming the screen.
        private const float TraversalEmissionRate = 80f;

        private Vector3 _from;
        private Vector3 _to;
        private float _duration;
        private float _age;
        private bool _stopped;
        private ParticleEmitter _emitter;

        public void Init(Vector3 from, Vector3 to, float duration)
        {
            _from = from;
            _to = to;
            _duration = Mathf.Max(0.01f, duration);
            transform.position = from;
            _emitter = GetComponent<ParticleEmitter>();
            if (_emitter != null) _emitter.SetEmissionRate(TraversalEmissionRate);
        }

        /// <summary>
        /// Public test seam — drives the lerp + stop-emitting transition with an
        /// explicit delta-time, so EditMode tests get deterministic motion
        /// regardless of the editor's frame-time. Production <see cref="Update"/>
        /// just delegates with <see cref="Time.deltaTime"/>.
        /// </summary>
        public void Tick(float dt)
        {
            _age += dt;
            float t = Mathf.Clamp01(_age / _duration);
            transform.position = Vector3.Lerp(_from, _to, t);
            if (!_stopped && t >= 1f)
            {
                _stopped = true;
                if (_emitter != null) _emitter.StopEmitting();
            }
        }

        private void Update() => Tick(Time.deltaTime);
    }

    /// <summary>
    /// Smoothly lerps a Rigidbody2D-less caster's transform from <c>from</c>
    /// to <c>to</c> over <c>duration</c>, then self-destructs. This is the
    /// fallback path used by <see cref="DashExecutor"/> when the caster has
    /// no <see cref="Rigidbody2D"/> — primarily the synthetic preview caster
    /// in the Spells Editor "View" panel, which would otherwise stay
    /// stationary while only the trail FX traverse the dash path. Real
    /// gameplay uses <see cref="Rigidbody2D.MovePosition"/> and never creates
    /// this component.
    /// </summary>
    internal class DashCasterMover : MonoBehaviour
    {
        private Vector3 _from;
        private Vector3 _to;
        private float _duration;
        private float _age;

        public void Init(Vector3 from, Vector3 to, float duration)
        {
            _from = from;
            _to = to;
            _duration = Mathf.Max(0.01f, duration);
            transform.position = from;
        }

        /// <summary>
        /// Public test seam — drives the lerp + snap + self-destroy with an
        /// explicit delta-time so EditMode tests get deterministic motion
        /// regardless of the editor's frame-time. Production <see cref="Update"/>
        /// just delegates with <see cref="Time.deltaTime"/>.
        /// </summary>
        public void Tick(float dt)
        {
            _age += dt;
            float t = Mathf.Clamp01(_age / _duration);
            transform.position = Vector3.Lerp(_from, _to, t);
            if (t >= 1f)
            {
                transform.position = _to;
                // SafeDestroy keeps the EditMode tests happy — Object.Destroy on
                // a Component is illegal in EditMode and was causing
                // "Destroy may not be called from edit mode!" failures.
                SafeDestroy.Of(this);
            }
        }

        private void Update() => Tick(Time.deltaTime);
    }
}
