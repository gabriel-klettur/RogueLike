using System.Collections.Generic;

namespace Valkur.Core.UI
{
    /// <summary>
    /// What each playable class IS, in one sentence, in the player's language.
    ///
    /// <para><b>Why it exists.</b> The class selector is the screen that decides what the player
    /// does for the next several hours, and it said nothing at all: six cards, each headed by
    /// the raw internal key in lowercase (<c>barbarian</c>, <c>mague</c>, <c>dwarf</c>) and
    /// followed by six unlabelled abbreviations — <c>HP ATK ARM SPD MANA ENG</c> — whose values
    /// were 1 and 2 for attack and 0 and 5 for armour. Nothing on the screen let anyone choose
    /// between them for a reason.</para>
    ///
    /// <para><b>Prose, not stats.</b> The numbers are already on the card and now have bars; a
    /// sentence has to say the thing the numbers cannot, which is what the class FEELS like to
    /// play. Each line names a strength and a cost, because a pitch with no cost is a pitch
    /// nobody believes.</para>
    ///
    /// <para><b>A key with no entry falls back to an empty string</b>, never to another class's
    /// line. A card with no description reads as a class nobody has written up yet; a card
    /// wearing the wrong description is a lie the player only discovers by playing it.</para>
    /// </summary>
    public static class MenuClassCopy
    {
        [SelfHealingStatic("Immutable table built once from literals. Nothing writes to it after the static initialiser, it holds no Unity object and no subscription, so it cannot carry a destroyed reference or a session decision across Play.")]
        private static readonly Dictionary<string, (string en, string es)> Lines =
            new Dictionary<string, (string, string)>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["barbarian"] = (
                    "Heavy and fearless. Hits hard, takes the hits, and has no answer at range.",
                    "Pesado y sin miedo. Pega fuerte, aguanta los golpes y no tiene respuesta a distancia."),
                ["elven"] = (
                    "Quick and precise. Fights at a distance and cannot afford a mistake up close.",
                    "Rápido y preciso. Pelea de lejos y no se puede permitir un error de cerca."),
                ["mague"] = (
                    "All the mana in the world and almost no armour. Wins the fights that end early.",
                    "Todo el maná del mundo y casi nada de armadura. Gana las peleas que acaban pronto."),
                ["valkyrie"] = (
                    "The fastest of the six. Arrives first, leaves first, and is fragile if she stays.",
                    "La más rápida de las seis. Llega antes, se va antes, y es frágil si se queda."),
                ["dwarf"] = (
                    "A wall with four dashes. Slow, enormously tough, and reaches nothing quickly.",
                    "Un muro con cuatro esquivas. Lento, durísimo, y no llega rápido a ningún sitio."),
                ["vampire"] = (
                    "Caster and quick at once, which nobody else is. She pays for it with her skin.",
                    "Lanzadora y rápida a la vez, que no lo es nadie más. Lo paga con su piel."),
            };

        /// <summary>One sentence about <paramref name="playerKey"/>, or empty.</summary>
        public static string Describe(string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return string.Empty;
            if (!Lines.TryGetValue(playerKey, out var pair)) return string.Empty;
            return GameLanguage.Pick(pair.en, pair.es);
        }

        /// <summary>True when the class has a written line. Read by the tests.</summary>
        public static bool Has(string playerKey)
            => !string.IsNullOrEmpty(playerKey) && Lines.ContainsKey(playerKey);

        /// <summary>Every key the table covers, so a test can compare it against the catalogue.</summary>
        public static IEnumerable<string> Keys => Lines.Keys;
    }
}
