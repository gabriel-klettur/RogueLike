using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The nine cast gestures. Each is a sentence about what the caster is doing, and every
    /// field below is that sentence made numeric — so a change here is a change of intent,
    /// not a tweak.
    /// </summary>
    internal static class CastFlourishFamilies
    {
        /// <summary>
        /// <b>Hurl</b> — throwing something. Power spirals into the hands and is flung along
        /// the cast direction; the lance grows with the spell's own reach, so a short-range
        /// bolt and a long one do not look identical.
        /// </summary>
        public static CastFlourishProfile Hurl(SpellDefinition spell)
        {
            float reach = spell != null ? CastFlourishProfile.FirstPositive(8f, spell.range, spell.distance) : 8f;
            return new CastFlourishProfile
            {
                FamilyName = "Hurl",
                Duration = 0.52f, Gather = 0.20f, Release = 0.07f,
                Sigil = SigilMotion.Contract, SigilRadius = 1.25f, SigilSpin = 95f, SigilAlpha = 0.60f,
                Approach = MoteApproach.SpiralIn, Departure = MoteDeparture.ThrowForward,
                MoteCount = 16, MoteRadius = 1.30f,
                MoteSpeedMin = 4.0f, MoteSpeedMax = 10.0f, MoteSize = 0.18f, MoteSpread = 0.55f,
                Lance = LanceAim.Forward, LanceLength = Mathf.Clamp(reach * 0.22f, 1.1f, 2.6f),
                Burst = BurstOrigin.Hand, BurstRadius = 1.75f,
                AuraDrive = 1.35f, BodyDrive = 0.55f, LightMul = 1f, HandScale = 1.40f,
                HandAnchored = true,
            };
        }

        /// <summary>
        /// <b>Edge</b> — a swing, not a conjuring. No circle is drawn on the ground because
        /// nothing is being summoned: the motes are struck OFF the arc the blade travels, and
        /// the whole thing is over in a third of a second, because a slash that glows for half
        /// a second after it has landed reads as a spell rather than as a cut.
        /// </summary>
        public static CastFlourishProfile Edge(SpellDefinition spell)
        {
            float arc = spell != null && spell.arcRangeDegrees > 0f ? spell.arcRangeDegrees : 110f;
            return new CastFlourishProfile
            {
                FamilyName = "Edge",
                Duration = 0.30f, Gather = 0.05f, Release = 0.05f,
                Sigil = SigilMotion.None, SigilRadius = 0f, SigilSpin = 0f, SigilAlpha = 0f,
                Approach = MoteApproach.SweepArc, Departure = MoteDeparture.PushOutward,
                // Wider arcs fling more, and further: a whirl throws sparks all the way round
                // while a thrust barely disturbs the air.
                MoteCount = Mathf.Clamp(Mathf.RoundToInt(arc / 12f), 6, 18),
                MoteRadius = Mathf.Clamp(arc / 90f, 0.6f, 1.6f),
                MoteSpeedMin = 5f, MoteSpeedMax = 12f, MoteSize = 0.13f, MoteSpread = 1.1f,
                Lance = LanceAim.Forward, LanceLength = 1.3f,
                Burst = BurstOrigin.Hand, BurstRadius = 1.1f,
                AuraDrive = 0.55f, BodyDrive = 0.30f, LightMul = 0.8f, HandScale = 0.85f,
                HandAnchored = true,
            };
        }

        /// <summary>
        /// <b>Conjure</b> — putting something into the world. The circle EXPANDS (the caster
        /// is laying power down, not taking it in), the motes fall out of the air into it, and
        /// the shockwave leaves along the floor rather than out of the hand. Sized off whatever
        /// the spell is actually building.
        /// </summary>
        public static CastFlourishProfile Conjure(SpellDefinition spell)
        {
            float footprint = spell != null
                ? CastFlourishProfile.FirstPositive(1.8f, spell.radius, spell.wallWidth * 0.35f,
                    spell.explosionRadius, spell.coneLength * 0.4f)
                : 1.8f;
            footprint = Mathf.Clamp(footprint, 1.1f, 2.9f);

            return new CastFlourishProfile
            {
                FamilyName = "Conjure",
                Duration = 0.70f, Gather = 0.30f, Release = 0.09f,
                Sigil = SigilMotion.Expand, SigilRadius = footprint, SigilSpin = -60f, SigilAlpha = 0.85f,
                Approach = MoteApproach.DescendFromAbove, Departure = MoteDeparture.PushOutward,
                MoteCount = 20, MoteRadius = footprint * 0.95f,
                MoteSpeedMin = 1.6f, MoteSpeedMax = 4.2f, MoteSize = 0.20f, MoteSpread = 3.14f,
                Lance = LanceAim.Down, LanceLength = 1.0f,
                Burst = BurstOrigin.Ground, BurstRadius = footprint * 1.5f,
                AuraDrive = 0.95f, BodyDrive = 0.48f, LightMul = 1.15f, HandScale = 1.15f,
                HandAnchored = true,
            };
        }

        /// <summary>
        /// <b>Invoke</b> — calling something down. Everything points UP: the motes lift off the
        /// floor and are thrown at the sky, the lance is vertical, and the gather is the
        /// longest of any family because the answer is coming from somewhere far away.
        /// </summary>
        public static CastFlourishProfile Invoke(SpellDefinition spell)
        {
            float scale = spell != null
                ? CastFlourishProfile.FirstPositive(2f, spell.meteorAreaRadius, spell.radius, spell.explosionRadius)
                : 2f;
            return new CastFlourishProfile
            {
                FamilyName = "Invoke",
                Duration = 0.78f, Gather = 0.42f, Release = 0.09f,
                Sigil = SigilMotion.Expand, SigilRadius = Mathf.Clamp(scale, 1.3f, 2.6f),
                SigilSpin = 140f, SigilAlpha = 0.95f,
                Approach = MoteApproach.RiseFromGround, Departure = MoteDeparture.ThrowUp,
                MoteCount = 22, MoteRadius = Mathf.Clamp(scale * 0.8f, 1.0f, 2.2f),
                MoteSpeedMin = 5f, MoteSpeedMax = 11f, MoteSize = 0.19f, MoteSpread = 0.35f,
                Lance = LanceAim.Up, LanceLength = 2.4f,
                Burst = BurstOrigin.Ground, BurstRadius = 2.1f,
                AuraDrive = 1.55f, BodyDrive = 0.62f, LightMul = 1.5f, HandScale = 1.25f,
                HandAnchored = false,
            };
        }

        /// <summary>
        /// <b>Ward</b> — power kept, not spent. Nothing leaves the caster: the motes orbit the
        /// body and stay orbiting, there is no lance because there is no direction, and the
        /// shockwave is centred on the character rather than on their hand. The longest family,
        /// because a ward settling is a slower idea than a bolt leaving.
        /// </summary>
        public static CastFlourishProfile Ward(SpellDefinition spell)
        {
            float radius = spell != null ? CastFlourishProfile.FirstPositive(1.7f, spell.radius) : 1.7f;
            radius = Mathf.Clamp(radius, 1.1f, 3.0f);

            return new CastFlourishProfile
            {
                FamilyName = "Ward",
                Duration = 0.82f, Gather = 0.34f, Release = 0.10f,
                Sigil = SigilMotion.Expand, SigilRadius = radius, SigilSpin = 70f, SigilAlpha = 0.80f,
                Approach = MoteApproach.OrbitBody, Departure = MoteDeparture.Linger,
                MoteCount = 18, MoteRadius = radius * 0.72f,
                MoteSpeedMin = 0.6f, MoteSpeedMax = 1.6f, MoteSize = 0.17f, MoteSpread = 3.14f,
                Lance = LanceAim.None, LanceLength = 0f,
                Burst = BurstOrigin.Body, BurstRadius = radius * 1.35f,
                AuraDrive = 1.9f, BodyDrive = 0.70f, LightMul = 1.35f, HandScale = 0.75f,
                HandAnchored = false,
            };
        }

        /// <summary>
        /// <b>Surge</b> — the body itself is the projectile. Almost no wind-up, and the motes
        /// are left BEHIND rather than thrown ahead: the caster has already gone, and what the
        /// player sees is the wake.
        /// </summary>
        public static CastFlourishProfile Surge(SpellDefinition spell)
        {
            return new CastFlourishProfile
            {
                FamilyName = "Surge",
                Duration = 0.38f, Gather = 0.07f, Release = 0.05f,
                Sigil = SigilMotion.Contract, SigilRadius = 0.95f, SigilSpin = 200f, SigilAlpha = 0.45f,
                Approach = MoteApproach.CollapseToBody, Departure = MoteDeparture.TrailBehind,
                MoteCount = 18, MoteRadius = 0.85f,
                MoteSpeedMin = 3f, MoteSpeedMax = 8f, MoteSize = 0.16f, MoteSpread = 0.30f,
                Lance = LanceAim.Forward, LanceLength = 2.2f,
                Burst = BurstOrigin.Body, BurstRadius = 1.3f,
                AuraDrive = 1.1f, BodyDrive = 0.60f, LightMul = 0.9f, HandScale = 0.9f,
                HandAnchored = false,
            };
        }

        /// <summary>
        /// <b>Vanish</b> — an implosion. The only family whose motes go INWARD at both ends:
        /// they collapse onto the body and are snuffed out there, with no burst leaving and no
        /// lance pointing anywhere, because nothing travels — the caster simply stops being
        /// where they were.
        /// </summary>
        public static CastFlourishProfile Vanish(SpellDefinition spell)
        {
            return new CastFlourishProfile
            {
                FamilyName = "Vanish",
                Duration = 0.46f, Gather = 0.20f, Release = 0.05f,
                Sigil = SigilMotion.Contract, SigilRadius = 1.5f, SigilSpin = -230f, SigilAlpha = 0.70f,
                Approach = MoteApproach.CollapseToBody, Departure = MoteDeparture.PullInward,
                MoteCount = 20, MoteRadius = 1.6f,
                MoteSpeedMin = 2f, MoteSpeedMax = 4f, MoteSize = 0.15f, MoteSpread = 0f,
                Lance = LanceAim.None, LanceLength = 0f,
                Burst = BurstOrigin.None, BurstRadius = 0f,
                AuraDrive = 1.7f, BodyDrive = 0.85f, LightMul = 1.2f, HandScale = 0.6f,
                HandAnchored = false,
            };
        }

        /// <summary>
        /// <b>Vortex</b> — a tornado standing on the caster. The only family with a
        /// SILHOUETTE of its own: every other gesture is points of light, and points have no
        /// outline, while a funnel is recognised by its shape long before any debris in it is.
        ///
        /// <para>It is one gesture with two directions, and <c>forceMode</c> picks which. A
        /// PULL turns one way and drags its debris inward; a PUSH turns the other and throws
        /// it out. Nothing else changes — reversing the spin and the departure is enough,
        /// because that is genuinely the whole difference between the two spells.</para>
        ///
        /// <para>It runs longer than any other family (0.72 s against Hurl's 0.52) and gathers
        /// for most of it. A vortex that snaps to full size has not spun up, it has appeared,
        /// and the winding is the part worth watching.</para>
        /// </summary>
        public static CastFlourishProfile Vortex(SpellDefinition spell)
        {
            bool pulling = spell != null &&
                           string.Equals(spell.forceMode, "pull", System.StringComparison.OrdinalIgnoreCase);

            // The funnel opens to the spell's own reach, so a wide vortex looks wide. Clamped
            // because `radius` on these two is authored in the legacy pixel scale (17.5), and
            // a funnel eight units across would fill the screen.
            float reach = spell != null ? CastFlourishProfile.FirstPositive(2.2f, spell.radius) : 2.2f;
            float top = Mathf.Clamp(reach * 0.11f, 1.15f, 2.4f);

            return new CastFlourishProfile
            {
                FamilyName = "Vortex",
                Duration = 0.72f, Gather = 0.42f, Release = 0.10f,
                Sigil = SigilMotion.Contract, SigilRadius = top * 0.85f, SigilSpin = pulling ? 210f : -210f,
                SigilAlpha = 0.55f,
                Approach = MoteApproach.SpiralFunnel,
                Departure = pulling ? MoteDeparture.PullInward : MoteDeparture.PushOutward,
                MoteCount = 20, MoteRadius = top * 0.8f,
                MoteSpeedMin = 2.4f, MoteSpeedMax = 6.5f, MoteSize = 0.14f, MoteSpread = 1.6f,
                // No lance: a vortex does not point anywhere, it turns.
                Lance = LanceAim.None, LanceLength = 0f,
                Burst = BurstOrigin.Ground, BurstRadius = top * 1.15f,
                FunnelBands = 7,
                FunnelHeight = 2.9f,
                FunnelBaseRadius = top * 0.22f,
                FunnelTopRadius = top,
                // Fast enough to blur, and signed by direction.
                FunnelSpin = pulling ? 520f : -520f,
                AuraDrive = 0.85f, BodyDrive = 0.40f, LightMul = 1.05f, HandScale = 1.0f,
                // Anchored on the BODY: the funnel encloses the caster rather than being held
                // out in front of them, so the motes must converge on the same axis.
                HandAnchored = false,
            };
        }

        /// <summary>
        /// <b>Channel</b> — a hold rather than an event. The circle breathes instead of
        /// resolving, the motes orbit the hand and never leave, and the release is a swell
        /// rather than a flash. Short-lived on purpose: a channelled spell re-enters its cast
        /// every frame it is held, so this plays as a repeating pulse and a long one would
        /// stack on itself.
        /// </summary>
        public static CastFlourishProfile Channel(SpellDefinition spell)
        {
            float reach = spell != null
                ? CastFlourishProfile.FirstPositive(6f, spell.length, spell.coneLength, spell.range, spell.radius)
                : 6f;
            return new CastFlourishProfile
            {
                FamilyName = "Channel",
                Duration = 0.50f, Gather = 0.22f, Release = 0.14f,
                Sigil = SigilMotion.Pulse, SigilRadius = 1.15f, SigilSpin = 45f, SigilAlpha = 0.55f,
                Approach = MoteApproach.OrbitBody, Departure = MoteDeparture.Linger,
                MoteCount = 14, MoteRadius = 0.55f,
                MoteSpeedMin = 0.5f, MoteSpeedMax = 1.4f, MoteSize = 0.15f, MoteSpread = 3.14f,
                Lance = LanceAim.Forward, LanceLength = Mathf.Clamp(reach * 0.28f, 1.2f, 3.0f),
                Burst = BurstOrigin.Hand, BurstRadius = 1.0f,
                AuraDrive = 0.9f, BodyDrive = 0.42f, LightMul = 1.1f, HandScale = 1.6f,
                HandAnchored = true,
            };
        }
    }
}
