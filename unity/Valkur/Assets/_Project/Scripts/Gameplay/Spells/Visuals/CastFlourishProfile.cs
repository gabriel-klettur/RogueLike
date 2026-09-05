using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>How the ground sigil behaves over the cast.</summary>
    internal enum SigilMotion
    {
        /// <summary>No circle at all. For gestures that are not conjuring anything.</summary>
        None,
        /// <summary>Draws inward. Power being gathered.</summary>
        Contract,
        /// <summary>Pushes outward. Power being laid down on the world.</summary>
        Expand,
        /// <summary>Breathes in place. A hold, not an event.</summary>
        Pulse,
    }

    /// <summary>Where the motes come from while the cast winds up.</summary>
    internal enum MoteApproach
    {
        /// <summary>
        /// Riding a helix around a funnel — the radius at any moment comes from the cone's
        /// profile at that mote's height, so debris hugs the neck and flies wide at the top.
        /// Only meaningful alongside a funnel; without one the motes orbit a shape that is
        /// not being drawn.
        /// </summary>
        SpiralFunnel,

        /// <summary>A ring in the air spiralling into the hands.</summary>
        SpiralIn,
        /// <summary>Off the floor around the caster, lifting as they converge.</summary>
        RiseFromGround,
        /// <summary>Out of the sky above, falling into the hands.</summary>
        DescendFromAbove,
        /// <summary>Circling the body at a steady radius, never converging.</summary>
        OrbitBody,
        /// <summary>Strung along an arc in front, travelling across it.</summary>
        SweepArc,
        /// <summary>Collapsing hard onto the body itself.</summary>
        CollapseToBody,
    }

    /// <summary>Where the motes go once the spell fires.</summary>
    internal enum MoteDeparture
    {
        /// <summary>Thrown along the cast direction.</summary>
        ThrowForward,
        /// <summary>Thrown at the sky.</summary>
        ThrowUp,
        /// <summary>Pushed radially away from the anchor.</summary>
        PushOutward,
        /// <summary>Pulled in and snuffed out.</summary>
        PullInward,
        /// <summary>Left behind, opposite the direction of travel.</summary>
        TrailBehind,
        /// <summary>Stays where it was and fades. For a hold.</summary>
        Linger,
    }

    /// <summary>Which way the lance of light points, if there is one.</summary>
    internal enum LanceAim { None, Forward, Up, Down }

    /// <summary>Where the shockwave leaves from.</summary>
    internal enum BurstOrigin
    {
        None,
        /// <summary>At the cast anchor, in the air.</summary>
        Hand,
        /// <summary>Centred on the silhouette.</summary>
        Body,
        /// <summary>Flat on the floor at the caster's feet.</summary>
        Ground,
    }

    /// <summary>
    /// The SHAPE of one spell's cast flourish, as distinct from its colour.
    ///
    /// <para>Colour comes from <see cref="ElementPalette"/> and answers "what element is
    /// this". This answers a different question — "what is the caster DOING" — and the two
    /// are genuinely independent: an ice wall and an ice bolt are the same blue and nothing
    /// like the same gesture, while a summoned totem and a summoned wall are different colours
    /// and the same gesture. Folding them together would have meant one flourish per element,
    /// which is exactly the version that shipped first and read as decoration rather than as
    /// the character casting a particular spell.</para>
    ///
    /// <para>This is the same shape as <c>SlashProfile</c>: one place that maps authored data
    /// to a family, and the family fixes every beat, so no call site tunes a flourish by hand
    /// and two spells of the same kind cannot drift apart.</para>
    /// </summary>
    internal struct CastFlourishProfile
    {
        public string FamilyName;

        public float Duration;
        /// <summary>Seconds of wind-up before the release. May be near zero.</summary>
        public float Gather;
        /// <summary>Seconds from the end of the gather to peak brightness.</summary>
        public float Release;

        public SigilMotion Sigil;
        public float SigilRadius;
        public float SigilSpin;
        public float SigilAlpha;

        public MoteApproach Approach;
        public MoteDeparture Departure;
        public int MoteCount;
        public float MoteRadius;
        public float MoteSpeedMin, MoteSpeedMax;
        public float MoteSize;
        /// <summary>Spread of the departure, in radians either side of its aim.</summary>
        public float MoteSpread;

        public LanceAim Lance;
        public float LanceLength;

        public BurstOrigin Burst;
        public float BurstRadius;

        /// <summary>Peak alpha multiplier of the halo over the body.</summary>
        public float AuraDrive;
        /// <summary>How far the body's own colour is pulled toward the element at the peak.</summary>
        public float BodyDrive;
        public float LightMul;
        /// <summary>Radius of the glow at the cast anchor, world units, at the peak.</summary>
        /// <summary>
        /// How many arcs the funnel is stacked from. Zero means this family draws none, which
        /// is every family but Vortex — the pieces a family switches off are never built.
        /// </summary>
        public int FunnelBands;

        /// <summary>World height of the funnel, floor to flared top.</summary>
        public float FunnelHeight;

        /// <summary>World radius where the funnel touches down, and where it opens out.</summary>
        public float FunnelBaseRadius, FunnelTopRadius;

        /// <summary>
        /// Degrees per second, SIGNED. The sign is the whole difference between a vortex that
        /// draws in and one that throws out — reverse it and a pull reads as a push.
        /// </summary>
        public float FunnelSpin;

        public float HandScale;

        /// <summary>
        /// False when the gesture belongs to the whole body rather than to the hands — a ward
        /// blooms out of the caster, it is not held in front of them.
        /// </summary>
        public bool HandAnchored;

        /// <summary>
        /// Resolve the flourish for <paramref name="spell"/>.
        ///
        /// <para>Dispatch is on <see cref="SpellDefinition.type"/> because that is what the
        /// spell DOES, and the families then read a few authored numbers — a ward's radius, a
        /// projectile's range, a wall's width — so two spells in the same family still differ
        /// by as much as their own data differs. A spell type with no case falls through to
        /// <see cref="CastFlourishFamilies.Hurl"/>, which is the least surprising gesture for something
        /// that has to go somewhere.</para>
        /// </summary>
        public static CastFlourishProfile Build(SpellDefinition spell)
            => CastGatherOverrides.Apply(BuildFamily(spell),
                                         spell != null ? spell.gatherOverride : null);

        /// <summary>
        /// The gesture this spell's TYPE alone dictates, before anything it pins itself.
        ///
        /// <para>Separate from <see cref="Build"/> so the Spells Editor can show what a knob
        /// would read if it were released — an unpinned row states the family's number rather
        /// than a blank, which is what makes the checkbox a decision instead of a guess. A
        /// caller that wants the flourish as it will actually play wants
        /// <see cref="Build"/>.</para>
        /// </summary>
        internal static CastFlourishProfile BuildFamily(SpellDefinition spell)
        {
            if (spell == null) return CastFlourishFamilies.Hurl(null);

            switch (spell.type)
            {
                case SpellType.Slash:
                    return CastFlourishFamilies.Edge(spell);

                case SpellType.Area:
                case SpellType.Wall:
                case SpellType.Trap:
                case SpellType.Mine:
                case SpellType.Puddle:
                case SpellType.Totem:
                case SpellType.Summon:
                case SpellType.Smoke:
                case SpellType.SmokeEmitter:
                // ArcaneFlame is a ground zone THROWN out in front of the caster and burned
                // into the floor, which is the Conjure gesture (the circle expands while
                // motes fall out of the sky — power being laid down). It sat under Channel
                // for as long as its executor placed it on a private constant and it read
                // as something the caster was holding open; it is aimed and placed now, the
                // same shape as the puddle two lines above.
                case SpellType.ArcaneFlame:
                    return CastFlourishFamilies.Conjure(spell);

                case SpellType.Meteor:
                case SpellType.Lightning:
                case SpellType.ChainLightning:
                case SpellType.FireworkLaunch:
                    return CastFlourishFamilies.Invoke(spell);

                case SpellType.Aura:
                case SpellType.Shield:
                case SpellType.SphereMagicShield:
                    return CastFlourishFamilies.Ward(spell);

                // A timed self-buff is USUALLY the Ward gesture — power orbits the body and
                // never leaves it — but not always, and the exception is not cosmetic. A
                // martial buff is a SHOUT: it summons nothing, so it draws no circle and no
                // orbit, and routing it to Ward gave the game's battle cry a rotating magic
                // sigil for as long as the spell existed.
                //
                // The test is BuffAuraProfile's, not a second opinion held here, so the cast
                // gesture and the sustained silhouette cannot end up disagreeing about which
                // school a spell belongs to. It also hands Ward the gather radius that same
                // rule derived, because every Buff in the game authors `radius: 0` and Ward
                // would otherwise size all of them identically.
                case SpellType.Buff:
                {
                    var buff = BuffAuraProfile.Resolve(spell);
                    return buff.Silhouette == BuffSilhouette.Fervor
                        ? CastFlourishFamilies.Rally(spell, buff.GatherRadius)
                        : CastFlourishFamilies.Ward(spell, buff.GatherRadius);
                }

                case SpellType.Dash:
                    return CastFlourishFamilies.Surge(spell);

                case SpellType.Teleport:
                    return CastFlourishFamilies.Vanish(spell);

                case SpellType.Beam:
                case SpellType.ConeBreath:
                    return CastFlourishFamilies.Channel(spell);

                case SpellType.VortexField:
                    return CastFlourishFamilies.Vortex(spell);

                case SpellType.Projectile:
                case SpellType.Boomerang:
                default:
                    return CastFlourishFamilies.Hurl(spell);
            }
        }

        /// <summary>
        /// The first authored value above zero, or <paramref name="fallback"/>. Spell data is
        /// sparse by design — <c>SpellFieldRelevance</c> exists because most fields mean
        /// nothing to most spells — so a family sizing itself off one field alone would size
        /// half its members off a zero.
        /// </summary>
        internal static float FirstPositive(float fallback, params float[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] > 0f) return candidates[i];
            return fallback;
        }
    }
}
