using UnityEngine;

namespace Valkur.Gameplay.Feel
{
    /// <summary>
    /// Every piece of arithmetic the camera director does, as pure functions.
    ///
    /// Nothing here touches a Camera, a Transform, Cinemachine, or a clock. That is the
    /// point: the reason the old <c>CameraShake</c> shipped two live bugs for months — an
    /// amplitude that ratcheted upward and never came back down, and a restore step that
    /// subtracted an offset the Cinemachine brain had already erased — is that neither could
    /// be observed without running the game and staring at it. All of this is reachable from
    /// EditMode with no scene.
    /// </summary>
    internal static class CameraFeelMath
    {
        // ── Lead ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The aim vector with the director's own offset removed.
        ///
        /// The player's facing is derived from <c>camera.ScreenToWorldPoint(cursor)</c>. If
        /// the camera leads toward the aim and the aim is read back off that same camera, the
        /// system solves <c>d = normalize(L·d + S)</c> — and as the cursor approaches the
        /// player, every direction becomes a fixed point. The camera would wander on noise.
        /// Subtracting the offset the director itself applied recovers the vector a rigid
        /// camera would have produced, making the loop gain exactly zero.
        /// </summary>
        internal static Vector2 ResolveAimVector(Vector2 mouseWorld, Vector2 playerPos,
                                                 Vector2 appliedOffset, float deadzoneWu)
        {
            Vector2 aim = mouseWorld - appliedOffset - playerPos;
            if (aim.sqrMagnitude < deadzoneWu * deadzoneWu) return Vector2.zero;
            return aim.normalized;
        }

        /// <summary>
        /// Where the camera wants to sit relative to the player.
        ///
        /// The movement term scales with speed; the aim term deliberately does not, so a
        /// standing player can still look around the room with the cursor.
        /// </summary>
        internal static Vector2 ResolveLeadTarget(Vector2 moveInput, Vector2 aimDir,
                                                  float moveLeadWu, float aimIdleWu,
                                                  float aimMovingWu, float maxLeadWu)
        {
            float speed01 = Mathf.Clamp01(moveInput.magnitude);
            Vector2 moveDir = speed01 > 0.0001f ? moveInput.normalized : Vector2.zero;

            Vector2 lead = moveDir * (moveLeadWu * speed01)
                         + aimDir * Mathf.Lerp(aimIdleWu, aimMovingWu, speed01);

            return Vector2.ClampMagnitude(lead, maxLeadWu);
        }

        /// <summary>
        /// Holds the lead still while the target is within a fraction of a screen pixel.
        ///
        /// <c>CameraPixelSnap</c> rounds the final camera position to the pixel lattice, so a
        /// lead that creeps by a thousandth of a unit does not move the camera smoothly — it
        /// flickers it between two pixel rows. Below the threshold, not moving is the correct
        /// answer.
        /// </summary>
        internal static Vector2 ApplyLeadDeadzone(Vector2 current, Vector2 target,
                                                  float deadzonePixels, float worldUnitsPerPixel)
        {
            float threshold = deadzonePixels * worldUnitsPerPixel;
            return (target - current).sqrMagnitude < threshold * threshold ? current : target;
        }

        // ── Springs ───────────────────────────────────────────────────────────

        /// <summary>
        /// Damped harmonic oscillator, solved in closed form.
        ///
        /// The obvious implementation — semi-implicit Euler, sub-stepped for stability — is
        /// wrong here in a way that only a test catches. Its very first step damps the
        /// velocity by <c>2*zeta*omega*h</c> before the position has moved at all, which at
        /// omega 26 and a 240 Hz sub-step costs 20% of the peak. An authored "0.14 world
        /// units" would then deliver 0.11, and by a different fraction for every damping
        /// ratio, so two cues with the same number would land differently for no reason a
        /// designer could see.
        ///
        /// The analytic solution has no such error, is exact at any step size, and is
        /// unconditionally stable — no sub-stepping needed.
        /// </summary>
        internal static void SpringStep(ref Vector2 x, ref Vector2 v, Vector2 target,
                                        float omega, float zeta, float dt)
        {
            if (dt <= 0f || omega <= 0f) return;

            Vector2 d = x - target;

            if (zeta < 1f)
            {
                float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
                float decay = Mathf.Exp(-zeta * omega * dt);
                float cos = Mathf.Cos(wd * dt);
                float sin = Mathf.Sin(wd * dt);

                Vector2 c = (v + zeta * omega * d) / wd;
                Vector2 next = decay * (d * cos + c * sin);
                v = decay * ((c * wd - zeta * omega * d) * cos - (d * wd + zeta * omega * c) * sin);
                x = target + next;
                return;
            }

            // Critically damped. An over-damped ratio is treated as critical: nothing in the
            // profile authors one, and the difference is imperceptible at these durations.
            float e = Mathf.Exp(-omega * dt);
            Vector2 k = v + omega * d;
            x = target + (d + k * dt) * e;
            v = (v - k * (omega * dt)) * e;
        }

