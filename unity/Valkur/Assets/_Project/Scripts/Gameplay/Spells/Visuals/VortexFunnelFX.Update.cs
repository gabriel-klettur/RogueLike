using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Driving the funnel: spin-up, sway, the debris riding the wall, and the tear-apart.
    ///
    /// <para>The clock lives in the controller, which owns the spell's duration; this half only
    /// says what the rig looks like at a given moment. That split is why the rig has no
    /// <c>Update</c> of its own and why nothing here reads <c>Time.time</c>.</para>
    /// </summary>
    internal sealed partial class VortexFunnelFX
    {
        /// <summary>
        /// How long the funnel takes to climb out of the ground. PUBLIC because the controller
        /// ramps its FORCE over the same window — the two were separate constants holding the
        /// same number, which is a desync waiting for whoever edits one of them.
        /// </summary>
        public const float SpinUpSeconds = 0.90f;

        /// <summary>
        /// How fast the rig's idea of its own velocity catches up to the truth. A tornado has
        /// mass: leaning the instant the ground contact turns reads as the whole column being
        /// dragged sideways by a hand.
        /// </summary>
        private const float TRAVEL_SMOOTHING = 2.4f;

        /// <summary>
        /// World units the flared top leans per unit/second of travel, at full height. The lean
        /// grows with height and reaches zero at the neck, because the neck is the part actually
        /// touching the ground and the top is the part being left behind.
        /// </summary>
        private const float LEAN_PER_SPEED = 0.45f;

        /// <summary>
        /// How long the torn-up ground trails the funnel that lifted it. Zero makes every piece
        /// move rigidly with the root, and a rig whose every child shares one velocity reads as
        /// a decal being slid across the floor rather than as a thing travelling over it.
        /// </summary>
        private const float DEBRIS_LAG_SECONDS = 0.55f;

        /// <summary>How far the flared top leans, as a fraction of the radius. A rigid cone is a prop.</summary>
        private const float SWAY_FRAC = 0.10f;

        /// <summary>How far the bands fly apart as the spell ends. A vortex that dims in place looks switched off.</summary>
        private const float TEAR_SPREAD = 1.70f;

        private const float LIGHT_BASE_INTENSITY = 1.15f;

        /// <summary>
        /// The band count the per-band alpha below was tuned against. Every band is on an
        /// ADDITIVE material, so what a pixel receives is the SUM of every band overlapping it —
        /// raising <c>BANDS</c> without dividing by this does not make the funnel finer, it
        /// makes it brighter, and a red vortex washes out to white through the middle of its own
        /// column. Measured at 3.6 units: the summed band alpha came to 7.97 at eighteen bands
        /// against 3.99 at nine. More bands buys RESOLUTION, not light.
        /// </summary>
        private const float BAND_ALPHA_REFERENCE_COUNT = 9f;

        /// <summary>
        /// Compensates the per-band alpha for how much SCREEN AREA one band covers. Thickness is
        /// a brightness dial for exactly the reason the COUNT above is — the additive material
        /// sums whatever overlaps a pixel — and the two are independent axes onto the same
        /// total. Measured when the rings were doubled in weight: one band's covered area went
        /// up x2.00, so without this the column arrives twice as bright and a red vortex washes
        /// to white, losing the identity that separates it from the blue one at a glance. Raise
        /// it towards 1 if the funnel should read heavier as well as thicker.
        /// </summary>
        private const float BAND_AREA_COMPENSATION = 0.5f;

        /// <summary>
        /// How much of the funnel's own height is kept faint at the bottom. `vortex_push` sets
        /// `followCaster`, so the caster is standing INSIDE the neck — and eighteen additive
        /// bands summing to 3.98 over their body washes them out of their own spell. The band
        /// radius at chest height is 1.17 units against a 0.9-wide character, so this is not a
        /// near miss: without the clearing they are inside the brightest part of it.
        /// </summary>
        private const float NECK_CLEAR_HEIGHT = 0.22f;

        private bool _endShockFired;
        private Vector2 _travelRaw;
        private Vector2 _travel;

        /// <summary>
        /// Where the funnel's surface sits at a given height, relative to the axis. ONE owner:
        /// the bands draw the cone, the debris and dust ride it and the discharges run along
        /// it, so a lean the bands know about and the arcs do not is a bolt hanging in the air
        /// beside the shape it is supposed to be crawling on.
        /// </summary>
        private Vector2 WallOffset(float height01)
        {
            float noise = Mathf.Sin(_age * 1.7f + height01 * 2.4f) * _radius * SWAY_FRAC * height01;
            return new Vector2(
                noise + _travel.x * LEAN_PER_SPEED * height01,
                // Squashed on Y for the same reason every horizontal circle here is: a lean
                // "away from the camera" covers less screen than the same lean across it.
                _travel.y * LEAN_PER_SPEED * GROUND_SQUASH * height01);
        }

        /// <summary>How far the ground it tore up trails behind it.</summary>
        private Vector3 DebrisLag() => -(Vector3)(_travel * DEBRIS_LAG_SECONDS);

        /// <summary>
        /// Advance the rig.
        /// </summary>
        /// <param name="deltaTime">Frame time.</param>
        /// <param name="fade">Overall visibility, 0..1. The controller ramps this down at the end.</param>
        /// <param name="dissipate">0 while the vortex holds, climbing to 1 as it is torn apart.</param>
        /// <summary>
        /// Tell the rig how fast its own root is moving through the world, in world units per
        /// second. Drives the lean and the debris lag. A separate setter rather than another
        /// Tick argument, so a rig that is standing still needs to say nothing.
        /// </summary>
        public void SetTravel(Vector2 worldVelocity) => _travelRaw = worldVelocity;

        public void Tick(float deltaTime, float fade, float dissipate)
        {
            _age += deltaTime;
            _travel = Vector2.Lerp(_travel, _travelRaw,
                                   1f - Mathf.Exp(-TRAVEL_SMOOTHING * deltaTime));

            float grown = EaseOutCubic(Mathf.Clamp01(_age / SpinUpSeconds));
            float pulse = 0.86f + 0.14f * Mathf.Sin(_age * 5.5f);

            // One shockwave on the way out as well as the one on the way in. Fired the frame
            // the tear-apart begins, which is the only frame that can tell the difference.
            if (dissipate > 0f && !_endShockFired) { _endShockFired = true; FireShockwave(); }

            TickGround(fade, pulse, dissipate);
            TickShockwave(deltaTime, fade);
            TickStreaks(deltaTime, grown, fade, dissipate);
            TickBands(grown, fade, pulse, dissipate);
            TickDebris(deltaTime, grown, fade, dissipate);
            TickDust(deltaTime, grown, fade, dissipate);
            TickArcs(deltaTime, grown, fade, dissipate);
            SetLightIntensity(LIGHT_BASE_INTENSITY * pulse * fade);
        }

        private void TickGround(float fade, float pulse, float dissipate)
        {
            // The ring pulses in BRIGHTNESS and never in size. Its radius is a promise about
            // where the force reaches, and a circle that breathes is a promise that moves.
            if (_groundRing != null)
                SetAlpha(_groundRing, 0.62f * pulse * fade * (1f - dissipate * 0.5f));

            if (_groundGlow != null)
                SetAlpha(_groundGlow, 0.30f * pulse * fade * (1f - dissipate));
        }

        private void TickBands(float grown, float fade, float pulse, float dissipate)
        {
            if (_bandRenderers == null) return;

            float spread = 1f + dissipate * TEAR_SPREAD;

            for (int i = 0; i < _bandRenderers.Length; i++)
            {
                float t = _bandHeight01[i];

                // Below its own height the band is simply not there yet, which is what makes
                // the funnel CLIMB out of the floor rather than fade in all at once.
                float reveal = Mathf.Clamp01((grown - t * 0.55f) / 0.45f);
                if (reveal <= 0f) { SetAlpha(_bandRenderers[i], 0f); continue; }

                float radius = _radius * Mathf.Lerp(NECK_FRAC, FLARE_FRAC, Mathf.Pow(t, 0.75f)) * spread;
                float height = Height * t * grown + dissipate * Height * 0.35f * t;

                // The lean grows with height and none of it reaches the neck: a tornado is
                // pinned where it touches down and loose where it opens.
                Vector2 offset = WallOffset(t);

                float span = radius / BAND_UNIT_RADIUS;
                _bandPivots[i].localPosition = new Vector3(offset.x, height + offset.y, 0f);
                _bandPivots[i].localScale = new Vector3(span, span * GROUND_SQUASH, 1f);

                float spin = SPIN_DEGREES * _spinSign * (1f + t * SPIN_TWIST);
                _bandSpinners[i].localRotation = Quaternion.Euler(0f, 0f, _bandPhase[i] + _age * spin);

                // Densest through the middle of the column: the top disperses and the very
                // bottom is buried in the ground glow.
                float body = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * 0.55f + 0.45f;
                float neckClear = Mathf.Clamp01(t / NECK_CLEAR_HEIGHT);
                float alpha = reveal * body * neckClear * (0.40f + 0.22f * pulse) * fade
                              * (BAND_ALPHA_REFERENCE_COUNT / _bandRenderers.Length)
                              * BAND_AREA_COMPENSATION;
                if (dissipate > 0f) alpha *= Mathf.Pow(1f - dissipate, 0.8f);

                SetAlpha(_bandRenderers[i], alpha);
            }
        }

        private void TickDust(float deltaTime, float grown, float fade, float dissipate)
        {
            if (_dust == null) return;

            // Pull drags its debris DOWN into the neck; push throws it UP and out. The sign is
            // the same statement the force makes, and the two disagreeing is the effect telling
            // the player the opposite of what the spell does.
            float climbDirection = _spinSign > 0f ? -1f : 1f;
            float spread = 1f + dissipate * TEAR_SPREAD;

            for (int i = 0; i < _dust.Length; i++)
            {
                _dustClimb[i] = Mathf.Repeat(_dustClimb[i] + climbDirection * DUST_CLIMB_SPEED * deltaTime, 1f);
                _dustAngle[i] += DUST_SWEEP * _spinSign * deltaTime;

                float t = _dustClimb[i];
                float radius = _radius * Mathf.Lerp(NECK_FRAC, FLARE_FRAC, Mathf.Pow(t, 0.75f)) * spread;
                float height = Height * t * grown;
                Vector2 offset = WallOffset(t);

                float angle = _dustAngle[i];
                float depth = Mathf.Sin(angle);   // -1 nearest the camera, +1 furthest

                _dust[i].localPosition = new Vector3(
                    offset.x + Mathf.Cos(angle) * radius,
                    height + offset.y + depth * radius * GROUND_SQUASH,
                    0f);

                // Near scraps are bigger, brighter and drawn IN FRONT of the funnel wall; far
                // ones go behind it. That flip is the only statement in the rig that the debris
                // is going AROUND something rather than sliding across it.
                float near01 = 0.5f - depth * 0.5f;
                _dust[i].localScale = Vector3.one * (_dustSize[i] * Mathf.Lerp(0.65f, 1.30f, near01));
                _dust[i].localRotation = Quaternion.Euler(0f, 0f, (angle * 2.3f + i) * Mathf.Rad2Deg);

                _dustRenderers[i].sortingOrder = depth < 0f ? ORDER_DUST : ORDER_BAND - 2;

                // Faded at both ends of the climb, so a scrap appears and vanishes inside the
                // column instead of popping at the wrap.
                float ends = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                float alpha = Mathf.Lerp(0.35f, 0.95f, near01) * ends * grown * fade * (1f - dissipate);
                SetAlpha(_dustRenderers[i], alpha);
            }
        }

        private void SetLightIntensity(float intensity)
        {
            if (_light == null) return;
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, intensity); }
            catch { }
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            var color = renderer.color;
            color.a = Mathf.Clamp01(alpha);
            renderer.color = color;
        }

        private static float EaseOutCubic(float t)
        {
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }
    }
}
