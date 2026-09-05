using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>The four shapes a persistent ground field in this game is allowed to be.</summary>
    internal enum GroundFieldShape
    {
        /// <summary>A pool lying on the floor. The historical <see cref="AreaFXRig"/> discs.</summary>
        Pool,
        /// <summary>A COLUMN of falling things over a hard floor boundary.</summary>
        Storm,
        /// <summary>A PATH: independent patches dropped behind a moving caster.</summary>
        Trail,
        /// <summary>Ground torn open. <see cref="RootWhipFX"/>.</summary>
        Roots,
    }

    /// <summary>
    /// Decides what a persistent ground field LOOKS like from what it DOES.
    ///
    /// <para>Same pattern as <see cref="ProjectileVisualProfile"/> and <c>SlashProfile</c>: the
    /// shape follows the verb, so the two cannot drift. Before this,
    /// <c>PuddleController.BuildVisual</c> called <c>AreaPalette.LavaPuddle()</c>
    /// UNCONDITIONALLY — its own comment admitted the branch that used to stand there returned
    /// the same palette from both sides — so <c>blizzard</c>, an Ice spell authoring
    /// <c>(0.72, 0.90, 1.00)</c>, drew ORANGE, and it drew pixel-for-pixel the same effect as
    /// <c>cinder_trail</c>. Two spells in different schools, one picture.</para>
    ///
    /// <para>WHY NOT AN AUTHORED KEY. A <c>groundFieldVisual</c> string on the asset would be a
    /// second, independent opinion about the spell, free to disagree with the mechanic
    /// silently. This project has recorded that failure under "authored and inert" a dozen
    /// times. A derived profile cannot enter that state, and every field it reads
    /// (<c>followCaster</c>, <c>ttl</c>, <c>element</c>) was already authored and — for the
    /// first two — already inert on this code path.</para>
    /// </summary>
    internal struct GroundFieldProfile
    {
        /// <summary>
        /// The one field that has never been a puddle. Recognised by SPELL KEY, which is the
        /// discriminator <c>PuddleExecutor</c> already used and documented: the older test
        /// (<c>vfxPreset == "root_whip"</c>) named a preset that has never existed.
        /// </summary>
        public const string RootWhipKey = "root_whip";

        public GroundFieldShape Shape;

        /// <summary>
        /// The colour every layer of the chosen rig is derived from. Resolved through
        /// <see cref="ElementPalette.RecolouredTo"/>, which already handles all three meanings
        /// of <c>particleColor</c> in the right order — opaque white is the "nobody authored
        /// this" sentinel, an achromatic value is a request for the ABSENCE of colour, and
        /// near-black adds nothing on an additive material.
        /// </summary>
        public ElementPalette Palette;

        /// <summary>The spell's own swatch, for rigs that derive their own palette from it.</summary>
        public Color Swatch;

        public static GroundFieldProfile Resolve(SpellDefinition spell)
        {
            var element = ProjectileExecutor.ResolveElement(spell);
            var palette = ElementPalette.For(element ?? SpellElement.Fire);
            if (spell != null) palette = palette.RecolouredTo(spell.particleColor);

            var profile = new GroundFieldProfile
            {
                Shape = GroundFieldShape.Pool,
                Palette = palette,
                Swatch = spell != null ? spell.particleColor : Color.white,
            };
            if (spell == null) return profile;

            if (string.Equals(spell.spellKey, RootWhipKey, System.StringComparison.OrdinalIgnoreCase))
            {
                profile.Shape = GroundFieldShape.Roots;
                return profile;
            }

            // followCaster FIRST, because it changes the field's TOPOLOGY rather than its
            // surface: a trail is many small independent patches, not one big one, and no
            // recolouring of a single disc can express that. The flag was authored on
            // cinder_trail and read by nothing on this path, so the spell was one static disc
            // parked at the cursor for eight seconds.
            if (spell.followCaster)
            {
                profile.Shape = GroundFieldShape.Trail;
                return profile;
            }

            // A field of ice is not a pool of ice. What the player has to read is that things
            // are FALLING inside a circle, and a stack of coplanar discs cannot draw that —
            // the same argument IceWallVisual makes for a line and VortexFunnelFX for a column.
            if (element == SpellElement.Ice) profile.Shape = GroundFieldShape.Storm;

            return profile;
        }
    }
}
