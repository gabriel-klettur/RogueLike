using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// One landed blow, already judged against the material it hit.
    /// </summary>
    public readonly struct HarvestBlow
    {
        /// <summary>How the blow was delivered, after the tool and element are resolved.</summary>
        public readonly DamageClass DamageClass;

        /// <summary>Damage multiplier from the matrix, with the tool tier gate folded in.</summary>
        public readonly float Multiplier;

        /// <summary>Tier of the tool that was chosen, 0 for bare hands and for magic.</summary>
        public readonly int ToolTier;

        /// <summary>
        /// True when the tool is physical and below the profile requirement. The blow still
        /// lands (scaled by <c>chipDamageFraction</c>) unless that fraction is zero, but the
        /// feedback layer needs to say "wrong tool" rather than "weak hit".
        /// </summary>
        public readonly bool WrongTool;

        public HarvestBlow(DamageClass damageClass, float multiplier, int toolTier, bool wrongTool)
        {
            DamageClass = damageClass;
            Multiplier = multiplier;
            ToolTier = toolTier;
            WrongTool = wrongTool;
        }

        /// <summary>
        /// A multiplier of exactly zero is a deliberate immunity: the blow does nothing at
        /// all, and callers are expected to report that differently from a blow that merely
        /// does little.
        /// </summary>
        public bool Immune => Multiplier <= 0f;
    }

    /// <summary>
    /// The single owner of "what did that blow actually amount to against this material".
    ///
    /// <para>It exists because two callers now ask the same question — a combat swing through
    /// <see cref="BuildingDurability"/>, and a harvest session through
    /// <c>HarvestNode</c> — and the whole point of
    /// <see cref="DestructionResistanceTable"/> is that there is ONE answer. Two
    /// implementations of the tier gate would drift the first time either is tuned, and the
    /// drift would be invisible: both halves stay internally consistent and disagree only on
    /// screen, which is the failure shape this project keeps recording.</para>
    /// </summary>
    public static class HarvestBlowResolver
    {
        /// <summary>
        /// Where the shared matrix lives, beside the project's other singleton tuning assets
        /// (<c>AudioCatalog</c>, <c>CameraFeelProfile</c>, <c>DayNightProfile</c>).
        /// </summary>
        private const string RESISTANCE_TABLE_RESOURCE = "DestructionResistanceTable";

        // Domain Reload is OFF, so a cached asset reference survives into the next Play
        // session pointing at an object that may have been unloaded. Assigning null is a
        // plain stsfld, the only reset shape DomainReloadStaticResetTests recognises.
        private static DestructionResistanceTable _sharedTable;
        private static bool _tableLoadAttempted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _sharedTable = null;
            _tableLoadAttempted = false;
        }

        /// <summary>
        /// The shared matrix, loaded once. Null when the project ships none, in which case
        /// every material takes full damage from every class — deliberately permissive, so a
        /// missing asset cannot make the world silently invincible.
        /// </summary>
        public static DestructionResistanceTable Table
        {
            get
            {
                if (_sharedTable != null) return _sharedTable;
                if (_tableLoadAttempted) return null;

                _tableLoadAttempted = true;
                _sharedTable = Resources.Load<DestructionResistanceTable>(RESISTANCE_TABLE_RESOURCE);

                if (_sharedTable == null)
                {
                    Debug.LogWarning(
                        $"[HarvestBlowResolver] No '{RESISTANCE_TABLE_RESOURCE}' asset under " +
                        "Resources. Every material will take full damage from every damage class.");
                }
                return _sharedTable;
            }
        }

        /// <summary>
        /// Let a test drive the resolver against a matrix it built itself, without an asset on
        /// disk. Passing null restores the load-from-Resources path.
        /// </summary>
        public static void OverrideTable(DestructionResistanceTable table)
        {
            _sharedTable = table;
            _tableLoadAttempted = table != null;
        }

        /// <summary>
        /// Judge a blow. <paramref name="element"/> set means it was cast rather than swung.
        /// </summary>
        public static HarvestBlow Resolve(DestructionProfile profile, GameObject attacker,
            SpellElement? element)
        {
            if (profile == null) return new HarvestBlow(DamageClass.None, 0f, 0, false);

            var table = Table;
            var damageClass = DamageClassResolver.Resolve(
                attacker, element, profile.material, table, out int toolTier);

            float multiplier = table != null
                ? table.Multiplier(profile.material, damageClass)
                : 1f;

            // The tier gate is physical-only: there is no such thing as the wrong tier of
            // fireball, so magic is judged by the matrix alone.
            bool wrongTool = DamageClassResolver.IsPhysical(damageClass)
                             && toolTier < profile.requiredToolTier;

            if (wrongTool) multiplier *= profile.chipDamageFraction;

            return new HarvestBlow(damageClass, multiplier, toolTier, wrongTool);
        }

        /// <summary>
        /// Apply a blow multiplier to a raw amount.
        ///
        /// <para>A blow with a real multiplier must never round away to nothing, or a weak but
        /// legitimate tool reads as an unbreakable building and the player concludes the thing
        /// cannot be harvested at all. A multiplier of exactly zero is a deliberate immunity
        /// and stays zero.</para>
        /// </summary>
        public static int Scale(int amount, float multiplier)
        {
            if (amount <= 0 || multiplier <= 0f) return 0;
            int dealt = Mathf.FloorToInt(amount * multiplier);
            return dealt > 0 ? dealt : 1;
        }
    }
}
