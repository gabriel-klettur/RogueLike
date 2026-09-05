using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>The five shapes a projectile in this game is allowed to be.</summary>
    public enum ProjectileSilhouette
    {
        /// <summary>A ball with a hot centre. The default, and the only one that says nothing.</summary>
        Orb,
        /// <summary>Long on the axis of travel, thin across it. Reads as piercing before it pierces.</summary>
        Lance,
        /// <summary>Opaque spinning metal. Carries no additive layer at all.</summary>
        Blade,
        /// <summary>A small core whose long trail is the actual subject — for anything that turns.</summary>
        Spark,
        /// <summary>Slow, faint, and not trying to kill anyone. A mark on its way to a target.</summary>
        Wisp,
    }

    /// <summary>
    /// Decides what a projectile LOOKS like from what it DOES.
    ///
    /// <para>Every field below is derived from mechanics the SpellDefinition already declares,
    /// so a spell gets its silhouette without a new authored field and without an asset edit.
    /// That is deliberate and it is the same pattern <c>SlashProfile</c> uses (it dispatches on
    /// <c>arcRangeDegrees</c>) and <c>CastFlourishProfile</c> uses (it dispatches on
    /// <c>SpellType</c>): the shape follows the verb, so the two cannot drift apart. A spell
    /// authored to pierce four bodies CANNOT end up drawn as a ball, because the same number
    /// decides both.</para>
    ///
    /// <para>The alternative — a <c>projectileVisualKey</c> string on the asset — was rejected
    /// because it makes the silhouette a second, independent opinion about the spell, free to
    /// disagree with the mechanic silently. This project has recorded that failure eleven times
    /// under "authored and inert"; a derived profile cannot enter that state.</para>
    /// </summary>
    internal struct ProjectileVisualProfile
    {
        public ProjectileSilhouette Silhouette;
        public ElementPalette Palette;

        /// <summary>Extent along travel, world units. The camera is 33.33 u wide.</summary>
        public float Length;
        /// <summary>Extent across travel, world units.</summary>
        public float Width;

        /// <summary>
        /// Law L3: exactly one dark, opaque layer separates "the world is being affected"
        /// from "something is lit". False only for the wisp, which is genuinely just light.
        /// </summary>
        public bool HasOpaqueCore;

        /// <summary>
        /// False for <see cref="ProjectileSilhouette.Blade"/> alone. Martial Forms' entire
        /// identity is that nothing in it glows because it is enchanted, and an additive
        /// layer on a thrown knife erases exactly that.
        /// </summary>
        public bool HasAdditiveShell;

        public float TrailTime;
        public float TrailWidth;

        /// <summary>Secondary pieces: lance fins, blade siblings, spark embers.</summary>
        public int Shards;

        /// <summary>Degrees per second about Z. Zero keeps the rig aligned to travel.</summary>
        public float SpinDegPerSecond;

        public bool HasLight;
        public float LightRadius;

        /// <summary>
        /// Seconds between discrete flashes — Law L4's event layer. A steady glow is read
        /// once and then ignored; something that appears and is gone resets attention.
        /// Zero disables it.
        /// </summary>
        public float GlintInterval;

        /// <summary>Base sprite alpha. Kept low on the wisp so the mark reads as unfinished.</summary>
        public float Opacity;

        /// <summary>
        /// The one place a projectile's colour is decided. Goes through
        /// <see cref="ElementPalette.RecolouredTo"/>, which already handles all three meanings
        /// of <c>particleColor</c> in the right order: opaque white is the "nobody authored
        /// this" sentinel and keeps the element's colour, an achromatic value is a deliberate
        /// request for the ABSENCE of colour, and near-black adds nothing on an additive
        /// material. Reading the raw field here instead would relight a grey blade pink.
        /// </summary>
        public static ElementPalette ResolvePalette(SpellDefinition spell)
        {
            var element = ProjectileExecutor.ResolveElement(spell);
            var basePalette = ElementPalette.For(element ?? SpellElement.Arcane);
            return spell != null ? basePalette.RecolouredTo(spell.particleColor) : basePalette;
        }

        /// <summary>
        /// Resolution order is the order in which a mechanic OWNS the silhouette, most
        /// distinctive first. A volley is five objects and that is the first thing the eye
        /// counts; a pierce is a line through bodies; a turn is a path. Damage is tested last
        /// because almost every spell has some.
        /// </summary>
        public static ProjectileVisualProfile Resolve(SpellDefinition spell)
        {
            var palette = ResolvePalette(spell);
            if (spell == null) return Orb(palette);

            if (spell.projectileCount > 1) return Blade(palette);
            if (spell.pierceCount > 0) return Lance(palette);
            if (spell.homingStrength > 0f && spell.homingRange > 0f) return Spark(palette);

            // A projectile that barely damages and carries a duration is not trying to kill
            // anything — it is delivering a status. Drawing it as hard as a fireball makes
            // the player brace for a hit that never comes.
            if (spell.damage <= 6f && spell.duration > 0f) return Wisp(palette);

            return Orb(palette);
        }

        private static ProjectileVisualProfile Orb(ElementPalette p) => new ProjectileVisualProfile
        {
            Silhouette = ProjectileSilhouette.Orb,
            Palette = p,
            Length = 0.52f,
            Width = 0.52f,
            HasOpaqueCore = true,
            HasAdditiveShell = true,
            TrailTime = 0.14f,
            TrailWidth = 0.30f,
            Shards = 4,
            SpinDegPerSecond = 0f,
            HasLight = true,
            LightRadius = 1.9f,
            GlintInterval = 0f,
            Opacity = 1f,
        };

        private static ProjectileVisualProfile Lance(ElementPalette p) => new ProjectileVisualProfile
        {
            Silhouette = ProjectileSilhouette.Lance,
            Palette = p,
            Length = 1.42f,
            Width = 0.30f,
            HasOpaqueCore = true,
            HasAdditiveShell = true,
            TrailTime = 0.18f,
            TrailWidth = 0.26f,
            Shards = 2,
            SpinDegPerSecond = 0f,
            HasLight = true,
            LightRadius = 2.05f,
            GlintInterval = 0f,
            Opacity = 1f,
        };

        // No additive shell and no light. Steel is matter: the only bright thing on it is the
        // glint as its spin brings an edge past the camera, which is why GlintInterval is the
        // one event this profile owns.
        private static ProjectileVisualProfile Blade(ElementPalette p) => new ProjectileVisualProfile
        {
            Silhouette = ProjectileSilhouette.Blade,
            Palette = p,
            Length = 0.46f,
            Width = 0.20f,
            HasOpaqueCore = true,
            HasAdditiveShell = false,
            TrailTime = 0.09f,
            TrailWidth = 0.10f,
            Shards = 2,
            SpinDegPerSecond = 720f,
            HasLight = false,
            LightRadius = 0f,
            GlintInterval = 0.42f,
            Opacity = 1f,
        };

        // The trail is longer than the body on purpose: for a projectile that turns, the PATH
        // is the spell, and a core with no ribbon behind it makes a curve unreadable.
        private static ProjectileVisualProfile Spark(ElementPalette p) => new ProjectileVisualProfile
        {
            Silhouette = ProjectileSilhouette.Spark,
            Palette = p,
            Length = 0.40f,
            Width = 0.26f,
            HasOpaqueCore = false,
            HasAdditiveShell = true,
            TrailTime = 0.46f,
            TrailWidth = 0.24f,
            Shards = 3,
            SpinDegPerSecond = 0f,
            HasLight = true,
            LightRadius = 1.75f,
            GlintInterval = 0.28f,
            Opacity = 1f,
        };

        private static ProjectileVisualProfile Wisp(ElementPalette p) => new ProjectileVisualProfile
        {
            Silhouette = ProjectileSilhouette.Wisp,
            Palette = p,
            Length = 0.60f,
            Width = 0.44f,
            HasOpaqueCore = false,
            HasAdditiveShell = true,
            TrailTime = 0.34f,
            TrailWidth = 0.20f,
            Shards = 3,
            SpinDegPerSecond = 26f,
            HasLight = true,
            LightRadius = 1.5f,
            GlintInterval = 0.9f,
            Opacity = 0.82f,
        };
    }
}
