using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>The five shapes a sustained self-buff in this game is allowed to be.</summary>
    internal enum BuffSilhouette
    {
        /// <summary>
        /// A ring, a rim and a few motes. The default, and the only one that says nothing —
        /// the same role <c>ProjectileSilhouette.Orb</c> plays for a shot.
        /// </summary>
        Aura,

        /// <summary>
        /// Plates standing off the silhouette, half in front of the character and half
        /// behind, so the body is INSIDE them. Armour.
        /// </summary>
        Shell,

        /// <summary>
        /// Opaque matter climbing the body from the feet. Bark, and the only silhouette
        /// whose main layer is not light at all.
        /// </summary>
        Growth,

        /// <summary>
        /// A column arriving out of the sky and resolving into a held halo. Something
        /// granted from elsewhere rather than summoned by the caster.
        /// </summary>
        Radiance,

        /// <summary>
        /// A shout. A shockwave across the floor, dust, a camera kick and heat on the skin —
        /// and deliberately no circle, no sigil and no orbit anywhere in it.
        /// </summary>
        Fervor,
    }

    /// <summary>
    /// Decides what a self-buff LOOKS like from what it DOES.
    ///
    /// <para>Before this, <c>BuffAuraFX</c> read exactly two fields of the SpellDefinition —
    /// <c>duration</c> and <c>particleColor</c> — and every other number in it was a
    /// <c>const</c>. So ice armour, growing bark, a column of light out of the sky and a
    /// battle shout were the SAME PICTURE in four hues: one ground ring, one body glow, six
    /// motes and a point light. Four spells from four schools whose only visible difference
    /// was a colour is not a monoculture of implementation, it is a monoculture of MEANING:
    /// nothing on screen told the player which of them was running.</para>
    ///
    /// <para>Every field below is derived from mechanics the spell already declares, so a buff
    /// gets its silhouette with no new authored field and no asset edit. Same pattern as
    /// <see cref="ProjectileVisualProfile"/> (which dispatches on pierce / count / homing),
    /// <c>SlashProfile</c> (on <c>arcRangeDegrees</c>) and <c>CastFlourishProfile</c> (on
    /// <c>SpellType</c>). A <c>buffVisualKey</c> string on the asset was rejected for the
    /// reason this project has now recorded a dozen times: an independent second opinion about
    /// a spell is free to disagree with the mechanic silently, and eventually does.</para>
    ///
    /// <para>THE SAME RULE ALSO CHOOSES THE CAST GESTURE. <c>CastFlourishProfile.BuildFamily</c>
    /// asks this struct whether a Buff is <see cref="BuffSilhouette.Fervor"/> before it picks
    /// between the martial <c>Rally</c> family and the magical <c>Ward</c> one, so the cast and
    /// the sustain cannot end up disagreeing about what school the spell belongs to.</para>
    /// </summary>
    internal struct BuffAuraProfile
    {
        public BuffSilhouette Silhouette;

        /// <summary>
        /// The one place a buff's colour is decided. Always through
        /// <see cref="ElementPalette.RecolouredTo"/>, never the raw <c>particleColor</c>: that
        /// helper already handles all three meanings of the field in the right order — opaque
        /// white is the "nobody authored this" sentinel, an achromatic value is a deliberate
        /// request for the ABSENCE of colour, and a near-black one adds nothing additive.
        /// </summary>
        public ElementPalette Palette;

        /// <summary>
        /// The four-colour MATTER ramp, read by the two silhouettes that draw something which
        /// is not light: <see cref="BuffSilhouette.Growth"/>'s tendrils and
        /// <see cref="BuffSilhouette.Fervor"/>'s dust. <see cref="ElementPalette"/> is a ramp
        /// of light — its hot core is near-white by design — so running bark or thrown earth
        /// off it washes both out exactly where they should be darkest. Same reason
        /// <c>RootWhipFX</c> does not use the element palette either.
        /// </summary>
        public RootPalette Bark;

        /// <summary>Seconds the shape takes to ARRIVE. A buff that snaps on has not arrived, it has appeared.</summary>
        public float OnsetSeconds;

        /// <summary>Facets, tendrils or column slices. Zero for a silhouette that builds none.</summary>
        public int PieceCount;

        /// <summary>
        /// How far the pieces stand off the silhouette, as a multiple of the body's HALF
        /// width. A fraction rather than a world number because the rig sizes itself off the
        /// caster's own bounds — a shell on a boss and a shell on the player are the same
        /// statement at two sizes.
        /// </summary>
        public float StandOff;

        /// <summary>Seconds between motes, and the pool that serves them. Deliberately long — see the class doc on BuffAuraFX.</summary>
        public float MoteInterval;
        public int MotePool;
        public float MoteLife;
        public float MoteSize;

        /// <summary>
        /// World radius of the ground circle, or 0 for a silhouette that draws none.
        /// <see cref="BuffSilhouette.Fervor"/> puts its 4 u shockwave here; the ring is not a
        /// sustained layer for it, which is the whole point.
        /// </summary>
        public float GroundRingRadius;

        /// <summary>True while the ground ring is a HELD layer rather than a one-shot wave.</summary>
        public bool GroundRingPersists;

        public bool HasLight;
        public float LightRadius;
        public float LightIntensity;

        /// <summary>
        /// Strength of the <c>SpriteTintStack</c> layer, and the colour it moves the body
        /// toward. That layer MULTIPLIES, so a strength above about 0.35 stops reading as
        /// power sitting on the character and starts reading as the character being dimmed —
        /// which is exactly what <see cref="BuffSilhouette.Growth"/> WANTS and every other
        /// silhouette must avoid.
        /// </summary>
        public float BodyTint;
        public Color BodyTintTarget;

        /// <summary>
        /// What the cast flourish should gather at, world units. All four shipped buffs author
        /// <c>radius: 0</c>, so <c>CastFlourishFamilies.Ward</c>'s <c>FirstPositive(1.7f,
        /// spell.radius)</c> returned 1.7 for every one of them and even the cast was
        /// byte-identical across the four apart from hue. This is the number that fixes that,
        /// and it is derived from the same rule as the silhouette so the two agree.
        /// </summary>
        public float GatherRadius;

        // ── Resolution ────────────────────────────────────────────────────────

        /// <summary>
        /// Resolution order is the order in which a MECHANIC owns the silhouette, sharpest
        /// signal first.
        ///
        /// <list type="number">
        /// <item>A buff that COSTS mobility is armour. Nothing else in the game trades speed
        /// for protection, so a negative <c>MoveSpeed</c> is unambiguous.</item>
        /// <item>A buff that authors NO element and sharpens melee is martial. Martial Forms
        /// deliberately leaves <c>element</c> empty because steel is not an element, and that
        /// absence is the school's one machine-readable signature.</item>
        /// <item>A buff that grants flat <c>MaxHp</c> is vitality, and vitality is MATTER
        /// grown onto the body rather than light laid over it.</item>
        /// <item>A buff that raises what a CASTER is worth — spell power, mana, experience —
        /// is something granted from elsewhere.</item>
        /// <item>Anything else gets the neutral aura, which says nothing and is honest about
        /// saying nothing.</item>
        /// </list>
        ///
        /// <para>Note test 2 rides on <c>ResolveElement</c> returning null, which it does both
        /// for an EMPTY field and for one that fails to parse. <c>barkskin</c> is the second
        /// case — it authors <c>element: Nature</c>, and <c>SpellElement</c> has no such
        /// member — so it reaches this method looking exactly like a martial spell. It is
        /// caught by test 3 instead, which is why test 2 also demands a martial STAT and does
        /// not fire on element-absence alone.</para>
        /// </summary>
        public static BuffAuraProfile Resolve(SpellDefinition spell)
        {
            if (spell == null) return Aura(PaletteFor(null, null));

            var element = ProjectileExecutor.ResolveElement(spell);

            if (CostsMobility(spell))
                return Shell(PaletteFor(spell, element ?? SpellElement.Ice));

            if (element == null && SharpensMelee(spell))
                return Fervor(PaletteFor(spell, SpellElement.Fire),
                              RootPalette.From(spell.particleColor));

            if (GrantsVitality(spell))
                return Growth(PaletteFor(spell, element ?? SpellElement.Arcane),
                              RootPalette.From(spell.particleColor));

            if (GrantsCasterPower(spell))
                return Radiance(PaletteFor(spell, element ?? SpellElement.Light));

            return Aura(PaletteFor(spell, element));
        }

        private static ElementPalette PaletteFor(SpellDefinition spell, SpellElement? element)
        {
            var basePalette = ElementPalette.For(element ?? SpellElement.Arcane);
            return spell != null ? basePalette.RecolouredTo(spell.particleColor) : basePalette;
        }

        /// <summary>Any modifier that makes the caster SLOWER. The armour signal.</summary>
        private static bool CostsMobility(SpellDefinition spell)
            => Has(spell, StatKind.MoveSpeed, positive: false);

        /// <summary>
        /// Melee sharpened in any of the three ways it can be. <c>MeleeCooldown</c> is checked
        /// NEGATIVE on purpose — lower is better on that stat, so a buff authors a negative
        /// percent there and a naive "above zero" test would miss the commonest martial
        /// modifier in the game.
        /// </summary>
        private static bool SharpensMelee(SpellDefinition spell)
            => Has(spell, StatKind.MeleeDamage, positive: true)
            || Has(spell, StatKind.MeleeRange, positive: true)
            || Has(spell, StatKind.MeleeCooldown, positive: false)
            || Has(spell, StatKind.MoveSpeed, positive: true);

        private static bool GrantsVitality(SpellDefinition spell)
            => Has(spell, StatKind.MaxHp, positive: true);

        private static bool GrantsCasterPower(SpellDefinition spell)
            => Has(spell, StatKind.SpellPower, positive: true)
            || Has(spell, StatKind.ManaRegen, positive: true)
            || Has(spell, StatKind.MaxMana, positive: true)
            || Has(spell, StatKind.SpellCooldownReduction, positive: true)
            || Has(spell, StatKind.ManaCostReduction, positive: true)
            || Has(spell, StatKind.XpGain, positive: true);

        private static bool Has(SpellDefinition spell, StatKind stat, bool positive)
        {
            var mods = spell != null ? spell.statModifiers : null;
            if (mods == null) return false;
            for (int i = 0; i < mods.Length; i++)
            {
                if (mods[i].stat != stat) continue;
                if (positive ? mods[i].value > 0f : mods[i].value < 0f) return true;
            }
            return false;
        }

        // ── The five silhouettes ──────────────────────────────────────────────

        /// <summary>The neutral fallback: what the four shipped buffs all used to look like.</summary>
        private static BuffAuraProfile Aura(ElementPalette p) => new BuffAuraProfile
        {
            Silhouette = BuffSilhouette.Aura,
            Palette = p,
            OnsetSeconds = 0.30f,
            PieceCount = 0,
            StandOff = 1.35f,
            MoteInterval = 0.70f, MotePool = 6, MoteLife = 1.10f, MoteSize = 0.22f,
            GroundRingRadius = 1.0f, GroundRingPersists = true,
            HasLight = true, LightRadius = 2.6f, LightIntensity = 0.55f,
            BodyTint = 0.16f, BodyTintTarget = p.core,
            GatherRadius = 1.7f,
        };

        /// <summary>
        /// Ice armour. Eight plates on a slowly turning ring, half of them BEHIND the
        /// character — the one statement that makes a shell read as a shell rather than as a
        /// decal, and the one <c>ShieldSphereFX</c> exists to make for the sphere.
        /// </summary>
        private static BuffAuraProfile Shell(ElementPalette p) => new BuffAuraProfile
        {
            Silhouette = BuffSilhouette.Shell,
            Palette = p,
            // Rime creeps. 0.6 s is long enough to be watched and short enough that the buff
            // is doing its job before the picture finishes arriving.
            OnsetSeconds = 0.60f,
            PieceCount = 8,
            StandOff = 1.18f,
            // ~1 per 0.4 s is the event layer at about 25 % duty. A defensive buff should
            // feel STEADY, so this is the sparsest of the four that carry motes.
            MoteInterval = 0.40f, MotePool = 6, MoteLife = 1.30f, MoteSize = 0.20f,
            GroundRingRadius = 0f, GroundRingPersists = false,
            // Ice catches light, and the plates are opaque — without a light they read as
            // grey paper. Small, because the armour is on the body and not around it.
            HasLight = true, LightRadius = 2.2f, LightIntensity = 0.65f,
            BodyTint = 0.22f, BodyTintTarget = p.core,
            GatherRadius = 1.25f,
        };

        /// <summary>
        /// Bark. The only profile whose main layer is OPAQUE and whose additive content is a
        /// single faint mote — that inversion is what makes it read as living wood instead of
        /// as a magic shell, and it is stated in the spec in as many words.
        /// </summary>
        private static BuffAuraProfile Growth(ElementPalette p, RootPalette bark) => new BuffAuraProfile
        {
            Silhouette = BuffSilhouette.Growth,
            Palette = p,
            Bark = bark,
            // Bark GROWS, and 0.8 s is the growing. Anything faster is bark appearing.
            OnsetSeconds = 0.80f,
            PieceCount = 7,
            StandOff = 0.86f,
            MoteInterval = 0.60f, MotePool = 4, MoteLife = 1.40f, MoteSize = 0.17f,
            GroundRingRadius = 0f, GroundRingPersists = false,
            // "No light: bark does not glow." The rig currently fired BuildLight
            // unconditionally at ~3.1 u, which lit a character in wood like a lantern.
            HasLight = false, LightRadius = 0f, LightIntensity = 0f,
            // The one silhouette that drives the multiply layer hard, because here darkening
            // IS the effect: the skin goes to wood.
            BodyTint = 0.34f, BodyTintTarget = bark.Bark,
            GatherRadius = 1.05f,
        };

        /// <summary>
        /// A column of light. Narrow at the top and wide at the floor — the opposite taper to
        /// a vortex, which is what makes it read as something ARRIVING rather than as
        /// something being taken.
        /// </summary>
        private static BuffAuraProfile Radiance(ElementPalette p) => new BuffAuraProfile
        {
            Silhouette = BuffSilhouette.Radiance,
            Palette = p,
            OnsetSeconds = 0.50f,
            // Slices, not a LineRenderer. A strip can bound a shape and can never fill one —
            // the lesson FlameConeFX, IceWallVisual and VortexFunnelFX each record.
            PieceCount = 12,
            StandOff = 1.10f,
            // ~12 % duty over a fifteen-second buff. A busy 15 s buff is a 15 s distraction.
            MoteInterval = 0.70f, MotePool = 5, MoteLife = 1.20f, MoteSize = 0.21f,
            GroundRingRadius = 0.92f, GroundRingPersists = true,
            HasLight = true, LightRadius = 3.0f, LightIntensity = 0.85f,
            BodyTint = 0.18f, BodyTintTarget = p.hotCore,
            GatherRadius = 2.10f,
        };

        /// <summary>
        /// A shout. NO sigil, NO orbiting ring, NO light — Martial Forms' entire identity is
        /// that nothing in it glows because it is enchanted, and the previous rig gave war_cry
        /// a rotating magic circle and eighteen motes orbiting the body, which is the single
        /// most damaging identity defect the audit found.
        /// </summary>
        private static BuffAuraProfile Fervor(ElementPalette p, RootPalette dust) => new BuffAuraProfile
        {
            Silhouette = BuffSilhouette.Fervor,
            Palette = p,
            // Dust is EARTH, not warm light. Taking the chips off the element palette would
            // make them glowing orange scraps, which is the one reading this school forbids.
            Bark = dust,
            // The shout is over in a fifth of a second. It is an EVENT, not an arrival.
            OnsetSeconds = 0.20f,
            PieceCount = 10,          // dust chips lifted along the wave
            StandOff = 1.10f,
            // ~1 per 0.8 s: the sparsest event layer of the four, because heat coming off a
            // shouting man is an occasional thing and a steady one is a bonfire.
            MoteInterval = 0.80f, MotePool = 4, MoteLife = 1.00f, MoteSize = 0.18f,
            // A one-shot wave, not a held circle. GroundRingPersists false is what says so.
            GroundRingRadius = 4.0f, GroundRingPersists = false,
            // No Light2D at all. A warm rim is heat on skin; a point light around a shouting
            // character is a spell going off, which is the exact reading this school forbids.
            HasLight = false, LightRadius = 0f, LightIntensity = 0f,
            BodyTint = 0.26f, BodyTintTarget = p.core,
            GatherRadius = 1.60f,
        };
    }
}
