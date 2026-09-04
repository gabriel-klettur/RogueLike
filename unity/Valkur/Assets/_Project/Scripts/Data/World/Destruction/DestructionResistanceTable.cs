using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One material's answer to every <see cref="DamageClass"/>. Authored as named floats
    /// rather than an array so the Inspector reads "Fire: 1.4" instead of "Element 5".
    /// </summary>
    [Serializable]
    public class DestructionResistanceRow
    {
        public MaterialClass material;

        [Range(0f, 3f)] public float none = 0.1f;
        [Range(0f, 3f)] public float axe = 1f;
        [Range(0f, 3f)] public float pick = 1f;
        [Range(0f, 3f)] public float blade = 1f;
        [Range(0f, 3f)] public float blunt = 1f;
        [Range(0f, 3f)] public float fire = 1f;
        [Range(0f, 3f)] public float ice = 1f;
        [Range(0f, 3f)] public float lightning = 1f;
        [Range(0f, 3f)] public float arcane = 1f;
        [Range(0f, 3f)] public float dark = 1f;
        [Range(0f, 3f)] public float light = 1f;

        public float Multiplier(DamageClass damageClass)
        {
            switch (damageClass)
            {
                case DamageClass.Axe:       return axe;
                case DamageClass.Pick:      return pick;
                case DamageClass.Blade:     return blade;
                case DamageClass.Blunt:     return blunt;
                case DamageClass.Fire:      return fire;
                case DamageClass.Ice:       return ice;
                case DamageClass.Lightning: return lightning;
                case DamageClass.Arcane:    return arcane;
                case DamageClass.Dark:      return dark;
                case DamageClass.Light:     return light;
                default:                    return none;
            }
        }
    }

    /// <summary>
    /// The material-by-damage-class matrix every destructible building is judged against.
    /// Lives at <c>Resources/DestructionResistanceTable.asset</c>, beside the other
    /// singleton tuning assets (<c>AudioCatalog</c>, <c>CameraFeelProfile</c>,
    /// <c>DayNightProfile</c>).
    ///
    /// <para>This one table is where most of the system's behaviour actually lives. Chopping
    /// with a sword being slow, a fireball burning a wooden house but glancing off a stone
    /// one, a mace denting an iron gate that an axe cannot touch — none of those are coded
    /// anywhere. They are cells here, and a designer rebalances all of them without a
    /// recompile.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "DestructionResistanceTable",
        menuName = "Valkur/World/Destruction Resistance Table")]
    public class DestructionResistanceTable : ScriptableObject
    {
        [Tooltip("One row per MaterialClass. A material with no row falls back to 1.0 for " +
                 "every damage class, which is deliberately permissive — a missing row must " +
                 "not make a building silently invincible.")]
        public List<DestructionResistanceRow> rows = new List<DestructionResistanceRow>();

        /// <summary>
        /// The multiplier applied to a blow. 0 means the blow does nothing at all, which
        /// callers are expected to report differently from a blow that merely does little.
        /// </summary>
        public float Multiplier(MaterialClass material, DamageClass damageClass)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].material == material)
                    return Mathf.Max(0f, rows[i].Multiplier(damageClass));
            }
            return 1f;
        }

        /// <summary>
        /// Fill in the shipped matrix. Exposed (rather than being a one-off in an importer)
        /// because it is the reference balance: a designer who has tuned themselves into a
        /// corner can get back to a known-good state, and the EditMode test seeds a fresh
        /// instance from it rather than asserting against the live asset's current numbers.
        /// </summary>
        [ContextMenu("Seed Shipped Matrix")]
        public void SeedShippedMatrix()
        {
            rows = new List<DestructionResistanceRow>
            {
                //                                     none   axe  pick blade blunt  fire   ice light'g arcane dark light
                Row(MaterialClass.Wood,    0.10f, 1.00f, 0.15f, 0.45f, 0.60f, 1.40f, 0.10f, 0.30f, 0.35f, 0.25f, 0.25f),
                Row(MaterialClass.Foliage, 0.25f, 0.80f, 0.10f, 1.00f, 0.35f, 1.60f, 0.20f, 0.20f, 0.35f, 0.30f, 0.20f),
                Row(MaterialClass.Stone,   0.02f, 0.10f, 1.00f, 0.10f, 0.70f, 0.05f, 0.35f, 0.40f, 0.35f, 0.20f, 0.20f),
                Row(MaterialClass.Metal,   0.02f, 0.15f, 0.55f, 0.25f, 0.90f, 0.20f, 0.15f, 0.75f, 0.35f, 0.25f, 0.25f),
                Row(MaterialClass.Cloth,   0.40f, 0.70f, 0.20f, 1.20f, 0.30f, 2.00f, 0.10f, 0.15f, 0.35f, 0.35f, 0.30f),
                Row(MaterialClass.Glass,   0.30f, 0.90f, 0.90f, 0.90f, 1.50f, 0.30f, 1.30f, 0.60f, 0.35f, 0.25f, 0.25f),
            };
        }

        private static DestructionResistanceRow Row(MaterialClass material,
            float none, float axe, float pick, float blade, float blunt,
            float fire, float ice, float lightning, float arcane, float dark, float light)
        {
            return new DestructionResistanceRow
            {
                material = material,
                none = none, axe = axe, pick = pick, blade = blade, blunt = blunt,
                fire = fire, ice = ice, lightning = lightning,
                arcane = arcane, dark = dark, light = light,
            };
        }
    }
}
