using System;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Editorial grouping for the Particles Editor (F1) preset picker.
    ///
    /// Deliberately NOT derived from <c>ParticlePresetDefinition.type</c> or
    /// <c>vfx.kind</c>. Those two disagree with each other — the catalog holds 39 presets
    /// typed "explosion" but only 35 of kind "explosion", and 24 typed "trail" against 8 of
    /// that kind — because <c>kind</c> selects a runtime recipe while <c>type</c> is leftover
    /// importer metadata. Neither answers the question the picker actually asks, which is
    /// "what is this FOR".
    ///
    /// The split matters because of how the catalog is shaped: of ~116 presets, roughly 46
    /// are the four projectile stacks (fireball / iceball / darkball / lightball) and another
    /// 20 are portal variants. Two thirds of the grid is spell internals that someone placing
    /// chimney smoke or a torch never wants to scroll past.
    ///
    /// Rules are prefix-based and ordered; the first match wins. A preset that matches
    /// nothing lands in <see cref="Category.SpellFx"/> rather than vanishing.
    /// </summary>
    public static class ParticlePresetCategory
    {
        public enum Category
        {
            /// <summary>World decoration: foliage, weather, chimney smoke, torches.</summary>
            Ambient,
            /// <summary>Flowing water, fountains and their sparkle.</summary>
            Water,
            /// <summary>Combustion and blast: explosions, embers, shockwaves, free smoke.</summary>
            Fire,
            /// <summary>Auras, healing, mana, arcane and storm fields.</summary>
            Magic,
            /// <summary>Portal rims, cores, sparks and swirls.</summary>
            Portals,
            /// <summary>Everything owned by a spell: projectile stacks, beams, slashes, dashes.</summary>
            SpellFx,
        }

        /// <summary>Tab order in the picker. Kept explicit so it never depends on enum order.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable tab order built once from enum literals. " +
            "Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        public static readonly Category[] TabOrder =
        {
            Category.Ambient, Category.Water, Category.Fire,
            Category.Magic, Category.Portals, Category.SpellFx,
        };

        /// <summary>Short label — the strip is ~490 px wide and shares it between tabs.</summary>
        public static string Label(Category c)
        {
            switch (c)
            {
                case Category.Ambient: return "Ambient";
                case Category.Water:   return "Water";
                case Category.Fire:    return "Fire";
                case Category.Magic:   return "Magic";
                case Category.Portals: return "Portals";
                case Category.SpellFx: return "Spell FX";
                default:               return c.ToString();
            }
        }

        // Ordered rules: first prefix that matches decides. Order matters — "fireball_" must
        // be tested before "fire", and "water_" before any generic fallback.
        [Valkur.Core.SelfHealingStatic("Immutable prefix table built once from string literals. " +
            "Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly (string Prefix, Category Cat)[] Rules =
        {
            // Projectile stacks first: their names contain "fire", "ice" and "light", which
            // would otherwise be captured by the elemental buckets below.
            ("fireball_",   Category.SpellFx),
            ("iceball_",    Category.SpellFx),
            ("darkball_",   Category.SpellFx),
            ("lightball_",  Category.SpellFx),

            ("portal_",     Category.Portals),

            // Decoration.
            ("falling_leaf",    Category.Ambient),
            ("falling_petal",   Category.Ambient),
            ("autumn_leaves",   Category.Ambient),
            ("flowers_",        Category.Ambient),
            ("rain_",           Category.Ambient),
            ("chimney_",        Category.Ambient),
            ("torch_",          Category.Ambient),
            ("forge_",          Category.Ambient),

            // Water.
            ("water_",      Category.Water),
            ("fountain_",   Category.Water),

            // Combustion and blast.
            ("explosion_",  Category.Fire),
            ("frost_explosion", Category.Fire),
            ("shockwave",   Category.Fire),
            ("ember_",      Category.Fire),
            ("smoke_",      Category.Fire),
            ("nebulous_smoke", Category.Fire),

            // Fields and channelled magic.
            ("aura_",           Category.Magic),
            ("healing_aura",    Category.Magic),
            ("holy_",           Category.Magic),
            ("mana_regen",      Category.Magic),
            ("arcane_flame",    Category.Magic),
            ("lightning_storm", Category.Magic),
        };

        /// <summary>
        /// Category for a preset id. Never throws; a null or unmatched id is
        /// <see cref="Category.SpellFx"/>, the bucket for spell-owned effects.
        /// </summary>
        public static Category Of(string presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return Category.SpellFx;
            string id = presetId.ToLowerInvariant();
            for (int i = 0; i < Rules.Length; i++)
                if (id.StartsWith(Rules[i].Prefix, StringComparison.Ordinal))
                    return Rules[i].Cat;
            return Category.SpellFx;
        }

        /// <summary>Convenience overload; a null preset is <see cref="Category.SpellFx"/>.</summary>
        public static Category Of(ParticlePresetDefinition preset)
            => Of(preset != null ? preset.id : null);
    }
}
