using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Dash: the caster crosses the gap in one physics step, and everything the player
    /// sees is <see cref="DashStreakFX"/> drawing that crossing over the next eighth of a
    /// second.
    ///
    /// Two things were wrong underneath the visuals. The contact sweep ran
    /// <c>OverlapCircle</c> at <c>ctx.Caster.position</c> immediately after
    /// <c>MovePosition</c> — which is deferred to the next physics step, so the query
    /// happened at the ORIGIN and dashing through a pack hit nobody. And the caster was
    /// only ever moved as far as a hardcoded fallback, because both dash spells author
    /// <c>distance: 0</c>. The sweep now follows the path the body actually takes.
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

        private const float DEFAULT_DISTANCE = 3f;

        /// <summary>Half-width of the body sweep used for contact, in world units.</summary>
        private const float SWEEP_RADIUS = 0.45f;

        private static readonly Color DefaultTint = new Color(0.62f, 0.86f, 1f, 1f);

        public void Execute(SpellContext ctx)
        {
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : DEFAULT_DISTANCE;
            Vector2 startPos = ctx.Caster.position;
            Vector2 endPos = startPos + ctx.Direction * dist;
            float moveDuration = ctx.Spell.duration > 0f
                ? Mathf.Clamp(ctx.Spell.duration, MinTrailMoveSeconds, MaxTrailMoveSeconds)
                : DefaultTrailMoveSeconds;

            // Contact is resolved against the path BEFORE the body is moved, because
            // MovePosition does not take effect until the next physics step.
            ApplyPathContact(ctx, startPos, dist);

            // Caster motion. In real gameplay the caster has a Rigidbody2D and
            // the dash is an instant teleport (1-frame physics step) — the
            // afterimages + particle wake sell the motion. The Spells Editor
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

            Color tint = ctx.Spell.particleColor != Color.clear ? ctx.Spell.particleColor : DefaultTint;
            var casterSr = ResolveBodyRenderer(ctx.Caster);
            DashStreakFX.Spawn(ctx.Caster, startPos, endPos, casterSr, tint);

            // Dust is kicked up by feet, so the wake runs on the same line the streak does.
            Vector3 feet = DashStreakFX.FeetOffset(ctx.Caster, casterSr);
            SpawnGroundWake(ctx, (Vector3)startPos + feet, (Vector3)endPos + feet, moveDuration);

            // The camera commits to where the dash is going and settles on arrival. Guarded
            // to the player inside the director — an NPC dashing must not move the frame.
            Feel.CameraFeel.Dash(ctx.Direction, dist, moveDuration);
            ServiceLocator.Get<IAudioService>()?.PlaySfxById("spell_dash_whoosh");
        }

        /// <summary>
        /// Everything standing between the two ends of the dash is shoulder-checked once.
        /// A circle cast rather than an overlap: the point of a dash is the line it draws.
        /// </summary>
        private static void ApplyPathContact(SpellContext ctx, Vector2 startPos, float dist)
        {
            if (ctx.Spell.collisionDamage <= 0 || ctx.TargetLayers.value == 0) return;

            var hits = Physics2D.CircleCastAll(startPos, SWEEP_RADIUS, ctx.Direction, dist,
                                               ctx.TargetLayers);
            if (hits.Length == 0) return;

            int damage = Mathf.RoundToInt(ctx.Spell.collisionDamage);
            var struck = new HashSet<Health>();

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D collider = hits[i].collider;
                if (collider == null) continue;
                if (collider.transform.IsChildOf(ctx.Caster)) continue;

                Health health = collider.GetComponentInParent<Health>();
                if (health == null || health.IsDead || !struck.Add(health)) continue;

                health.TakeDamage(damage, ctx.Caster.gameObject);
                GameEvents.FireHitDealt(ctx.Caster.gameObject, health.gameObject, damage);

                if (ctx.Spell.knockback <= 0) continue;
                var hitRb = health.GetComponent<Rigidbody2D>();
                if (hitRb != null)
                    hitRb.AddForce(ctx.Direction.normalized * ctx.Spell.knockback,
                                   ForceMode2D.Impulse);
            }
        }

        /// <summary>
        /// Ground dust, spawned once per authored layer and lerped along the path. Because
        /// "dash" presets simulate in world space, every particle stays where it was
        /// emitted and the result is a continuous wake rather than a puff at the origin.
        /// </summary>
        private static void SpawnGroundWake(SpellContext ctx, Vector3 startPos, Vector3 endPos,
                                            float moveDuration)
        {
            if (VFXManager.Instance == null) return;

            foreach (var presetId in ctx.Spell.CollectVfxPresets())
            {
                var trailGo = VFXManager.Instance.SpawnParticlePreset(presetId, startPos);
                if (trailGo == null) continue;
                trailGo.AddComponent<DashTrailMover>().Init(startPos, endPos, moveDuration);
            }
        }

        /// <summary>The renderer that draws the character, for the afterimages to copy.</summary>
        private static SpriteRenderer ResolveBodyRenderer(Transform caster)
        {
            var sr = caster.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr;

            foreach (var candidate in caster.GetComponentsInChildren<SpriteRenderer>())
                if (candidate != null && candidate.sprite != null) return candidate;

            return null;
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
