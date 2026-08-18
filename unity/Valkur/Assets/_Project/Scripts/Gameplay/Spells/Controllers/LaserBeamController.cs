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
        // Additive blending accumulates: dst += rgb * a. With the core at 1.0 and the glow at
        // 0.34 the centreline summed past 1.0 on its own, so the beam clipped to pure white and
        // a travelling charge had no headroom left to be brighter in — it was mathematically
        // invisible before it ever reached the screen. These values leave the baseline near 0.6
        // so the charge can add ~0.5 and only the head blooms out.
        private const float CORE_ALPHA = 0.45f;
        private const float GLOW_ALPHA = 0.22f;

        /// <summary>Edge falloff of the two band textures. See BeamTextureLibrary.</summary>
        // Widths were retuned when the lines became additive and textured. A textured band
        // fades out toward its edges, so it reads narrower than its geometric width; and
        // additive blending reads brighter than alpha at the same value, so the glow's alpha
        // came down as its width went up. Net at scale 2: a ~2.8 px core inside a ~12 px
        // halo, against the previous 2 px core inside 6 px.
        private const float CORE_SOFTNESS = 0.25f;
        private const float GLOW_SOFTNESS = 0.80f;

        /// <summary>Charges in flight at once. Staggered in phase so the flow reads as steady.</summary>
        public const int PACKET_COUNT = 2;

        /// <summary>Trips per second each charge makes from the caster to the impact point.</summary>
        private const float PACKET_RATE = 1.35f;

        /// <summary>World length of one charge.</summary>
        public const float PACKET_LENGTH = 1.7f;

        /// <summary>Charge width, as a multiple of beam width. Wider than the core so it bulges.</summary>
        private const float PACKET_WIDTH_MULT = 1.35f;
        private const float PACKET_ALPHA = 0.60f;
        private const float PACKET_SOFTNESS = 0.5f;

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
        // The travelling charges. Short lines whose endpoints slide from the caster to the
        // impact point and restart. Geometry rather than a scrolling texture, because URP's
        // particle shaders sample UV0 raw and ignore the ST transform entirely — see the note
        // in BeamMaterialCache.
        private LineRenderer[] _packetLines;
        private float _packetPhase;
        private float _authoredCoreWidth;
        private float _authoredPacketWidth;
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

        /// <summary>
        /// Where one travelling charge sits on the beam right now.
        ///
        /// <paramref name="phase"/> is 0..1 through its trip. The charge is placed so that at
        /// phase 0 its head is at the caster and at phase 1 its tail has reached the impact
        /// point, which means it grows out of the muzzle and is absorbed at the far end rather
        /// than popping into existence fully formed at both ends.
        ///
        /// Returns false when the visible span has collapsed — a zero-length LineRenderer
        /// draws a dot at the origin, which reads as a stuck bead on the caster.
        /// </summary>
        public static bool ResolvePacketSpan(float phase, float beamLength, float packetLength,
                                             out float from, out float to)
        {
            from = 0f;
            to = 0f;
            if (beamLength <= 0f || packetLength <= 0f) return false;

            // Head sweeps 0 .. beamLength + packetLength so the whole charge clears the tip.
            float head = Mathf.Clamp01(phase) * (beamLength + packetLength);

            from = Mathf.Clamp(head - packetLength, 0f, beamLength);
            to = Mathf.Clamp(head, 0f, beamLength);

            // Below about a tenth of a tile the charge is subpixel at any sane zoom, and the
            // stretched texture degenerates into a flat smear of its brightest column.
            return (to - from) > 0.05f;
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
            Color packetBaseColor = _packetLines != null && _packetLines.Length > 0 && _packetLines[0] != null
                ? _packetLines[0].startColor : Color.white;

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
                // Hand height, the same origin the fireball leaves from. ResolveCasterCenter
                // returns the geometric middle of the sprite — the waist on a humanoid — so
                // the beam used to emerge from the caster's stomach.
                //
                // Resolved every frame rather than cached: the caster moves, and a beam is
                // held. And used for the raycast as well as the visuals, so what is drawn and
                // what is hit stay the same line.
                Vector2 origin = (Vector2)ProjectileExecutor.ResolveCastOrigin(transform);

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

                // Advance the charges. One phase drives all of them; each is offset by an
                // even fraction of a cycle so they arrive evenly spaced rather than together.
                _packetPhase += PACKET_RATE * Time.deltaTime;
                if (_packetPhase > 1f) _packetPhase -= 1f;   // keep it bounded forever

                float pulse = 1f + WIDTH_PULSE_AMOUNT * Mathf.Sin(Time.time * WIDTH_PULSE_HZ);

                if (_glowLine != null)
                {
                    // Glow and core breathe in antiphase, so the beam thickens and thins
                    // without its total brightness visibly changing.
                    float w = _authoredGlowWidth * (2f - pulse) * _growT;
                    _glowLine.startWidth = w;
                    _glowLine.endWidth = w;
                }
                if (_coreLine != null)
                {
                    float w = _authoredCoreWidth * pulse * _growT;
                    _coreLine.startWidth = w;
                    _coreLine.endWidth = w;
                }

                if (_packetLines != null)
                {
                    float pw = _authoredPacketWidth * _growT;
                    var pc = packetBaseColor; pc.a = packetBaseColor.a * alphaMult;

                    for (int i = 0; i < _packetLines.Length; i++)
                    {
                        var line = _packetLines[i];
                        if (line == null) continue;

                        float phase = Mathf.Repeat(_packetPhase + (i / (float)_packetLines.Length), 1f);
                        bool visible = ResolvePacketSpan(phase, visibleLength, PACKET_LENGTH,
                                                         out float from, out float to);

                        line.enabled = visible;
                        if (!visible) continue;

                        line.startWidth = pw;
                        line.endWidth = pw;
                        line.startColor = pc;
                        line.endColor = pc;
                        line.SetPosition(0, visualOrigin + dir * from);
                        line.SetPosition(1, visualOrigin + dir * to);
                    }
                }

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
            if (_packetLines != null)
                foreach (var p in _packetLines)
                    if (p != null) Destroy(p.gameObject);
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