        /// <summary>
        /// The initial velocity that makes a spring impulse peak at exactly one world unit.
        ///
        /// Without it, an authored "0.14 world units" would be a velocity, not a distance,
        /// and the actual displacement would silently depend on the damping — so two cues
        /// with the same authored amplitude and different zeta would hit differently for no
        /// reason a designer could see.
        /// </summary>
        internal static float ImpulseGainForUnitPeak(float omega, float zeta)
        {
            if (omega <= 0f) return 0f;

            // Critically or over-damped: peak of (v0 t e^-wt) is v0 / (w e).
            if (zeta >= 1f) return omega * Mathf.Exp(1f);

            // Under-damped: x(t) = (v0/wd) e^-zwt sin(wd t), peak at t = atan(wd/(zw))/wd.
            float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
            float tPeak = Mathf.Atan2(wd, zeta * omega) / wd;
            float peakForUnitVelocity = Mathf.Exp(-zeta * omega * tPeak) * Mathf.Sin(wd * tPeak) / wd;
            return peakForUnitVelocity > 0.0001f ? 1f / peakForUnitVelocity : omega * Mathf.Exp(1f);
        }

        // ── Noise ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Two independent Perlin lanes, in [-1,1] each.
        ///
        /// Perlin rather than <c>Random.value</c>: white noise reads as a rendering fault
        /// because successive frames are uncorrelated, while a smooth signal reads as the
        /// camera being physically shaken. This is the single biggest visible difference
        /// between the old shake and this one.
        /// </summary>
        internal static Vector2 ShakeSample(float seedX, float seedY, float time,
                                            float frequencyHz, float normalisation)
        {
            float t = time * frequencyHz;
            float x = (Mathf.PerlinNoise(seedX, t) * 2f - 1f) * normalisation;
            float y = (Mathf.PerlinNoise(seedY, t) * 2f - 1f) * normalisation;
            return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
        }

        /// <summary>
        /// Shake amplitude from trauma. Quadratic, so light hits stay subtle while heavy ones
        /// are unmistakable — a linear curve makes everything feel like the same event.
        /// </summary>
        internal static float TraumaToAmplitude(float trauma, float maxShakeWu)
            => trauma * trauma * maxShakeWu;

        /// <summary>
        /// Trauma accumulates. The old shake took <c>Math.Max</c> of the new and current
        /// amplitude and never lowered it, so one Whirl slash raised every later shake in the
        /// session — including effects authored a quarter as strong — for as long as the game
        /// ran.
        /// </summary>
        internal static float AddTrauma(float current, float add)
            => Mathf.Clamp01(current + Mathf.Max(0f, add));

        internal static float DecayTrauma(float current, float perSecond, float dt)
            => Mathf.Max(0f, current - Mathf.Max(0f, perSecond) * Mathf.Max(0f, dt));

        // ── Cue scaling ───────────────────────────────────────────────────────

        /// <summary>Scales a cue by how hard the blow was, never below 55% of the authored value.</summary>
        internal static float ScaleByDamage(float baseValue, int damage, float damageReference)
        {
            if (damageReference <= 0f) return baseValue;
            return baseValue * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(damage / damageReference));
        }

        /// <summary>Escalates a cue with the live combo, saturating at the cap.</summary>
        internal static float ScaleByCombo(float value, int combo, int comboCap, float comboGain)
        {
            if (comboCap <= 0) return value;
            return value * (1f + comboGain * Mathf.Clamp01(combo / (float)comboCap));
        }

        /// <summary>
        /// How badly that hurt, as a fraction of the health bar rather than an absolute
        /// number — so a hit that takes a quarter of the player's life reads the same whether
        /// they have 40 max HP or 400.
        /// </summary>
        internal static float SeverityFromDamage(int amount, int maxHp, float severeFraction)
        {
            if (maxHp <= 0 || severeFraction <= 0f) return 0f;
            return Mathf.Clamp01((amount / (float)maxHp) / severeFraction);
        }

        // ── Classification ────────────────────────────────────────────────────

        /// <summary>
        /// A swing worth a whiff response: it deals damage and it reaches nowhere. Verified
        /// against the shipped catalog — every <c>slash_*</c> has range 0, distance 0 and
        /// damage above 0; fireball has range 15; dash has distance 4.5 and damage 0.
        /// </summary>
        internal static bool IsMeleeSwing(float range, float distance, float damage)
            => damage > 0f && range <= 0f && distance <= 0f;

        /// <summary>
        /// A cast heavy enough to move the camera. Note that mana cost is 0-2 across the
        /// entire shipped catalog, so the duration fields are what discriminate today; the
        /// mana threshold is here so retuned costs work without a code change.
        /// </summary>
        internal static bool IsHeavyCast(float prepareDuration, float cooldownDuration,
                                         float manaCost, float heavyPrepare,
                                         float heavyCooldown, float heavyMana)
            => prepareDuration >= heavyPrepare
            || cooldownDuration >= heavyCooldown
            || manaCost >= heavyMana;

        // ── Geometry ──────────────────────────────────────────────────────────

        /// <summary>Direction from one point to another, never NaN.</summary>
        internal static Vector2 SafeDirection(Vector2 from, Vector2 to, Vector2 fallback)
        {
            Vector2 delta = to - from;
            return delta.sqrMagnitude <= 0.000001f ? fallback : delta.normalized;
        }

        /// <summary>
        /// True when the player crossed more ground in one frame than they could have walked.
        /// A warp is not movement, and chasing one drags the whole transient layer across the
        /// map. Self-detecting here rather than at the call sites, because at least one
        /// teleport path — <c>ZonePortal</c> — writes the transform directly and notifies
        /// nobody.
        /// </summary>
        internal static bool IsTeleport(Vector2 previous, Vector2 current, float thresholdWu)
            => (current - previous).sqrMagnitude > thresholdWu * thresholdWu;
    }
}
