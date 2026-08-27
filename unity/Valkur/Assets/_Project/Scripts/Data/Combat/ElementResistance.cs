using System;

namespace Valkur.Data
{
    /// <summary>
    /// One entry of an entity's elemental damage table: how much of a given
    /// <see cref="SpellElement"/>'s damage actually lands. An element with no entry in
    /// <see cref="EntityStats.resistances"/> defaults to a multiplier of 1.0 (no
    /// change) — see <c>Health.ResolveElementMultiplier</c>.
    /// </summary>
    [Serializable]
    public struct ElementResistance
    {
        public SpellElement element;

        /// <summary>
        /// Damage multiplier for <see cref="element"/>. 1.0 = normal, 0.5 = resistant
        /// (half damage), 0 = immune to this element specifically, greater than 1 =
        /// vulnerable (extra damage). Applied BEFORE flat defense — see
        /// <c>Health.MitigateDamage</c> for the documented order and why.
        /// </summary>
        public float multiplier;
    }
}
