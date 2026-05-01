using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Sustained laser beam component attached temporarily to the caster.
    /// Renders a LineRenderer beam toward the cast direction and deals damage on ticks.
    /// Maps to Python's LaserBeamEmitterSystem: particles along line, per-tick damage, duration-limited.
    ///
    /// Lifecycle (hold-to-channel):
    /// - <see cref="Begin"/> starts the beam.
    /// - <see cref="Refresh"/> must be called every frame the player keeps the trigger held;
    ///   if no refresh is received for <see cref="AUTO_STOP_GRACE"/> seconds, the beam ends.
    /// - <see cref="Stop"/> requests an immediate, graceful shutdown.
    /// - The beam also auto-stops if the caster runs out of mana or
    ///   <see cref="SpellDefinition.channelDuration"/> is reached (when > 0).
    /// </summary>
    public class LaserBeamController : MonoBehaviour
    {
        private const float TICK_INTERVAL = 0.25f;

        /// <summary>Fallback max travel distance (world units) when the spell asset
        /// leaves <see cref="SpellDefinition.range"/> at 0.</summary>
        public const float DEFAULT_RANGE = 10f;

        private const float DEFAULT_BEAM_WIDTH = 0.12f;

        // Visual layering: bright thin core inside a softer wider glow.
        private const float CORE_WIDTH_MULT = 0.55f;   // core line width = beam width * this
        private const float GLOW_WIDTH_MULT = 1.6f;    // outer glow width = beam width * this
        private const float CORE_ALPHA = 1.0f;
        private const float GLOW_ALPHA = 0.45f;

        /// <summary>Mana drained per second while the beam is active.</summary>
        public const float MANA_PER_SECOND = 2f;

        /// <summary>Maximum gap between two <see cref="Refresh"/> calls before the beam auto-stops.</summary>
        public const float AUTO_STOP_GRACE = 0.15f;

        /// <summary>Time (s) for the beam to grow from origin to full length when started.</summary>
        public const float GROW_DURATION = 0.08f;

        /// <summary>Time (s) for the beam to fade out after Stop() is requested.</summary>
        public const float FADE_DURATION = 0.12f;

        /// <summary>
        /// Visual-only backward offset (world units) applied to the beam's rendered
        /// start point along the OPPOSITE of the firing direction. This makes the
        /// beam appear to emerge from BEHIND the caster (the sprite covers the
        /// origin), matching the silhouette in the reference screenshot.
        /// Damage / raycast still use the true caster center; only the rendered line,
        /// trail particles and impact-tip are anchored at the offset start.
        /// </summary>
        public const float VISUAL_BACK_OFFSET = 0.45f;

        private LineRenderer _coreLine;
        private LineRenderer _glowLine;
        private Material _coreMaterial;
        private Material _glowMaterial;
        private ParticleSystem _impactBurst;
        private GameObject _impactGo;
        private ParticleSystem _trailPS;
        private GameObject _trailGo;
        private Color _beamColor;
        private SpellContext _ctx;
        private float _lastRefreshTime;
        private bool _stopRequested;
        private float _manaDebt;
        private float _growT;       // 0..1 grow envelope at start
        private float _fadeT;       // 1..0 fade envelope on stop (1 = fully visible)
        private bool _fading;       // true while playing the fade-out animation

        /// <summary>Starts the beam coroutine. Call immediately after AddComponent.</summary>
        public void Begin(SpellContext ctx)
        {
            _ctx = ctx;
            _lastRefreshTime = Time.time;
            _stopRequested = false;
            _manaDebt = 0f;
            _growT = 0f;
            _fadeT = 1f;
            _fading = false;
            BuildVisual(ctx);
            StartCoroutine(RunBeam());
        }

        /// <summary>Bumps the keep-alive timestamp. Call every frame the trigger is held.</summary>
        public void Refresh() => _lastRefreshTime = Time.time;

        /// <summary>Requests immediate graceful shutdown of the beam.</summary>
        public void Stop() => _stopRequested = true;

        /// <summary>
        /// Resolves the effective max travel distance for a beam from a spell definition.
        /// Honors <see cref="SpellDefinition.range"/> when &gt; 0; otherwise falls back
        /// to <see cref="DEFAULT_RANGE"/>. Pure function — safe to call from tests.
        /// </summary>
        public static float ResolveBeamRange(SpellDefinition spell)
        {
            if (spell == null) return DEFAULT_RANGE;
            return spell.range > 0f ? spell.range : DEFAULT_RANGE;
        }

        /// <summary>
        /// Resolves the effective max channel duration for a beam.
        /// Honors <see cref="SpellDefinition.channelDuration"/> when &gt; 0; otherwise
        /// returns <see cref="float.PositiveInfinity"/> meaning "hold-controlled".
        /// </summary>
        public static float ResolveMaxDuration(SpellDefinition spell)
        {
            if (spell == null || spell.channelDuration <= 0f) return float.PositiveInfinity;
            return spell.channelDuration;
        }

        private void BuildVisual(SpellContext ctx)
        {
            float width = DEFAULT_BEAM_WIDTH * (ctx.Spell.scale > 0 ? ctx.Spell.scale : 1f);

            _beamColor = ctx.Spell.particleColor != Color.clear && ctx.Spell.particleColor.a > 0
                ? ctx.Spell.particleColor
                : new Color(0f, 0.9f, 1f, 1f);

            // Outer glow line (wider, soft alpha).
            _glowLine = BuildLine("LaserBeam_Glow", width * GLOW_WIDTH_MULT,
                new Color(_beamColor.r, _beamColor.g, _beamColor.b, GLOW_ALPHA),
                sortingOrder: 4, out _glowMaterial);

            // Inner bright core line (narrower, full alpha, slightly washed-out toward white).
            Color coreCol = Color.Lerp(_beamColor, Color.white, 0.35f);
            coreCol.a = CORE_ALPHA;
            _coreLine = BuildLine("LaserBeam_Core", width * CORE_WIDTH_MULT,
                coreCol, sortingOrder: 5, out _coreMaterial);

            // Impact burst at the laser tip — continuous particles in laser color.
            _impactGo = new GameObject("LaserBeam_Impact");
            _impactGo.transform.SetParent(transform, false);
            _impactBurst = BuildImpactBurst(_impactGo, _beamColor, width);

            // Trail particles along the beam path — emit perpendicular drift to
            // sell the energy travelling through the line.
            _trailGo = new GameObject("LaserBeam_Trail");
            _trailGo.transform.SetParent(transform, false);
            _trailPS = BuildTrailParticles(_trailGo, _beamColor, width);
        }

        /// <summary>
        /// Builds an Edge-shape ParticleSystem that emits along the beam line.
        /// Each frame the controller orients/scales it to span origin → end.
        /// </summary>
        private static ParticleSystem BuildTrailParticles(GameObject host, Color color, float beamWidth)
        {
            var ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.35f;
            main.startSpeed = 0.6f;        // slow perpendicular drift
            main.startSize = beamWidth * 0.9f;
            main.startColor = color;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = 60f;   // density along the beam

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
            shape.radius = 0.5f;           // overwritten each frame to half-length
            shape.randomDirectionAmount = 1f; // random perpendicular spread

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.Lerp(color, Color.white, 0.3f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // Trail also renders behind the player so the start of the beam is
                // visually covered by the caster sprite.
                renderer.sortingLayerName = "WallsBottom";
                renderer.sortingOrder = 5;
                renderer.material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                    ?? Shader.Find("Sprites/Default"))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            ps.Play();
            return ps;
        }

        /// <summary>Builds a uniform-width LineRenderer (start/end widths equal) for the beam.</summary>
        private LineRenderer BuildLine(string name, float width, Color color, int sortingOrder, out Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 6;       // rounded ends -> more "laser" look
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;

            // Uniform thickness from origin to impact (no taper).
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;

            material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"));
            material.hideFlags = HideFlags.HideAndDontSave;
            lr.sharedMaterial = material;

            // Render BELOW the player sprite (which lives on "Entities"). This is
            // half of the "beam emerges from behind the caster" effect; the other
            // half is the VISUAL_BACK_OFFSET applied each frame in RunBeam.
            lr.sortingLayerName = "WallsBottom";
            lr.sortingOrder = sortingOrder;
            return lr;
        }

        /// <summary>
        /// Builds a small ParticleSystem that simulates the explosion happening at
        /// the laser's impact point. Particles spray outward in the laser's color.
        /// </summary>
        private static ParticleSystem BuildImpactBurst(GameObject host, Color color, float beamWidth)
        {
            var ps = host.AddComponent<ParticleSystem>();
            // A freshly-added ParticleSystem auto-plays (playOnAwake defaults to true).
            // Mutating `main.duration` while it's playing logs an error, so we stop and
            // clear it before configuring the main module, then Play() at the end.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.25f;
            main.startSpeed = 2.5f;
            main.startSize = beamWidth * 1.4f;
            main.startColor = color;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = 80f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = beamWidth * 0.5f;
            shape.radiusThickness = 1f;

            // Fade-out via color over lifetime.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.Lerp(color, Color.white, 0.5f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.2f)
            );
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Renderer: additive-ish unlit material with same shader as beam.
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sortingLayerName = "VFX";
                renderer.sortingOrder = 6;
                renderer.material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                    ?? Shader.Find("Sprites/Default"))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            ps.Play();
            return ps;
        }

        private IEnumerator RunBeam()
        {
            // channelDuration <= 0 means "unbounded; lifecycle controlled by Stop()/Refresh()".
            float maxDuration = ResolveMaxDuration(_ctx.Spell);
            // range <= 0 means "use system default".
            float range = ResolveBeamRange(_ctx.Spell);
            float beamHalfWidth = DEFAULT_BEAM_WIDTH * (_ctx.Spell.scale > 0 ? _ctx.Spell.scale : 1f);
            int dmg = Mathf.Max(1, Mathf.RoundToInt(_ctx.Spell.damage > 0 ? _ctx.Spell.damage : 1f));

            var mana = GetComponent<Mana>();

            float elapsed = 0f;
            float nextTick = 0f;
            var damagedThisTick = new HashSet<GameObject>();

            // Determine the blocking layers (world geometry, buildings)
            int blockMask = LayerMask.GetMask("World", "Building");
            // Cache the original "full opacity" colors so we can modulate during fade.
            Color glowBaseColor = _glowLine != null ? _glowLine.startColor : Color.white;
            Color coreBaseColor = _coreLine != null ? _coreLine.startColor : Color.white;

            // Loop continues until either the active phase ends naturally OR the fade
            // animation completes (whichever comes second).
            while (true)
            {
                bool naturalEndConditionsMet = elapsed >= maxDuration
                                               || (Time.time - _lastRefreshTime) >= AUTO_STOP_GRACE;

                // Transition into fade-out exactly once.
                if (!_fading && (_stopRequested || naturalEndConditionsMet))
                {
                    _fading = true;
                    _fadeT = 1f;
                    // Stop spawning new trail/impact particles (existing ones still die naturally).
                    if (_trailPS != null)   _trailPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    if (_impactBurst != null) _impactBurst.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }

                // Advance grow / fade envelopes.
                if (!_fading)
                    _growT = Mathf.Min(1f, _growT + Time.deltaTime / Mathf.Max(0.0001f, GROW_DURATION));
                else
                    _fadeT = Mathf.Max(0f, _fadeT - Time.deltaTime / Mathf.Max(0.0001f, FADE_DURATION));

                // Resolve current beam direction & origin every frame (player may rotate).
                Vector2 dir = ResolveDirection();
                Vector2 origin = (Vector2)ProjectileExecutor.ResolveCasterCenter(transform);

                // Find beam endpoint, stopping at solid obstacles. Raycast always
                // uses the TRUE caster center so the visual back-offset doesn't
                // change what the beam can hit.
                var wallHit = Physics2D.Raycast(origin, dir, range, blockMask);
                Vector2 fullEnd = wallHit.collider != null ? wallHit.point : origin + dir * range;

                // VISUAL origin: pushed backwards along -dir so the rendered beam
                // appears to emerge from behind the caster sprite (which is also
                // rendered above the line via sortingLayer "WallsBottom").
                Vector2 visualOrigin = origin - dir * VISUAL_BACK_OFFSET;

                // Apply grow envelope: beam visually extends from visualOrigin to fullEnd over GROW_DURATION.
                Vector2 visibleEnd = Vector2.Lerp(visualOrigin, fullEnd, _growT);
                float visibleLength = Vector2.Distance(visualOrigin, visibleEnd);

                // Update both LineRenderers with current visible endpoints + fade alpha.
                // Visual start uses the back-offset origin so the beam emerges
                // from behind the player sprite.
                float alphaMult = _fadeT;
                if (_glowLine != null)
                {
                    _glowLine.SetPosition(0, visualOrigin);
                    _glowLine.SetPosition(1, visibleEnd);
                    var c = glowBaseColor; c.a = glowBaseColor.a * alphaMult;
                    _glowLine.startColor = c;
                    _glowLine.endColor = c;
                }
                if (_coreLine != null)
                {
                    _coreLine.SetPosition(0, visualOrigin);
                    _coreLine.SetPosition(1, visibleEnd);
                    var c = coreBaseColor; c.a = coreBaseColor.a * alphaMult;
                    _coreLine.startColor = c;
                    _coreLine.endColor = c;
                }

                // Anchor the impact burst at the (visible) beam tip facing outward.
                if (_impactGo != null)
                {
                    _impactGo.transform.position = visibleEnd;
                    Vector2 outward = -dir;
                    float deg = Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg;
                    _impactGo.transform.rotation = Quaternion.Euler(0f, 0f, deg);
                }

                // Anchor + size the trail PS to span visualOrigin → visibleEnd. Edge shape emits
                // along local +X with total length 2 * radius, so set midpoint position,
                // rotate so right-axis matches dir, and radius = halfLength.
                if (_trailGo != null)
                {
                    Vector2 mid = (visualOrigin + visibleEnd) * 0.5f;
                    _trailGo.transform.position = mid;
                    float deg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    _trailGo.transform.rotation = Quaternion.Euler(0f, 0f, deg);
                    if (_trailPS != null)
                    {
                        var shape = _trailPS.shape;
                        shape.radius = Mathf.Max(0.001f, visibleLength * 0.5f);
                    }
                }

                // While fading, skip damage + mana drain (gameplay has ended).
                if (!_fading)
                {
                    elapsed += Time.deltaTime;

                    // Damage tick (uses current visible end so half-extended beams hit only what they touch).
                    if (elapsed >= nextTick && _growT > 0.001f)
                    {
                        nextTick += TICK_INTERVAL;
                        damagedThisTick.Clear();

                        // Damage capsule spans from the TRUE caster center to the
                        // current visible tip, NOT from the visual back-offset
                        // origin (which is behind the player and would let the
                        // beam damage things behind the caster).
                        float damageLength = Mathf.Max(0f, Vector2.Distance(origin, visibleEnd));
                        Vector2 capsuleCenter = origin + dir * (damageLength * 0.5f);
                        float angle = Vector2.SignedAngle(Vector2.right, dir);

                        var hits = Physics2D.OverlapCapsuleAll(
                            capsuleCenter,
                            new Vector2(damageLength, beamHalfWidth * 2f),
                            CapsuleDirection2D.Horizontal,
                            angle,
                            _ctx.TargetLayers
                        );

                        foreach (var c in hits)
                        {
                            if (c.gameObject == gameObject) continue;
                            if (damagedThisTick.Contains(c.gameObject)) continue;

                            var health = c.GetComponent<Health>();
                            if (health != null && !health.IsDead)
                            {
                                health.TakeDamage(dmg);
                                damagedThisTick.Add(c.gameObject);
                            }
                        }
                    }

                    // Continuous mana drain. Accumulate fractional debt and consume integer chunks;
                    // if mana runs out, request stop (which will trigger the fade-out next frame).
                    if (mana != null)
                    {
                        _manaDebt += MANA_PER_SECOND * Time.deltaTime;
                        if (_manaDebt >= 1f)
                        {
                            int toConsume = Mathf.FloorToInt(_manaDebt);
                            _manaDebt -= toConsume;
                            if (!mana.TryConsume(toConsume))
                                _stopRequested = true;
                        }
                    }
                }

                // Loop exit: only when the fade has fully completed.
                if (_fading && _fadeT <= 0f) break;

                yield return null;
            }

            // Final impact flash so the explosion is visible even on a quick tap.
            Vector2 finalEnd = _impactGo != null ? (Vector2)_impactGo.transform.position : (Vector2)transform.position;
            if (VFXManager.Instance != null)
                VFXManager.Instance.SpawnImpact(finalEnd, _beamColor, 0.25f, 1.2f);

            // Cleanup all visual children spawned in BuildVisual.
            if (_glowLine != null) Destroy(_glowLine.gameObject);
            if (_coreLine != null) Destroy(_coreLine.gameObject);
            if (_impactGo != null)
            {
                if (_impactBurst != null) _impactBurst.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_impactGo, 0.5f); // let trailing particles fade out
            }
            if (_trailGo != null)
            {
                if (_trailPS != null) _trailPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_trailGo, 0.5f);
            }
            Destroy(this);
        }

        private Vector2 ResolveDirection()
        {
            var pc = GetComponent<PlayerController>();
            if (pc != null)
                return pc.FacingDirection;
            return _ctx.Direction;
        }

        private void OnDestroy()
        {
            if (_coreMaterial != null) Destroy(_coreMaterial);
            if (_glowMaterial != null) Destroy(_glowMaterial);
        }
    }
}
