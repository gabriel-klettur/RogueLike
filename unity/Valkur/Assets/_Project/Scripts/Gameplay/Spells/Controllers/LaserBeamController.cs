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
    public partial class LaserBeamController : MonoBehaviour
    {
        private const float TICK_INTERVAL = 0.25f;

        /// <summary>Fallback max travel distance (world units) when the spell asset
        /// leaves <see cref="SpellDefinition.range"/> at 0.</summary>
        public const float DEFAULT_RANGE = 10f;

        private const float DEFAULT_BEAM_WIDTH = 0.16f;

        // Visual layering: bright thin core inside a softer wider glow.
        private const float CORE_WIDTH_MULT = 0.55f;   // core line width = beam width * this
        private const float GLOW_WIDTH_MULT = 2.4f;    // outer glow width = beam width * this
        private const float CORE_ALPHA = 1.0f;
        private const float GLOW_ALPHA = 0.34f;

        /// <summary>Edge falloff of the two band textures. See BeamTextureLibrary.</summary>
        // Widths were retuned when the lines became additive and textured. A textured band
        // fades out toward its edges, so it reads narrower than its geometric width; and
        // additive blending reads brighter than alpha at the same value, so the glow's alpha
        // came down as its width went up. Net at scale 2: a ~2.8 px core inside a ~12 px
        // halo, against the previous 2 px core inside 6 px.
        private const float CORE_SOFTNESS = 0.25f;
        private const float GLOW_SOFTNESS = 0.80f;

        /// <summary>World units of beam per repeat of the energy texture.</summary>
        private const float SCROLL_TILE_WORLD_LENGTH = 1.6f;

        /// <summary>Texture repeats per second travelling along the beam, toward the target.</summary>
        private const float SCROLL_SPEED = 2.2f;

        /// <summary>Width wobble, as a fraction of the authored width. Keeps the beam alive.</summary>
        private const float WIDTH_PULSE_AMOUNT = 0.12f;
        private const float WIDTH_PULSE_HZ = 11f;

        /// <summary>Mana drained per second while the beam is active.</summary>
        public const float MANA_PER_SECOND = 2f;

        /// <summary>Maximum gap between two <see cref="Refresh"/> calls before the beam auto-stops.</summary>
        public const float AUTO_STOP_GRACE = 0.15f;

        /// <summary>Time (s) for the beam to grow from origin to full length when started.</summary>
        public const float GROW_DURATION = 0.08f;

        /// <summary>Time (s) for the beam to fade out after Stop() is requested.</summary>
        public const float FADE_DURATION = 0.12f;

        /// <summary>
        /// Visual-only forward offset (world units, ≈ tiles) applied to the beam's
        /// rendered start point along the firing direction. Mirrors the slash spawn
        /// convention (1.25 tiles in front of the body centre) so every spell except
        /// Dash visually originates from the same point. Damage / raycast still use
        /// the true caster centre; only the rendered line, trail particles and
        /// impact-tip are anchored at the forward offset.
        /// </summary>
        public const float VISUAL_FORWARD_OFFSET = 1.25f;

        private LineRenderer _coreLine;
        private LineRenderer _glowLine;
        // Scroll lives in a MaterialPropertyBlock rather than on the material: the material
        // is shared across every beam in the scene, so writing tiling/offset on it would
        // make two simultaneous beams scroll as one.
        private MaterialPropertyBlock _coreBlock;
        private MaterialPropertyBlock _glowBlock;
        private float _scrollOffset;
        private float _authoredCoreWidth;
        private float _authoredGlowWidth;
        private ParticleSystem _impactBurst;
        private GameObject _impactGo;
        private ParticleSystem _trailPS;
        private GameObject _trailGo;
        // Muzzle: continuous emitter at the beam's visual origin. Mirrors the
        // fireball spawn flash but stays alive for the full channel — only stops
        // emitting when the fade-out begins (Stop / mana-out / channelDuration end).
        private ParticleSystem _muzzlePS;
        private GameObject _muzzleGo;
        private float _muzzleBeamWidth;   // cached for per-frame position jitter
        private Color _beamColor;

        // Lightning beam mode — when the spell's vfxPreset is "lightning_emitter"
        // we skip the LineRenderer beam entirely and instead emit zig-zag bolt
        // particles along the beam path. Same gameplay as a regular laser; only
        // the visual swaps. Null in regular laser mode.
        private ParticleSystem _lightningPS;
        private GameObject _lightningGo;
        // Spell asset's vfxPreset string used to decide the visual mode.
        private const string LIGHTNING_BEAM_PRESET = "lightning_emitter";
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
                    // Stop spawning new trail/impact/muzzle/lightning particles
                    // (existing ones still die naturally over their startLifetime
                    // so the visual fades out smoothly rather than popping off).
                    if (_trailPS != null)     _trailPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    if (_impactBurst != null) _impactBurst.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    if (_muzzlePS != null)    _muzzlePS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    if (_lightningPS != null) _lightningPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }

                // Advance grow / fade envelopes.
                if (!_fading)
                    _growT = Mathf.Min(1f, _growT + Time.deltaTime / Mathf.Max(0.0001f, GROW_DURATION));
                else
                    _fadeT = Mathf.Max(0f, _fadeT - Time.deltaTime / Mathf.Max(0.0001f, FADE_DURATION));

                // Resolve current beam direction & origin every frame (player may rotate).
                Vector2 dir = ResolveDirection();
                Vector2 origin = (Vector2)ProjectileExecutor.ResolveCasterCenter(transform);

                // Player-only: clamp the effective range to the cursor's distance
                // from the caster centre. If the mouse is BEFORE the spell's max
                // range, the beam stops at the cursor; otherwise the full range
                // applies. NPCs use the full range as before.
                float effectiveRange = ResolveEffectiveRange(origin, range);

                // Find beam endpoint, stopping at solid obstacles. Raycast uses the
                // true caster centre + the (possibly mouse-clamped) effective range.
                var wallHit = Physics2D.Raycast(origin, dir, effectiveRange, blockMask);
                Vector2 fullEnd = wallHit.collider != null ? wallHit.point : origin + dir * effectiveRange;

                // VISUAL origin: pushed FORWARD along dir so the rendered beam
                // emerges from ~1 tile in front of the caster — same convention as
                // the slash spawn point. Always at least 1.25 tiles ahead of body
                // centre regardless of mouse distance.
                Vector2 visualOrigin = origin + dir * VISUAL_FORWARD_OFFSET;
                // If the mouse is closer than the forward offset, fullEnd would land
                // BEHIND visualOrigin and the line renderer would draw backwards.
                // Collapse the beam visually in that case (mouse on top of caster).
                if (Vector2.Dot(fullEnd - visualOrigin, dir) < 0f)
                    fullEnd = visualOrigin;

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

                // ── Energy flow and breathing ────────────────────────────────
                // The beam is otherwise geometrically static once it has grown: same two
                // points, same width, same colour, frame after frame. These two are what
                // separate a beam that is ON from a beam that is FIRING.

                // Scroll toward the target. Negative because texture offset moves the
                // sampling window, so subtracting walks the pattern forward along +U.
                _scrollOffset -= SCROLL_SPEED * Time.deltaTime;
                if (_scrollOffset < -1f) _scrollOffset += 1f;   // keep it bounded forever

                // Tiling from world length, so a 2-unit beam and a 6-unit beam show the
                // same size of energy pattern instead of one stretched copy.
                float tiling = Mathf.Max(1f, visibleLength / SCROLL_TILE_WORLD_LENGTH);

                float pulse = 1f + WIDTH_PULSE_AMOUNT * Mathf.Sin(Time.time * WIDTH_PULSE_HZ);

                if (_glowLine != null)
                {
                    _glowBlock = _glowBlock ?? new MaterialPropertyBlock();
                    BeamMaterialCache.ApplyScroll(_glowLine, _glowBlock, tiling, _scrollOffset * 0.5f);
                    // The glow breathes in antiphase with the core, so the beam looks like it
                    // is pressurised rather than simply flickering.
                    float w = _authoredGlowWidth * (2f - pulse) * _growT;
                    _glowLine.startWidth = w;
                    _glowLine.endWidth = w;
                }
                if (_coreLine != null)
                {
                    _coreBlock = _coreBlock ?? new MaterialPropertyBlock();
                    BeamMaterialCache.ApplyScroll(_coreLine, _coreBlock, tiling, _scrollOffset);
                    float w = _authoredCoreWidth * pulse * _growT;
                    _coreLine.startWidth = w;
                    _coreLine.endWidth = w;
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

                // Anchor the muzzle emitter at the beam's visual origin with a tiny
                // random jitter so the whole emitter visibly vibrates each frame.
                // Combined with the ParticleSystem.noise module this sells the
                // "energy crackling at the staff tip" feel.
                if (_muzzleGo != null)
                {
                    Vector2 jitter = Random.insideUnitCircle * (_muzzleBeamWidth * 0.7f);
                    _muzzleGo.transform.position = (Vector3)(visualOrigin + jitter);
                }

                // Anchor + size the lightning emitter to span visualOrigin → visibleEnd.
                // Edge shape emits along local +X with total length 2 * radius, so set
                // midpoint position, rotate so right-axis matches dir, radius = halfLength.
                if (_lightningGo != null)
                {
                    Vector2 mid = (visualOrigin + visibleEnd) * 0.5f;
                    _lightningGo.transform.position = mid;
                    float deg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    _lightningGo.transform.rotation = Quaternion.Euler(0f, 0f, deg);
                    if (_lightningPS != null)
                    {
                        var shape = _lightningPS.shape;
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
            if (_muzzleGo != null)
            {
                if (_muzzlePS != null) _muzzlePS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_muzzleGo, 0.5f);
            }
            if (_lightningGo != null)
            {
                if (_lightningPS != null) _lightningPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_lightningGo, 0.5f);
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

        /// <summary>
        /// Returns the beam's effective max travel distance for this frame.
        /// For the player, clamps to the cursor's world-space distance from the
        /// caster centre so the beam stops at the cursor when the cursor is closer
        /// than the spell's nominal range. NPCs always get the full range.
        /// </summary>
        private float ResolveEffectiveRange(Vector2 origin, float maxRange)
        {
            // Only the player controls the laser via mouse — NPCs cast in a fixed
            // direction at full range.
            var pc = GetComponent<PlayerController>();
            if (pc == null) return maxRange;

            var cam = Camera.main;
            if (cam == null) return maxRange;

            if (!Valkur.Core.Input.MouseInputManager.TryGetWorldMousePosition(
                    out Vector2 mouseWorld,
                    cam,
                    requireInView: true,
                    requireApplicationFocus: false))
            {
                return maxRange;
            }

            float mouseDist = Vector2.Distance(origin, mouseWorld);
            return Mathf.Min(maxRange, mouseDist);
        }

        private void OnDestroy()
        {
        }
    }
}
