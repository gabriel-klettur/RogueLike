using System.Collections.Generic;
using System.Text;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Every player-visible string the grimoire draws, in one place and in the language
    /// <see cref="GameLanguage"/> says.
    ///
    /// <para><b>Why a file rather than literals at the call site.</b> The panel shipped
    /// entirely in English inside a game whose HUD, chat, quests and menus are Spanish —
    /// "Grimoire", "Learn", "Available", "Locked", and the placeholder plural
    /// <c>"arcane point(s)"</c>. HUD_VISUAL_LANGUAGE.md R15 forbids that for a game window.
    /// The shape is <c>MenuText</c>'s and <c>ChatLanguage</c>'s: a table of pairs, no
    /// catalogue asset and no key lookup, which is all two languages and forty strings
    /// justify.</para>
    ///
    /// <para><b>Plurals are resolved, never parenthesised.</b> "1 punto arcano" and "2 puntos
    /// arcanos" are two strings, and a panel that says "punto(s)" is a panel nobody
    /// finished.</para>
    ///
    /// <para><b>The SCHOOL's own name and flavour are not here.</b> They are authored content
    /// on the nine <c>SpellTree</c> assets, in Spanish, and English leaves authored content
    /// alone — the rule <c>GameLanguage</c> states for the NPC dialogue and the one the
    /// talents tab follows for its own trees.</para>
    /// </summary>
    public static class GrimoireText
    {
        private static string P(string english, string spanish) =>
            GameLanguage.Pick(english, spanish);

        public static string Title      => P("Grimoire", "Grimorio");
        public static string Learn      => P("Learn", "Aprender");
        public static string NoSchools  => P("Grimoire  —  no schools loaded",
                                             "Grimorio  —  no hay escuelas cargadas");
        public static string Known      => P("Known", "Conocido");
        public static string Available  => P("Available", "Disponible");
        public static string Affinity   => P("affinity", "afinidad");

        /// <summary>The filter chip that turns the filter off.</summary>
        public static string AllRoles => P("All", "Todo");

        /// <summary>What the detail card says before the player has chosen anything.</summary>
        public static string PickANode => P("Pick a node", "Elige un nodo");

        /// <summary>Separator between two lock reasons on one line.</summary>
        public const string ReasonSeparator = "  ·  ";

        /// <summary>"1 arcane point" / "1 punto arcano", pluralised in both languages.</summary>
        public static string Points(int n) => n == 1
            ? P("1 arcane point", "1 punto arcano")
            : P(n + " arcane points", n + " puntos arcanos");

        /// <summary>The short form used on a row: "1 AP" / "1 PA".</summary>
        public static string PointsShort(int n) => n + P(" AP", " PA");

        /// <summary>
        /// "te quedan 3" — what the purse holds AFTER the purchase. A price with no budget
        /// beside it is a number the player has to hold in their head, and the old header
        /// showed the purse and the cost in two different places.
        /// </summary>
        public static string Remaining(int n) =>
            P("you keep " + n, "te " + (n == 1 ? "queda " : "quedan ") + n);

        /// <summary>The surcharge, said out loud.</summary>
        public static string OffAffinity(float multiplier) =>
            P("off-affinity x", "fuera de afinidad x") + multiplier.ToString("0.#");

        /// <summary>Header: the school, whether it is the character's, and the purse.</summary>
        public static string Header(string schoolName, bool hasAffinity, float multiplier,
                                    int availablePoints)
        {
            string tag = hasAffinity ? Affinity : OffAffinity(multiplier);
            string purse = GameLanguage.IsEnglish
                ? Points(availablePoints) + " available"
                : Points(availablePoints) + (availablePoints == 1 ? " disponible" : " disponibles");
            return schoolName + "  (" + tag + ")   —   " + purse;
        }

        /// <summary>The name of a role, for the row tag and (at F2) the filter.</summary>
        public static string Role(SpellRole role)
        {
            switch (role)
            {
                case SpellRole.Damage:     return P("Damage", "Daño");
                case SpellRole.Control:    return P("Control", "Control");
                case SpellRole.Protection: return P("Protection", "Protección");
                case SpellRole.Healing:    return P("Healing", "Curación");
                case SpellRole.Mobility:   return P("Mobility", "Movilidad");
                case SpellRole.Summon:     return P("Summoning", "Invocación");
                case SpellRole.Utility:    return P("Utility", "Utilidad");
                default:                   return P("Utility", "Utilidad");
            }
        }

        /// <summary>
        /// One lock reason as the player reads it. Short by construction: the old panel wrote
        /// a full sentence into a 30 px row whose second line was silently truncated, so the
        /// reason the whole row exists for was the part that got cut.
        /// </summary>
        public static string Reason(in SpellLock lockReason)
        {
            switch (lockReason.Kind)
            {
                case SpellLockKind.AlreadyKnown:
                    return Known;
                case SpellLockKind.Level:
                    return P("Level ", "Nivel ") + lockReason.Required;
                case SpellLockKind.Prerequisite:
                    return P("Needs ", "Requiere ") + (lockReason.Missing != null
                        ? lockReason.Missing.ResolveDisplayName()
                        : "?");
                case SpellLockKind.Points:
                    string missing = P("Short ", "Faltan ") +
                                     PointsShort(lockReason.Required - lockReason.Have);
                    return lockReason.OffAffinity
                        ? missing + " (" + OffAffinity(lockReason.School != null
                              ? lockReason.School.offAffinityCostMultiplier : 1f) + ")"
                        : missing;
                default:
                    return P("Malformed node", "Nodo mal formado");
            }
        }

        /// <summary>
        /// What a node grants, in the player's language.
        ///
        /// <para><b>Why not <c>SpellNode.DescribeEffects</c>.</b> That one builds "Unlocks
        /// {name} ({role})" inside <c>Valkur.Data</c>, which owns no language table and should
        /// not — it is the string the Spells editor and the console want. Captured live, it was
        /// the one line left in English on a card whose every other word had been translated.
        /// The modifiers keep their own <c>Describe()</c>, which is already Spanish because
        /// <c>StatCatalog</c> is.</para>
        /// </summary>
        public static string Effects(SpellNode node, StringBuilder into)
        {
            into.Length = 0;
            if (node == null) return string.Empty;

            // The ROLE, and not the name again. The card already prints the spell's name at
            // the top in the largest type it has, so "Desbloquea Tajo (Hendidura)  (Dano)"
            // said it twice in one panel with a stray double space between the halves -
            // captured live, and it is the shape of a line that was written for a console
            // where nothing else names the node. What the player cannot read anywhere else on
            // the card is what this row exists for.
            if (node.spell != null)
                into.Append(P("Unlocks a ", "Desbloquea un hechizo de ")).Append(Role(node.role));

            if (node.modifiers != null)
            {
                for (int i = 0; i < node.modifiers.Length; i++)
                {
                    if (into.Length > 0) into.AppendLine();
                    into.Append(node.modifiers[i].Describe());
                }
            }
            return into.ToString();
        }

        /// <summary>Every reason on one line, in the order the model collected them.</summary>
        public static string Reasons(List<SpellLock> locks, StringBuilder into)
        {
            into.Length = 0;
            if (locks == null) return string.Empty;

            for (int i = 0; i < locks.Count; i++)
            {
                if (into.Length > 0) into.Append(ReasonSeparator);
                into.Append(Reason(locks[i]));
            }
            return into.ToString();
        }
    }
}
