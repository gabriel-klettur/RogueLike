using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The colour and the size of a floating damage number, from what the blow was.
    ///
    /// Two axes, both read at a glance: the ELEMENT picks the hue (a fire hit is orange, an
    /// ice hit is cyan, an untyped blow the plain red it always was) and a CRITICAL is bigger,
    /// gold-white and punched — the stat existed for the life of the project and never once
    /// showed, so it was a stat the player could not see working. Pure, so the mapping is one
    /// fixture and not a screenshot.
    /// </summary>
    public static class DamageNumberPalette
    {
        /// <summary>Untyped damage. The red the number has always been.</summary>
        public static readonly Color Plain = new Color(1f, 0.30f, 0.30f, 1f);

        /// <summary>A critical: gold leaning white, whatever the element — it has to read as a category.</summary>
        public static readonly Color Critical = new Color(1f, 0.90f, 0.45f, 1f);

        /// <summary>Base size of a plain number, in TMP world points.</summary>
        public const float PlainSize = 4.6f;

        /// <summary>Size of a critical. 1.6x, which is what separates "big hit" from "same font".</summary>
        public const float CriticalSize = 7.4f;

        public static Color ColourFor(SpellElement? element, bool critical)
        {
            if (critical) return Critical;
            if (!element.HasValue) return Plain;
            switch (element.Value)
            {
                case SpellElement.Fire:      return new Color(1f, 0.55f, 0.20f, 1f);
                case SpellElement.Ice:       return new Color(0.55f, 0.88f, 1f, 1f);
                case SpellElement.Lightning: return new Color(1f, 0.95f, 0.45f, 1f);
                case SpellElement.Dark:      return new Color(0.72f, 0.45f, 0.95f, 1f);
                case SpellElement.Light:     return new Color(1f, 0.96f, 0.80f, 1f);
                case SpellElement.Arcane:    return new Color(0.85f, 0.55f, 1f, 1f);
                case SpellElement.Boomerang: return new Color(0.80f, 0.90f, 0.60f, 1f);
                default:                     return new Color(0.55f, 0.95f, 0.50f, 1f);   // Verdant and anything appended later
            }
        }

        public static float SizeFor(bool critical) => critical ? CriticalSize : PlainSize;
    }
}
