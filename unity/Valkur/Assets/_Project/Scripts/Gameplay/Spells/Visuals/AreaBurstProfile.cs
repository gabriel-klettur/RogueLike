using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>The six shapes an area burst in this game is allowed to be.</summary>
    internal enum AreaSilhouette
    {
        /// <summary>A generic detonation. The default, and the only one that says nothing.</summary>
        Bloom,
        /// <summary>A ring travelling outward that leaves ice standing behind it.</summary>
        Rime,
        /// <summary>Matter coming UP through the floor. The one silhouette whose main layer is opaque.</summary>
        Thorns,
        /// <summary>A patch that HOLDS. Persistent, per-target, and it withers when it lets go.</summary>
        Snare,
        /// <summary>A sphere of light that expands and is gone. Purely additive, on purpose.</summary>
        Radiance,
        /// <summary>A shockwave: a ring with an inside, and the speed IS the character.</summary>
        Shock,
    }

    /// <summary>
    /// Decides what an area spell LOOKS like from what it DOES.
    ///
    /// <para>Same contract as <see cref="ProjectileVisualProfile"/>, for the same reason: every
    /// field below is derived from mechanics the SpellDefinition already declares, so a spell
    /// gets its silhouette with no new authored field and no asset edit. <c>SlashProfile</c>
    /// dispatches on <c>arcRangeDegrees</c> and <c>CastFlourishProfile</c> on <c>SpellType</c>;
    /// this one dispatches on the STATUS the spell inflicts, because for an area spell the
    /// status is the verb. A spell authored to root without damaging CANNOT come out drawn as a
    /// detonation, because the same fields decide both.</para>
    ///
    /// <para>The alternative — an <c>areaVisualKey</c> string on the asset — makes the
    /// silhouette a second, independent opinion about the spell, free to disagree with the
    /// mechanic silently. This project has recorded that failure eleven times under
    /// "authored and inert".</para>
    ///
    /// <para>Note the five shipped Area spells author <c>element</c> as a STRING, and two of
    /// them ("Nature") do not parse to any <see cref="SpellElement"/>. That is exactly why the
    /// status comes first in the resolution order: it is the half that is always present and
    /// always meaningful, while the element may be a word nothing reads.</para>
    /// </summary>
    internal struct AreaBurstProfile
    {
        public AreaSilhouette Silhouette;

        /// <summary>
        /// The one place an area burst's ADDITIVE colour is decided. Comes through
        /// <see cref="ElementPalette.RecolouredTo"/>, which already handles the three meanings
        /// of <c>particleColor</c> in the right order — opaque white is the "nobody authored
        /// this" sentinel, an achromatic value is a deliberate request for the ABSENCE of
        /// colour, and near-black adds nothing on an additive material.
        /// </summary>
        public ElementPalette Palette;

        /// <summary>
        /// The raw authored swatch, carried for exactly one consumer: <see cref="RootPalette"/>,
        /// whose ramp is the right one for MATTER (soil, bark, leaf, sap) and which owns the
        /// same three sentinel meanings itself. Running a thorn through the element palette
        /// washes it out where it should be greenest — that is <c>RootPalette</c>'s own note.
        /// Nothing additive may read this field.
        /// </summary>
        public Color Swatch;

        /// <summary>The damage circle in WORLD UNITS. The drawn ring is pinned to it exactly.</summary>
        public float Radius;

        /// <summary>Seconds the wave takes to travel from the centre to <see cref="Radius"/>.</summary>
        public float WaveSeconds;

        /// <summary>Seconds the whole rig lives. A burst that lingers stops being a burst.</summary>
        public float Life;

        /// <summary>
        /// How many discrete things the wave trips as it passes them — Law L4's event layer.
        /// Something that appears and is gone, at roughly a third duty; continuous motion
        /// stops being read after about a second.
        /// </summary>
        public int EventCount;

        /// <summary>
        /// Law L3: the dark OPAQUE layer, the only thing that says the WORLD was affected
        /// rather than lit. Zero for <see cref="AreaSilhouette.Radiance"/> alone.
        /// </summary>
        public int GritCount;

        /// <summary>Where along the radius the events are seeded, as fractions of <see cref="Radius"/>.</summary>
        public float EventBandMin, EventBandMax;

        public bool HasHaze;
        public bool HasGroundRing;

        /// <summary>
        /// True for <see cref="AreaSilhouette.Radiance"/> only, and it is a deliberate
        /// exemption from L3 rather than an oversight: light is the one thing in this game
        /// that genuinely IS light, so a chip of broken ground would be lying.
        /// </summary>
        public bool PurelyAdditive;

        public float LightRadius, LightIntensity, LightRise, LightFall;

        /// <summary>
        /// 0..1 pulse handed to <c>SkyFlash</c>, or 0 for no global lift. A clap and a
        /// detonation are events in the ROOM; a local <c>Light2D</c> alone reads as a flare.
        /// </summary>
        public float SkyFlash;

        /// <summary>
        /// The colour of the wash laid over the caster when the burst goes off ON them.
        /// <c>Color.clear</c> means none. Drawn by <see cref="AreaBurstBloom"/>, which never
        /// touches the body's own <c>SpriteRenderer.color</c> — see that class for why an
        /// additive overlay is both the only legal and the only WORKING answer here.
        /// </summary>
        public Color CasterTint;

        /// <summary>
        /// Overdrive for the additive layers. HDR is on and an authored 2.4 reads back
        /// unchanged, so COLOUR above 1 is how a burst gets fiercer — raising alpha instead
        /// widens it into fog, because on an additive surface alpha is COVERAGE.
        /// </summary>
        public float Gain;

        /// <summary>
        /// See <see cref="Palette"/>. Reading <c>spell.particleColor</c> raw here instead
        /// would relight a grey spell pink and make a near-black one disappear.
        /// </summary>
        public static ElementPalette ResolvePalette(SpellDefinition spell)
        {
            var element = ProjectileExecutor.ResolveElement(spell);
            var basePalette = ElementPalette.For(element ?? SpellElement.Arcane);
            return spell != null ? basePalette.RecolouredTo(spell.particleColor) : basePalette;
        }

        /// <summary>
        /// Resolution order is the order in which a mechanic OWNS the silhouette, most
        /// distinctive first. A spell that refuses the feet and does no damage IS its hold and
        /// nothing else; poison is delivered by matter and never by light; ice and lightning
        /// each claim a wave of their own; and a burst that heals the caster is made of the
        /// same stuff that heals them. Damage is never tested, because almost every spell has
        /// some and it therefore separates nothing.
        /// </summary>
        public static AreaBurstProfile Resolve(SpellDefinition spell, float radius)
        {
            var palette = ResolvePalette(spell);
            Color swatch = spell != null ? spell.particleColor : Color.white;
            radius = Mathf.Max(0.4f, radius);

            if (spell == null) return Bloom(palette, swatch, radius);

            var element = ProjectileExecutor.ResolveElement(spell);

            // The whole spell is the hold: there is no damage number for the player to read,
            // so the rig is the ONLY thing that tells them anyone was caught.
            if (Applies(spell, StatusEffectKind.Root) && spell.damage <= 0f)
                return Snare(palette, swatch, radius, LongestDuration(spell, StatusEffectKind.Root));

            if (Applies(spell, StatusEffectKind.Poison))
                return Thorns(palette, swatch, radius);

            if (element == SpellElement.Ice || Applies(spell, StatusEffectKind.Freeze))
                return Rime(palette, swatch, radius);

            if (element == SpellElement.Lightning || Applies(spell, StatusEffectKind.Stun))
                return Shock(palette, swatch, radius);

            if (spell.healPerTick > 0f || element == SpellElement.Light)
                return Radiance(palette, swatch, radius);

            return Bloom(palette, swatch, radius);
        }

        private static bool Applies(SpellDefinition spell, StatusEffectKind kind)
        {
            var list = spell.statusApplications;
            if (list == null) return false;
            for (int i = 0; i < list.Length; i++)
            {
                // duration <= 0 and chance <= 0 are both authored no-ops (StatusApplication's
                // own doc says so), and a silhouette chosen from one would be a picture of
                // something that never happens.
                if (list[i].type != kind) continue;
                if (list[i].duration > 0f && list[i].chance > 0f) return true;
            }
            return false;
        }

        private static float LongestDuration(SpellDefinition spell, StatusEffectKind kind)
        {
            float best = 0f;
            var list = spell.statusApplications;
            if (list == null) return best;
            for (int i = 0; i < list.Length; i++)
                if (list[i].type == kind && list[i].duration > best) best = list[i].duration;
            return best;
        }

        // ── the six ──────────────────────────────────────────────────────────────────

        private static AreaBurstProfile Bloom(ElementPalette p, Color swatch, float r)
            => new AreaBurstProfile
            {
                Silhouette = AreaSilhouette.Bloom, Palette = p, Swatch = swatch, Radius = r,
                WaveSeconds = 0.22f, Life = 0.70f,
                EventCount = 8, GritCount = 10,
                EventBandMin = 0.30f, EventBandMax = 0.95f,
                HasHaze = true, HasGroundRing = true, PurelyAdditive = false,
                LightRadius = r * 1.05f, LightIntensity = 2.0f, LightRise = 0.07f, LightFall = 0.45f,
                SkyFlash = 0f, CasterTint = Color.clear, Gain = 1.5f,
            };

        // A ring TRAVELLING, not a disc appearing: the spikes exist so the wave has something
        // to trip, and the sequence is what turns an expanding circle into a wave front.
        private static AreaBurstProfile Rime(ElementPalette p, Color swatch, float r)
            => new AreaBurstProfile
            {
                Silhouette = AreaSilhouette.Rime, Palette = p, Swatch = swatch, Radius = r,
                WaveSeconds = 0.18f, Life = 0.90f,
                EventCount = 14, GritCount = 12,
                EventBandMin = 0.32f, EventBandMax = 0.98f,
                HasHaze = true, HasGroundRing = true, PurelyAdditive = false,
                LightRadius = r * 1.06f, LightIntensity = 2.2f, LightRise = 0.08f, LightFall = 0.50f,
                SkyFlash = 0f, CasterTint = Color.clear, Gain = 1.7f,
            };

        // The inversion that is the whole point: the SILHOUETTE is opaque and only the pops at
        // each base are additive. Matter, not light — so the "wave" is slow enough to read as
        // ground opening rather than as a flash.
        private static AreaBurstProfile Thorns(ElementPalette p, Color swatch, float r)
            => new AreaBurstProfile
            {
                Silhouette = AreaSilhouette.Thorns, Palette = p, Swatch = swatch, Radius = r,
                WaveSeconds = 0.25f, Life = 1.55f,
                EventCount = 18, GritCount = 14,
                EventBandMin = 0.18f, EventBandMax = 0.92f,
                HasHaze = false, HasGroundRing = true, PurelyAdditive = false,
                // Plants do not glow. The light is an accent on the eruption, decaying fast,
                // never an ambience.
                LightRadius = r * 0.93f, LightIntensity = 1.5f, LightRise = 0.05f, LightFall = 0.25f,
                SkyFlash = 0f, CasterTint = Color.clear, Gain = 1.25f,
            };

        // THE ZEROES HERE ARE A DELEGATION, NOT AN OVERSIGHT. The snare is the one silhouette
        // that does not build its own ground: RootWhipFX already owns the ring, the cracks, the
        // scattered stems, the thrown clods and the Light2D, and a second set authored here
        // would be numbers nothing reads — which is how a field ends up "authored and inert".
        // EventCount is the exception and is live: it is stems PER CAUGHT TARGET, which is the
        // only population the patch does not decide.
        private static AreaBurstProfile Snare(ElementPalette p, Color swatch, float r, float hold)
            => new AreaBurstProfile
            {
                Silhouette = AreaSilhouette.Snare, Palette = p, Swatch = swatch, Radius = r,
                WaveSeconds = 0f,
                // The patch outlives the hold by the wither, because the release is the beat
                // that tells the player they can move again.
                Life = Mathf.Max(1.2f, hold) + AreaBurstTiming.WitherSeconds,
                EventCount = 4, GritCount = 0,
                EventBandMin = 0f, EventBandMax = 0f,
                HasHaze = false, HasGroundRing = false, PurelyAdditive = false,
                LightRadius = 0f, LightIntensity = 0f, LightRise = 0f, LightFall = 0f,
                SkyFlash = 0f, CasterTint = Color.clear, Gain = 1.2f,
            };

        // The exemption to L3, declared rather than implied. Over in 0.45 s: a heal that
        // lingers reads as an aura, and this is a detonation.
        private static AreaBurstProfile Radiance(ElementPalette p, Color swatch, float r)
            => new AreaBurstProfile
            {
                Silhouette = AreaSilhouette.Radiance, Palette = p, Swatch = swatch, Radius = r,
                WaveSeconds = 0.20f, Life = 0.45f,
                EventCount = 0, GritCount = 0,
                EventBandMin = 0.20f, EventBandMax = 1f,
                HasHaze = true, HasGroundRing = true, PurelyAdditive = true,
                LightRadius = r * 1.13f, LightIntensity = 3.0f, LightRise = 0.05f, LightFall = 0.35f,
                SkyFlash = 0.45f,
                // The heal lands on the caster, so the light has to as well, or the spell's
                // best half happens somewhere the player is not looking.
                CasterTint = new Color(1f, 0.98f, 0.90f, 1f),
                Gain = 2.6f,
            };

        // 0.12 s to the rim. Sound is fast, and the speed is the only thing separating this
        // from every other expanding circle in the game.
        private static AreaBurstProfile Shock(ElementPalette p, Color swatch, float r)
            => new AreaBurstProfile
            {
                Silhouette = AreaSilhouette.Shock, Palette = p, Swatch = swatch, Radius = r,
                WaveSeconds = 0.12f, Life = 0.55f,
                EventCount = 7, GritCount = 16,
                // The forks are ON the ring, not scattered under it: a bolt inside the wave
                // says the middle is solid when the whole silhouette says it is hollow.
                EventBandMin = 0.86f, EventBandMax = 1f,
                HasHaze = true, HasGroundRing = true, PurelyAdditive = false,
                LightRadius = r * 1.08f, LightIntensity = 2.6f, LightRise = 0.03f, LightFall = 0.30f,
                SkyFlash = 0.75f, CasterTint = Color.clear, Gain = 2.2f,
            };
    }
}
